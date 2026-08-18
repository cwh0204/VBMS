using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VBMS.Models;
using VBMS.Services.Communications;
using VBMS.Services.Evaluators;
using VBMS.Services.OpcUa; // ★ 검증/평가 서비스 네임스페이스 추가

namespace VBMS.Services.Orchestrators
{
    /// <summary>
    /// CRP 통신 서비스와 상위 뷰모델 간의 데이터 흐름과 비즈니스 로직을 조율하는 오케스트레이터 클래스입니다.
    /// </summary>
    public class FdsDataOrchestrator : IFdsDataOrchestrator
    {
        private readonly ICrpCommunicationService _commService;
        private readonly FdsOpcServer _opcServer;
        private readonly IFdsMappingService _mappingService;
        private readonly FdsOptions _fdsOptions;
        private readonly IFireSignalEvaluator _signalEvaluator; // ★ 추가
        private readonly IFireVerificationService _verificationService; // ★ 추가

        // 워치독 타이머 및 보드별 수신 시각/타임아웃 상태 관리
        private readonly ConcurrentDictionary<string, DateTime> _boardLastSeen = new();
        private readonly ConcurrentDictionary<string, bool> _boardTimeoutState = new();
        private readonly Timer _watchdogTimer;
        private readonly TimeSpan _timeoutThreshold = TimeSpan.FromSeconds(10); // 10초 미수신 시 타임아웃

        private bool _disposed = false;

        public event Action<CrpPacket>? OnPacketProcessed;
        public event Action<bool>? OnConnectionChanged;
        public event Action<string, string>? OnLogMessage;

