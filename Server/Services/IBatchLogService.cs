using Server.Models;

namespace TrueCapture.Services
{
    public interface IBatchLogService
    {
        Task<int> LogBatchEntry(BatchLogEntry logEntry);
        Task<List<BatchLogEntry>> GetBatchLogs(int batchId);
        Task<BatchLogSummary> GetBatchLogSummary(int batchId);
        Task<bool> UpdateBatchLogStatus(int logId, string status, string message = "");
        Task<int> LogBatchTask(BatchTaskLog taskLog);
        Task<bool> UpdateBatchTaskStatus(int taskLogId, string status, string? errorMessage = null);
        Task<List<BatchTaskLog>> GetBatchTaskLogs(int batchId);
        Task<bool> LogToFile(string batchId, string task, string message, string status = "INFO", string? details = null, string? errorMessage = null);
        
        // Additional methods needed by existing code
        Task<int> LogBatchTaskAsync(int batchId, string taskType, string taskDescription, int? documentId, string? userId);
        Task<bool> UpdateTaskStatusAsync(int taskLogId, string status, string? errorMessage, string? details = null);
        Task<BatchTaskLog?> GetLatestBatchLogAsync(int batchId, string taskType);
        
        // Refactored with DTO to resolve S107 (Too many parameters)
        Task<bool> LogTaskCompletionAsync(BatchTaskCompletionRequest request);
        
        // Task Timing Methods refactored with DTO
        Task<int> StartTaskTimingAsync(BatchTaskTimingRequest request);
        Task<List<BatchTaskTimeDto>> GetBatchTaskTimingsAsync(int batchId);

        // Log File Retrieval Methods
        Task<List<LogFile>> GetBatchLogFilesAsync(int batchId);
        Task<string?> GetLogFileContentAsync(int batchId, string fileName);
    }

    public class BatchTaskCompletionRequest
    {
        public int BatchId { get; set; }
        public string TaskType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string TaskDescription { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? Details { get; set; }
        public int? DocumentId { get; set; }
        public string? UserId { get; set; }
    }

    public class BatchTaskTimingRequest
    {
        public int BatchId { get; set; }
        public int TaskId { get; set; }
        public string? Description { get; set; }
        public int? DocumentId { get; set; }
        public string? UserId { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class RawBatchTaskTime
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public int TaskId { get; set; }
        public DateTime TaskStartTime { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class StepInfo
    {
        public int ID { get; set; }
        public string StepName { get; set; } = string.Empty;
    }

    public class BatchTaskTimeDto
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public int TaskId { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public DateTime TaskStartTime { get; set; }
        public DateTime? TaskEndTime { get; set; }
        public int? TaskDurationSeconds { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
    }
}
