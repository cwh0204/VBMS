using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VBMS.Enums; // ⭐ [추가] AnomalyStatus Enum 참조
using VBMS.Models;
using VBMS.Repositories;
using VBMS.Services.Communications;
using VBMS.Services.Evaluators;
using VBMS.Services.OpcUa;
using VBMS.Services.Storage;

namespace VBMS.Services.Orchestrators
{
    public class FdsDataOrchestrator : IFdsDataOrchestrator
    {
        private readonly ICrpCommunicationService _commService;
        private readonly FdsOpcServer _opcServer;
        private readonly IFdsMappingService _mappingService;
        private readonly FdsOptions _fdsOptions;
        private readonly IFireSignalEvaluator _signalEvaluator;
        private readonly IFireVerificationService _verificationService;
        private readonly ITelemetryStorageService _telemetryStorageService;
        private readonly IDetectorRepository _repository;

        private readonly ConcurrentDictionary<string, DateTime> _boardLastSeen = new();
        private readonly ConcurrentDictionary<string, bool> _boardTimeoutState = new();
        private readonly Timer _watchdogTimer;
        private readonly Timer _summaryTimer;

        // 4초 간격 Summary 집계를 위해 데이터를 임시 보관하는 버퍼
        private readonly ConcurrentBag<DetectorData> _summaryBuffer = new();

        private readonly TimeSpan _timeoutThreshold = TimeSpan.FromSeconds(10);
        private bool _disposed = false;

        public event Action<CrpPacket>? OnPacketProcessed;
        public event Action<int, CrpPacket>? OnPacketProcessedWithOffset;
        public event Action<bool>? OnConnectionChanged;
        public event Action<string, string>? OnLogMessage;

