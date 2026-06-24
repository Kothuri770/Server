using Dapper;
using Server.Models;
using System.Text;
using TrueCapture.Services;

namespace Server.Repositories
{
    public interface IMonitorRepository
    {
        Task<PagedJobMonitorDto> GetJobsAsync(string username, string userType, int pageNumber = 1, int pageSize = 100);
        Task<PagedJobMonitorDto> GetVerifyBatchesAsync(string username, string userType, int pageNumber = 1, int pageSize = 100);
        Task<IEnumerable<ColumnTypeDto>> GetFilterColumnsAsync();
        Task<PagedJobMonitorDto> GetFilteredDataAsync(FilterInputDto filter, int pageNumber = 1, int pageSize = 100);
        Task<IEnumerable<BatchTaskTimeDto>> GetBatchTaskTimingsBulkAsync(IEnumerable<int> batchIds);
        Task<MonitoringDashboardDto> GetDashboardDataAsync();
        Task<IEnumerable<Step>> GetStepsAsync();
        Task<bool> UpdateBatchStepAsync(int batchId, int stepId, string username);
    }

    public class MonitorRepository : BaseRepository, IMonitorRepository
    {

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _columnDataTypeCache = new();

        public MonitorRepository(string connectionString, string provider) : base(connectionString, provider) 
        { 
        }

        public async Task<PagedJobMonitorDto> GetJobsAsync(string username, string userType, int pageNumber = 1, int pageSize = 100)
        {
            using var conn = CreateConnection();
            bool seeAll = CanSeeAllBatches(userType);

            var countSql = seeAll
                ? "SELECT COUNT(*) FROM JobMonitorReportQuery"
                : "SELECT COUNT(*) FROM JobMonitorReportQuery WHERE UserName = @username";
            
            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, new { username });

            var sql = seeAll
                ? "SELECT * FROM JobMonitorReportQuery ORDER BY CreatedOn DESC"
                : "SELECT * FROM JobMonitorReportQuery WHERE UserName = @username ORDER BY CreatedOn DESC";
            
            sql = AddPagination(sql, pageNumber, pageSize);
            
            var items = await conn.QueryAsync<JobMonitorDto>(sql, new { username });

            return new PagedJobMonitorDto
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        private string AddPagination(string sql, int pageNumber, int pageSize)
        {
            int offset = (pageNumber - 1) * pageSize;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                return $"{sql} OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";
            }
            return $"{sql} LIMIT {pageSize} OFFSET {offset}";
        }

        private static bool CanSeeAllBatches(string? userType)
        {
            string normalizedType = (userType ?? "").ToLower().Trim();
            if (string.IsNullOrEmpty(normalizedType)) return true;

            return normalizedType switch
            {
                "admin" or "verifier" or "monitor" or "scanner" or "configeditor" or "user" or "scanverify" => true,
                _ => false
            };
        }

        public async Task<PagedJobMonitorDto> GetVerifyBatchesAsync(string username, string userType, int pageNumber = 1, int pageSize = 100)
        {
            using var conn = CreateConnection();
            bool seeAll = CanSeeVerifyBatches(userType);
            string likeOp = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "LIKE" : "ILIKE";
            
            var countSql = seeAll
                ? $"SELECT COUNT(*) FROM JobMonitorReportQuery WHERE Task {likeOp} 'Index%'"
                : $"SELECT COUNT(*) FROM JobMonitorReportQuery WHERE Task {likeOp} 'Index%' AND UserName = @username";

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, new { username });

            var sql = seeAll
                ? $"SELECT * FROM JobMonitorReportQuery WHERE Task {likeOp} 'Index%' ORDER BY CreatedOn DESC"
                : $"SELECT * FROM JobMonitorReportQuery WHERE Task {likeOp} 'Index%' AND UserName = @username ORDER BY CreatedOn DESC";

            sql = AddPagination(sql, pageNumber, pageSize);
            var items = await conn.QueryAsync<JobMonitorDto>(sql, new { username });

            return new PagedJobMonitorDto
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        private static bool CanSeeVerifyBatches(string? userType)
        {
            string normalizedType = (userType ?? "").ToLower().Trim();
            if (string.IsNullOrEmpty(normalizedType)) return true;

            return normalizedType switch
            {
                "admin" or "verifier" or "monitor" => true,
                _ => normalizedType != "scanner"
            };
        }

        public async Task<IEnumerable<ColumnTypeDto>> GetFilterColumnsAsync()
        {
            using var conn = CreateConnection();
            var integerType = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "int" : "integer";
            var sql = 
                "SELECT COLUMN_NAME as column_name, DATA_TYPE as datatype, " +
                $"CASE WHEN DATA_TYPE = '{integerType}' THEN '1' WHEN DATA_TYPE IN ('varchar', 'text', 'nvarchar') THEN '2'  " +
                "ELSE '2' END as dtid " +
                "FROM INFORMATION_SCHEMA.COLUMNS " +
                "WHERE TABLE_NAME = 'jobmonitorreportquery' " +
                "ORDER BY ORDINAL_POSITION";
            return await conn.QueryAsync<ColumnTypeDto>(sql);
        }

