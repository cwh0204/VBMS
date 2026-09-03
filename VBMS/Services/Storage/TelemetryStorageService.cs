using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
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

            // 1. Purge 태스크 시작
            var purgeTask = RunPeriodicPurgeAsync(stoppingToken);
            var batch = new List<DetectorData>(BatchSize);

            try
            {
                // ★ while문 전체를 try로 감싸 종료 시 WaitToReadAsync가 던지는 취소 예외를 안전하게 포획
                while (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    try
                    {
                        while (batch.Count < BatchSize && _channel.Reader.TryRead(out var item))
                        {
                            batch.Add(item);
                        }

                        if (batch.Count > 0)
                        {
                            await FlushBatchWithRetryAsync(batch);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "텔레메트리 데이터 수집 처리 중 예외 발생");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Telemetry Storage Service 종료 신호 수신.");
            }
            finally
            {
                // ★ 2. 앱 종료 시 백그라운드 Purge 루프가 완전히 끝날 때까지 대기 (DB 리소스 보호)
                try
                {
                    await purgeTask;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Purge 태스크 종료 대기 중 오류 발생");
                }

                // ★ 3. 취소 예외가 발생해도 finally에서 채널 잔여 데이터를 반드시 DB에 플러시
                await FlushRemainingDataAsync();
            }
        }

        private async Task FlushBatchWithRetryAsync(List<DetectorData> batch)
        {
            int retryCount = 0;
            const int maxRetries = 3;

            while (retryCount < maxRetries)
            {
                try
                {
                    await _repository.InsertTelemetryBatchAsync(batch);
                    batch.Clear();
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, "DuckDB 배치 저장 실패 ({Retry}/{MaxRetries}). 200ms 후 재시도합니다.", retryCount, maxRetries);

                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError("DuckDB 배치 저장 최종 실패. 데이터 유실 발생 ({Count}건)", batch.Count);
                        batch.Clear();
                    }
                    else
                    {
                        await Task.Delay(200);
                    }
                }
            }
        }

        private async Task RunPeriodicPurgeAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(12));

            try
            {
                // 서비스 시작 10초 후 첫 삭제 실행 (빠른 앱 종료 시 예외 방지를 위해 try 내부 배치)
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                await PurgeOldDataAsync();

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
                string archiveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Archive");
                Directory.CreateDirectory(archiveDir);

                // 1. 원시 텔레메트리 데이터 정리 (최근 3일 보존)
                _logger.LogInformation("3일 경과 원시 텔레메트리 데이터 삭제 진행...");
                await Task.Run(() => _repository.PurgeOldTelemetry(3));

                // ★ 파일명에 초 단위 타임스탬프를 부여하여 12시간 주기 실행 시 기존 파일 덮어쓰기(삭제) 방지
                string nowStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // 2. 주기 요약 통계 아카이빙 (30일 경과)
                DateTime summaryCutoff = DateTime.Now.AddDays(-30);
                string summaryParquet = Path.Combine(archiveDir, $"summary_{nowStamp}_cut_{summaryCutoff:yyyyMMdd}.parquet");
                await Task.Run(() => _repository.ArchiveAndPurgeOldData(summaryCutoff, summaryParquet));

                // 3. 이상징후 덤프 로그 아카이빙 (365일 경과)
                DateTime dumpCutoff = DateTime.Now.AddDays(-365);
                string dumpParquet = Path.Combine(archiveDir, $"dump_{nowStamp}_cut_{dumpCutoff:yyyyMMdd}.parquet");
                await Task.Run(() => _repository.ArchiveAndPurgeDumpLogs(dumpCutoff, dumpParquet));

                // 4. 시스템 이벤트 로그 아카이빙 (365일 경과)
                DateTime eventCutoff = DateTime.Now.AddDays(-365);
                string eventParquet = Path.Combine(archiveDir, $"event_{nowStamp}_cut_{eventCutoff:yyyyMMdd}.parquet");
                await Task.Run(() => _repository.ArchiveAndPurgeEventLogs(eventCutoff, eventParquet));

                _logger.LogInformation("데이터 보관 및 Purge 검사 완료.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "데이터 수명주기 유지보수 작업 중 오류 발생");
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