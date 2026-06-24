//using Dapper;
//using Npgsql;
//using Server.Models;
//using Server.Services.Configuration;
//using Server.Repositories;

//namespace TrueCapture.Services
//{
//    public class DatabaseBatchLogService : BaseRepository, IBatchLogService
//    {
//        private readonly IConfigurationService _configService;
//        private readonly ILogger<DatabaseBatchLogService> _logger;

//        public DatabaseBatchLogService(string connectionString, IConfigurationService configService, ILogger<DatabaseBatchLogService> logger) : base(connectionString)
//        {
//            _configService = configService;
//            _logger = logger;
//        }

//        public async Task<int> LogBatchEntry(BatchLogEntry logEntry)
//        {
//            try
//            {
//                using var connection = new NpgsqlConnection(_connectionString);
                
//                const string sql = @"
//                    INSERT INTO BatchLog (BatchId, BatchType, DocumentCount, CompletedOn, StepId, StationId, PageCount, BatchName) 
//                    VALUES (@BatchId, @BatchType, @DocumentCount, @CompletedOn, @StepId, @StationId, @PageCount, @BatchName)
//                    RETURNING BatchId";
                    
//                var result = await connection.QuerySingleAsync<int>(sql, new
//                {
//                    BatchId = logEntry.BatchId,
//                    BatchType = "",
//                    DocumentCount = 0,
//                    CompletedOn = logEntry.Timestamp,
//                    StepId = 0,
//                    StationId = 0,
//                    PageCount = 0,
//                    BatchName = ""
//                });
                
//                return result;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error logging batch entry to database for batch {BatchId}", logEntry.BatchId);
//                throw;
//            }
//        }

//        public async Task<List<BatchLogEntry>> GetBatchLogs(int batchId)
//        {
//            try
//            {
//                using var connection = new NpgsqlConnection(_connectionString);
                
//                // Since we don't have a dedicated table for logs, we'll return empty list for now
//                return new List<BatchLogEntry>();
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error getting batch logs from database for batch {BatchId}", batchId);
//                return new List<BatchLogEntry>();
//            }
//        }

//        public async Task<BatchLogSummary> GetBatchLogSummary(int batchId)
//        {
//            try
//            {
//                // Return basic summary since we're not using a comprehensive logging table
//                return new BatchLogSummary
//                {
//                    BatchId = batchId,
//                    BatchName = $"Batch {batchId}",
//                    StartTime = DateTime.Now,
//                    OverallStatus = "UNKNOWN",
//                    LogEntries = new List<BatchLogEntry>()
//                };
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error getting batch log summary for batch {BatchId}", batchId);
//                return new BatchLogSummary
//                {
//                    BatchId = batchId,
//                    BatchName = $"Batch {batchId}",
//                    StartTime = DateTime.Now,
//                    OverallStatus = "ERROR",
//                    LogEntries = new List<BatchLogEntry>()
//                };
//            }
//        }

//        public async Task<bool> UpdateBatchLogStatus(int logId, string status, string message = "")
//        {
//            try
//            {
//                // No-op since we're not using a comprehensive logging table
//                return true;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error updating batch log status for log ID {LogId}", logId);
//                return false;
//            }
//        }

//        public async Task<int> LogBatchTask(BatchTaskLog taskLog)
//        {
//            try
//            {
//                using var connection = new NpgsqlConnection(_connectionString);
                
//                const string sql = @"
//                    INSERT INTO BatchTaskTime (
//                        BatchId, TaskName, TaskStartTime, Status, CreatedOn
//                    ) VALUES (
//                        @BatchId, @TaskName, @TaskStartTime, @Status, @CreatedOn
//                    ) RETURNING Id";
                    
//                var result = await connection.QuerySingleAsync<int>(sql, new
//                {
//                    BatchId = taskLog.BatchId,
//                    TaskName = taskLog.TaskType,
//                    TaskStartTime = DateTime.Now,
//                    Status = taskLog.Status,
//                    CreatedOn = DateTime.Now
//                });
                
