using DuckDB.NET.Data;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using VBMS.Models;

namespace VBMS.Repositories
{
    public class DuckDbRepository : IDetectorRepository
    {
        private readonly string _connectionString;
        private readonly object _dbLock = new object();

        public DuckDbRepository(IConfiguration configuration)
        {
            // appsettings.json의 DbPath 설정 읽기 (기본값: Data/fds_data.duckdb)
            string dbPath = configuration["FdsConfig:DbPath"] ?? "Data/fds_data.duckdb";

            // 데이터베이스 저장 폴더 자동 생성
            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={dbPath}";
            InitDatabase();
        }

        /// <summary>
        /// DuckDB 테이블, 인덱스 생성 및 초기화
        /// </summary>
        public void InitDatabase()
        {
            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();

                // 1. 4초 주기 요약 테이블 생성
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS crp_summary_logs (
                        timestamp TIMESTAMP,
                        rack_id VARCHAR,
                        board_id INTEGER,
                        avg_temp DOUBLE,
                        max_temp DOUBLE,
                        min_temp DOUBLE,
                        avg_gas DOUBLE,
                        max_gas DOUBLE,
                        has_fire_alarm BOOLEAN,
                        has_sensor_error BOOLEAN
                    );
                    CREATE INDEX IF NOT EXISTS idx_summary_time_rack ON crp_summary_logs (timestamp, rack_id);
                ";
                cmd.ExecuteNonQuery();

                // 2. 이상징후 Dump 데이터 테이블 생성
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS detector_dump_logs (
                        received_at TIMESTAMP,
                        rack_id VARCHAR,
                        board_id INTEGER,
                        temperature DOUBLE,
                        gas_level DOUBLE,
                        is_fire BOOLEAN,
                        is_error BOOLEAN
                    );
                    CREATE INDEX IF NOT EXISTS idx_dump_time ON detector_dump_logs (received_at);
                ";
                cmd.ExecuteNonQuery();

                // 3. 이벤트/알람 로그 테이블 생성 (EventLogModel 연동)
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS event_logs (
                        row_num INTEGER,
                        bay_num INTEGER,
                        level_num INTEGER,
                        content VARCHAR,
                        log_time TIMESTAMP
                    );
                    CREATE INDEX IF NOT EXISTS idx_event_time ON event_logs (log_time);
                ";
                cmd.ExecuteNonQuery();
            }
        }

        #region [데이터 저장 구현]

        /// <summary>
        /// 4초 간격 주기 요약 데이터 일괄 저장
        /// </summary>
        public void SaveSummaryBatch(IEnumerable<CrpSummaryLog> summaryLogs)
        {
            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                string sql = @"
                    INSERT INTO crp_summary_logs 
                    (timestamp, rack_id, board_id, avg_temp, max_temp, min_temp, avg_gas, max_gas, has_fire_alarm, has_sensor_error)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10);";

