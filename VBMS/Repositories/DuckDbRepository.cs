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

        private DuckDBConnection CreateConnection()
        {
            return new DuckDBConnection(_connectionString);
        }

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
                );");
                ExecuteQuery(conn, "CREATE INDEX IF NOT EXISTS idx_summary_time_rack ON crp_summary_logs (timestamp, rack_id);");

                ExecuteQuery(conn, @"
                CREATE TABLE IF NOT EXISTS detector_dump_logs (
                    received_at TIMESTAMP,
                    bay INTEGER,
                    level INTEGER,
                    row_num INTEGER,
                    temperature DOUBLE,
                    is_fire BOOLEAN,
                    is_error BOOLEAN
                );");
                ExecuteQuery(conn, "CREATE INDEX IF NOT EXISTS idx_dump_time ON detector_dump_logs (received_at);");

                ExecuteQuery(conn, @"
                CREATE TABLE IF NOT EXISTS event_logs (
                    row_num INTEGER,
                    bay_num INTEGER,
                    level_num INTEGER,
                    content VARCHAR,
                    log_time TIMESTAMP
                );");
                ExecuteQuery(conn, "CREATE INDEX IF NOT EXISTS idx_event_time ON event_logs (log_time);");

                ExecuteQuery(conn, @"
                CREATE TABLE IF NOT EXISTS detector_telemetry (
                    board_id INTEGER,
                    detector_index INTEGER,
                    bay INTEGER,
                    level INTEGER,
                    temperature DOUBLE,
                    gas_density DOUBLE,
                    status INTEGER,
                    created_at TIMESTAMP
                );");
                ExecuteQuery(conn, "CREATE INDEX IF NOT EXISTS idx_telemetry_created ON detector_telemetry (created_at);");
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

        #region [데이터 저장 구현]

        public void SaveSummaryBatch(IEnumerable<CrpSummaryLog> summaryLogs)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                string sql = @"
                    INSERT INTO crp_summary_logs (timestamp, rack_id, board_id, avg_temp, max_temp, min_temp, has_fire_alarm, has_sensor_error)
                    VALUES ($Timestamp, $RackId, $BoardId, $AvgTemperature, $MaxTemperature, $MinTemperature, $HasFireAlarm, $HasSensorError);";
                conn.Execute(sql, summaryLogs);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public void SaveDumpBatch(IEnumerable<(DetectorData Data, DateTime ReceivedAt)> items)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                var paramsList = items.Select(x => new
                {
                    ReceivedAt = x.ReceivedAt,
                    Bay = x.Data.Bay,
                    Level = x.Data.Level,
                    Row = x.Data.BoardId,
                    Temperature = x.Data.Temperature,
                    IsFire = (x.Data.Status & 0x01) != 0,
                    IsError = (x.Data.Status & 0x02) != 0
                });

                string sql = @"
                    INSERT INTO detector_dump_logs (received_at, bay, level, row_num, temperature, is_fire, is_error)
                    VALUES ($ReceivedAt, $Bay, $Level, $Row, $Temperature, $IsFire, $IsError);";
                conn.Execute(sql, paramsList);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task InsertTelemetryBatchAsync(IEnumerable<DetectorData> telemetries)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var conn = CreateConnection();
                await conn.OpenAsync();

                // DetectorData에 없는 항목은 기본값(0 및 현재시간)으로 대체하여 8개 매개변수 충족
                var paramList = telemetries.Select(x => new
                {
                    BoardId = x.BoardId,
                    DetectorIndex = 0,               // DetectorData에 속성이 없으므로 기본값 0 지정
                    Bay = x.Bay,
                    Level = x.Level,
                    Temperature = x.Temperature,
                    GasDensity = x.GasDensity,
                    Status = x.Status,
                    CreatedAt = DateTime.Now         // 현재 시각 저장
                });

                string sql = @"
            INSERT INTO detector_telemetry (board_id, detector_index, bay, level, temperature, gas_density, status, created_at)
            VALUES ($BoardId, $DetectorIndex, $Bay, $Level, $Temperature, $GasDensity, $Status, $CreatedAt);";

                await conn.ExecuteAsync(sql, paramList);
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
                string sql = @"
                    INSERT INTO event_logs (row_num, bay_num, level_num, content, log_time)
                    VALUES ($Row, $Bay, $Level, $Content, $DateTime);";
                conn.Execute(sql, eventLog);
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
                string sql = @"
                    INSERT INTO event_logs (row_num, bay_num, level_num, content, log_time)
                    VALUES ($Row, $Bay, $Level, $Content, $LogTime);";
                await conn.ExecuteAsync(sql, new { Row = row, Bay = bay, Level = level, Content = content, LogTime = now });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        #endregion

        #region [통계 및 시각화 조회 구현]

        public IEnumerable<CrpSummaryLog> GetSummaryLogs(string? rackId, DateTime startTime, DateTime endTime)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                string sql = @"
                    SELECT timestamp AS Timestamp, rack_id AS RackId, board_id AS BoardId,
                           avg_temp AS AvgTemperature, max_temp AS MaxTemperature, min_temp AS MinTemperature,
                           has_fire_alarm AS HasFireAlarm, has_sensor_error AS HasSensorError
                    FROM crp_summary_logs
                    WHERE timestamp BETWEEN $StartTime AND $EndTime
                      AND ($RackId IS NULL OR rack_id = $RackId)
                    ORDER BY timestamp ASC";
                return conn.Query<CrpSummaryLog>(sql, new { StartTime = startTime, EndTime = endTime, RackId = rackId });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public IEnumerable<DetectorStatistics> GetStatistics(DateTime startTime, DateTime endTime, string? rackId = null)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                string sql = @"
                    SELECT 
                        rack_id AS RackId,
                        AVG(avg_temp) AS AvgTemperature,
                        MAX(max_temp) AS MaxTemperature,
                        MIN(min_temp) AS MinTemperature,
                        COUNT(CASE WHEN has_fire_alarm = true THEN 1 END) AS FireAlarmCount,
                        COUNT(CASE WHEN has_sensor_error = true THEN 1 END) AS SensorErrorCount
                    FROM crp_summary_logs
                    WHERE timestamp BETWEEN $StartTime AND $EndTime
                      AND ($RackId IS NULL OR rack_id = $RackId)
                    GROUP BY rack_id";

                return conn.Query<DetectorStatistics>(sql, new { StartTime = startTime, EndTime = endTime, RackId = rackId });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public IEnumerable<EventLogModel> GetEventLogs(DateTime startTime, DateTime endTime)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                string sql = @"
                    SELECT row_num AS Row, bay_num AS Bay, level_num AS Level, content AS Content, log_time AS DateTime
                    FROM event_logs
                    WHERE log_time BETWEEN $StartTime AND $EndTime
                    ORDER BY log_time DESC";

                return conn.Query<EventLogModel>(sql, new { StartTime = startTime, EndTime = endTime });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public IEnumerable<DetectorDumpLogModel> GetDumpLogs(DateTime startTime, DateTime endTime, string? rackId = null)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                string sql = @"
                    SELECT received_at AS ReceivedAt, bay AS Bay, level AS Level, row_num AS Row,
                           temperature AS Temperature, is_fire AS IsFire, is_error AS IsError
                    FROM detector_dump_logs
                    WHERE received_at BETWEEN $StartTime AND $EndTime
                    ORDER BY received_at DESC";
                return conn.Query<DetectorDumpLogModel>(sql, new { StartTime = startTime, EndTime = endTime });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        #endregion

        #region [데이터 관리 및 모니터링 구현]

        public void ArchiveAndPurgeOldData(DateTime archiveBeforeDate, string outputParquetPath)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();

                string safePath = outputParquetPath.Replace("\\", "/").Replace("'", "''");
                string formattedDate = archiveBeforeDate.ToString("yyyy-MM-dd HH:mm:ss");

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"COPY (SELECT * FROM crp_summary_logs WHERE timestamp < '{formattedDate}') TO '{safePath}' (FORMAT PARQUET, COMPRESSION ZSTD);";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM crp_summary_logs WHERE timestamp < $1;";
                    cmd.Parameters.Add(new DuckDBParameter(archiveBeforeDate));
                    cmd.ExecuteNonQuery();
                }
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public void PurgeOldTelemetry(int retentionDays = 7)
        {
            _dbSemaphore.Wait();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
                string sql = "DELETE FROM detector_telemetry WHERE created_at < $Cutoff;";
                conn.Execute(sql, new { Cutoff = cutoff });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public (long SummaryCount, long DumpCount, long EventCount) GetLogCounts()
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

                return conn.QueryFirstOrDefault<(long SummaryCount, long DumpCount, long EventCount)>(sql);
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