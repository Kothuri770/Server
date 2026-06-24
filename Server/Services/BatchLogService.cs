using Dapper;
using Npgsql;
using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;
using System.Threading.Tasks;

namespace TrueCapture.Services
{
    public class BatchLogService : BaseRepository, IBatchLogService
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<BatchLogService> _logger;

        public BatchLogService(string connectionString, string provider, IConfigurationService configService, ILogger<BatchLogService> logger) : base(connectionString, provider)
        {
            _configService = configService;
            _logger = logger;
        }

        public async Task<int> LogBatchEntry(BatchLogEntry logEntry)
        {
            try
            {
                // Convert BatchLogEntry to file-based logging
                await LogToFile(logEntry.BatchId.ToString(), logEntry.TaskName, logEntry.Message, logEntry.Status, logEntry.Details);
                return 1; // Return dummy ID since we're not using database
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging batch entry to file for batch {BatchId}", logEntry.BatchId);
                throw;
            }
        }

        public Task<List<BatchLogEntry>> GetBatchLogs(int batchId)
        {
            // Return empty list since we're not using database
            return Task.FromResult(new List<BatchLogEntry>());
        }

        public Task<BatchLogSummary> GetBatchLogSummary(int batchId)
        {
            // Return basic summary since we're not using database
            return Task.FromResult(new BatchLogSummary
            {
                BatchId = batchId,
                BatchName = $"Batch {batchId}",
                StartTime = DateTime.Now,
                OverallStatus = "UNKNOWN",
                LogEntries = new List<BatchLogEntry>()
            });
        }

        public Task<bool> UpdateBatchLogStatus(int logId, string status, string message = "")
        {
            // No-op since we're not using database
            return Task.FromResult(true);
        }

        public async Task<int> LogBatchTask(BatchTaskLog taskLog)
        {
            try
            {
                // Convert BatchTaskLog to file-based logging with complete details
                var details = $"Task: {taskLog.TaskType}, Description: {taskLog.TaskDescription}";
                if (!string.IsNullOrEmpty(taskLog.Details))
                    details += $", Details: {taskLog.Details}";

                if (!string.IsNullOrEmpty(taskLog.UserId))
                    details += $", UserId: {taskLog.UserId}";
                
                await LogToFile(taskLog.BatchId.ToString(), taskLog.TaskType, taskLog.TaskDescription, taskLog.Status, details, taskLog.ErrorMessage);
                return 1; // Return dummy ID since we're not using database
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging batch task to file for batch {BatchId}", taskLog.BatchId);
                throw;
            }
        }

        public Task<bool> UpdateBatchTaskStatus(int taskLogId, string status, string? errorMessage = null)
        {
            // No-op since we're not using database
            return Task.FromResult(true);
        }

        public Task<List<BatchTaskLog>> GetBatchTaskLogs(int batchId)
        {
            // Return empty list since we're not using database
            return Task.FromResult(new List<BatchTaskLog>());
        }