                foreach (var log in summaryLogs)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.Add(new DuckDBParameter(log.Timestamp));
                    cmd.Parameters.Add(new DuckDBParameter(log.RackId));
                    cmd.Parameters.Add(new DuckDBParameter(log.BoardId));
                    cmd.Parameters.Add(new DuckDBParameter(log.AvgTemperature));
                    cmd.Parameters.Add(new DuckDBParameter(log.MaxTemperature));
                    cmd.Parameters.Add(new DuckDBParameter(log.MinTemperature));
                    cmd.Parameters.Add(new DuckDBParameter(log.AvgGasLevel));
                    cmd.Parameters.Add(new DuckDBParameter(log.MaxGasLevel));
                    cmd.Parameters.Add(new DuckDBParameter(log.HasFireAlarm));
                    cmd.Parameters.Add(new DuckDBParameter(log.HasSensorError));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        /// <summary>
        /// 이상징후 발생 시 링버퍼 정밀 Dump 데이터 일괄 저장
        /// </summary>
        public void SaveDumpBatch(IEnumerable<(DetectorData Data, DateTime ReceivedAt)> items)
        {
            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                string sql = @"
            INSERT INTO detector_dump_logs 
            (received_at, rack_id, board_id, temperature, gas_level, is_fire, is_error)
            VALUES ($1, $2, $3, $4, $5, $6, $7);";

                foreach (var item in items)
                {
                    var data = item.Data;

                    // 모델을 변경하지 않고 저장 시점에 파생 값 산출
                    string rackId = $"BAY{data.Bay:D2}_LV{data.Level:D2}";
                    bool isFire = data.Status == 1 || data.Status == 2; // 또는 data.IsAlarm
                    bool isError = data.Status >= 3;

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.Add(new DuckDBParameter(item.ReceivedAt));
                    cmd.Parameters.Add(new DuckDBParameter(rackId));
                    cmd.Parameters.Add(new DuckDBParameter(data.BoardId));
                    cmd.Parameters.Add(new DuckDBParameter(data.Temperature));
                    cmd.Parameters.Add(new DuckDBParameter(data.GasDensity)); // GasDensity 직접 사용
                    cmd.Parameters.Add(new DuckDBParameter(isFire));
                    cmd.Parameters.Add(new DuckDBParameter(isError));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        /// <summary>
        /// 이벤트 로그 저장 (EventLogModel)
        /// </summary>
        public void SaveEventLog(EventLogModel eventLog)
        {
            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO event_logs (row_num, bay_num, level_num, content, log_time)
                    VALUES ($1, $2, $3, $4, $5);";

                DateTime parsedTime = DateTime.TryParse(eventLog.DateTime, out var dt) ? dt : DateTime.Now;

                cmd.Parameters.Add(new DuckDBParameter(eventLog.Row));
                cmd.Parameters.Add(new DuckDBParameter(eventLog.Bay));
                cmd.Parameters.Add(new DuckDBParameter(eventLog.Level));
                cmd.Parameters.Add(new DuckDBParameter(eventLog.Content));
                cmd.Parameters.Add(new DuckDBParameter(parsedTime));
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        #region [통계 및 시각화 조회 구현]

        /// <summary>
        /// 특정 기간 및 Rack의 4초 주기 요약 데이터 조회 (차트용)
        /// </summary>
        public IEnumerable<CrpSummaryLog> GetSummaryLogs(string? rackId, DateTime startTime, DateTime endTime)
        {
            var list = new List<CrpSummaryLog>();

            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                string sql = @"
                    SELECT timestamp, rack_id, board_id, avg_temp, max_temp, min_temp, avg_gas, max_gas, has_fire_alarm, has_sensor_error
                    FROM crp_summary_logs
                    WHERE timestamp BETWEEN $1 AND $2";

                if (!string.IsNullOrEmpty(rackId))
                {
                    sql += " AND rack_id = $3";
                }
                sql += " ORDER BY timestamp ASC";

                cmd.CommandText = sql;
                cmd.Parameters.Add(new DuckDBParameter(startTime));
                cmd.Parameters.Add(new DuckDBParameter(endTime));
                if (!string.IsNullOrEmpty(rackId))
                {
                    cmd.Parameters.Add(new DuckDBParameter(rackId));
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new CrpSummaryLog
                    {
                        Timestamp = reader.GetDateTime(0),
                        RackId = reader.GetString(1),
                        BoardId = reader.GetInt32(2),
                        AvgTemperature = reader.GetDouble(3),
                        MaxTemperature = reader.GetDouble(4),
                        MinTemperature = reader.GetDouble(5),
                        AvgGasLevel = reader.GetDouble(6),
                        MaxGasLevel = reader.GetDouble(7),
                        HasFireAlarm = reader.GetBoolean(8),
                        HasSensorError = reader.GetBoolean(9)
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// 기간별 Rack 통계 데이터 집계 (최고/최저/평균 온도 및 가스 농도 등)
        /// </summary>
        public IEnumerable<DetectorStatistics> GetStatistics(DateTime startTime, DateTime endTime, string? rackId = null)
        {
            var list = new List<DetectorStatistics>();

            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                string sql = @"
                    SELECT 
                        rack_id,
                        MIN(timestamp) AS period_start,
                        MAX(timestamp) AS period_end,
                        AVG(avg_temp) AS avg_temp,
                        MAX(max_temp) AS max_temp,
                        MIN(min_temp) AS min_temp,
                        MAX(max_gas) AS max_gas,
                        SUM(CASE WHEN has_fire_alarm THEN 1 ELSE 0 END) AS total_fire,
                        SUM(CASE WHEN has_sensor_error THEN 1 ELSE 0 END) AS total_error
                    FROM crp_summary_logs
                    WHERE timestamp BETWEEN $1 AND $2";

                if (!string.IsNullOrEmpty(rackId))
                {
                    sql += " AND rack_id = $3";
                }
                sql += " GROUP BY rack_id ORDER BY rack_id";

                cmd.CommandText = sql;
                cmd.Parameters.Add(new DuckDBParameter(startTime));
                cmd.Parameters.Add(new DuckDBParameter(endTime));
                if (!string.IsNullOrEmpty(rackId))
                {
                    cmd.Parameters.Add(new DuckDBParameter(rackId));
                }

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new DetectorStatistics
                    {
                        RackId = reader.GetString(0),
                        PeriodStart = reader.IsDBNull(1) ? startTime : reader.GetDateTime(1),
                        PeriodEnd = reader.IsDBNull(2) ? endTime : reader.GetDateTime(2),
                        AvgTemperature = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3),
                        MaxTemperature = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4),
                        MinTemperature = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
                        MaxGasLevel = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6),
                        TotalFireEvents = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7)),
                        TotalSensorErrors = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8))
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// 이벤트 로그 조회
        /// </summary>
        public IEnumerable<EventLogModel> GetEventLogs(DateTime startTime, DateTime endTime)
        {
            var list = new List<EventLogModel>();

            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT row_num, bay_num, level_num, content, log_time
                    FROM event_logs
                    WHERE log_time BETWEEN $1 AND $2
                    ORDER BY log_time DESC";

                cmd.Parameters.Add(new DuckDBParameter(startTime));
                cmd.Parameters.Add(new DuckDBParameter(endTime));

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new EventLogModel
                    {
                        Row = reader.GetInt32(0),
                        Bay = reader.GetInt32(1),
                        Level = reader.GetInt32(2),
                        Content = reader.GetString(3),
                        DateTime = reader.GetDateTime(4).ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
            }

            return list;
        }

        #endregion

        #region [데이터 관리 및 모니터링 구현]

        /// <summary>
        /// 과거 데이터를 ZSTD 압축 Parquet 파일로 내보낸 후 DB 삭제 (아카이빙)
        /// </summary>
        public void ArchiveAndPurgeOldData(DateTime archiveBeforeDate, string outputParquetPath)
        {
            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();

                string safePath = outputParquetPath.Replace("'", "''");

                // 1. 요약 데이터 Parquet 저장 및 Purge
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"COPY (SELECT * FROM crp_summary_logs WHERE timestamp < $1) TO '{safePath}' (FORMAT PARQUET, COMPRESSION ZSTD);";
                    cmd.Parameters.Add(new DuckDBParameter(archiveBeforeDate));
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM crp_summary_logs WHERE timestamp < $1;";
                    cmd.Parameters.Add(new DuckDBParameter(archiveBeforeDate));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }

        /// <summary>
        /// 현재 DB 테이블별 로그 건수 산출
        /// </summary>
        public (long SummaryCount, long DumpCount, long EventCount) GetLogCounts()
        {
            lock (_dbLock)
            {
                using var conn = new DuckDBConnection(_connectionString);
                conn.Open();

                long summaryCount = ExecuteScalarCount(conn, "SELECT COUNT(*) FROM crp_summary_logs");
                long dumpCount = ExecuteScalarCount(conn, "SELECT COUNT(*) FROM detector_dump_logs");
                long eventCount = ExecuteScalarCount(conn, "SELECT COUNT(*) FROM event_logs");

                return (summaryCount, dumpCount, eventCount);
            }
        }

        private static long ExecuteScalarCount(DuckDBConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            object? res = cmd.ExecuteScalar();
            return res != null && res != DBNull.Value ? Convert.ToInt64(res) : 0L;
        }

        #endregion
    }
}