//                return result;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error logging batch task to database for batch {BatchId}", taskLog.BatchId);
//                throw;
//            }
//        }

//        public async Task<bool> UpdateBatchTaskStatus(int taskLogId, string status, string? errorMessage = null)
//        {
//            try
//            {
//                using var connection = new NpgsqlConnection(_connectionString);
                
//                const string sql = @"UPDATE BatchTaskTime SET Status = @Status WHERE Id = @TaskLogId";
                
//                var rowsAffected = await connection.ExecuteAsync(sql, new
//                {
//                    Status = status,
//                    TaskLogId = taskLogId
//                });
                
//                return rowsAffected > 0;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error updating batch task status for task log ID {TaskLogId}", taskLogId);
//                return false;
//            }
//        }

//        public async Task<List<BatchTaskLog>> GetBatchTaskLogs(int batchId)
//        {
//            try
//            {
//                using var connection = new NpgsqlConnection(_connectionString);
                
//                const string sql = @"
//                    SELECT Id as Id, BatchId, DocumentId, TaskType as TaskType, 
//                           Description as TaskDescription, Status, ErrorMessage, 
//                           CreatedOn as Timestamp, '' as Details, UserId
//                    FROM BatchTaskTime 
//                    WHERE BatchId = @BatchId";
                    
//                var results = await connection.QueryAsync<BatchTaskLog>(sql, new { BatchId = batchId });
//                return results.ToList();
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error getting batch task logs from database for batch {BatchId}", batchId);
//                return new List<BatchTaskLog>();
//            }
//        }

//        public async Task<bool> LogToFile(string batchId, string task, string message, string status = "INFO", string? details = null, string? errorMessage = null)
//        {
//            // This method is for file-based logging, but we'll also log to database
//            try
//            {
//                // Get batch folder path from configuration service
//                var batchFolderPath = await _configService.GetConfigurationsValue("Batch Folder");
//                if (string.IsNullOrEmpty(batchFolderPath))
//                {
//                    // Fallback to default if not found in configuration
//                    batchFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "batches");
//                }

//                var batchFolder = Path.Combine(batchFolderPath, batchId);
//                Directory.CreateDirectory(batchFolder); // Ensure directory exists

//                var logFilePath = Path.Combine(batchFolder, $"batch_{batchId}_log.txt");

//                // Enhanced log entry with complete details and stack trace capture
//                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
//                var logEntry = $"[{timestamp}] {task} | {status} | {message}";

//                if (!string.IsNullOrEmpty(details))
//                {
//                    logEntry += $" | DETAILS: {details}";
//                }

//                if (!string.IsNullOrEmpty(errorMessage))
//                {
//                    logEntry += $" | ERROR: {errorMessage}";
//                }

//                logEntry += Environment.NewLine;
//                await File.AppendAllTextAsync(logFilePath, logEntry);

//                return true;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error writing to log file for batch {BatchId}", batchId);
//                return false;
//            }
//        }

//        // Additional methods needed by existing code
//        public async Task<int> LogBatchTaskAsync(int batchId, string taskType, string taskDescription, int? documentId, string? userId)
//        {
//            var taskLog = new BatchTaskLog
//            {
//                BatchId = batchId,
//                TaskType = taskType,
//                TaskDescription = taskDescription,
//                DocumentId = documentId,
//                Status = "IN_PROGRESS",
//                Timestamp = DateTime.Now,
//                UserId = userId
//            };
            
//            return await LogBatchTask(taskLog);
//        }

//        public async Task<bool> UpdateTaskStatusAsync(int taskLogId, string status, string? errorMessage, string? details = null)
//        {
//            try
//            {
//                using var connection = new NpgsqlConnection(_connectionString);
                
//                var sql = "UPDATE BatchTaskTime SET Status = @Status";
//                var parameters = new DynamicParameters();
//                parameters.Add("Status", status);
//                parameters.Add("TaskLogId", taskLogId);
                
