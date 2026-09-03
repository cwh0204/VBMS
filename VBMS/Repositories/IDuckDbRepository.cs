using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Repositories
{
    /// <summary>
    /// DB 로그 카운트 조회 결과 DTO (Dapper 매핑 보장용)
    /// </summary>
    public record LogCountsResult(long SummaryCount, long DumpCount, long EventCount);

    /// <summary>
    /// FDS/VBMS 감지기 데이터 처리를 위한 DuckDB 레포지토리 인터페이스
    /// </summary>
    public interface IDetectorRepository : IDisposable
    {
        /// <summary>
        /// DuckDB 테이블 및 인덱스 초기화
        /// </summary>
        void InitDatabase();

        #region [데이터 저장]

        /// <summary>
        /// 4초 간격 주기 요약 데이터 일괄 저장 (DuckDBAppender 고속 적재)
        /// </summary>
        void SaveSummaryBatch(IEnumerable<CrpSummaryLog> summaryLogs);

        /// <summary>
        /// 화재/이상징후 발생 시 링버퍼 정밀 Dump 데이터 일괄 저장 (DuckDBAppender 고속 적재)
        /// </summary>
        void SaveDumpBatch(IEnumerable<(DetectorData Data, DateTime ReceivedAt)> items);

        /// <summary>
        /// 감지기 텔레메트리 데이터 대량(Batch) 비동기 저장 (DuckDBAppender 고속 적재)
        /// </summary>
        Task InsertTelemetryBatchAsync(IEnumerable<DetectorData> telemetries);

        /// <summary>
        /// 시스템 이벤트/알람 이력 저장 (동기)
        /// </summary>
        void SaveEventLog(EventLogModel eventLog);

        /// <summary>
        /// 센서 상태 변화 및 시스템 이벤트 로그 비동기 저장
        /// </summary>
        Task SaveEventLogAsync(int row, int bay, int level, string content, DateTime now);

        #endregion

        #region [통계 및 시각화 조회 (UI 비동기 호출용)]

        /// <summary>
        /// 특정 기간 및 Rack의 4초 주기 요약 데이터 비동기 조회 (트렌드 차트/그래프용)
        /// </summary>
        Task<IEnumerable<CrpSummaryLog>> GetSummaryLogsAsync(string? rackId, DateTime startTime, DateTime endTime);

        /// <summary>
        /// 특정 기간 동안의 Rack별/감지기별 통계 비동기 집계
        /// </summary>
        Task<IEnumerable<DetectorStatistics>> GetStatisticsAsync(DateTime startTime, DateTime endTime, string? rackId = null);

        /// <summary>
        /// 특정 기간 내 발생한 이벤트/알람 이력 비동기 조회 (UI 리스트용)
        /// </summary>
        Task<IEnumerable<EventLogModel>> GetEventLogsAsync(DateTime startTime, DateTime endTime);

        /// <summary>
        /// 이상징후 정밀 Dump 데이터 비동기 조회 (UI 목록 및 포렌식 분석용)
        /// </summary>
        Task<IEnumerable<DetectorDumpLogModel>> GetDumpLogsAsync(DateTime startTime, DateTime endTime);

        #endregion

        #region [데이터 수명주기 관리 및 모니터링 (Archive & Purge)]

        /// <summary>
        /// 지정한 날짜(기본 30일) 이전의 crp_summary_logs 데이터를 ZSTD 압축 Parquet 파일로 아카이빙 후 DB에서 삭제
        /// </summary>
        void ArchiveAndPurgeOldData(DateTime archiveBeforeDate, string outputParquetPath);

        /// <summary>
        /// 지정한 날짜(기본 1년/365일) 이전의 detector_dump_logs 데이터를 ZSTD 압축 Parquet 파일로 아카이빙 후 DB에서 삭제
        /// </summary>
        void ArchiveAndPurgeDumpLogs(DateTime archiveBeforeDate, string outputParquetPath);

        /// <summary>
        /// 지정한 날짜(기본 1년/365일) 이전의 event_logs 데이터를 ZSTD 압축 Parquet 파일로 아카이빙 후 DB에서 삭제
        /// </summary>
        void ArchiveAndPurgeEventLogs(DateTime archiveBeforeDate, string outputParquetPath);

        /// <summary>
        /// 지정된 보존 기간(기본 3일)이 지난 detector_telemetry 대용량 원시 데이터를 DB에서 영구 삭제
        /// </summary>
        void PurgeOldTelemetry(int retentionDays = 3);

        /// <summary>
        /// 현재 DB에 저장된 총 로그 건수 조회 (요약 로그 / 정밀 Dump 로그 / 이벤트 로그)
        /// </summary>
        LogCountsResult GetLogCounts();

        #endregion
    }
}