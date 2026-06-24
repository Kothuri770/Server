using Dapper;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Server.Repositories
{
    public interface IVerifyRepository
    {
        Task<IEnumerable<DocumentModel>> GetDocumentsForVerifyAsync(int batchId);
        Task<IEnumerable<PageModel>> GetPagesForVerifyAsync(int batchId);
        Task<IEnumerable<IndexFieldDto>> GetIndexFieldsAsync(int docTypeId);
        Task<IEnumerable<IndexFieldDto>> GetBatchIndexFieldsAsync(int batchTypeId);
        Task<Dictionary<string, string>> GetIndexValuesAsync(int batchId, int docId);
        Task<Dictionary<string, string>> GetBatchIndexValuesAsync(int batchId);
        Task<Dictionary<string, string>> GetIndexValuesForVerifyAsync(int batchId, int docId);
        Task<Dictionary<string, string>> GetBatchIndexValuesForVerifyAsync(int batchId);
        Task<string> GetBatchTypeNameAsync(int batchId);
        Task<IEnumerable<string>> GetDocumentTypeNamesAsync(int batchId);
        Task<IEnumerable<string>> GetDocumentNamesAsync(int batchId, int docTypeId);
        Task<List<int>> GetDocumentPageNumbersAsync(int batchId, int docIdHandle);
        Task<bool> SaveIndexDataAsync(SaveIndexDataDto data);
        Task<bool> SaveBatchIndexDataAsync(int batchId, Dictionary<string, string> batchValues);
        Task<bool> SaveDocumentIndexDataAsync(int docId, Dictionary<string, string> docValues);
        Task<string> GetPageImageAsync(int pageId);
        Task<(string FilePath, string FileName)> GetPageFilePathAsync(int pageId);
        Task<string> GenerateThumbnailAsync(int pageId, string fileName);

        Task<OcrResult> ExtractOcrValueAsync(int docId, int zoneId);
        Task<string> GetDocumentTypeNameAsync(int docTypeId);
        Task<BatchInfoDto> GetBatchInfoAsync(int batchId);
        Task<IEnumerable<ZoneDto>> GetZonesForDocumentTypeAsync(int docTypeId);
        Task<int> GetDocumentTypeIdByBatchIdAsync(int batchId);
        Task<string> ExtractTextFromZoneAsync(ZoneExtractionRequest request);
        Task SaveOcrResultAsync(int docId, int zoneId, string extractedText, double confidence = 95.0);
        Task UpdateDocumentFieldFromOcrAsync(int docId, int zoneId, string extractedText);
        Task<IEnumerable<OcrResult>> GetOcrResultsByDocumentIdAsync(int docId);
        Task SaveAzureDocIntelResultAsync(int docId, string analysisResult);
        Task<string> GetAzureDocIntelResultsByDocumentIdAsync(int docId);
        Task<AzureDocIntelResult?> GetAzureDocIntelResultByDocumentIdAsync(int docId);
        Task<DocumentInfoDto?> GetDocumentInfoByIdAsync(int docId);
        
        Task<IEnumerable<DocumentModel>> GetUncategorizedDocumentsAsync(int batchId);
        Task<IEnumerable<DocumentModel>> GetDocumentsInBatchAsync(int batchId);
        Task<IEnumerable<PageModel>> GetDocumentPagesAsync(int docTypeId);
        Task<bool> UpdateDocumentTypeAsync(int pageId, int newDocTypeId);
        Task<bool> MovePagesToDocumentAsync(int sourcePageId, int targetPageId);
        Task<bool> DeleteDocumentAsync(int pageId);
        Task<string> GetDocumentTypeNameByIdAsync(int docTypeId);
        Task<bool> UpdateDocumentNameAsync(int pageId, string newName);
        Task<int> GetNextDocumentSequenceAsync(int batchId, int docTypeId, int pageId);
        Task<bool> UpdateBatchDetailMappingAsync(int pageId, int docTypeId, int docPage, string docName);
        Task<bool> UpdateBatchDetailStatusAsync(int pageId, string status);
        Task<bool> UpdateDocumentStatusAsync(int pageId, string status);
        Task<BatchVerificationContextDto> GetBatchVerificationContextAsync(int batchId);
        Task<bool> BulkSaveIndexDataAsync(BulkSaveIndexDto data);
        Task EnsureDoctableExistsAsync(int docTypeId);
    }
    public class VerifyRepository : BaseRepository, IVerifyRepository
    {
        private static readonly string[] NumericTypes = { "numeric", "decimal", "real", "double" };

        // #8: Cache dynamic table existence to avoid hitting information_schema on every request
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool Exists, DateTime CachedAt)> _tableExistsCache = new();
        private static readonly TimeSpan _tableCacheTtl = TimeSpan.FromMinutes(5);

        public VerifyRepository(string connectionString, string provider) : base(connectionString, provider) { }
      
        public async Task<IEnumerable<DocumentModel>> GetDocumentsForVerifyAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT MIN(bd.ID) as DocId, bd.DocName, bd.DocTypeId, dt.Name as DocTypeName, 
                       CASE WHEN MIN(bd.Status) = 'A' THEN 1 ELSE 0 END as Status
                FROM BatchDetail bd
                LEFT JOIN ObjectTypes dt ON bd.DocTypeId = dt.Id
                WHERE bd.BatchID = @batchId AND bd.Status = 'A'
                GROUP BY bd.DocTypeId, bd.DocName, dt.Name
                ORDER BY MIN(bd.ID), bd.DocName";
            return await conn.QueryAsync<DocumentModel>(sql, new { batchId });
        }

        public async Task<IEnumerable<PageModel>> GetPagesForVerifyAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT bd.ID as PageId, 
                       (SELECT MIN(ID) FROM BatchDetail WHERE BatchID = bd.BatchID AND (DocName = bd.DocName OR (DocName IS NULL AND bd.DocName IS NULL))) as DocId, 
                       bd.FileName, 
                       '' as Thumbnail, 0 as Rotation, bd.DocPage, 'Page' as PageType,
                       COALESCE(bd.DocName, '') as DocName, bd.originalFilename as OriginalFilename,
                       bd.Format as Format
                FROM BatchDetailWithDocType bd
                WHERE bd.BatchID = @batchId AND bd.Status = 'A'
                ORDER BY bd.ID, bd.DocPage";
            return await conn.QueryAsync<PageModel>(sql, new { batchId });
        }

        public async Task<IEnumerable<IndexFieldDto>> GetIndexFieldsAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var concatOp = isSqlServer ? " + " : " || ";
            var isEditableCol = isSqlServer ? "COALESCE(dp.IsEnabled, 1)" : "COALESCE(dp.IsEnabled, TRUE)";
            var caseLookup = isSqlServer ? "CAST(CASE WHEN dp.LookupId IS NOT NULL THEN 1 ELSE 0 END AS BIT)" : "(dp.LookupId IS NOT NULL)";

            var sql = $@"
                SELECT 'Column_' {concatOp} CAST(dp.PropertyId AS VARCHAR) as ColumnId, dp.Propertyname as Label, 
                        dp.PropertyType as Type, dp.IsRequired as Required, {isEditableCol} as IsEditable,
                        {caseLookup} as IsLookup, dp.ZoneId as ZoneId, dp.Length as Length
                FROM DocTypeProperties dp
                WHERE dp.DocTypeId = @docTypeId
                ORDER BY dp.PropertyOrder";

            return await conn.QueryAsync<IndexFieldDto>(sql, new { docTypeId });
        }

        public async Task<IEnumerable<IndexFieldDto>> GetBatchIndexFieldsAsync(int batchTypeId)
        {
            using var conn = CreateConnection();
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var concatOp = isSqlServer ? " + " : " || ";
            var isEditableCol = isSqlServer ? "COALESCE(bp.IsEnabled, 1)" : "COALESCE(bp.IsEnabled, TRUE)";
            var caseLookup = isSqlServer ? "CAST(CASE WHEN bp.LookupId IS NOT NULL THEN 1 ELSE 0 END AS BIT)" : "(bp.LookupId IS NOT NULL)";

            var sql = $@"
                SELECT 'Column_' {concatOp} CAST(bp.PropertyId AS VARCHAR) as ColumnId, bp.Propertyname as Label, 
                        bp.PropertyType as Type, bp.IsRequired as Required, {isEditableCol} as IsEditable,
                        {caseLookup} as IsLookup, bp.ZoneID as ZoneId, bp.Length as Length
                FROM BatchTypeProperties bp
                WHERE bp.BatchTypeId = @batchTypeId
                ORDER BY bp.PropertyOrder";

            return await conn.QueryAsync<IndexFieldDto>(sql, new { batchTypeId });
        }

        public async Task<Dictionary<string, string>> GetIndexValuesAsync(int batchId, int docId)
        {
            using var conn = CreateConnection();
            var docTypeId = await conn.QuerySingleAsync<int>("SELECT DocTypeId FROM BatchDetail WHERE ID = @docId", new { docId });
            var tableName = $"doctable_{docTypeId}";
            
            bool tableExists = await CheckTableExistsAsync(conn, tableName);
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var columnToPropertyMap = await GetPropertyToColumnMapAsync(docTypeId, false, conn);
            
            if (tableExists)
            {
                var docSql = $"SELECT * FROM {tableName} WHERE docid = @docId";
                var docRow = await conn.QueryFirstOrDefaultAsync(docSql, new { docId });
                if (docRow != null)
                {
                    MapRowToDictionary(results, (IDictionary<string, object>)docRow, columnToPropertyMap);
                }
            }
            
            var properties = await GetDocTypePropertiesWithZonesAsync(conn, docTypeId);
            foreach (var prop in properties)
            {
                if (!results.ContainsKey(prop.PropertyName))
                {
                    string? ocrValue = null;
                    if (prop.ZoneId.HasValue)
                    {
                        ocrValue = await conn.QueryFirstOrDefaultAsync<string>(
                            "SELECT ocrvalue FROM OCRResults WHERE DocId = @docId AND ZoneId = @zoneId",
                            new { docId, zoneId = prop.ZoneId.Value });
                    }
                                
                    results[prop.PropertyName] = ocrValue ?? "";
                }

                // Enforce DefaultValue and IsEnabled
                if (!prop.IsEnabled)
                {
                    results[prop.PropertyName] = prop.DefaultValue ?? "";
                }
                else if (string.IsNullOrEmpty(results[prop.PropertyName]))
                {
                    results[prop.PropertyName] = prop.DefaultValue ?? "";
                }
            }
            return results;
        }

        private async Task<bool> CheckTableExistsAsync(IDbConnection conn, string tableName)
        {
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                return await conn.QuerySingleOrDefaultAsync<bool>(
                    "SELECT CAST(CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = @TableName) THEN 1 ELSE 0 END AS BIT)", 
                    new { TableName = tableName });
            }
            return await conn.QuerySingleOrDefaultAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = @TableName)", 
                new { TableName = tableName });
        }

        private async Task<Dictionary<string, string>> GetPropertyToColumnMapAsync(int typeId, bool isBatch, IDbConnection conn)
        {
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var concatOp = isSqlServer ? " + " : " || ";
            
            string sql;
            if (isBatch)
            {
                sql = $@"SELECT p.PropertyName, 'column_' {concatOp} CAST(bp.PropertyId AS VARCHAR) as ColumnName 
                         FROM BatchTypeProperties bp JOIN Property p ON bp.PropertyId = p.Id WHERE bp.BatchTypeId = @typeId";
            }
            else
            {
                sql = $@"SELECT PropertyName, 'column_' {concatOp} CAST(PropertyId AS VARCHAR) as ColumnName 
                         FROM DocTypeProperties WHERE DocTypeId = @typeId";
            }

            var mappings = await conn.QueryAsync<(string PropertyName, string ColumnName)>(sql, new { typeId });
            return mappings.ToDictionary(p => p.ColumnName, p => p.PropertyName, StringComparer.OrdinalIgnoreCase);
        }

        private static void MapRowToDictionary(Dictionary<string, string> results, IDictionary<string, object> row, Dictionary<string, string> mappings)
        {
            foreach (var kvp in row)
            {
                if (mappings.TryGetValue(kvp.Key, out string? propertyName))
                {
                    results[propertyName] = kvp.Value switch
                    {
                        DateTime dt => dt.ToString("yyyy-MM-dd"),
                        DateTimeOffset dto => dto.ToString("yyyy-MM-dd"),
                        _ => kvp.Value?.ToString() ?? ""
                    };
                }
            }
        }

        private async Task<IEnumerable<(string PropertyKey, int PropertyId, int? ZoneId, string PropertyName, string? DefaultValue, bool IsEnabled)>> GetDocTypePropertiesWithZonesAsync(IDbConnection conn, int docTypeId)
        {
            var concatOp = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? " + " : " || ";
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var isEnabledCol = isSqlServer ? "COALESCE(dp.IsEnabled, 1)" : "COALESCE(dp.IsEnabled, TRUE)";
            string sql = $"SELECT 'column_' {concatOp} CAST(dp.PropertyId AS VARCHAR) as PropertyKey, dp.PropertyId, dp.ZoneId, p.PropertyName, dp.DefaultValue, {isEnabledCol} as IsEnabled FROM DocTypeProperties dp JOIN Property p ON dp.PropertyId = p.Id WHERE dp.DocTypeId = @docTypeId ORDER BY dp.PropertyOrder";

            return await conn.QueryAsync<(string PropertyKey, int PropertyId, int? ZoneId, string PropertyName, string? DefaultValue, bool IsEnabled)>(sql, new { docTypeId });
        }

        public async Task<Dictionary<string, string>> GetBatchIndexValuesAsync(int batchId)
        {
            using var conn = CreateConnection();
            var batchTypeId = await conn.QuerySingleAsync<int>("SELECT BatchTypeId FROM Batch WHERE ID = @batchId", new { batchId });
            var tableName = $"batchtable_{batchTypeId}";
            
            if (!await CheckTableExistsAsync(conn, tableName))
            {
                return new Dictionary<string, string>();
            }
            
            var row = await conn.QueryFirstOrDefaultAsync($"SELECT * FROM {tableName} WHERE batchid = @batchId", new { batchId });
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var columnToPropertyMap = await GetPropertyToColumnMapAsync(batchTypeId, true, conn);
            
            if (row != null)
            {
                MapRowToDictionary(results, (IDictionary<string, object>)row, columnToPropertyMap);
            }

            var properties = await GetBatchTypePropertiesAsync(conn, batchTypeId);
            foreach (var prop in properties)
            {
                if (!results.ContainsKey(prop.PropertyName))
                {
                    results[prop.PropertyName] = "";
                }

                // Enforce DefaultValue and IsEnabled
                if (!prop.IsEnabled)
                {
                    results[prop.PropertyName] = prop.DefaultValue ?? "";
                }
                else if (string.IsNullOrEmpty(results[prop.PropertyName]))
                {
                    results[prop.PropertyName] = prop.DefaultValue ?? "";
                }
            }
            
            return results;
        }

        private async Task<IEnumerable<(string PropertyKey, int PropertyId, string PropertyName, string? DefaultValue, bool IsEnabled)>> GetBatchTypePropertiesAsync(IDbConnection conn, int batchTypeId)
        {
            var concatOp = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? " + " : " || ";
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var isEnabledCol = isSqlServer ? "COALESCE(bp.IsEnabled, 1)" : "COALESCE(bp.IsEnabled, TRUE)";
            string propertySql = $"SELECT 'column_' {concatOp} CAST(bp.PropertyId AS VARCHAR) as PropertyKey, bp.PropertyId, p.PropertyName, bp.DefaultValue, {isEnabledCol} as IsEnabled FROM BatchTypeProperties bp JOIN Property p ON bp.PropertyId = p.Id WHERE bp.BatchTypeId = @batchTypeId ORDER BY bp.PropertyOrder";

            return await conn.QueryAsync<(string PropertyKey, int PropertyId, string PropertyName, string? DefaultValue, bool IsEnabled)>(propertySql, new { batchTypeId });
        }

        public async Task<bool> SaveIndexDataAsync(SaveIndexDataDto data)
        {
            using var conn = CreateConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var field in data.Fields)
                {
                    if (field.ColumnId.StartsWith("batch_"))
                    {
                        continue;
                    }
                    
                    var propertyId = int.Parse(field.ColumnId.Split('_')[1]);
                    string sql;
                    if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                    {
                        sql = @"
                            MERGE INTO OCRResults AS target
                            USING (SELECT @DocId AS DocId, ZoneId, @Value AS OCRValue, 95.0 AS OCRConfidence
                                   FROM DocumentClassDetail
                                   WHERE DocTypeId = (SELECT DocTypeId FROM BatchDetail WHERE ID = @DocId)
                                   AND PropertyId = @propertyId) AS source
                            ON (target.DocId = source.DocId AND target.ZoneId = source.ZoneId)
                            WHEN MATCHED THEN
                                UPDATE SET OCRValue = source.OCRValue, OCRConfidence = source.OCRConfidence
                            WHEN NOT MATCHED THEN
                                INSERT (DocId, ZoneId, OCRValue, OCRConfidence)
                                VALUES (source.DocId, source.ZoneId, source.OCRValue, source.OCRConfidence);";
                    }
                    else
                    {
                        sql = @"
                            INSERT INTO OCRResults (DocId, ZoneId, OCRValue, OCRConfidence)
                            SELECT @DocId, ZoneId, @Value, 95.0
                            FROM DocumentClassDetail
                            WHERE DocTypeId = (SELECT DocTypeId FROM BatchDetail WHERE ID = @DocId)
                            AND PropertyId = @propertyId
                            ON CONFLICT (DocId, ZoneId) DO UPDATE 
                            SET OCRValue = @Value, OCRConfidence = 95.0";
                    }
                    await conn.ExecuteAsync(sql, new { data.DocId, Value = field.Value, propertyId, data.BatchId }, transaction);
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> SaveBatchIndexDataAsync(int batchId, Dictionary<string, string> batchValues)
        {
            using var conn = CreateConnection();
            return await SaveBatchIndexDataInternalAsync(batchId, batchValues, conn, null);
        }

        private async Task<bool> SaveBatchIndexDataInternalAsync(int batchId, Dictionary<string, string> batchValues, IDbConnection conn, IDbTransaction? transaction)
        {
            var batchTypeId = await conn.QuerySingleAsync<int>("SELECT BatchTypeId FROM Batch WHERE ID = @batchId", new { batchId }, transaction);
            var tableName = $"batchtable_{batchTypeId}";

            if (!await TableExistsAsync(tableName, conn, transaction)) return false;

            // Mapping direction: PropertyName -> ColumnName for saving
            var propertyToColumnMap = await GetPropertyToColumnMapForSavingAsync(batchTypeId, true, conn, transaction);
            var recordExists = await RecordExistsAsync(tableName, "batchid", batchId, conn, transaction);

            if (batchValues.Count == 0) return true;

            var columnTypes = await GetColumnTypesAsync(tableName, conn, transaction);
            var mappedValues = MapValuesToColumns(batchValues, propertyToColumnMap);

            var (sql, parameters) = BuildDynamicSql(tableName, "batchid", batchId, mappedValues, columnTypes, recordExists);
            if (sql == null) return false;

            var result = await conn.ExecuteAsync(sql, parameters, transaction);
            return result > 0;
        }

        public async Task<bool> SaveDocumentIndexDataAsync(int docId, Dictionary<string, string> docValues)
        {
            using var conn = CreateConnection();
            return await SaveDocumentIndexDataInternalAsync(docId, docValues, conn, null);
        }

        private async Task<bool> SaveDocumentIndexDataInternalAsync(int docId, Dictionary<string, string> docValues, IDbConnection conn, IDbTransaction? transaction)
        {
            var docTypeId = await conn.QueryFirstOrDefaultAsync<int?>("SELECT DocTypeId FROM BatchDetail WHERE ID = @docId", new { docId }, transaction);
            if (docTypeId == null || docTypeId == 0) return true; // Nothing to save for uncategorized documents
            
            var tableName = $"doctable_{docTypeId}";

            if (!await TableExistsAsync(tableName, conn, transaction)) return false;

            // Mapping direction: PropertyName -> ColumnName for saving
            var propertyToColumnMap = await GetPropertyToColumnMapForSavingAsync(docTypeId.Value, false, conn, transaction);
            var recordExists = await RecordExistsAsync(tableName, "docid", docId, conn, transaction);

            if (docValues.Count == 0) return true;

            var columnTypes = await GetColumnTypesAsync(tableName, conn, transaction);
            var mappedValues = MapValuesToColumns(docValues, propertyToColumnMap);

            var (sql, parameters) = BuildDynamicSql(tableName, "docid", docId, mappedValues, columnTypes, recordExists);
            if (sql == null) return false;

            var result = await conn.ExecuteAsync(sql, parameters, transaction);
            return result > 0;
        }

        private async Task<Dictionary<string, string>> GetPropertyToColumnMapForSavingAsync(int typeId, bool isBatch, IDbConnection conn, IDbTransaction? transaction = null)
        {
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var concatOp = isSqlServer ? " + " : " || ";
            
            string sql;
            if (isBatch)
            {
                sql = $@"SELECT p.PropertyName, 'column_' {concatOp} CAST(bp.PropertyId AS VARCHAR) as ColumnName FROM BatchTypeProperties bp JOIN Property p ON bp.PropertyId = p.Id WHERE bp.BatchTypeId = @typeId";
            }
            else
            {
                sql = $@"SELECT PropertyName, 'column_' {concatOp} CAST(PropertyId AS VARCHAR) as ColumnName FROM DocTypeProperties WHERE DocTypeId = @typeId";
            }

            var mappings = await conn.QueryAsync<(string PropertyName, string ColumnName)>(sql, new { typeId }, transaction);
            return mappings.ToDictionary(m => m.PropertyName, m => m.ColumnName, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<string> GetPageImageAsync(int pageId)
        {
            var (filePath, _) = await GetPageFilePathAsync(pageId);

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                var bytes = await File.ReadAllBytesAsync(filePath);
                return Convert.ToBase64String(bytes);
            }
            return string.Empty;
        }

        // #18: Cache base path — it rarely changes at runtime
        private static string? _cachedBasePath;
        private static DateTime _basePathCachedAt = DateTime.MinValue;

        public async Task<(string FilePath, string FileName)> GetPageFilePathAsync(int pageId)
        {
            using var conn = CreateConnection();
            // #18: Combine page info + batch data into a single JOIN query (3 queries → 1)
            var pageData = await conn.QueryFirstOrDefaultAsync<(string FileName, int BatchId, string BatchName, string AppName)>(@"
                SELECT bd.FileName, bd.BatchID as BatchId, b.BatchName, o.Name as AppName 
                FROM BatchDetail bd 
                JOIN Batch b ON bd.BatchID = b.ID
                LEFT JOIN ObjectTypes o ON b.BatchTypeId = o.Id 
                WHERE bd.ID = @pageId", new { pageId });

            if (string.IsNullOrEmpty(pageData.FileName) || pageData.BatchId <= 0)
            {
                return (string.Empty, string.Empty);
            }

            var fileName = pageData.FileName;
            var batchId = pageData.BatchId;

            // #18: Cache the base path — only query config table once every 5 minutes
            if (_cachedBasePath == null || (DateTime.UtcNow - _basePathCachedAt).TotalMinutes > 5)
            {
                _cachedBasePath = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT ConfigValue FROM Configuration WHERE ConfigName = 'Batch Folder'") ?? Constants.DefaultBatchFolder;
                _basePathCachedAt = DateTime.UtcNow;
            }
            var basePath = _cachedBasePath;

            string finalBatchFolder = batchId.ToString();
            if (!string.IsNullOrEmpty(pageData.BatchName))
            {
                var appName = SanitizeFolderName(pageData.AppName ?? "Unsorted");
                var batchName = SanitizeFolderName(pageData.BatchName);
                var newFilePath = Path.Combine(basePath, appName, batchName, fileName);

                if (File.Exists(newFilePath))
                {
                    finalBatchFolder = Path.Combine(appName, batchName);
                }
                else if (File.Exists(Path.Combine(basePath, batchId.ToString(), fileName)))
                {
                    finalBatchFolder = batchId.ToString();
                }
                else
                {
                    finalBatchFolder = Path.Combine(appName, batchName);
                }
            }

            var filePath = Path.Combine(basePath, finalBatchFolder, fileName);
            return (filePath, fileName);
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return sanitized.Trim();
        }

        public async Task<string> GenerateThumbnailAsync(int pageId, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;

            var filePath = Path.Combine("uploads", fileName);
            if (File.Exists(filePath))
            {
                try
                {
                    // Load the image
                    using var image = await Image.LoadAsync(filePath);
                    
                    // Resize to thumbnail size (80x100 pixels)
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(80, 100),
                        Mode = ResizeMode.Max
                    }));
                    
                    // Save as JPEG to memory stream
                    using var ms = new MemoryStream();
                    await image.SaveAsJpegAsync(ms);
                    return Convert.ToBase64String(ms.ToArray());
                }
                catch
                {
                    // Return empty string if thumbnail generation fails
                    return string.Empty;
                }
            }
            return string.Empty;
        }
        public async Task<OcrResult> ExtractOcrValueAsync(int docId, int zoneId)
        {
            using var conn = CreateConnection();
            var isSqlServer = _provider == "SqlServer";
            var sql = isSqlServer
                ? @"SELECT TOP 1 ZoneId, OCRValue, OCRConfidence as Confidence FROM OCRResults WHERE DocId = @docId AND ZoneId = @zoneId ORDER BY CreatedOn DESC"
                : @"SELECT ZoneId, OCRValue, OCRConfidence as Confidence FROM OCRResults WHERE DocId = @docId AND ZoneId = @zoneId ORDER BY CreatedOn DESC LIMIT 1";

            return await conn.QueryFirstOrDefaultAsync<OcrResult>(sql, new { docId, zoneId })
                   ?? new OcrResult();
        }



        public async Task<string> GetBatchTypeNameAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT Name
                FROM AllBatchesWithTypeName
                WHERE ID = @batchId";
            return await conn.QueryFirstOrDefaultAsync<string>(sql, new { batchId }) ?? "Unknown Batch";
        }

        public async Task<IEnumerable<string>> GetDocumentTypeNamesAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT DISTINCT ot.Name
                FROM BatchDetail bd
                INNER JOIN ObjectTypes ot ON ot.Id = bd.DocTypeId
                WHERE bd.BatchID = @batchId AND bd.Status = 'A' AND bd.DocName IS NOT NULL
                ORDER BY ot.Name";
            return await conn.QueryAsync<string>(sql, new { batchId });
        }

        public async Task<IEnumerable<string>> GetDocumentNamesAsync(int batchId, int docTypeId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT DISTINCT DocName
                FROM BatchDetail
                WHERE BatchID = @batchId AND DocTypeId = @docTypeId AND Status = 'A' AND DocName IS NOT NULL
                ORDER BY DocName";
            return await conn.QueryAsync<string>(sql, new { batchId, docTypeId });
        }

        public async Task<List<int>> GetDocumentPageNumbersAsync(int batchId, int docIdHandle)
        {
            using var conn = CreateConnection();
            // Using the first page ID (docIdHandle) to identify the document group (by DocName)
            var sql = @"
                SELECT PageNo 
                FROM BatchDetail 
                WHERE BatchID = @batchId 
                AND DocName = (SELECT DocName FROM BatchDetail WHERE ID = @docIdHandle)
                AND Status = 'A'
                ORDER BY PageNo";
            var result = await conn.QueryAsync<int>(sql, new { batchId, docIdHandle });
            return result.ToList();
        }
        public async Task<BatchInfoDto> GetBatchInfoAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT ID, BatchName, BatchTypeId, CreatedOn, BatchStatus, StepID as StepId, userName as CreatedBy
                FROM AllBatchesWithTypeName 
                WHERE ID = @batchId";

            var result = await conn.QuerySingleOrDefaultAsync<BatchInfoDto>(sql, new { batchId });
            return result ?? new BatchInfoDto();
        }

        public async Task<int> GetDocumentTypeIdByBatchIdAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = _provider == "SqlServer"
                ? "SELECT DISTINCT TOP 1 DocTypeId FROM BatchDetail WHERE BatchID = @batchId AND DocName IS NOT NULL"
                : "SELECT DISTINCT DocTypeId FROM BatchDetail WHERE BatchID = @batchId AND DocName IS NOT NULL LIMIT 1";
            var docTypeId = await conn.QueryFirstOrDefaultAsync<int?>(sql, new { batchId });
            return docTypeId ?? 0;
        }

        public async Task<string> GetDocumentTypeNameAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var sql = "SELECT Name FROM ObjectTypes WHERE Id = @docTypeId";
            return await conn.QueryFirstOrDefaultAsync<string>(sql, new { docTypeId }) ?? "Unknown Document Type";
        }
        
        public async Task<Dictionary<string, string>> GetIndexValuesForVerifyAsync(int batchId, int docId)
        {
            using var conn = CreateConnection();
            var docTypeId = await conn.QueryFirstOrDefaultAsync<int>("SELECT doctypeid FROM BatchDetail WHERE ID = @docId", new { docId });
            var tableName = $"doctable_{docTypeId}";
            var tableExists = await TableExistsAsync(tableName, conn);
            
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var columnToPropertyMap = await GetColumnToPropertyIdMapAsync(conn, docTypeId, false);
            
            if (tableExists)
            {
                var docRow = await conn.QueryFirstOrDefaultAsync(
                    $"SELECT * FROM {tableName} WHERE docid = @docId", new { docId });
                
                if (docRow != null)
                {
                    MapDataRowToDictionary((IDictionary<string, object>)docRow, columnToPropertyMap, results);
                }
            }
            
            var properties = await GetDocPropertiesWithZonesAsync(conn, docTypeId);
            await FillMissingWithOcrResultsAsync(conn, docId, properties, results);
            
            // Enforce DefaultValue and IsEnabled for document properties
            foreach (var prop in properties)
            {
                if (prop.PropertyKey == null) continue;

                // If disabled, enforce DefaultValue
                if (!prop.IsEnabled)
                {
                    results[prop.PropertyKey] = prop.DefaultValue ?? "";
                }
                // If not disabled but the value in results is empty/null, fall back to DefaultValue if present
                else if (!results.TryGetValue(prop.PropertyKey, out var val) || string.IsNullOrEmpty(val))
                {
                    results[prop.PropertyKey] = prop.DefaultValue ?? "";
                }
            }
                        
            return results;
        }
        
        public async Task<Dictionary<string, string>> GetBatchIndexValuesForVerifyAsync(int batchId)
        {
            using var conn = CreateConnection();
            var batchTypeId = await conn.QuerySingleAsync<int>("SELECT BatchTypeId FROM Batch WHERE ID = @batchId", new { batchId });
            var tableName = $"batchtable_{batchTypeId}";
            var tableExists = await TableExistsAsync(tableName, conn);
            
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            if (tableExists)
            {
                var row = await conn.QueryFirstOrDefaultAsync(
                    $"SELECT * FROM {tableName} WHERE batchid = @batchId", new { batchId });
                
                var columnToPropertyMap = await GetColumnToPropertyIdMapAsync(conn, batchTypeId, true);
                
                if (row != null)
                {
                    MapDataRowToDictionary((IDictionary<string, object>)row, columnToPropertyMap, results);
                }
            }
            
            await EnsureAllBatchPropertiesRepresentedAsync(conn, batchTypeId, results);
            return results;
        }
        
        public async Task<IEnumerable<ZoneDto>> GetZonesForDocumentTypeAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT ID, Name, LeftX, TopY, RightX, BottomY, DocTypeID, PageNo, Type, StartPosition, Length, DisplayedWidth, DisplayedHeight
                FROM Zones 
                WHERE DocTypeID = @docTypeId";
            
            var zones = await conn.QueryAsync<ZoneDto>(sql, new { docTypeId });
            return zones;
        }
        
        public Task<string> ExtractTextFromZoneAsync(ZoneExtractionRequest request)
        {
            // This method will be implemented to use the OCR service
            // For now, return a placeholder - in a real implementation, this would use Tesseract
            return Task.FromResult($"Extracted text from zone at ({request.JsLeft},{request.JsTop}) to ({request.JsRight},{request.JsBottom}) with display size {request.DisplayedWidth}x{request.DisplayedHeight} - OCR result");
        }
        
        public async Task SaveOcrResultAsync(int docId, int zoneId, string extractedText, double confidence = 95.0)
        {
            using var conn = CreateConnection();
            string sql;
            
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    MERGE INTO OCRResults AS target
                    USING (SELECT @docId AS DocId, @zoneId AS ZoneId, @extractedText AS OCRValue, @confidence AS OCRConfidence) AS source
                    ON (target.DocId = source.DocId AND target.ZoneId = source.ZoneId)
                    WHEN MATCHED THEN
                        UPDATE SET OCRValue = source.OCRValue, OCRConfidence = source.OCRConfidence
                    WHEN NOT MATCHED THEN
                        INSERT (DocId, ZoneId, OCRValue, OCRConfidence)
                        VALUES (source.DocId, source.ZoneId, source.OCRValue, source.OCRConfidence);";
            }
            else
            {
                sql = @"
                    INSERT INTO OCRResults (DocId, ZoneId, OCRValue, OCRConfidence)
                    VALUES (@docId, @zoneId, @extractedText, @confidence)
                    ON CONFLICT (DocId, ZoneId) DO UPDATE 
                    SET OCRValue = EXCLUDED.OCRValue, OCRConfidence = EXCLUDED.OCRConfidence;";
            }
            
            await conn.ExecuteAsync(sql, new { docId, zoneId, extractedText, confidence });
        }
        
        public async Task UpdateDocumentFieldFromOcrAsync(int docId, int zoneId, string extractedText)
        {
            using var conn = CreateConnection();
            var propertyName = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT p.PropertyName FROM DocumentClassDetail dc JOIN Property p ON dc.PropertyId = p.Id WHERE dc.ZoneId = @zoneId", 
                new { zoneId });
            
            if (string.IsNullOrEmpty(propertyName)) return;

            var docTypeId = await conn.QueryFirstOrDefaultAsync<int>("SELECT doctypeid FROM BatchDetail WHERE ID = @docId", new { docId });
            var tableName = $"doctable_{docTypeId}";
            if (!await TableExistsAsync(tableName, conn)) return;

            var concatOp = _provider == "SqlServer" ? " + " : " || ";
            var columnName = await conn.QueryFirstOrDefaultAsync<string>(
                $@"SELECT 'column_' {concatOp} CAST(dp.PropertyId AS VARCHAR) FROM DocTypeProperties dp 
                   JOIN Property p ON dp.PropertyId = p.Id WHERE dp.DocTypeId = @docTypeId AND p.PropertyName = @propertyName",
                new { docTypeId, propertyName });

            if (string.IsNullOrEmpty(columnName)) return;

            var columnInfo = await conn.QueryFirstOrDefaultAsync<(string ColumnName, string DataType)>(
                "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = @TableName AND column_name = @ColumnName",
                new { TableName = tableName, ColumnName = columnName });

            var processedValue = ParseValueByColumnType(extractedText, columnInfo.DataType?.ToLower() ?? "text");
            await UpsertDynamicDataAsync(conn, tableName, columnName, docId, processedValue);
        }

        private static object? ParseValueByColumnType(string text, string columnType)
        {
            if (string.IsNullOrEmpty(text)) return null;

            if (columnType.Contains("timestamp") || columnType.Contains("date"))
                return DateTime.TryParse(text, out var dt) ? dt : null;

            if (columnType.Contains("int"))
                return int.TryParse(text, out var i) ? i : null;

            if (NumericTypes.Any(t => columnType.Contains(t)))
                return decimal.TryParse(text, out var d) ? d : null;

            return text;
        }

        private async Task UpsertDynamicDataAsync(IDbConnection conn, string tableName, string columnName, int docId, object? value)
        {
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = $@"
                    MERGE INTO {tableName} AS target
                    USING (SELECT @docId AS docid, @value AS val) AS source
                    ON (target.docid = source.docid)
                    WHEN MATCHED THEN
                        UPDATE SET ""{columnName}"" = source.val
                    WHEN NOT MATCHED THEN
                        INSERT (docid, ""{columnName}"")
                        VALUES (source.docid, source.val);";
            }
            else
            {
                sql = $@"
                    INSERT INTO {tableName} (docid, ""{columnName}"") 
                    VALUES (@docId, @value)
                    ON CONFLICT (docid) DO UPDATE 
                    SET ""{columnName}"" = EXCLUDED.""{columnName}"";";
            }

            await conn.ExecuteAsync(sql, new { value, docId });
        }
        
        public async Task<IEnumerable<OcrResult>> GetOcrResultsByDocumentIdAsync(int docId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT ZoneId, OCRValue, OCRConfidence as Confidence
                FROM OCRResults
                WHERE DocId = @docId";

            return await conn.QueryAsync<OcrResult>(sql, new { docId });
        }
        
        public async Task SaveAzureDocIntelResultAsync(int docId, string analysisResult)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider == "SqlServer")
            {
                sql = @"
                    IF EXISTS (SELECT 1 FROM DocIntelResults WHERE DocId = @DocId)
                    BEGIN
                        UPDATE DocIntelResults SET AnalysisResult = @AnalysisResult, CreatedOn = @CreatedOn WHERE DocId = @DocId
                    END
                    ELSE
                    BEGIN
                        INSERT INTO DocIntelResults (DocId, AnalysisResult, CreatedOn) VALUES (@DocId, @AnalysisResult, @CreatedOn)
                    END";
            }
            else
            {
                sql = @"
                    INSERT INTO DocIntelResults (DocId, AnalysisResult, CreatedOn) 
                    VALUES (@DocId, @AnalysisResult::jsonb, @CreatedOn)
                    ON CONFLICT (DocId) DO UPDATE SET 
                        AnalysisResult = EXCLUDED.AnalysisResult::jsonb,
                        CreatedOn = EXCLUDED.CreatedOn";
            }
            
            try
            {
                await conn.ExecuteAsync(sql, new 
                { 
                    DocId = docId, 
                    AnalysisResult = analysisResult, 
                    CreatedOn = DateTime.UtcNow 
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VerifyRepository] Error executing SaveAzureDocIntelResultAsync for DocId {docId}: {ex.Message}");
                throw; // Re-throw to be caught by the controller
            }
        }
        
        public async Task<string> GetAzureDocIntelResultsByDocumentIdAsync(int docId)
        {
            using var conn = CreateConnection();
            var sql = @"SELECT AnalysisResult FROM DocIntelResults WHERE DocId = @docId";
            
            var result = await conn.QuerySingleOrDefaultAsync<string>(sql, new { docId });
            return result ?? string.Empty;
        }
        
        public async Task<AzureDocIntelResult?> GetAzureDocIntelResultByDocumentIdAsync(int docId)
        {
            using var conn = CreateConnection();
            var sql = @"SELECT id as DocIntelId, DocId, AnalysisResult, CreatedOn as CreatedAt FROM DocIntelResults WHERE DocId = @docId";
            
            var result = await conn.QuerySingleOrDefaultAsync<AzureDocIntelResult>(sql, new { docId });
            return result;
        }
        
        public async Task<DocumentInfoDto?> GetDocumentInfoByIdAsync(int docId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT MIN(ID) as DocId, DocName, DocTypeId, BatchID as BatchId
                FROM BatchDetail
                WHERE DocName = (SELECT DocName FROM BatchDetail WHERE ID = @docId) 
                AND BatchID = (SELECT BatchID FROM BatchDetail WHERE ID = @docId)
                GROUP BY DocName, DocTypeId, BatchID";
            
            var result = await conn.QueryFirstOrDefaultAsync<DocumentInfoDto>(sql, new { docId });
            return result;
        }
        
        public async Task<IEnumerable<DocumentModel>> GetUncategorizedDocumentsAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT MIN(bd.ID) as DocId, bd.DocName, bd.DocTypeId, dt.Name as DocTypeName, 
                       CASE WHEN MIN(bd.Status) = 'A' THEN 1 ELSE 0 END as Status, MAX(bd.FileName) as FileName
                FROM BatchDetail bd
                INNER JOIN ObjectTypes dt ON bd.DocTypeId = dt.Id
                WHERE bd.BatchID = @batchId AND bd.Status = 'A' AND (bd.DocTypeId IS NULL OR bd.DocTypeId = 0) AND bd.DocName IS NOT NULL
                GROUP BY bd.DocName, bd.DocTypeId, dt.Name, bd.BatchID";
            return await conn.QueryAsync<DocumentModel>(sql, new { batchId });
        }

        public async Task<IEnumerable<DocumentModel>> GetDocumentsInBatchAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT bd.ID as DocId, bd.DocName, bd.DocTypeId, dt.Name as DocTypeName, 
                       CASE WHEN bd.Status = 'A' THEN 1 ELSE 0 END as Status, bd.FileName, bd.InternalName, bd.originalfilename as OriginalFileName
                FROM BatchDetail bd
                LEFT JOIN ObjectTypes dt ON bd.DocTypeId = dt.Id
                WHERE bd.BatchID = @batchId AND bd.Status = 'A'
                ORDER BY bd.PageNo, bd.ID";
            return await conn.QueryAsync<DocumentModel>(sql, new { batchId });
        }

        public async Task<IEnumerable<PageModel>> GetDocumentPagesAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var sql = "SELECT ID as PageId, DocPage, FileName FROM BatchDetail WHERE DocTypeId = @docTypeId AND Status = 'A' ORDER BY DocPage";
            return await conn.QueryAsync<PageModel>(sql, new { docTypeId });
        }

        public async Task<bool> UpdateDocumentTypeAsync(int pageId, int newDocTypeId)
        {
            using var conn = CreateConnection();
            var sql = "UPDATE BatchDetail SET doctypeid = @newDocTypeId WHERE ID = @pageId";
            var result = await conn.ExecuteAsync(sql, new { newDocTypeId, pageId });
            return result > 0;
        }
        
        public async Task<string> GetDocumentTypeNameByIdAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var sql = "SELECT Name FROM ObjectTypes WHERE Id = @docTypeId";
            return await conn.QueryFirstOrDefaultAsync<string>(sql, new { docTypeId }) ?? string.Empty;
        }
        
        public async Task<bool> UpdateDocumentNameAsync(int pageId, string newName)
        {
            using var conn = CreateConnection();
            var sql = "UPDATE BatchDetail SET DocName = @newName WHERE ID = @pageId";
            var result = await conn.ExecuteAsync(sql, new { newName, pageId });
            return result > 0;
        }

        public async Task<int> GetNextDocumentSequenceAsync(int batchId, int docTypeId, int pageId)
        {
            using var conn = CreateConnection();
            // Count existing unique document names of the same type in the batch
            // that appear before the current page.
            var sql = @"SELECT COUNT(DISTINCT DocName) + 1 
                        FROM BatchDetail 
                        WHERE BatchID = @batchId AND DocTypeId = @docTypeId 
                        AND DocName IS NOT NULL AND ID < @pageId AND Status = 'A'";
            return await conn.QuerySingleAsync<int>(sql, new { batchId, docTypeId, pageId });
        }
        
        public async Task<bool> MovePagesToDocumentAsync(int sourcePageId, int targetPageId)
        {
            using var conn = CreateConnection();
            
            // sourcePageId and targetPageId are the handle-IDs (MIN(ID))
            // We need to get the DocName and BatchID for the target document
            var targetInfo = await conn.QueryFirstOrDefaultAsync<BatchDetailDto>(
                "SELECT BatchID, DocName, DocTypeId FROM BatchDetail WHERE ID = @targetPageId", new { targetPageId });
            
            var sourceInfo = await conn.QueryFirstOrDefaultAsync<BatchDetailDto>(
                "SELECT BatchID, DocName FROM BatchDetail WHERE ID = @sourcePageId", new { sourcePageId });

            if (targetInfo != null && sourceInfo != null)
            {
                var targetDocName = targetInfo.DocName ?? string.Empty;
                var sourceDocName = sourceInfo.DocName ?? string.Empty;
                var targetDocTypeId = targetInfo.DocTypeId;
                var targetBatchId = targetInfo.BatchId;
                var sourceBatchId = sourceInfo.BatchId;

                var lastPageSql = "SELECT COALESCE(MAX(DocPage), 0) FROM BatchDetail WHERE BatchID = @BatchID AND DocName = @DocName AND Status = 'A'";
                var lastPage = await conn.ExecuteScalarAsync<int>(lastPageSql, new { BatchID = targetBatchId, DocName = targetDocName });

                var updateSql = @"
                UPDATE BatchDetail 
                SET DocName = @targetDocName, 
                    DocPage = @lastPage + DocPage,
                    DocTypeId = @targetDocTypeId
                WHERE BatchID = @sourceBatchId AND DocName = @sourceDocName AND Status = 'A'";

                await conn.ExecuteAsync(updateSql, new
                {
                    targetDocName = targetDocName,
                    lastPage = lastPage,
                    targetDocTypeId = targetDocTypeId,
                    sourceBatchId = sourceBatchId,
                    sourceDocName = sourceDocName
                });
                return true;
            }
            return false;
        }

        public async Task<bool> UpdateBatchDetailMappingAsync(int pageId, int docTypeId, int docPage, string docName)
        {
            using var conn = CreateConnection();
            // Update mapping and DocName (grouping is now handled by DocTypeId)
            var sql = "UPDATE BatchDetail SET DocPage = @docPage, doctypeid = @docTypeId, DocName = @docName WHERE ID = @pageId";
            var result = await conn.ExecuteAsync(sql, new { docTypeId, docPage, pageId, docName });
            return result > 0;
        }

        public async Task<bool> UpdateBatchDetailStatusAsync(int pageId, string status)
        {
            using var conn = CreateConnection();
            var sql = "UPDATE BatchDetail SET Status = @status WHERE ID = @pageId";
            var result = await conn.ExecuteAsync(sql, new { status, pageId });
            return result > 0;
        }

        public async Task<bool> UpdateDocumentStatusAsync(int pageId, string status)
        {
            using var conn = CreateConnection();
            var sql = @"
                UPDATE BatchDetail SET Status = @status 
                WHERE DocName = (SELECT DocName FROM BatchDetail WHERE ID = @pageId) 
                AND BatchId = (SELECT BatchId FROM BatchDetail WHERE ID = @pageId);
            ";
            
            var result = await conn.ExecuteAsync(sql, new { status, pageId });
            return result > 0;
        }

        public async Task<bool> DeleteDocumentAsync(int pageId)
        {
            using var conn = CreateConnection();
            var sql = "UPDATE BatchDetail SET Status = 'D' WHERE DocName = (SELECT DocName FROM BatchDetail WHERE ID = @pageId) AND BatchId = (SELECT BatchId FROM BatchDetail WHERE ID = @pageId)";
            var result = await conn.ExecuteAsync(sql, new { pageId });
            return result > 0;
        }

        private async Task<bool> TableExistsAsync(string tableName, System.Data.IDbConnection conn, IDbTransaction? transaction = null)
        {
            // #8: Check cache first
            if (_tableExistsCache.TryGetValue(tableName, out var cached) && (DateTime.UtcNow - cached.CachedAt) < _tableCacheTtl)
            {
                return cached.Exists;
            }

            bool exists;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                var checkSql = "SELECT CAST(CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = @TableName) THEN 1 ELSE 0 END AS BIT)";
                exists = await conn.QuerySingleOrDefaultAsync<bool>(checkSql, new { TableName = tableName }, transaction);
            }
            else
            {
                var checkSql = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = @TableName)";
                exists = await conn.QuerySingleOrDefaultAsync<bool>(checkSql, new { TableName = tableName }, transaction);
            }

            // Cache the result
            _tableExistsCache[tableName] = (exists, DateTime.UtcNow);
            return exists;
        }

        public async Task EnsureDoctableExistsAsync(int docTypeId)
        {
            var tableName = $"doctable_{docTypeId}";
            using var conn = CreateConnection();
            
            if (await TableExistsAsync(tableName, conn))
                return;

            // Get all properties for this document type
            var concatOp = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? " + " : " || ";
            var propertySql = $@"SELECT p.id as Id, p.propertyname as PropertyName, p.propertytype as PropertyType, p.propertylength as PropertyLength
                               FROM property p 
                               JOIN doctypeproperties dp ON p.id = dp.propertyid 
                               WHERE dp.doctypeid = @DocTypeId 
                               ORDER BY dp.propertyorder";

            var properties = (await conn.QueryAsync<(int Id, string PropertyName, string PropertyType, int? PropertyLength)>(propertySql, new { DocTypeId = docTypeId })).ToList();

            if (!properties.Any())
                return; // No properties configured, nothing to create

            var columns = new List<string> { "docid INTEGER PRIMARY KEY" };
            foreach (var prop in properties)
            {
                var colName = $"column_{prop.Id}";
                var dataType = prop.PropertyType?.ToUpper() switch
                {
                    "STRING" => prop.PropertyLength > 0 ? $"VARCHAR({prop.PropertyLength})" : "VARCHAR(100)",
                    "INTEGER" => "INTEGER",
                    "DECIMAL" => "DECIMAL(18,2)",
                    "DATETIME" => "TIMESTAMP",
                    "BOOLEAN" => "BOOLEAN",
                    _ => "VARCHAR(100)"
                };
                columns.Add($"{colName} {dataType}");
            }

            var createSql = $"CREATE TABLE IF NOT EXISTS {tableName} ({string.Join(", ", columns)})";
            await conn.ExecuteAsync(createSql);

            var indexSql = $"CREATE INDEX IF NOT EXISTS idx_{tableName}_docid ON {tableName}(docid)";
            try { await conn.ExecuteAsync(indexSql); } catch { /* Index might already exist */ }

            // Update cache
            _tableExistsCache[tableName] = (true, DateTime.UtcNow);
        }

        private async Task<bool> RecordExistsAsync(string tableName, string idColumn, int idValue, System.Data.IDbConnection conn, IDbTransaction? transaction = null)
        {
            var sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? $"SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM {tableName} WHERE {idColumn} = @id) THEN 1 ELSE 0 END AS BIT)"
                : $"SELECT EXISTS (SELECT 1 FROM {tableName} WHERE {idColumn} = @id)";
            return await conn.QuerySingleOrDefaultAsync<bool>(sql, new { id = idValue }, transaction);
        }

        private static async Task<Dictionary<string, string>> GetColumnTypesAsync(string tableName, System.Data.IDbConnection conn, IDbTransaction? transaction = null)
        {
            var columnInfo = await conn.QueryAsync<(string ColumnName, string DataType)>(
                "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = @TableName",
                new { TableName = tableName }, transaction);
            return columnInfo.ToDictionary(c => c.ColumnName.ToLower(), c => c.DataType, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> MapValuesToColumns(Dictionary<string, string> inputValues, Dictionary<string, string> propertyToColumnMap)
        {
            var mappedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var columnIdToColumnMap = propertyToColumnMap.ToDictionary(p => "Column_" + p.Value.Replace("column_", ""), p => p.Value, StringComparer.OrdinalIgnoreCase);

            foreach (var kv in inputValues)
            {
                if (propertyToColumnMap.TryGetValue(kv.Key, out var columnName))
                    mappedValues[columnName] = kv.Value;
                else if (columnIdToColumnMap.TryGetValue(kv.Key, out var reverseColumnName))
                    mappedValues[reverseColumnName] = kv.Value;
                else
                    mappedValues[kv.Key.ToLower()] = kv.Value;
            }
            return mappedValues;
        }

        private static (string? sql, DynamicParameters parameters) BuildDynamicSql(string tableName, string idColumn, int idValue, Dictionary<string, string> values, Dictionary<string, string> columnTypes, bool isUpdate)
        {
            var parameters = new DynamicParameters();
            parameters.Add("id", idValue);

            var sqlParts = new List<string>();
            int i = 0;

            foreach (var kv in values)
            {
                if (!columnTypes.TryGetValue(kv.Key.ToLower(), out var columnType)) continue;
                columnType = columnType.ToLower();

                if (!TryFormatParameter(kv.Key, kv.Value, columnType, i++, parameters, out var sqlFragment))
                {
                    if (columnType.Contains("int")) return (null, parameters);
                    sqlFragment = $"{kv.Key} = NULL";
                }
                sqlParts.Add(sqlFragment);
            }

            if (sqlParts.Count == 0) return (null, parameters);

            return isUpdate 
                ? ($"UPDATE {tableName} SET {string.Join(", ", sqlParts)} WHERE {idColumn} = @id", parameters)
                : BuildInsertSql(tableName, idColumn, values, columnTypes, parameters);
        }

        private static (string sql, DynamicParameters parameters) BuildInsertSql(string tableName, string idColumn, Dictionary<string, string> values, Dictionary<string, string> columnTypes, DynamicParameters parameters)
        {
            var columns = new List<string>();
            var placeholders = new List<string>();
            int pIdx = 0;
 
            var filteredValues = values.Where(kv => columnTypes.ContainsKey(kv.Key.ToLower())).ToList();
            foreach (var kv in filteredValues)
            {
                columns.Add(kv.Key);
                placeholders.Add(parameters.ParameterNames.Contains($"value{pIdx}") ? $"@value{pIdx}" : "NULL");
                pIdx++;
            }

            return ($"INSERT INTO {tableName} ({idColumn}, {string.Join(", ", columns)}) VALUES (@id, {string.Join(", ", placeholders)})", parameters);
        }

        private static bool TryFormatParameter(string key, string value, string columnType, int index, DynamicParameters parameters, out string sqlFragment)
        {
            sqlFragment = $"{key} = @value{index}";
            if (string.IsNullOrEmpty(value))
            {
                sqlFragment = $"{key} = NULL";
                return true;
            }

            if (columnType.Contains("timestamp") || columnType.Contains("date"))
            {
                if (DateTime.TryParse(value, out DateTime parsedDate)) { parameters.Add($"value{index}", parsedDate); return true; }
            }
            else if (columnType.Contains("int"))
            {
                if (int.TryParse(value, out int parsedInt)) { parameters.Add($"value{index}", parsedInt); return true; }
            }
            else if (NumericTypes.Any(t => columnType.Contains(t)))
            {
                if (decimal.TryParse(value, out decimal parsedDecimal)) { parameters.Add($"value{index}", parsedDecimal); return true; }
            }
            else
            {
                parameters.Add($"value{index}", value);
                return true;
            }

            sqlFragment = $"{key} = NULL";
            return false;
        }

        private async Task<Dictionary<string, string>> GetColumnToPropertyIdMapAsync(IDbConnection conn, int typeId, bool isBatch)
        {
            var concatOp = _provider == "SqlServer" ? " + " : " || ";
            
            string sql;
            if (isBatch)
            {
                sql = $@"SELECT 'column_' {concatOp} CAST(PropertyId AS VARCHAR) as ColumnName, 
                               'Column_' {concatOp} CAST(PropertyId AS VARCHAR) as ColumnId
                        FROM BatchTypeProperties WHERE BatchTypeId = @typeId";
            }
            else
            {
                sql = $@"SELECT 'column_' {concatOp} CAST(PropertyId AS VARCHAR) as ColumnName, 
                               'Column_' {concatOp} CAST(PropertyId AS VARCHAR) as ColumnId
                        FROM DocTypeProperties WHERE DocTypeId = @typeId";
            }

            var mappings = await conn.QueryAsync<(string ColumnName, string ColumnId)>(sql, new { typeId });
            return mappings.ToDictionary(p => p.ColumnName, p => p.ColumnId, StringComparer.OrdinalIgnoreCase);
        }

        private static void MapDataRowToDictionary(IDictionary<string, object> row, Dictionary<string, string> mappings, Dictionary<string, string> results)
        {
            foreach (var kvp in row)
            {
                if (kvp.Key.Equals("docid", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("batchid", StringComparison.OrdinalIgnoreCase)) continue;

                var propertyKey = mappings.TryGetValue(kvp.Key, out var mappedKey) ? mappedKey : kvp.Key;
                results[propertyKey] = kvp.Value switch
                {
                    DateTime dt => dt.ToString("yyyy-MM-dd"),
                    DateTimeOffset dto => dto.ToString("yyyy-MM-dd"),
                    _ => kvp.Value?.ToString() ?? ""
                };
            }
        }

        private class PropertyDocDetails
        {
            public string PropertyKey { get; set; } = string.Empty;
            public int? ZoneId { get; set; }
            public string? DefaultValue { get; set; }
            public bool IsEnabled { get; set; } = true;
        }

        private class BatchPropertyDetails
        {
            public string PropertyKey { get; set; } = string.Empty;
            public string? DefaultValue { get; set; }
            public bool IsEnabled { get; set; } = true;
        }

        private async Task<IEnumerable<PropertyDocDetails>> GetDocPropertiesWithZonesAsync(IDbConnection conn, int docTypeId)
        {
            var concatOp = _provider == "SqlServer" ? " + " : " || ";
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var isEnabledCol = isSqlServer ? "COALESCE(dp.IsEnabled, 1)" : "COALESCE(dp.IsEnabled, TRUE)";
            var sql = $@"
                SELECT 'Column_' {concatOp} CAST(dp.PropertyId AS VARCHAR) as PropertyKey, 
                       dp.ZoneId, 
                       dp.DefaultValue, 
                       {isEnabledCol} as IsEnabled
                FROM DocTypeProperties dp
                WHERE dp.DocTypeId = @docTypeId";

            return await conn.QueryAsync<PropertyDocDetails>(sql, new { docTypeId });
        }

        // #4: Fetch all OCR results for the document in ONE query instead of N+1
        private static async Task FillMissingWithOcrResultsAsync(IDbConnection conn, int docId, IEnumerable<PropertyDocDetails> properties, Dictionary<string, string> results)
        {
            var missingProperties = properties.Where(p => !results.ContainsKey(p.PropertyKey)).ToList();
            if (missingProperties.Count == 0) return;

            // Single query to fetch all OCR results for this document
            var allOcrResults = await conn.QueryAsync<(int ZoneId, string OcrValue)>(
                "SELECT ZoneId, ocrvalue as OcrValue FROM OCRResults WHERE DocId = @docId",
                new { docId });
            var ocrLookup = allOcrResults.ToDictionary(r => r.ZoneId, r => r.OcrValue);

            foreach (var prop in missingProperties)
            {
                if (prop.ZoneId.HasValue)
                {
                    results[prop.PropertyKey] = ocrLookup.TryGetValue(prop.ZoneId.Value, out var val) ? val ?? "" : "";
                }
                else
                {
                    results[prop.PropertyKey] = "";
                }
            }
        }

        private async Task EnsureAllBatchPropertiesRepresentedAsync(IDbConnection conn, int batchTypeId, Dictionary<string, string> results)
        {
            var concatOp = _provider == "SqlServer" ? " + " : " || ";
            var isSqlServer = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
            var isEnabledCol = isSqlServer ? "COALESCE(bp.IsEnabled, 1)" : "COALESCE(bp.IsEnabled, TRUE)";
            var propertySql = $@"
                SELECT 'Column_' {concatOp} CAST(bp.PropertyId AS VARCHAR) as PropertyKey,
                       bp.DefaultValue,
                       {isEnabledCol} as IsEnabled
                FROM BatchTypeProperties bp
                WHERE bp.BatchTypeId = @batchTypeId";

            var properties = await conn.QueryAsync<BatchPropertyDetails>(propertySql, new { batchTypeId });

            foreach (var prop in properties)
            {
                if (prop.PropertyKey == null) continue;

                // If disabled, enforce DefaultValue
                if (!prop.IsEnabled)
                {
                    results[prop.PropertyKey] = prop.DefaultValue ?? "";
                }
                // If not disabled but doesn't exist or is empty in results, fall back to DefaultValue if present
                else if (!results.TryGetValue(prop.PropertyKey, out var val) || string.IsNullOrEmpty(val))
                {
                    results[prop.PropertyKey] = prop.DefaultValue ?? "";
                }
            }
        }

        public async Task<BatchVerificationContextDto> GetBatchVerificationContextAsync(int batchId)
        {
            try
            {
                var context = new BatchVerificationContextDto { BatchId = batchId };

                using var conn = CreateConnection();
            
            // 1. Get Batch Info
            var batch = await conn.QueryFirstOrDefaultAsync<BatchDto>(
                "SELECT ID, BatchName, BatchTypeId FROM Batch WHERE ID = @batchId", new { batchId });
            if (batch == null) return context;
            
            context.BatchName = batch.BatchName;
            context.BatchTypeId = batch.BatchTypeId;

            // 2. Get Documents and Pages
            context.Documents = (await GetDocumentsForVerifyAsync(batchId)).ToList();
            context.Pages = (await GetPagesForVerifyAsync(batchId)).ToList();

            if (!context.Documents.Any()) return context;

            // 3. Get Batch-level fields and values
            context.BatchIndexFields = (await GetBatchIndexFieldsAsync(batch.BatchTypeId)).ToList();
            context.BatchIndexValues = await GetBatchIndexValuesForVerifyAsync(batchId);

            // 4. Get DocType fields for all unique DocTypeIds
            var docTypeIds = context.Documents.Select(d => d.DocTypeId).Distinct().ToList();
            foreach (var docTypeId in docTypeIds)
            {
                context.DocTypeFields[docTypeId] = (await GetIndexFieldsAsync(docTypeId)).ToList();
            }

            // 5. Get all Document index values (this is the most complex part to do efficiently)
            // For each DocType, fetch from doctable_{id}
            foreach (var docTypeId in docTypeIds)
            {
                var tableName = $"doctable_{docTypeId}";
                if (await TableExistsAsync(tableName, conn))
                {
                    var docIdsForType = context.Documents.Where(d => d.DocTypeId == docTypeId).Select(d => d.DocId).ToList();
                    if (docIdsForType.Any())
                    {
                        var columnToPropertyMap = await GetColumnToPropertyIdMapAsync(conn, docTypeId, false);
                        
                        var isPostgres = !_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
                        var sql = isPostgres 
                            ? $"SELECT * FROM {tableName} WHERE docid = ANY(@docIdsForType)" 
                            : $"SELECT * FROM {tableName} WHERE docid IN @docIdsForType";

                        var docRows = await conn.QueryAsync(sql, new { docIdsForType = isPostgres ? (object)docIdsForType.ToArray() : docIdsForType });
                        
                        foreach (var row in docRows)
                        {
                            var dict = (IDictionary<string, object>)row;
                            if (dict.ContainsKey("docid"))
                            {
                                var dId = Convert.ToInt32(dict["docid"]);
                                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                MapDataRowToDictionary(dict, columnToPropertyMap, values);
                                context.DocumentIndexValues[dId] = values;
                            }
                        }
                    }
                }
            }

            // 6. Fill in missing values with OCR results for all documents in ONE query
            var allDocIds = context.Documents.Select(d => d.DocId).ToList();
            if (allDocIds.Any())
            {
                // Fetch properties with default values for each distinct DocTypeId
                var docTypePropertiesMap = new Dictionary<int, List<PropertyDocDetails>>();
                foreach (var docTypeId in docTypeIds)
                {
                    docTypePropertiesMap[docTypeId] = (await GetDocPropertiesWithZonesAsync(conn, docTypeId)).ToList();
                }

                var isPostgres = !_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
                var ocrSql = isPostgres
                    ? "SELECT docid as DocId, zoneid as ZoneId, ocrvalue as OCRValue FROM ocrresults WHERE docid = ANY(@allDocIds)"
                    : "SELECT DocId, ZoneId, OCRValue FROM OCRResults WHERE DocId IN @allDocIds";
                
                var allOcrResults = await conn.QueryAsync(ocrSql, new { allDocIds = isPostgres ? (object)allDocIds.ToArray() : allDocIds });
                
                var ocrLookup = allOcrResults
                    .GroupBy(r => Convert.ToInt32(r.DocId))
                    .ToDictionary(
                        g => g.Key, 
                        g => g.GroupBy(r => Convert.ToInt32(r.ZoneId))
                              .ToDictionary(zg => zg.Key, zg => (string)zg.First().OCRValue)
                    );

                foreach (var doc in context.Documents)
                {
                    if (!context.DocumentIndexValues.ContainsKey(doc.DocId))
                    {
                        context.DocumentIndexValues[doc.DocId] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }

                    var results = context.DocumentIndexValues[doc.DocId];
                    var fields = context.DocTypeFields[doc.DocTypeId];
                    
                    if (ocrLookup.TryGetValue(doc.DocId, out var docOcr))
                    {
                        foreach (var field in fields)
                        {
                            if (field.ZoneId.HasValue && !results.ContainsKey(field.ColumnId))
                            {
                                results[field.ColumnId] = docOcr.TryGetValue(field.ZoneId.Value, out var val) ? val ?? "" : "";
                            }
                        }
                    }
                    
                    // Enforce DefaultValue and IsEnabled
                    if (docTypePropertiesMap.TryGetValue(doc.DocTypeId, out var docProps))
                    {
                        foreach (var prop in docProps)
                        {
                            if (prop.PropertyKey == null) continue;

                            // If disabled, enforce DefaultValue
                            if (!prop.IsEnabled)
                            {
                                results[prop.PropertyKey] = prop.DefaultValue ?? "";
                            }
                            // If not disabled but the value in results is empty/null, fall back to DefaultValue if present
                            else if (!results.TryGetValue(prop.PropertyKey, out var val) || string.IsNullOrEmpty(val))
                            {
                                results[prop.PropertyKey] = prop.DefaultValue ?? "";
                            }
                        }
                    }
                    
                    // Ensure all fields are represented
                    foreach (var field in fields)
                    {
                        if (!results.ContainsKey(field.ColumnId))
                        {
                            results[field.ColumnId] = "";
                        }
                    }
                }
            }

                return context;
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText("verify_context_error.txt", ex.ToString());
                throw;
            }
        }

        public async Task<bool> BulkSaveIndexDataAsync(BulkSaveIndexDto data)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();

            using var transaction = conn.BeginTransaction();
            try
            {
                // 1. Save Batch Index Data if provided
                if (data.BatchFields.Any())
                {
                    var batchValues = data.BatchFields.ToDictionary(f => f.ColumnId, f => f.Value);
                    await SaveBatchIndexDataInternalAsync(data.BatchId, batchValues, conn, transaction);
                }

                // 2. Process each document save
                foreach (var docSave in data.DocumentSaves)
                {
                    var docValues = docSave.Fields.ToDictionary(f => f.ColumnId, f => f.Value);
                    
                    // Save to dynamic doctable
                    await SaveDocumentIndexDataInternalAsync(docSave.DocId, docValues, conn, transaction);

                    // Save to OCRResults in bulk for this document
                    await SaveOcrResultsBulkInternalAsync(docSave.DocId, docSave.Fields, conn, transaction);
                }

                transaction.Commit();
                return true;
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        private async Task SaveOcrResultsBulkInternalAsync(int docId, List<FieldValueDto> fields, IDbConnection conn, IDbTransaction transaction)
        {
            var docFields = fields.Where(f => !f.ColumnId.StartsWith("batch_")).ToList();
            if (!docFields.Any()) return;

            var zoneIdSql = @"
                SELECT ZoneId 
                FROM DocumentClassDetail 
                WHERE DocTypeId = (SELECT DocTypeId FROM BatchDetail WHERE ID = @DocId)
                AND PropertyId = @PropertyId";

            var checkSql = "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM OCRResults WHERE DocId = @DocId AND ZoneId = @ZoneId) THEN 1 ELSE 0 END AS BIT)";
            if (!_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                checkSql = "SELECT EXISTS (SELECT 1 FROM OCRResults WHERE DocId = @DocId AND ZoneId = @ZoneId)";
            }

            var insertSql = "INSERT INTO OCRResults (DocId, ZoneId, OCRValue, OCRConfidence) VALUES (@DocId, @ZoneId, @Value, 95.0)";
            var updateSql = "UPDATE OCRResults SET OCRValue = @Value, OCRConfidence = 95.0 WHERE DocId = @DocId AND ZoneId = @ZoneId";

            foreach (var field in docFields)
            {
                var parts = field.ColumnId.Split('_');
                if (parts.Length > 1 && int.TryParse(parts[1], out int propertyId))
                {
                    // Find the ZoneId for this property
                    var zoneId = await conn.QueryFirstOrDefaultAsync<int?>(zoneIdSql, new { DocId = docId, PropertyId = propertyId }, transaction);
                    
                    if (zoneId.HasValue && zoneId.Value > 0)
                    {
                        var exists = await conn.QuerySingleOrDefaultAsync<bool>(checkSql, new { DocId = docId, ZoneId = zoneId.Value }, transaction);
                        
                        if (exists)
                        {
                            await conn.ExecuteAsync(updateSql, new { DocId = docId, ZoneId = zoneId.Value, Value = field.Value }, transaction);
                        }
                        else
                        {
                            await conn.ExecuteAsync(insertSql, new { DocId = docId, ZoneId = zoneId.Value, Value = field.Value }, transaction);
                        }
                    }
                }
            }
        }
    }
}