        public async Task<bool> LogToFile(string batchId, string task, string message, string status = "INFO", string? details = null, string? errorMessage = null)
        {
            try
            {
                var batchFolder = await GetBatchFolderPathAsync(int.Parse(batchId));
                Directory.CreateDirectory(batchFolder); // Ensure directory exists

                var logFilePath = Path.Combine(batchFolder, $"batch_{batchId}_log.txt");

                // Enhanced log entry with complete details and stack trace capture
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logEntry = $"[{timestamp}] {task} | {status} | {message}";

                if (!string.IsNullOrEmpty(details))
                {
                    logEntry += $" | DETAILS: {details}";
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    logEntry += $" | ERROR: {errorMessage}";

                    // Capture stack trace for errors
                    var stackTrace = Environment.StackTrace;
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        var stackLines = stackTrace.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                        var relevantStack = stackLines.Skip(3).Take(10); 
                        logEntry += $" | STACK_TRACE: {string.Join(" -> ", relevantStack)}";
                    }
                }

                logEntry += Environment.NewLine;
                await File.AppendAllTextAsync(logFilePath, logEntry);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing to log file for batch {BatchId}", batchId);
                return false;
            }
        }
        
        // Additional methods needed by existing code
        public async Task<int> LogBatchTaskAsync(int batchId, string taskType, string taskDescription, int? documentId, string? userId)
        {
            var taskLog = new BatchTaskLog
            {
                BatchId = batchId,
                TaskType = taskType,
                TaskDescription = taskDescription,
                Status = "IN_PROGRESS",
                Timestamp = DateTime.Now,
                UserId = userId
            };
            
            return await LogBatchTask(taskLog);
        }

        // Resolves CS1998 by using Task.CompletedTask or synchronous return where appropriate
        public Task<bool> UpdateTaskStatusAsync(int taskLogId, string status, string? errorMessage, string? details = null)
        {
            try
            {
                var message = $"Task status updated to {status}";
                if (!string.IsNullOrEmpty(details))
                    message += $" - {details}";
                
                _logger.LogInformation("Task {TaskLogId} update: {Message}", taskLogId, message);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task status");
                return Task.FromResult(false);
            }
        }

        public Task<BatchTaskLog?> GetLatestBatchLogAsync(int batchId, string taskType)
        {
            // Return null since we're not using database
            return Task.FromResult<BatchTaskLog?>(null);
        }

        public async Task<bool> LogTaskCompletionAsync(BatchTaskCompletionRequest request)
        {
            var taskLog = new BatchTaskLog
            {
                BatchId = request.BatchId,
                TaskType = request.TaskType,
                TaskDescription = request.TaskDescription,
                Status = request.Status,
                ErrorMessage = request.ErrorMessage,
                Details = request.Details,
                Timestamp = DateTime.Now,
                UserId = request.UserId
            };
            
            var result = await LogBatchTask(taskLog);
            return result > 0;
        }
        
        // Task Timing Methods Implementation
        public async Task<int> StartTaskTimingAsync(BatchTaskTimingRequest request)
        {
            try
            {
                using var connection = CreateConnection();
                
                DateTime startTime = DateTime.Now;
                
                var sql = _provider == "SqlServer" 
                    ? @"INSERT INTO BatchTaskTime (BatchId, TaskId, TaskStartTime, Status) 
                        OUTPUT INSERTED.Id
                        VALUES (@BatchId, @TaskId, @TaskStartTime, @Status)"
                    : @"INSERT INTO BatchTaskTime (BatchId, TaskId, TaskStartTime, Status) 
                        VALUES (@BatchId, @TaskId, @TaskStartTime, @Status) 
                        RETURNING Id";
                    
                var result = await connection.QuerySingleAsync<int>(sql, new
                {
                    BatchId = request.BatchId,
                    TaskId = request.TaskId,
                    TaskStartTime = startTime,
                    Status = request.Status ?? request.Description ?? "Start",
                });
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in task timing for batch {BatchId} and task {TaskId}", request.BatchId, request.TaskId);
                throw;
            }
        }

        public async Task<List<BatchTaskTimeDto>> GetBatchTaskTimingsAsync(int batchId)
        {
            try
            {
                using var connection = CreateConnection();
                
                const string sql = @"
                    SELECT btt.Id, btt.BatchId, btt.TaskId, btt.TaskStartTime, btt.Status
                    FROM BatchTaskTime btt
                    WHERE btt.BatchId = @BatchId
                    ORDER BY btt.TaskStartTime ASC";
                        
                var rawData = await connection.QueryAsync<RawBatchTaskTime>(sql, new { BatchId = batchId });
                var processedResults = ProcessTaskTimings(rawData);
                var taskIds = processedResults.Select(x => x.TaskId).Distinct().ToList();

                if (taskIds.Any())
                {
                    var stepsSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) 
                        ? @"SELECT ID, StepName FROM Steps WHERE ID IN @TaskIds"
                        : @"SELECT ID, StepName FROM Steps WHERE ID = ANY(@TaskIds)";
                    var stepMappings = (await connection.QueryAsync<StepInfo>(stepsSql, new { TaskIds = taskIds }))
                        .ToDictionary(s => s.ID, s => s.StepName);
                            
                    foreach (var result in processedResults)
                    {
                        result.TaskName = stepMappings.ContainsKey(result.TaskId) ? stepMappings[result.TaskId] : result.TaskId.ToString();
                    }
                }
                        
                return processedResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch task timings from database for batch {BatchId}", batchId);
                return new List<BatchTaskTimeDto>();
            }
        }
                
        private static List<BatchTaskTimeDto> ProcessTaskTimings(IEnumerable<RawBatchTaskTime> rawData)
        {
            var results = new List<BatchTaskTimeDto>();
            var pendingTasks = new Dictionary<string, RawBatchTaskTime>();
                    
            foreach (var record in rawData)
            {
                var taskKey = $"{record.TaskId}_{record.Status.ToUpper()}";
                        
                if (record.Status.ToUpper() == "END" || record.Status.ToUpper().Contains("ENDED"))
                {
                    var startKey = $"{record.TaskId}_START";
                    if (pendingTasks.ContainsKey(startKey))
                    {
                        var startRecord = pendingTasks[startKey];
                        var duration = (record.TaskStartTime - startRecord.TaskStartTime).TotalSeconds;
                                
                        results.Add(new BatchTaskTimeDto
                        {
                            Id = startRecord.Id,
                            BatchId = startRecord.BatchId,
                            TaskId = startRecord.TaskId,
                            TaskName = "", 
                            TaskStartTime = startRecord.TaskStartTime,
                            TaskEndTime = record.TaskStartTime,
                            TaskDurationSeconds = (int)duration,
                            Status = "COMPLETED",
                            CreatedOn = startRecord.TaskStartTime
                        });
                                
                        pendingTasks.Remove(startKey);
                    }
                    else
                    {
                        results.Add(new BatchTaskTimeDto
                        {
                            Id = record.Id,
                            BatchId = record.BatchId,
                            TaskId = record.TaskId,
                            TaskStartTime = record.TaskStartTime,
                            TaskEndTime = record.TaskStartTime, 
                            TaskDurationSeconds = 0,
                            Status = record.Status,
                            CreatedOn = record.TaskStartTime
                        });
                    }
                }
                else if (record.Status.ToUpper() == "START")
                {
                    pendingTasks[taskKey] = record;
                }
                else
                {
                    results.Add(new BatchTaskTimeDto
                    {
                        Id = record.Id,
                        BatchId = record.BatchId,
                        TaskId = record.TaskId,
                        TaskStartTime = record.TaskStartTime,
                        TaskEndTime = null,
                        TaskDurationSeconds = null,
                        Status = record.Status,
                        CreatedOn = record.TaskStartTime
                    });
                }
            }
                    
            foreach (var pending in pendingTasks.Values)
            {
                results.Add(new BatchTaskTimeDto
                {
                    Id = pending.Id,
                    BatchId = pending.BatchId,
                    TaskId = pending.TaskId,
                    TaskStartTime = pending.TaskStartTime,
                    TaskEndTime = null,
                    TaskDurationSeconds = null,
                    Status = "IN_PROGRESS", 
                    CreatedOn = pending.TaskStartTime
                });
            }
                    
            return results;
        }
                
        public async Task<List<LogFile>> GetBatchLogFilesAsync(int batchId)
        {
            var logFiles = new List<LogFile>();
            try
            {
                var batchFolder = await GetBatchFolderPathAsync(batchId);
                if (Directory.Exists(batchFolder))
                {
                    var files = Directory.GetFiles(batchFolder, "batch_*_log.txt");
                    foreach (var file in files)
                    {
                        var info = new FileInfo(file);
                        logFiles.Add(new LogFile
                        {
                            FileName = Path.GetFileName(file),
                            FilePath = file,
                            CreatedOn = info.CreationTime,
                            FileSize = info.Length
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving log files for batch {BatchId}", batchId);
            }
            return logFiles.OrderByDescending(f => f.CreatedOn).ToList();
        }

        public async Task<string?> GetLogFileContentAsync(int batchId, string fileName)
        {
            try
            {
                var batchFolder = await GetBatchFolderPathAsync(batchId);
                var logFilePath = Path.Combine(batchFolder, fileName);

                var fullPath = Path.GetFullPath(logFilePath);
                var batchPath = Path.GetFullPath(batchFolder);
                if (!fullPath.StartsWith(batchPath)) return null;

                if (!File.Exists(logFilePath)) return null;

                return await File.ReadAllTextAsync(logFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading log file {FileName} for batch {BatchId}", fileName, batchId);
                return null;
            }
        }

        private async Task<string> GetBatchFolderPathAsync(int batchId)
        {
            var batchFolderPath = await _configService.GetConfigurationsValue("Batch Folder");
            if (string.IsNullOrEmpty(batchFolderPath))
            {
                batchFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "batches");
            }

            using var conn = CreateConnection();
            var sql = @"
                SELECT b.BatchName, o.Name as AppName 
                FROM Batch b 
                LEFT JOIN ObjectTypes o ON b.BatchTypeId = o.Id 
                WHERE b.ID = @batchId";
            
            var batchData = await conn.QueryFirstOrDefaultAsync<(string BatchName, string AppName)>(sql, new { batchId });
            
            if (batchData.BatchName != null)
            {
                var appName = SanitizeFolderName(batchData.AppName ?? "Unsorted");
                var batchName = SanitizeFolderName(batchData.BatchName);
                
                var hierarchicalPath = Path.Combine(batchFolderPath, appName, batchName);
                if (Directory.Exists(hierarchicalPath)) return hierarchicalPath;

                var legacyPath = Path.Combine(batchFolderPath, batchId.ToString());
                if (Directory.Exists(legacyPath)) return legacyPath;

                return hierarchicalPath;
            }

            return Path.Combine(batchFolderPath, batchId.ToString());
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return sanitized.Trim();
        }

        // Changed to public to resolve S1144 while maintaining Dapper compatibility
    }
}
