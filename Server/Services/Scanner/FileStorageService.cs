using Microsoft.AspNetCore.Http;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;

namespace Server.Services
{
    public interface IFileStorageService
    {
        Task<string> SaveFileAsync(IFormFile file, int batchId);
        Task<byte[]> GetFileAsync(string fileName, int batchId);
        Task DeleteFileAsync(string fileName, int batchId);
        Task<string> GetBasePathAsync(int batchId);
        Task<string> GetBatchPathAsync(int batchId);
        Task UpdateBatchMetadataAsync(int batchId);
    }

    public class FileStorageService : IFileStorageService
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<FileStorageService> _logger;
        private readonly IBatchRepository _batchRepository;
        private readonly IVerifyRepository _verifyRepository;
        private readonly string _connectionString;
        private readonly string _provider;
        private string _basePath = string.Empty;

        public FileStorageService(
            IConfigurationService configService,
            ILogger<FileStorageService> logger,
            IBatchRepository batchRepository,
            IVerifyRepository verifyRepository,
            IConfiguration configuration)
        {
            _configService = configService;
            _logger = logger;
            _batchRepository = batchRepository;
            _verifyRepository = verifyRepository;
            _connectionString = configuration.GetConnectionString("TrueCaptureDb") ?? throw new InvalidOperationException("Connection string not configured");
            _provider = configuration.GetSection("ConnectionStrings")["DatabaseProvider"] ?? "PostgreSql";
        }

        private System.Data.IDbConnection CreateConnection()
        {
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                return new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            }
            return new Npgsql.NpgsqlConnection(_connectionString);
        }

        public async Task<string> GetBatchPathAsync(int batchId)
        {
            if (string.IsNullOrEmpty(_basePath))
            {
                _basePath = await _configService.GetConfigurationsValue("Batch Folder") ?? @"C:\TrueCapture\ICBatches";
            }

            try
            {
                using var conn = CreateConnection();

                // Query Batch and ObjectTypes directly with explicit typed mapping
                string sql;
                if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    sql = @"SELECT b.BatchName, o.Name AS AppName
                            FROM Batch b
                            LEFT JOIN ObjectTypes o ON b.BatchTypeId = o.Id
                            WHERE b.ID = @batchId";
                }
                else
                {
                    // PostgreSQL: use lowercase aliases to be safe
                    sql = @"SELECT b.batchname, o.name AS appname
                            FROM batch b
                            LEFT JOIN objecttypes o ON b.batchtypeid = o.id
                            WHERE b.id = @batchId";
                }

                _logger.LogDebug("Querying batch info for path resolution: {BatchId}", batchId);
                var row = await conn.QueryFirstOrDefaultAsync<(string batchname, string appname)>(sql, new { batchId });

                if (row.batchname != null)
                {
                    string batchName = row.batchname;
                    string appName = row.appname ?? "Unsorted";

                    _logger.LogDebug("Resolved batch info: AppName={AppName}, BatchName={BatchName}", appName, batchName);

                    appName = SanitizeFolderName(appName);
                    batchName = SanitizeFolderName(batchName);

                    var newPath = Path.Combine(_basePath, appName, batchName);
                    _logger.LogInformation("Using hierarchical folder for BatchId {BatchId}: {Path}", batchId, newPath);
                    return newPath;
                }
                else
                {
                    _logger.LogWarning("No batch info found for BatchId {BatchId} - falling back to ID-based folder", batchId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving batch path for {BatchId}", batchId);
            }

            // Fallback if DB query fails or batch not found
            return Path.Combine(_basePath, batchId.ToString());
        }

        private string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return sanitized.Trim();
        }

        public async Task UpdateBatchMetadataAsync(int batchId)
        {
            try
            {
                var batchInfo = await _batchRepository.GetBatchByIdAsync(batchId);
                if (batchInfo == null) return;

                var batchTypeName = await _verifyRepository.GetBatchTypeNameAsync(batchId);
                var batchPath = await GetBatchPathAsync(batchId);

                if (batchPath == null) return;

                if (!Directory.Exists(batchPath))
                {
                    Directory.CreateDirectory(batchPath);
                }

                var batchDetailJson = new BatchInfoJsonDto
                {
                    BatchType = batchTypeName,
                    BatchId = batchId.ToString(),
                    BatchName = batchInfo.BatchName,
                    CreatedDate = batchInfo.CreatedOn,
                    CreatedBy = batchInfo.CreatedBy,
                    TotalDocuments = 0, // Simplified for performance
                    TotalPages = 0,
                    Source = "Inbound",
                    Properties = new Dictionary<string, string>(),
                    Documents = new List<DocumentJsonDto>()
                };

                var jsonString = System.Text.Json.JsonSerializer.Serialize(new { batch = batchDetailJson }, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

                var jsonFileName = $"{batchTypeName}_{batchInfo.BatchName}.json";
                var jsonPath = Path.Combine(batchPath, jsonFileName);

                await File.WriteAllTextAsync(jsonPath, jsonString);
                _logger.LogInformation("Metadata JSON updated for batch {BatchId} at {Path}", batchId, jsonPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating metadata JSON for batch {BatchId}", batchId);
            }
        }

        public async Task<string> SaveFileAsync(IFormFile file, int batchId)
        {
            try
            {
                var batchPath = await GetBatchPathAsync(batchId);
                if (!Directory.Exists(batchPath))
                {
                    Directory.CreateDirectory(batchPath);
                }

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                var filePath = Path.Combine(batchPath, fileName);

                _logger.LogInformation("Saving file to: {FilePath}", filePath);

                using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream);

                return fileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving file for batch {BatchId}", batchId);
                throw;
            }
        }

        public async Task<byte[]> GetFileAsync(string fileName, int batchId)
        {
            try
            {
                var batchPath = await GetBatchPathAsync(batchId);
                var filePath = Path.Combine(batchPath, fileName);

                _logger.LogDebug("Attempting to read file: {FilePath}", filePath);

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("File not found: {FilePath}", filePath);
                    return Array.Empty<byte>();
                }

                return await System.IO.File.ReadAllBytesAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading file {FileName} for batch {BatchId}", fileName, batchId);
                return Array.Empty<byte>();
            }
        }

        public async Task DeleteFileAsync(string fileName, int batchId)
        {
            var batchPath = await GetBatchPathAsync(batchId);
            var filePath = Path.Combine(batchPath, fileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
                _logger.LogInformation("Deleted file: {FilePath}", filePath);
            }
        }

        public async Task<string> GetBasePathAsync(int batchId)
        {
            return await GetBatchPathAsync(batchId);
        }
    }
}
