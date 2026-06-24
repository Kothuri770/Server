using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;

namespace Server.Services;

public interface IFileBasedBatchLogService
{
    Task LogBatchOperationAsync(int batchId, string taskType, string taskDescription, string status, string? errorMessage = null, string? details = null, int? documentId = null, string? userId = null);
    Task<string[]> GetBatchLogFilesAsync(int batchId);
    Task<string> ReadBatchLogFileAsync(int batchId, string fileName);
}

public class FileBasedBatchLogService : IFileBasedBatchLogService
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<FileBasedBatchLogService> _logger;

    public FileBasedBatchLogService(IConfigurationService configService, ILogger<FileBasedBatchLogService> logger)
    {
        _logger = logger;
        _configService = configService;
    }

    public async Task LogBatchOperationAsync(int batchId, string taskType, string taskDescription, string status, string? errorMessage = null, string? details = null, int? documentId = null, string? userId = null)
    {
        try
        {
            // Get the batch folder path from configuration
            var batchFolderPath = await _configService.GetConfigurationsValue("Batch Folder");
            
            // Fallback to temp path if batch folder is not configured
            if (string.IsNullOrEmpty(batchFolderPath))
            {
                batchFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Batches");
            }
            
            // Create batch-specific directory in the batch folder
            var batchLogPath = Path.Combine(batchFolderPath, batchId.ToString(), "Logs");
            Directory.CreateDirectory(batchLogPath);

            // Create log entry with timestamp and complete details
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] {taskType} | {status} | {taskDescription}";
            
            if (documentId.HasValue)
            {
                logEntry += $" | DocumentId: {documentId.Value}";
            }
            
            if (!string.IsNullOrEmpty(userId))
            {
                logEntry += $" | UserId: {userId}";
            }
            
            if (!string.IsNullOrEmpty(details))
            {
                logEntry += $" | Details: {details}";
            }
            
            if (!string.IsNullOrEmpty(errorMessage))
            {
                logEntry += $" | ERROR: {errorMessage}";
            }

            // Define log file name with current date
            var logFileName = $"Batch_{batchId}_Log_{DateTime.Now:yyyyMMdd}.txt";
            var logFilePath = Path.Combine(batchLogPath, logFileName);

            // Write log entry to file (append mode)
            await File.AppendAllTextAsync(logFilePath, logEntry + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing batch log to file for batch {BatchId}", batchId);
        }
    }

    public async Task<string[]> GetBatchLogFilesAsync(int batchId)
    {
        try
        {
            // Get the batch folder path from configuration
            var batchFolderPath = await _configService.GetConfigurationsValue("Batch Folder");
            
            // Fallback to temp path if batch folder is not configured
            if (string.IsNullOrEmpty(batchFolderPath))
            {
                batchFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Batches");
            }
            
            // Create batch-specific directory in the batch folder
            var batchLogPath = Path.Combine(batchFolderPath, batchId.ToString(), "Logs");
            
            if (!Directory.Exists(batchLogPath))
            {
                return new string[0];
            }

            var logFiles = Directory.GetFiles(batchLogPath, "Batch_*_Log_*.txt")
                                   .Select(Path.GetFileName)
                                   .OrderByDescending(f => f) // Sort by filename descending (most recent first)
                                   .ToArray();

            return await Task.FromResult(logFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving batch log files for batch {BatchId}", batchId);
            return new string[0];
        }
    }

    public async Task<string> ReadBatchLogFileAsync(int batchId, string fileName)
    {
        try
        {
            // Get the batch folder path from configuration
            var batchFolderPath = await _configService.GetConfigurationsValue("Batch Folder");
            
            // Fallback to temp path if batch folder is not configured
            if (string.IsNullOrEmpty(batchFolderPath))
            {
                batchFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Batches");
            }
            
            // Create batch-specific directory in the batch folder
            var batchLogPath = Path.Combine(batchFolderPath, batchId.ToString(), "Logs");
            var filePath = Path.Combine(batchLogPath, fileName);
            
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Log file not found: {fileName}");
            }

            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading batch log file {FileName} for batch {BatchId}", fileName, batchId);
            throw;
        }
    }
}
