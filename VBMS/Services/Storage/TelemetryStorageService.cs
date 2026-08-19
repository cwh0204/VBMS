using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VBMS.Models;
using VBMS.Repositories;

namespace VBMS.Services.Storage
{
    public class TelemetryStorageService : BackgroundService, ITelemetryStorageService
    {
        private readonly IDetectorRepository _repository;
        private readonly ILogger<TelemetryStorageService> _logger;

        // SingleReader = true로 백그라운드 스레드 단일 처리 보장
        private readonly Channel<DetectorData> _channel = Channel.CreateUnbounded<DetectorData>(
            new UnboundedChannelOptions { SingleReader = true });

        private const int BatchSize = 100;

        public TelemetryStorageService(IDetectorRepository repository, ILogger<TelemetryStorageService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool Enqueue(DetectorData data)
        {
            return _channel.Writer.TryWrite(data);
        }

        public bool EnqueueRange(IEnumerable<DetectorData> dataList)
        {
            bool allSuccess = true;
            foreach (var item in dataList)
            {
                if (!_channel.Writer.TryWrite(item))
                {
                    allSuccess = false;
                }
            }
            return allSuccess;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Telemetry Storage Service 가동 완료.");

            // 7일 경과 데이터 자동 삭제 백그라운드 루프
            var purgeTask = RunPeriodicPurgeAsync(stoppingToken);

            var batch = new List<DetectorData>(BatchSize);

            // 🌟 예외 발생 없는 표준 Channel 수신 루프
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                try
                {
                    // 큐에 들어와 있는 데이터를 대기 없이 최대 BatchSize만큼 즉시 꺼냄
                    while (batch.Count < BatchSize && _channel.Reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }

                    // 꺼낸 데이터가 있다면 즉시 DB에 저장
                    if (batch.Count > 0)
                    {
                        await FlushBatchWithRetryAsync(batch);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "텔레메트리 데이터 수집 처리 중 예외 발생");
                }
            }

            // 앱 종료 시 남아있는 메모리 데이터 최종 저장
            await FlushRemainingDataAsync();
        }

        // 🌟 [개선] DB Lock 등 일시적 실패 발생 시 최대 3회 재시도하여 데이터 유실 방지
        private async Task FlushBatchWithRetryAsync(List<DetectorData> batch)
        {
            int retryCount = 0;
            const int maxRetries = 3;

            while (retryCount < maxRetries)
            {
                try
                {
                    await _repository.InsertTelemetryBatchAsync(batch);
                    batch.Clear(); // 🌟 DB 저장 성공시에만 배치를 비움!
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, "DuckDB 배치 저장 실패 ({Retry}/{MaxRetries}). 200ms 후 재시도합니다.", retryCount, maxRetries);

                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError("DuckDB 배치 저장 최종 실패. 데이터 유실 발생 ({Count}건)", batch.Count);
                        batch.Clear(); // 최대 재시도 실패 시에만 다음 수신을 위해 비움
                    }
                    else
                    {
                        await Task.Delay(200); // DB 락 해제 대기
                    }
                }
            }
        }

        private async Task RunPeriodicPurgeAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(12));

            // 서비스 시작 10초 후 첫 삭제 실행 (초기 구동 시 DB Insert와의 충돌 방지)
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            await PurgeOldDataAsync();

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await PurgeOldDataAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 종료
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "텔레메트리 Purge 스케줄러 실행 중 예외 발생");
            }
        }

        private async Task PurgeOldDataAsync()
        {
            try
            {
                _logger.LogInformation("7일 경과 원시 텔레메트리 데이터 정리를 시작합니다.");
                await Task.Run(() => _repository.PurgeOldTelemetry(7));
                _logger.LogInformation("7일 경과 원시 텔레메트리 데이터 정리 완료.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "7일 경과 텔레메트리 데이터 Purge 실패");
            }
        }

        private async Task FlushRemainingDataAsync()
        {
            _logger.LogInformation("남아있는 텔레메트리 데이터 Flush 시작...");
            var remainingBatch = new List<DetectorData>();

            while (_channel.Reader.TryRead(out var item))
            {
                remainingBatch.Add(item);
            }

            if (remainingBatch.Count > 0)
            {
                await FlushBatchWithRetryAsync(remainingBatch);
                _logger.LogInformation("미저장 데이터 {Count}건 정리 완료.", remainingBatch.Count);
            }
        }
    }
}