        public FdsDataOrchestrator(
            ICrpCommunicationService commService,
            FdsOpcServer opcServer,
            IFdsMappingService mappingService,
            IOptions<FdsOptions> fdsOptions,
            IFireSignalEvaluator signalEvaluator,
            IFireVerificationService verificationService)
        {
            _commService = commService ?? throw new ArgumentNullException(nameof(commService));
            _opcServer = opcServer ?? throw new ArgumentNullException(nameof(opcServer));
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
            _fdsOptions = fdsOptions?.Value ?? throw new ArgumentNullException(nameof(fdsOptions));
            _signalEvaluator = signalEvaluator ?? throw new ArgumentNullException(nameof(signalEvaluator));
            _verificationService = verificationService ?? throw new ArgumentNullException(nameof(verificationService));

            // 통신 서비스 이벤트 바인딩
            _commService.OnPacketReceived += HandlePacketReceived;
            _commService.OnConnectionChanged += HandleConnectionChanged;
            _commService.OnLogMessage += HandleLogMessage;

            // 3초 주기 워치독 타이머 작동 (3초마다 보드별 타임아웃 감지)
            _watchdogTimer = new Timer(CheckBoardTimeouts, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }

        /// <summary>
        /// 지정된 포트로 TCP 서버를 가동합니다.
        /// </summary>
        public async Task StartServerAsync(int port)
        {
            await _commService.StartServerAsync(port);
        }

        /// <summary>
        /// 서버 연결을 해제하고 서비스를 중지합니다.
        /// </summary>
        public void StopServer()
        {
            _commService.Disconnect();
        }

        // 비동기 리셋 명령어 전송(await)을 위해 async void로 변경
        private async void HandlePacketReceived(CrpPacket packet)
        {
            if (packet != null && !string.IsNullOrEmpty(packet.Id))
            {
                // 보드 수신 시간 기록
                _boardLastSeen[packet.Id] = DateTime.Now;

                // 기존에 복구되지 않았던 타임아웃 보드라면 통신 정상 복구 알림
                if (_boardTimeoutState.TryGetValue(packet.Id, out bool isTimeout) && isTimeout)
                {
                    _boardTimeoutState[packet.Id] = false;
                    OnLogMessage?.Invoke($"[INFO] CRP 보드 통신 정상 복구: Board ID = {packet.Id}", "Info");
                }
            }

            if (packet != null && packet.Detectors != null && packet.Detectors.Count > 0)
            {
                // 패킷의 maxLine(보드당 연 수) 파싱 (기본값 16)
                int maxLine = 16;
                if (int.TryParse(packet.MaxLine, out int ml) && ml > 0)
                {
                    maxLine = ml;
                }

                // appsettings.json 기반으로 보드 ID에 해당하는 레인 번호 및 Bay 오프셋 조회
                if (_mappingService.TryGetBoardMapping(packet.Id, maxLine, out int lane, out int bayOffset))
                {
                    // OPC UA 서버가 구동되어 NodeManager가 생성되었는지 확인 후 업데이트
                    if (_opcServer.NodeManager != null)
                    {
                        int.TryParse(packet.Id, out int boardId);

                        foreach (var det in packet.Detectors)
                        {
                            // 1. 상태/온도 기반 1차 신호 도출 (0: 정상, 1: 연기, 2: 온도)
                            uint rawSignal = _signalEvaluator.Evaluate(det.Status, det.Temperature);

                            // 2. 감지기 식별 키 생성
                            string detectorKey = $"Lane_{lane}_Bay_{det.Bay}_Level_{det.Level}";

                            // 3. 개별 감지기 2회 리셋([001RSR1600]) 검증 처리
                            uint finalSignal = await _verificationService.VerifySignalAsync(
                                detectorKey,
                                rawSignal,
                                packet.Id,
                                det.Bay,
                                det.Level
                            );

                            // 4. 셀 좌표 계산 및 OPC UA 노드 개별 반영
                            int globalRow = (det.Bay - 1) + bayOffset;
                            int col = (det.Level >= 13) ? det.Level - 1 : det.Level;

                            _opcServer.NodeManager.UpdateRackCell(lane, globalRow, col, finalSignal);
                        }
                    }
                    else
                    {
                        OnLogMessage?.Invoke("[WARN] OPC UA NodeManager가 아직 초기화되지 않았습니다.", "Warning");
                    }
                }
                else
                {
                    OnLogMessage?.Invoke($"[WARN] appsettings.json에 등록되지 않은 보드 ID 수신: {packet.Id}", "Warning");
                }
            }

            if (packet != null)
            {
                OnPacketProcessed?.Invoke(packet);
            }
        }

        /// <summary>
        /// 주기적으로 보드별 마지막 수신 시각을 확인하여 타임아웃 발생 시 장애 상태를 반영합니다.
        /// </summary>
        private void CheckBoardTimeouts(object? state)
        {
            DateTime now = DateTime.Now;

            if (_fdsOptions?.Lanes == null) return;

            foreach (var lane in _fdsOptions.Lanes)
            {
                if (lane.BoardIds == null) continue;

                for (int i = 0; i < lane.BoardIds.Count; i++)
                {
                    string boardId = lane.BoardIds[i];
                    int bayOffset = i * 16; // CRP 1개당 16연 기준

                    // 데이터를 수신한 적이 있고, 10초 이상 지난 경우
                    if (_boardLastSeen.TryGetValue(boardId, out DateTime lastSeen))
                    {
                        if (now - lastSeen > _timeoutThreshold)
                        {
                            bool alreadyTimeout = _boardTimeoutState.TryGetValue(boardId, out bool isT) && isT;

                            // 최초 타임아웃 감지시에만 에러 처리 및 로그 실행
                            if (!alreadyTimeout)
                            {
                                _boardTimeoutState[boardId] = true;

                                // 1. OPC UA 노드 상 해당 영역 통신 장애(Signal = 3) 처리
                                if (_opcServer.NodeManager != null)
                                {
                                    _opcServer.NodeManager.SetBoardCommunicationFault(lane.LaneNumber, bayOffset, 16);
                                }

                                // 2. 로그 발생
                                OnLogMessage?.Invoke($"[ERROR] CRP 보드 통신 두절 감지: Board ID = {boardId} (레인 {lane.LaneNumber}, 연 오프셋 {bayOffset})", "Error");
                            }
                        }
                    }
                }
            }
        }

        private void HandleConnectionChanged(bool isConnected)
        {
            OnConnectionChanged?.Invoke(isConnected);
        }

        private void HandleLogMessage(string message, string level)
        {
            OnLogMessage?.Invoke(message, level);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // 워치독 타이머 자원 해제
                _watchdogTimer?.Dispose();

                // 이벤트 구독 해제 (메모리 누수 방지)
                _commService.OnPacketReceived -= HandlePacketReceived;
                _commService.OnConnectionChanged -= HandleConnectionChanged;
                _commService.OnLogMessage -= HandleLogMessage;

                _commService.Disconnect();
                _disposed = true;
            }
        }
    }
}