        public async Task<PagedJobMonitorDto> GetFilteredDataAsync(FilterInputDto filter, int pageNumber = 1, int pageSize = 100)
        {
            using var conn = CreateConnection();
            var baseSql = "FROM JobMonitorReportQuery WHERE 1=1 ";
            var sqlBuilder = new StringBuilder(baseSql);
            var parameters = new DynamicParameters();

            for (int i = 0; i < filter.filterdata.filter.Count; i++)
            {
                var rule = filter.filterdata.filter[i];
                var dataType = await GetCachedFilterDatatypeAsync(rule.column);
                AppendFilterCondition(sqlBuilder, parameters, rule, dataType, i);
            }

            var countSql = "SELECT COUNT(*) " + sqlBuilder.ToString();
            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

            var selectSql = "SELECT * " + sqlBuilder.ToString() + " ORDER BY CreatedOn DESC";
            selectSql = AddPagination(selectSql, pageNumber, pageSize);

            var items = await conn.QueryAsync<JobMonitorDto>(selectSql, parameters);

            return new PagedJobMonitorDto
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<IEnumerable<BatchTaskTimeDto>> GetBatchTaskTimingsBulkAsync(IEnumerable<int> batchIds)
        {
            if (batchIds == null || !batchIds.Any()) return new List<BatchTaskTimeDto>();

            using var conn = CreateConnection();
            // Use IN clause for bulk retrieval
            var sql = @"
                SELECT t.ID, t.BatchId, t.TaskId, s.StepName as TaskName, 
                       t.TaskStartTime, t.TaskEndTime, t.TaskDurationSeconds, t.Status, t.CreatedOn
                FROM BatchTaskTiming t
                LEFT JOIN Steps s ON t.TaskId = s.ID
                WHERE t.BatchId IN @batchIds
                ORDER BY t.BatchId, t.TaskStartTime";

            return await conn.QueryAsync<BatchTaskTimeDto>(sql, new { batchIds });
        }

        private void AppendFilterCondition(StringBuilder sqlBuilder, DynamicParameters parameters, FilterCriteriaDto rule, string dataType, int index)
        {
            var op = GetSqlOperator(rule.condition);
            var paramName = $"value{index}";

            if (IsBetweenDateRange(dataType, op))
            {
                AppendBetweenDateCondition(sqlBuilder, parameters, rule, index);
            }
            else if (IsNumericType(dataType))
            {
                AppendNumericCondition(sqlBuilder, parameters, rule, op, paramName);
            }
            else if (IsDateType(dataType))
            {
                AppendDateCondition(sqlBuilder, parameters, rule, op, paramName);
            }
            else
            {
                AppendDefaultCondition(sqlBuilder, parameters, rule, op, paramName);
            }
        }

        private static bool IsBetweenDateRange(string dataType, string op) =>
            (dataType.Contains("timestamp") || dataType.Contains("datetime")) && op == "BETWEEN";

        private static bool IsNumericType(string dataType) =>
            dataType.Contains("integer") || dataType.Contains("numeric") || dataType.Contains("bigint") || dataType.Equals("int", StringComparison.OrdinalIgnoreCase);

        private static bool IsDateType(string dataType) =>
            dataType.Contains("timestamp") || dataType.Contains("date") || dataType.Contains("datetime");

        private static void AppendBetweenDateCondition(StringBuilder sqlBuilder, DynamicParameters parameters, FilterCriteriaDto rule, int index)
        {
            sqlBuilder.Append($" AND CAST({rule.column} AS DATE) BETWEEN @start{index} AND @end{index}");
            parameters.Add($"start{index}", rule.start);
            parameters.Add($"end{index}", rule.end);
        }

        private void AppendNumericCondition(StringBuilder sqlBuilder, DynamicParameters parameters, FilterCriteriaDto rule, string op, string paramName)
        {
            if (int.TryParse(rule.value, out int intValue))
            {
                sqlBuilder.Append($" AND {rule.column} {op} @{paramName}");
                parameters.Add(paramName, intValue);
            }
            else if (decimal.TryParse(rule.value, out decimal decValue))
            {
                sqlBuilder.Append($" AND {rule.column} {op} @{paramName}");
                parameters.Add(paramName, decValue);
            }
            else
            {
                AppendDefaultCondition(sqlBuilder, parameters, rule, op, paramName);
            }
        }

        private void AppendDateCondition(StringBuilder sqlBuilder, DynamicParameters parameters, FilterCriteriaDto rule, string op, string paramName)
        {
            if (DateTime.TryParse(rule.value, out DateTime dateValue))
            {
                sqlBuilder.Append($" AND {rule.column} {op} @{paramName}");
                parameters.Add(paramName, dateValue);
            }
            else
            {
                AppendDefaultCondition(sqlBuilder, parameters, rule, op, paramName);
            }
        }

        private static void AppendDefaultCondition(StringBuilder sqlBuilder, DynamicParameters parameters, FilterCriteriaDto rule, string op, string paramName)
        {
            var finalValue = op == "LIKE" ? $"%{rule.value}%" : rule.value;
            sqlBuilder.Append($" AND {rule.column} {op} @{paramName}");
            parameters.Add(paramName, finalValue);
        }

        public async Task<MonitoringDashboardDto> GetDashboardDataAsync()
        {
            using var conn = CreateConnection();

            var sqlBase = GetDashboardBaseSql();
            var todayStats = await conn.QueryFirstAsync<ProcessingStatisticsDto>(sqlBase + GetDashboardPeriodCondition("today"));
            var weekStats = await conn.QueryFirstAsync<ProcessingStatisticsDto>(sqlBase + GetDashboardPeriodCondition("week"));

            return new MonitoringDashboardDto
            {
                TodayStats = todayStats,
                WeekStats = weekStats,
                LastUpdated = DateTime.Now
            };
        }

        private string GetDashboardBaseSql()
        {
            var avgProcessingTimeSql = _provider == "SqlServer"
                ? "COALESCE(AVG(DATEDIFF(SECOND, CreatedOn, GETUTCDATE()) / 3600.0), 0)"
                : "COALESCE(AVG(EXTRACT(EPOCH FROM (NOW() - CreatedOn))/3600), 0)";

            return "SELECT  " +
                   "COUNT(*) as TotalBatches, " +
                   "COUNT(CASE WHEN BatchStatus = 'Active' THEN 1 END) as ActiveBatches, " +
                   "COUNT(CASE WHEN BatchStatus = 'Complete' THEN 1 END) as CompletedBatches, " +
                   "COUNT(CASE WHEN BatchStatus = 'Hold' THEN 1 END) as HeldBatches, " +
                   "COUNT(DISTINCT UserName) as UsersProcessed, " +
                   $"{avgProcessingTimeSql} as AvgProcessingTime " +
                   "FROM JobMonitorReportQuery";
        }

        private string GetDashboardPeriodCondition(string period)
        {
            bool isSqlServer = _provider == "SqlServer";
            if (period == "today")
            {
                return isSqlServer
                    ? " WHERE CAST(CreatedOn AS DATE) = CAST(GETUTCDATE() AS DATE)"
                    : " WHERE CreatedOn::date = CURRENT_DATE";
            }
            
            return isSqlServer
                ? " WHERE CreatedOn >= DATEADD(DAY, -7, GETUTCDATE())"
                : " WHERE CreatedOn >= CURRENT_DATE - INTERVAL '7 days'";
        }

        private async Task<string> GetCachedFilterDatatypeAsync(string colName)
        {
            if (_columnDataTypeCache.TryGetValue(colName, out var cachedType))
                return cachedType;

            var type = await GetFilterDatatypeAsync(colName);
            _columnDataTypeCache.TryAdd(colName, type);
            return type;
        }

        private async Task<string> GetFilterDatatypeAsync(string colName)
        {
            using var conn = CreateConnection();
            var sql = _provider == "SqlServer"
                ? "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'jobmonitorreportquery' AND COLUMN_NAME = @colName"
                : "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'public' AND TABLE_NAME = 'jobmonitorreportquery' AND COLUMN_NAME = @colName";
            
            return await conn.QueryFirstOrDefaultAsync<string>(sql, new { colName }) ?? "varchar";
        }

        private static string GetSqlOperator(string condition) => condition.ToLower() switch
        {
            "equals" => "=",
            "like" => "LIKE",
            "isgreaterthan" => ">",
            "islessthan" => "<",
            "range" => "BETWEEN",
            _ => "="
        };

        public async Task<IEnumerable<Step>> GetStepsAsync()
        {
            using var conn = CreateConnection();
            var sql = "SELECT ID, StepName, Status, StepOrder FROM Steps WHERE Status = 'A' ORDER BY StepOrder";
            return await conn.QueryAsync<Step>(sql);
        }

        public async Task<bool> UpdateBatchStepAsync(int batchId, int stepId, string username)
        {
            using var conn = CreateConnection();
            
            // Update batch step and set status to 'A' (Active)
            var updateBatchSql = "UPDATE Batch SET StepID = @stepId, BatchStatus = 'A' WHERE ID = @batchId";
            var result = await conn.ExecuteAsync(updateBatchSql, new { stepId, batchId });
            
            // Insert action log
            var insertActionSql = "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'STEP_CHANGED', @username, @actionStamp)";
            await conn.ExecuteAsync(insertActionSql, new { batchId, username, actionStamp = DateTime.Now });
            
            return result > 0;
        }
    }
}