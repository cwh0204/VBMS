using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VBMS.Enums;
using VBMS.Models;
using VBMS.Repositories;
using VBMS.Services.Communications;
using VBMS.Services.Evaluators;
using VBMS.Services.OpcUa;
using VBMS.Services.Storage;

namespace VBMS.Services.Orchestrators
{
    public class FdsDataOrchestrator : IFdsDataOrchestrator, IDisposable
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

        private readonly ConcurrentQueue<DetectorData> _summaryBuffer = new();
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

        private async void HandlePacketReceived(CrpPacket? packet)
        {
            try
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
                            _summaryBuffer.Enqueue(det);
                        }

                        // 2. 원시 텔레메트리 데이터 DB 수집
                        _telemetryStorageService.EnqueueRange(packet.Detectors);

                        // 3. OPC UA 서버 업데이트 및 이상징후/화재 검증
                        if (_opcServer.NodeManager != null)
                        {
                            // ⭐ Line과 GlobalBay를 명확히 포함하는 튜플 리스트로 전환
                            var dumpList = new List<(int Line, int Bay, DetectorData Data, DateTime ReceivedAt)>();
                            DateTime now = DateTime.Now;

                            var validDetectors = packet.Detectors.Where(d => d.Status < 3 && d.Temperature > 0).ToList();
                            double rackAvgTemp = validDetectors.Count > 0 ? validDetectors.Average(d => d.Temperature) : 0;

                            foreach (var det in packet.Detectors)
                            {
                                // ⭐ 전체 랙 기준 연 번호 계산 (예: 2번 보드의 16연 -> 16 + 16 = 32연)
                                int globalBay = det.Bay + bayOffset;

                                AnomalyStatus anomaly = _signalEvaluator.EvaluateAnomaly(det.Status, det.Temperature, rackAvgTemp);
                                uint rawSignal = _signalEvaluator.Evaluate(det.Status, det.Temperature);
                                string detectorKey = $"Lane_{lane}_Bay_{globalBay}_Level_{det.Level}";

                                uint finalSignal = await _verificationService.VerifySignalAsync(
                                    detectorKey,
                                    rawSignal,
                                    packet.Id,
                                    det.Bay,
                                    det.Level
                                );

                                if (finalSignal > 0 || anomaly == AnomalyStatus.DeltaTempWarning || anomaly == AnomalyStatus.AbsoluteTempWarning)
                                {
                                    // ⭐ [Line(lane), 전체 연(globalBay), 센서데이터, 수신시각] 추가
                                    dumpList.Add((lane, globalBay, det, now));

                                    if (anomaly == AnomalyStatus.DeltaTempWarning || anomaly == AnomalyStatus.AbsoluteTempWarning)
                                    {
                                        string logMsg = $"[이상징후 감지] Line:{lane}, Bay:{globalBay}, LV:{det.Level}, 상태:{anomaly}, 현재온도:{det.Temperature}℃, 평균온도:{rackAvgTemp:F1}℃";
                                        OnLogMessage?.Invoke(logMsg, "Warning");

                                        // ⭐ 올바른 좌표 순서: Line(lane), Bay(globalBay), Level(det.Level)
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                await _repository.SaveEventLogAsync(lane, globalBay, det.Level, logMsg, now);
                                            }
                                            catch (Exception ex)
                                            {
                                                OnLogMessage?.Invoke($"[ERROR] 이벤트 로그 DB 저장 실패: {ex.Message}", "Error");
                                            }
                                        });
                                    }
                                }

                                int cellRow = globalBay - 1;
                                int col = (det.Level >= 13) ? det.Level - 1 : det.Level;

                                _opcServer.NodeManager.UpdateRackCell(lane, cellRow, col, finalSignal);
                            }

                            if (dumpList.Count > 0)
                            {
                                _ = Task.Run(() =>
                                {
                                    try
                                    {
                                        // ⭐ Line과 GlobalBay가 담긴 dumpList 저장
                                        _repository.SaveDumpBatch(dumpList);
                                    }
                                    catch (Exception ex)
                                    {
                                        OnLogMessage?.Invoke($"[ERROR] DumpBatch DB 저장 실패: {ex.Message}", "Error");
                                    }
                                });
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
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[CRITICAL] 패킷 처리 중 예외 발생 (Board ID: {packet?.Id}): {ex.Message}", "Error");
            }
        }

        private void ProcessSummaryLogs(object? state)
        {
            if (_summaryBuffer.IsEmpty) return;

            var snapshot = new List<DetectorData>();
            while (_summaryBuffer.TryDequeue(out var item))
            {
                snapshot.Add(item);
            }

            if (snapshot.Count == 0) return;

            var summaryList = snapshot
                .GroupBy(d => d.BoardId)
                .Select(g =>
                {
                    string rackId = $"BOARD_{g.Key:D2}";
                    var validDetectors = g.Where(d => d.Status < 3 && d.Temperature > 0).ToList();

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
                _ = Task.Run(() =>
                {
                    try
                    {
                        _repository.SaveSummaryBatch(summaryList);
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage?.Invoke($"[ERROR] SummaryBatch DB 저장 실패: {ex.Message}", "Error");
                    }
                });
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

                                string errorMsg = $"[ERROR] CRP 보드 통신 두절 감지: Board ID = {boardId} (Line {lane.LaneNumber}, 연 오프셋 {bayOffset})";
                                OnLogMessage?.Invoke(errorMsg, "Error");

                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        // ⭐ 보드 ID 대신 실제 Line 번호(lane.LaneNumber)와 시작 연(bayOffset + 1)을 저장
                                        await _repository.SaveEventLogAsync(lane.LaneNumber, bayOffset + 1, 0, errorMsg, now);
                                    }
                                    catch (Exception ex)
                                    {
                                        OnLogMessage?.Invoke($"[ERROR] 통신두절 이벤트 DB 저장 실패: {ex.Message}", "Error");
                                    }
                                });
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
                _watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _summaryTimer?.Change(Timeout.Infinite, Timeout.Infinite);

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