        public FdsDataOrchestrator(
            ICrpCommunicationService commService,
            FdsOpcServer opcServer,
            IFdsMappingService mappingService,
            IOptions<FdsOptions> fdsOptions,
            IFireSignalEvaluator signalEvaluator,
            IFireVerificationService verificationService,
            ITelemetryStorageService telemetryStorageService,
            IDetectorRepository repository)
        {
            _commService = commService ?? throw new ArgumentNullException(nameof(commService));
            _opcServer = opcServer ?? throw new ArgumentNullException(nameof(opcServer));
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
            _fdsOptions = fdsOptions?.Value ?? throw new ArgumentNullException(nameof(fdsOptions));
            _signalEvaluator = signalEvaluator ?? throw new ArgumentNullException(nameof(signalEvaluator));
            _verificationService = verificationService ?? throw new ArgumentNullException(nameof(verificationService));
            _telemetryStorageService = telemetryStorageService ?? throw new ArgumentNullException(nameof(telemetryStorageService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            _commService.OnPacketReceived += HandlePacketReceived;
            _commService.OnConnectionChanged += HandleConnectionChanged;
            _commService.OnLogMessage += HandleLogMessage;

            _watchdogTimer = new Timer(CheckBoardTimeouts, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

            // 4초 주기로 Summary 데이터를 집계하여 DB에 저장하는 타이머 작동
            _summaryTimer = new Timer(ProcessSummaryLogs, null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
        }

        public async Task StartServerAsync(int port)
        {
            await _commService.StartServerAsync(port);
        }

        public void StopServer()
        {
            _commService.Disconnect();
        }

        private async void HandlePacketReceived(CrpPacket packet)
        {
            if (packet != null && !string.IsNullOrEmpty(packet.Id))
            {
                _boardLastSeen[packet.Id] = DateTime.Now;

                if (_boardTimeoutState.TryGetValue(packet.Id, out bool isTimeout) && isTimeout)
                {
                    _boardTimeoutState[packet.Id] = false;
                    OnLogMessage?.Invoke($"[INFO] CRP 보드 통신 정상 복구: Board ID = {packet.Id}", "Info");
                }
            }

            if (packet != null && packet.Detectors != null && packet.Detectors.Count > 0)
            {
                int maxLine = 16;
                if (int.TryParse(packet.MaxLine, out int ml) && ml > 0)
                {
                    maxLine = ml;
                }

                if (_mappingService.TryGetBoardMapping(packet.Id, maxLine, out int lane, out int bayOffset))
                {
                    int.TryParse(packet.Id, out int boardId);

                    // 1. 각 감지기에 BoardId 채워주기 및 서머리 버퍼 수집
                    foreach (var det in packet.Detectors)
                    {
                        det.BoardId = boardId;
                        _summaryBuffer.Add(det);
                    }

                    // 2. 원시 텔레메트리 데이터 DB 수집 채널 밀어넣기
                    _telemetryStorageService.EnqueueRange(packet.Detectors);

                    // 3. OPC UA 서버 업데이트 및 이상징후/화재 검증 로직 실행
                    if (_opcServer.NodeManager != null)
                    {
                        var dumpList = new List<(DetectorData Data, DateTime ReceivedAt)>();
                        DateTime now = DateTime.Now;

                        // ⭐ [추가] 정상 작동 중인 유효 센서만으로 랙 평균 온도 산출 (0℃ 및 에러 상태 제외)
                        var validDetectors = packet.Detectors.Where(d => d.Status < 3 && d.Temperature > 0).ToList();
                        double rackAvgTemp = validDetectors.Count > 0 ? validDetectors.Average(d => d.Temperature) : 0;

                        foreach (var det in packet.Detectors)
                        {
                            // ⭐ [추가] 평균 온도 기반 사전 이상징후(DeltaTempWarning, AbsoluteTempWarning) 평가
                            AnomalyStatus anomaly = _signalEvaluator.EvaluateAnomaly(det.Status, det.Temperature, rackAvgTemp);

                            uint rawSignal = _signalEvaluator.Evaluate(det.Status, det.Temperature);
                            string detectorKey = $"Lane_{lane}_Bay_{det.Bay}_Level_{det.Level}";

                            uint finalSignal = await _verificationService.VerifySignalAsync(
                                detectorKey,
                                rawSignal,
                                packet.Id,
                                det.Bay,
                                det.Level
                            );

                            // ⭐ [수정] 화재 확정(finalSignal > 0)뿐만 아니라 사전 이상징후(편차 이상, 주의 온도) 감지 시에도 Dump 저장 대상 수집
                            if (finalSignal > 0 || anomaly == AnomalyStatus.DeltaTempWarning || anomaly == AnomalyStatus.AbsoluteTempWarning)
                            {
                                dumpList.Add((det, now));

                                // 이상징후 감지 시 시스템 로그 출력
                                if (anomaly == AnomalyStatus.DeltaTempWarning || anomaly == AnomalyStatus.AbsoluteTempWarning)
                                {
                                    OnLogMessage?.Invoke($"[이상징후 감지] Board:{packet.Id}, Bay:{det.Bay}, LV:{det.Level}, 상태:{anomaly}, 현재온도:{det.Temperature}℃, 평균온도:{rackAvgTemp:F1}℃", "Warning");
                                }
                            }

                            int globalRow = (det.Bay - 1) + bayOffset;
                            int col = (det.Level >= 13) ? det.Level - 1 : det.Level;

                            _opcServer.NodeManager.UpdateRackCell(lane, globalRow, col, finalSignal);
                        }

                        // ⭐ [수정] 화재 및 이상 징후 감지 건이 있을 경우 Dump 데이터 일괄 DB 저장
                        if (dumpList.Count > 0)
                        {
                            Task.Run(() => _repository.SaveDumpBatch(dumpList));
                        }
                    }
                    else
                    {
                        OnLogMessage?.Invoke("[WARN] OPC UA NodeManager가 아직 초기화되지 않았습니다.", "Warning");
                    }

                    OnPacketProcessedWithOffset?.Invoke(bayOffset, packet);
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

        // 4초 주기 Summary 데이터 집계 및 저장 로직
        private void ProcessSummaryLogs(object? state)
        {
            if (_summaryBuffer.IsEmpty) return;

            var snapshot = _summaryBuffer.ToList();
            _summaryBuffer.Clear();

            if (snapshot.Count == 0) return;

            var summaryList = snapshot
                .GroupBy(d => d.BoardId)
                .Select(g =>
                {
                    var firstData = g.First();
                    string rackId = $"BAY{firstData.Bay:D2}_LV{firstData.Level:D2}";

                    // 정상 작동 중인 유효 센서만 필터링 (에러 상태 제외 및 0℃ 초과 데이터)
                    var validDetectors = g.Where(d => d.Status < 3 && d.Temperature > 0).ToList();

                    // 만약 보드 내 모든 센서가 미연결/에러라면 0으로 처리, 유효 센서가 1개라도 있다면 유효 센서로만 집계
                    double avgTemp = validDetectors.Count > 0 ? validDetectors.Average(d => d.Temperature) : 0;
                    double maxTemp = validDetectors.Count > 0 ? validDetectors.Max(d => d.Temperature) : 0;
                    double minTemp = validDetectors.Count > 0 ? validDetectors.Min(d => d.Temperature) : 0;

                    double avgGas = validDetectors.Count > 0 ? validDetectors.Average(d => d.GasDensity) : 0;
                    double maxGas = validDetectors.Count > 0 ? validDetectors.Max(d => d.GasDensity) : 0;

                    return new CrpSummaryLog
                    {
                        Timestamp = DateTime.Now,
                        BoardId = g.Key,
                        RackId = rackId,
                        AvgTemperature = Math.Round(avgTemp, 2),
                        MaxTemperature = Math.Round(maxTemp, 2),
                        MinTemperature = Math.Round(minTemp, 2),
                        AvgGasLevel = Math.Round(avgGas, 2),
                        MaxGasLevel = Math.Round(maxGas, 2),
                        HasFireAlarm = g.Any(d => d.Status == 1 || d.Status == 2),
                        HasSensorError = g.Any(d => d.Status >= 3)
                    };
                })
                .ToList();

            if (summaryList.Count > 0)
            {
                Task.Run(() => _repository.SaveSummaryBatch(summaryList));
            }
        }

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
                    int bayOffset = i * 16;

                    if (_boardLastSeen.TryGetValue(boardId, out DateTime lastSeen))
                    {
                        if (now - lastSeen > _timeoutThreshold)
                        {
                            bool alreadyTimeout = _boardTimeoutState.TryGetValue(boardId, out bool isT) && isT;

                            if (!alreadyTimeout)
                            {
                                _boardTimeoutState[boardId] = true;

                                if (_opcServer.NodeManager != null)
                                {
                                    _opcServer.NodeManager.SetBoardCommunicationFault(lane.LaneNumber, bayOffset, 16);
                                }

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
                _watchdogTimer?.Dispose();
                _summaryTimer?.Dispose();

                _commService.OnPacketReceived -= HandlePacketReceived;
                _commService.OnConnectionChanged -= HandleConnectionChanged;
                _commService.OnLogMessage -= HandleLogMessage;

                _commService.Disconnect();
                _disposed = true;
            }
        }
    }
}