//                if (!string.IsNullOrEmpty(errorMessage))
//                {
//                    sql += ", ErrorMessage = @ErrorMessage";
//                    parameters.Add("ErrorMessage", errorMessage);
//                }
                
//                sql += " WHERE Id = @TaskLogId";
                
//                var rowsAffected = await connection.ExecuteAsync(sql, parameters);
                
//                return rowsAffected > 0;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error updating task status for task log ID {TaskLogId}", taskLogId);
//                return false;
//            }
//        }

//        public async Task<BatchTaskLog?> GetLatestBatchLogAsync(int batchId, string taskType)
//        {
//            try
//            {
//                using var connection = new NpgsqlConnection(_connectionString);
                
//                const string sql = @"
//                    SELECT Id as Id, BatchId, DocumentId, TaskType as TaskType, 
//                           Description as TaskDescription, Status, ErrorMessage, 
//                           CreatedOn as Timestamp, '' as Details, UserId
//                    FROM BatchTaskTime 
//                    WHERE BatchId = @BatchId AND TaskType = @TaskType
//                    ORDER BY CreatedOn DESC
//                    LIMIT 1";
                    
//                var result = await connection.QueryFirstOrDefaultAsync<BatchTaskLog>(sql, new { BatchId = batchId, TaskType = taskType });
//                return result;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error getting latest batch log for batch {BatchId} and task {TaskType}", batchId, taskType);
//                return null;
//            }
//        }

//        public async Task<bool> LogTaskCompletionAsync(int batchId, string taskType, string status, string taskDescription, string errorMessage, string? details, int? documentId, string? userId)
//        {
//            var taskLog = new BatchTaskLog
//            {
//                BatchId = batchId,
//                TaskType = taskType,
//                TaskDescription = taskDescription,
//                DocumentId = documentId,
//                Status = status,
//                ErrorMessage = errorMessage,
//                Details = details,
//                Timestamp = DateTime.Now,
//                UserId = userId
//            };
            
//            var result = await LogBatchTask(taskLog);
//            return result > 0;
//        }

//        public async Task<int> StartTaskTimingAsync(int batchId, int taskId, string? description = null, int? documentId = null, string? userId = null, string? status = null, string? errorMessage = null)
//        {
//            try
//            {
//                using var connection = CreateConnection();

//                DateTime startTime = DateTime.Now;
//                DateTime? endTime = null;
//                int? duration = null;

//                // If status is provided, it means we're ending the task immediately
//                if (!string.IsNullOrEmpty(status))
//                {
//                    endTime = startTime;
//                    duration = 0; // Duration is 0 when start and end happen at the same moment
//                }

//                const string sql = @"
//                    INSERT INTO BatchTaskTime (
//                        BatchId, TaskId, TaskStartTime,  Status
//                    ) VALUES (
//                        @BatchId, @TaskId, @TaskStartTime,  @Status
//                    ) RETURNING Id";

//                var result = await connection.QuerySingleAsync<int>(sql, new
//                {
//                    BatchId = batchId,
//                    TaskId = taskId,
//                    TaskStartTime = startTime,
//                    Status = status ?? description ?? "Start",
//                });

//                return result;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error in task timing for batch {BatchId} and task {TaskName}", batchId, taskId);
//                throw;
//            }
//        }




//        public async Task<List<BatchTaskTimeDto>> GetBatchTaskTimingsAsync(int batchId)
//        {
//            try
//            {
//                using var connection = CreateConnection();
                
//                const string sql = @"
//                    SELECT Id, BatchId, TaskName, TaskStartTime, TaskEndTime, TaskDurationSeconds, 
//                           Status, CreatedOn
//                    FROM BatchTaskTime 
//                    WHERE BatchId = @BatchId
//                    ORDER BY TaskStartTime DESC";
                    
//                var results = await connection.QueryAsync<BatchTaskTimeDto>(sql, new { BatchId = batchId });
//                return results.ToList();
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error getting batch task timings from database for batch {BatchId}", batchId);
//                return new List<BatchTaskTimeDto>();
//            }
//        }
//    }
//}
