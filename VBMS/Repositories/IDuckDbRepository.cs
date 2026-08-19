using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Repositories
{
    public interface IDetectorRepository
    {
        /// <summary>
        /// DuckDB 테이블, 인덱스 및 통계용 뷰 초기화
        /// </summary>
        void InitDatabase();

        #region [데이터 저장]

        /// <summary>
        /// 4초 간격 주기 요약 데이터 일괄 저장 (시계열 차트/통계용)
        /// </summary>
        void SaveSummaryBatch(IEnumerable<CrpSummaryLog> summaryLogs);

        /// <summary>
        /// 화재/이상징후 발생 시 링버퍼 정밀 Dump 데이터 일괄 저장
        /// </summary>
        void SaveDumpBatch(IEnumerable<(DetectorData Data, DateTime ReceivedAt)> items);

        /// <summary>
        /// 시스템 이벤트/알람 이력 저장 (기존 EventLogModel 활용)
        /// </summary>
        /// <param name="eventLog">이벤트 로그 모델</param>
        void SaveEventLog(EventLogModel eventLog);

        #endregion

        #region [통계 및 시각화 조회 (UI용)]

        /// <summary>
        /// 특정 기간 및 Rack의 4초 주기 요약 데이터 조회 (트렌드 차트/그래프용)
        /// </summary>
        /// <param name="rackId">Rack ID (null인 경우 전체 Rack)</param>
        /// <param name="startTime">조회 시작 시각</param>
        /// <param name="endTime">조회 종료 시각</param>
        IEnumerable<CrpSummaryLog> GetSummaryLogs(string? rackId, DateTime startTime, DateTime endTime);

        /// <summary>
        /// 특정 기간 동안의 Rack별/감지기별 통계 집계 (최고/최저/평균 온도, 최대 가스 농도 등)
        /// </summary>
        /// <param name="startTime">집계 시작 시각</param>
        /// <param name="endTime">집계 종료 시각</param>
        /// <param name="rackId">특정 Rack 지정 (선택)</param>
        IEnumerable<DetectorStatistics> GetStatistics(DateTime startTime, DateTime endTime, string? rackId = null);

        /// <summary>
        /// 특정 기간 내 발생한 이벤트/알람 이력 조회 (UI 리스트용)
        /// </summary>
        /// <param name="startTime">조회 시작 시각</param>
        /// <param name="endTime">조회 종료 시각</param>
        IEnumerable<EventLogModel> GetEventLogs(DateTime startTime, DateTime endTime);

        #endregion

        #region [데이터 관리 및 모니터링]

        /// <summary>
        /// 지정한 날짜 이전의 과거 데이터를 ZSTD 압축 Parquet 파일로 아카이빙 후 DB에서 삭제
        /// </summary>
        /// <param name="archiveBeforeDate">기준 날짜 (이 날짜 이전 데이터 아카이빙)</param>
        /// <param name="outputParquetPath">저장할 Parquet 파일 경로</param>
        void ArchiveAndPurgeOldData(DateTime archiveBeforeDate, string outputParquetPath);

        /// <summary>
        /// 지정된 보존 기간(기본 7일)이 지난 detector_telemetry 원시 데이터를 삭제합니다.
        /// </summary>
        /// <param name="retentionDays">보존 일수 (기본값: 7일)</param>
        void PurgeOldTelemetry(int retentionDays = 7);

        /// <summary>
        /// 현재 DB에 저장된 총 로그 건수 조회 (요약 로그 / 원시 Dump 로그 / 이벤트 로그)
        /// </summary>
        (long SummaryCount, long DumpCount, long EventCount) GetLogCounts();

        /// <summary>
        /// 감지기 텔레메트리 데이터 대량(Batch) 비동기 저장
        /// </summary>
        Task InsertTelemetryBatchAsync(IEnumerable<DetectorData> telemetries);

        /// <summary>
        /// 센서 상태 변화 및 시스템 이벤트 로그를 DB에 저장합니다.
        /// </summary>
        Task SaveEventLogAsync(int row, int bay, int level, string content, DateTime createdAt);
        #endregion
    }
}