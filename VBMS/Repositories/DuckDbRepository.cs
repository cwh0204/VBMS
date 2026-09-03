using Dapper;
using DuckDB.NET.Data;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Repositories
{
    public class DuckDbRepository : IDetectorRepository, IDisposable
    {
        private readonly string _connectionString;
        private static readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public DuckDbRepository(IConfiguration configuration)
        {
            string rawDbPath = configuration["FdsConfig:DbPath"] ?? "Data/fds_data.duckdb";
            string dbPath = Path.GetFullPath(rawDbPath);

            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={dbPath}";
            InitDatabase();
        }

        private DuckDBConnection CreateConnection() => new DuckDBConnection(_connectionString);

        public void InitDatabase()
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                ExecuteQuery(conn, @"
                CREATE TABLE IF NOT EXISTS crp_summary_logs (
                    timestamp TIMESTAMP,
                    rack_id VARCHAR,
                    board_id INTEGER,
                    avg_temp DOUBLE,
                    max_temp DOUBLE,
                    min_temp DOUBLE,
                    has_fire_alarm BOOLEAN,
                    has_sensor_error BOOLEAN
                );
                CREATE INDEX IF NOT EXISTS idx_summary_time_rack ON crp_summary_logs (timestamp, rack_id);

                CREATE TABLE IF NOT EXISTS detector_dump_logs (
                    received_at TIMESTAMP,
                    bay INTEGER,
                    level INTEGER,
                    row_num INTEGER,
                    temperature DOUBLE,
                    is_fire BOOLEAN,
                    is_error BOOLEAN
                );
                CREATE INDEX IF NOT EXISTS idx_dump_time ON detector_dump_logs (received_at);

                CREATE TABLE IF NOT EXISTS event_logs (
                    row_num INTEGER,
                    bay_num INTEGER,
                    level_num INTEGER,
                    content VARCHAR,
                    log_time TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_event_time ON event_logs (log_time);

                CREATE TABLE IF NOT EXISTS detector_telemetry (
                    board_id INTEGER,
                    detector_index INTEGER,
                    bay INTEGER,
                    level INTEGER,
                    temperature DOUBLE,
                    gas_density DOUBLE,
                    status INTEGER,
                    created_at TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_telemetry_created ON detector_telemetry (created_at);");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private static void ExecuteQuery(DuckDBConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        #region [고속 벌크 배치 저장 구현 (DuckDBAppender & 파라미터 안전 처리)]

        public void SaveSummaryBatch(IEnumerable<CrpSummaryLog> summaryLogs)
        {
            var list = summaryLogs as IList<CrpSummaryLog> ?? summaryLogs.ToList();
            if (list.Count == 0) return;

            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                using var appender = conn.CreateAppender("crp_summary_logs");
                foreach (var log in list)
                {
                    var row = appender.CreateRow();
                    row.AppendValue(log.Timestamp);
                    row.AppendValue(log.RackId);
                    row.AppendValue(log.BoardId);
                    row.AppendValue(log.AvgTemperature);
                    row.AppendValue(log.MaxTemperature);
                    row.AppendValue(log.MinTemperature);
                    row.AppendValue(log.HasFireAlarm);
                    row.AppendValue(log.HasSensorError);
                    row.EndRow();
                }
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public void SaveDumpBatch(IEnumerable<(DetectorData Data, DateTime ReceivedAt)> items)
        {
            var itemList = items as IList<(DetectorData Data, DateTime ReceivedAt)> ?? items.ToList();
            if (itemList.Count == 0) return;

            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                using var appender = conn.CreateAppender("detector_dump_logs");
                foreach (var item in itemList)
                {
                    var row = appender.CreateRow();
                    row.AppendValue(item.ReceivedAt);
                    row.AppendValue(item.Data.Bay);
                    row.AppendValue(item.Data.Level);
                    row.AppendValue(item.Data.BoardId);
                    row.AppendValue(item.Data.Temperature);
                    row.AppendValue((item.Data.Status & 0x01) != 0); // is_fire
                    row.AppendValue((item.Data.Status & 0x02) != 0); // is_error
                    row.EndRow();
                }
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task InsertTelemetryBatchAsync(IEnumerable<DetectorData> telemetries)
        {
            var list = telemetries as IList<DetectorData> ?? telemetries.ToList();
            if (list.Count == 0) return;

            await _dbSemaphore.WaitAsync();
            try
            {
                using var conn = CreateConnection();
                await conn.OpenAsync();

                DateTime now = DateTime.UtcNow;

                using var appender = conn.CreateAppender("detector_telemetry");
                foreach (var t in list)
                {
                    var row = appender.CreateRow();
                    row.AppendValue(t.BoardId);
                    row.AppendValue(0); // detector_index
                    row.AppendValue(t.Bay);
                    row.AppendValue(t.Level);
                    row.AppendValue(t.Temperature);
                    row.AppendValue(t.GasDensity);
                    row.AppendValue(t.Status);
                    row.AppendValue(now);
                    row.EndRow();
                }
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public void SaveEventLog(EventLogModel eventLog)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                // DuckDB 바인더 에러(@) 방지를 위해 위치 파라미터($1~$5) 사용
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO event_logs (row_num, bay_num, level_num, content, log_time)
                    VALUES ($1, $2, $3, $4, $5);";
                cmd.Parameters.Add(new DuckDBParameter(eventLog.Row));
                cmd.Parameters.Add(new DuckDBParameter(eventLog.Bay));
                cmd.Parameters.Add(new DuckDBParameter(eventLog.Level));
                cmd.Parameters.Add(new DuckDBParameter(eventLog.Content));
                cmd.Parameters.Add(new DuckDBParameter(eventLog.DateTime));
                cmd.ExecuteNonQuery();
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task SaveEventLogAsync(int row, int bay, int level, string content, DateTime now)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var conn = CreateConnection();
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO event_logs (row_num, bay_num, level_num, content, log_time)
                    VALUES ($1, $2, $3, $4, $5);";
                cmd.Parameters.Add(new DuckDBParameter(row));
                cmd.Parameters.Add(new DuckDBParameter(bay));
                cmd.Parameters.Add(new DuckDBParameter(level));
                cmd.Parameters.Add(new DuckDBParameter(content));
                cmd.Parameters.Add(new DuckDBParameter(now));
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        #endregion

        #region [통계 및 시각화 조회 구현 (바인더 에러 차단 & 리터럴 주입)]

        public async Task<IEnumerable<CrpSummaryLog>> GetSummaryLogsAsync(string? rackId, DateTime startTime, DateTime endTime)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var conn = CreateConnection();
                await conn.OpenAsync();

                string sTime = startTime.ToString("yyyy-MM-dd HH:mm:ss");
                string eTime = endTime.ToString("yyyy-MM-dd HH:mm:ss");

                string rackCondition = string.IsNullOrEmpty(rackId)
                    ? string.Empty
                    : $"AND rack_id = '{rackId.Replace("'", "''")}'";

                string sql = $@"
                    SELECT timestamp AS Timestamp, rack_id AS RackId, board_id AS BoardId,
                           avg_temp AS AvgTemperature, max_temp AS MaxTemperature, min_temp AS MinTemperature,
                           has_fire_alarm AS HasFireAlarm, has_sensor_error AS HasSensorError
                    FROM crp_summary_logs
                    WHERE timestamp BETWEEN '{sTime}' AND '{eTime}'
                      {rackCondition}
                    ORDER BY timestamp ASC";

                return await conn.QueryAsync<CrpSummaryLog>(sql);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task<IEnumerable<DetectorStatistics>> GetStatisticsAsync(DateTime startTime, DateTime endTime, string? rackId = null)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var conn = CreateConnection();
                await conn.OpenAsync();

                string sTime = startTime.ToString("yyyy-MM-dd HH:mm:ss");
                string eTime = endTime.ToString("yyyy-MM-dd HH:mm:ss");

                string rackCondition = string.IsNullOrEmpty(rackId)
                    ? string.Empty
                    : $"AND rack_id = '{rackId.Replace("'", "''")}'";

                string sql = $@"
                    SELECT 
                        rack_id AS RackId,
                        AVG(avg_temp) AS AvgTemperature,
                        MAX(max_temp) AS MaxTemperature,
                        MIN(min_temp) AS MinTemperature,
                        COUNT(CASE WHEN has_fire_alarm = true THEN 1 END) AS FireAlarmCount,
                        COUNT(CASE WHEN has_sensor_error = true THEN 1 END) AS SensorErrorCount
                    FROM crp_summary_logs
                    WHERE timestamp BETWEEN '{sTime}' AND '{eTime}'
                      {rackCondition}
                    GROUP BY rack_id";

                return await conn.QueryAsync<DetectorStatistics>(sql);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task<IEnumerable<EventLogModel>> GetEventLogsAsync(DateTime startTime, DateTime endTime)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var conn = CreateConnection();
                await conn.OpenAsync();

                string sTime = startTime.ToString("yyyy-MM-dd HH:mm:ss");
                string eTime = endTime.ToString("yyyy-MM-dd HH:mm:ss");

                string sql = $@"
                    SELECT row_num AS Row, bay_num AS Bay, level_num AS Level, content AS Content, log_time AS DateTime
                    FROM event_logs
                    WHERE log_time BETWEEN '{sTime}' AND '{eTime}'
                    ORDER BY log_time DESC";

                return await conn.QueryAsync<EventLogModel>(sql);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task<IEnumerable<DetectorDumpLogModel>> GetDumpLogsAsync(DateTime startTime, DateTime endTime)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var conn = CreateConnection();
                await conn.OpenAsync();

                string sTime = startTime.ToString("yyyy-MM-dd HH:mm:ss");
                string eTime = endTime.ToString("yyyy-MM-dd HH:mm:ss");

                string sql = $@"
                    SELECT received_at AS ReceivedAt, bay AS Bay, level AS Level, row_num AS Row,
                           temperature AS Temperature, is_fire AS IsFire, is_error AS IsError
                    FROM detector_dump_logs
                    WHERE received_at BETWEEN '{sTime}' AND '{eTime}'
                    ORDER BY received_at DESC";

                return await conn.QueryAsync<DetectorDumpLogModel>(sql);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        #endregion

        #region [데이터 수명주기 관리 및 모니터링 (안전성 보강)]

        public void ArchiveAndPurgeOldData(DateTime archiveBeforeDate, string outputParquetPath)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                string formattedDate = archiveBeforeDate.ToString("yyyy-MM-dd HH:mm:ss");

                // 1. 아카이빙 대상 건수 사전 확인 (0건이면 파일 생성 및 트랜잭션 건너뜀)
                using var countCmd = conn.CreateCommand();
                countCmd.CommandText = $"SELECT COUNT(*) FROM crp_summary_logs WHERE timestamp < '{formattedDate}';";
                long count = (long)(countCmd.ExecuteScalar() ?? 0L);
                if (count == 0) return;

                // 2. 파일 중복 방지 (기존 동일 파일 존재 시 삭제 후 재생성)
                if (File.Exists(outputParquetPath))
                {
                    File.Delete(outputParquetPath);
                }

                string safePath = outputParquetPath.Replace("\\", "/").Replace("'", "''");

                using var tx = conn.BeginTransaction();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"COPY (SELECT * FROM crp_summary_logs WHERE timestamp < '{formattedDate}') TO '{safePath}' (FORMAT PARQUET, COMPRESSION ZSTD);";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"DELETE FROM crp_summary_logs WHERE timestamp < '{formattedDate}';";
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public void ArchiveAndPurgeDumpLogs(DateTime archiveBeforeDate, string outputParquetPath)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                string formattedDate = archiveBeforeDate.ToString("yyyy-MM-dd HH:mm:ss");

                using var countCmd = conn.CreateCommand();
                countCmd.CommandText = $"SELECT COUNT(*) FROM detector_dump_logs WHERE received_at < '{formattedDate}';";
                long count = (long)(countCmd.ExecuteScalar() ?? 0L);
                if (count == 0) return;

                if (File.Exists(outputParquetPath))
                {
                    File.Delete(outputParquetPath);
                }

                string safePath = outputParquetPath.Replace("\\", "/").Replace("'", "''");

                using var tx = conn.BeginTransaction();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"COPY (SELECT * FROM detector_dump_logs WHERE received_at < '{formattedDate}') TO '{safePath}' (FORMAT PARQUET, COMPRESSION ZSTD);";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"DELETE FROM detector_dump_logs WHERE received_at < '{formattedDate}';";
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public void ArchiveAndPurgeEventLogs(DateTime archiveBeforeDate, string outputParquetPath)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                string formattedDate = archiveBeforeDate.ToString("yyyy-MM-dd HH:mm:ss");

                using var countCmd = conn.CreateCommand();
                countCmd.CommandText = $"SELECT COUNT(*) FROM event_logs WHERE log_time < '{formattedDate}';";
                long count = (long)(countCmd.ExecuteScalar() ?? 0L);
                if (count == 0) return;

                if (File.Exists(outputParquetPath))
                {
                    File.Delete(outputParquetPath);
                }

                string safePath = outputParquetPath.Replace("\\", "/").Replace("'", "''");

                using var tx = conn.BeginTransaction();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"COPY (SELECT * FROM event_logs WHERE log_time < '{formattedDate}') TO '{safePath}' (FORMAT PARQUET, COMPRESSION ZSTD);";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"DELETE FROM event_logs WHERE log_time < '{formattedDate}';";
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public void PurgeOldTelemetry(int retentionDays = 3)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM detector_telemetry WHERE created_at < $1;";
                cmd.Parameters.Add(new DuckDBParameter(cutoff));
                cmd.ExecuteNonQuery();
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public LogCountsResult GetLogCounts()
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                string sql = @"
                    SELECT 
                        (SELECT COUNT(*) FROM crp_summary_logs) AS SummaryCount,
                        (SELECT COUNT(*) FROM detector_dump_logs) AS DumpCount,
                        (SELECT COUNT(*) FROM event_logs) AS EventCount";

                return conn.QueryFirstOrDefault<LogCountsResult>(sql) ?? new LogCountsResult(0, 0, 0);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}