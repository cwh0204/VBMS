using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VBMS.Models;
using VBMS.Repositories;

namespace VBMS.Services.Storage
{
    public class TelemetryStorageService : ITelemetryStorageService
    {
        private readonly IDetectorRepository _repository;
        private readonly Channel<(DetectorData Data, DateTime ReceivedAt)> _channel;
        private CancellationTokenSource? _cts;
        private Task? _processingTask;

        public TelemetryStorageService(IDetectorRepository repository)
        {
            _repository = repository;

            // 비동기 큐 (단일 수신자, 다중 비동기 송신자)
            _channel = Channel.CreateUnbounded<(DetectorData, DateTime)>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });
        }

        public void Start()
        {
            if (_processingTask != null) return;

            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            _channel.Writer.Complete();

            if (_cts != null)
            {
                _cts.Cancel();
            }

            if (_processingTask != null)
            {
                await _processingTask;
            }
        }

        public void EnqueueData(IEnumerable<DetectorData> logs)
        {
            if (logs == null) return;

            DateTime receivedAt = DateTime.Now;

            foreach (var log in logs)
            {
                _channel.Writer.TryWrite((log, receivedAt));
            }
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            var buffer = new List<(DetectorData Data, DateTime ReceivedAt)>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 4초 주기로 큐의 데이터를 모아서 처리 (UI 차트/통계 최적화 주기)
                    await Task.Delay(4000, cancellationToken);

                    while (_channel.Reader.TryRead(out var item))
                    {
                        buffer.Add(item);
                    }

                    if (buffer.Count > 0)
                    {
                        // 1. 4초간 쌓인 감지기 데이터를 Rack별로 그룹화하여 4초 요약 로그(CrpSummaryLog) 생성
                        var summaryLogs = buffer
                            .GroupBy(x => new
                            {
                                RackId = $"BAY{x.Data.Bay:D2}_LV{x.Data.Level:D2}",
                                x.Data.BoardId
                            })
                            .Select(g => new CrpSummaryLog
                            {
                                Timestamp = DateTime.Now,
                                RackId = g.Key.RackId,
                                BoardId = g.Key.BoardId,
                                AvgTemperature = Math.Round(g.Average(x => x.Data.Temperature), 1),
                                MaxTemperature = g.Max(x => x.Data.Temperature),
                                MinTemperature = g.Min(x => x.Data.Temperature),
                                AvgGasLevel = Math.Round(g.Average(x => x.Data.GasDensity), 1),
                                MaxGasLevel = g.Max(x => x.Data.GasDensity),
                                HasFireAlarm = g.Any(x => x.Data.Status == 1 || x.Data.Status == 2),
                                HasSensorError = g.Any(x => x.Data.Status >= 3)
                            })
                            .ToList();

                        // 요약 데이터 저장 (통계/차트용)
                        _repository.SaveSummaryBatch(summaryLogs);

                        // 2. 이상징후(화재 경보 또는 센서 오류)가 포함된 원시 데이터는 Dump 테이블에 저장
                        var anomalyItems = buffer
                            .Where(x => x.Data.Status != 0)
                            .ToList();

                        if (anomalyItems.Count > 0)
                        {
                            _repository.SaveDumpBatch(anomalyItems);
                        }

                        // 버퍼 비우기
                        buffer.Clear();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TelemetryStorageService] 저장 오류: {ex.Message}");
                }
            }
        }
    }
}