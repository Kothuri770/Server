using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;
using Server.Controllers;
using Server.Models;
using System;
using System.Data;
using System.Linq;
using TrueCapture.Services;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Server.Repositories
{
    public interface IBatchRepository
    {
        // Batch Operations
        Task<long> GetNextBatchNumberAsync();
        Task<int> InsertBatchAsync(string batchName, DateTime createdOn, int batchTypeId, string batchStatus, int stepId, string userName);
        Task UpdateBatchAsync(int batchid, int appid);
        Task CreateBatchAsync(string batchName, int appId, string username);
        Task<BatchDto> GetBatchByIdAsync(int batchId);
        Task<Batch?> GetBatchInternalAsync(int batchId);
        Task MoveToNextStepAsync(int batchId, string username);
        Task MoveToSpecificStepAsync(int batchId, int stepId, string username);
        Task<bool> HoldBatchAsync(int batchId, string username);
        Task<bool> UpdateBatchStatusAsync(int batchId, string status, string username);
        Task InsertBatchLogAsync(int batchId, string batchType, int documentCount, int stepId, int stationId, int pageCount, string batchName);

        // New methods for batch completion and exception handling
        Task CompleteBatchAsync(int batchId, string username);
        Task MoveToExceptionStepAsync(int batchId, string username);
        Task<bool> UpdateBatchOcrTypeAsync(int batchId, string ocrType);
        Task<string> GetBatchOcrTypeAsync(int batchId);

        // Locking & Parallel Processing
        Task<bool> ClaimBatchAsync(int batchId, int stepId, string workerId, int timeoutMinutes = 60);
        Task<bool> ReleaseBatchAsync(int batchId, string workerId);
        Task<int> RecoverStaleLocksAsync(int timeoutMinutes);

        // Application & Document Types
        Task<IEnumerable<ApplicationNameDto>> GetApplicationNamesAsync();
        Task<IEnumerable<DocumentNameDto>> GetDocumentTypesAsync(GetDocumentRequest gd);
        Task<IEnumerable<ObjectTypeDto>> GetObjectTypesAsync(int appId);

        // Image Operations
        Task<int> GetMaxPageNumberAsync(int batchId);
        Task<int> GetNextDocumentIdAsync(int batchId);
        Task<int> InsertBatchDetailAsync(BatchDetailDto detail);
        Task<bool> HasUncategorizedImagesAsync(int batchId);
        Task<bool> DeleteBatchDetailAsync(string fileName, long batchId);
        Task<bool> UpdateDocumentStatusAsync(string fileName, long batchId, string status);
        Task<bool> SaveBatchDetailsAsync(SaveBatchModel model);
        Task<IEnumerable<BatchImageWithDocNameDto>> GetBatchImagesAsync(int batchId);
        Task<int> InsertBatchDetailsBulkAsync(IEnumerable<BatchDetailDto> details);

        // Monitoring
        Task<IEnumerable<JobMonitorDto>> GetJobDataAsync(UserDto job);
        Task<IEnumerable<Batch>> GetBatchesByStepIdAsync(int stepId);
        Task<IEnumerable<ColumnTypeDto>> GetFilterColumnsAsync();
        Task<IEnumerable<JobMonitorDto>> GetFilteredDataAsync(FilterInputDto fi);
        
        // Logging
        Task<IEnumerable<Batch>> GetLatestBatchesAsync(int count = 100);
        Task<Batch?> PickNextBatchAsync(int stepId, string workerId, int timeoutMinutes = 60);

        // Purging
        Task<IEnumerable<Batch>> GetBatchesInDateRangeAsync(DateTime startDate, DateTime endDate, int applicationId = 0, int stepId = 0);
        Task<bool> DeleteBatchRecordAsync(int batchId);
        Task UpdateBatchPurgeStatusAsync(int batchId, bool isPurging);
    }

    public partial class BatchRepository : BaseRepository, IBatchRepository
    {
        [GeneratedRegex(@"\{SEQ:(\d+)\}", RegexOptions.IgnoreCase)]
        private static partial Regex SeqRegex();

        [GeneratedRegex(@"\{ID:(\d+)\}", RegexOptions.IgnoreCase)]
        private static partial Regex IdRegex();

        private sealed class BatchInfo
        {
            public string BatchName { get; set; } = string.Empty;
 
            public int CurrentStepId { get; set; } = 0;
            public string BatchTypeName { get; set; } = string.Empty;
            public string OcrMode { get; set; } = "Manual";
            public string SeparationMode { get; set; } = "Global";
        }

        public BatchRepository(string connectionString, string provider) : base(connectionString, provider)
        {
        }

        // ========== BATCH OPERATIONS ==========
        public async Task<long> GetNextBatchNumberAsync()
        {
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(ID), 0) + 1 FROM Batch");
        }

        public async Task<int> InsertBatchAsync(string batchName, DateTime createdOn, int batchTypeId,
            string batchStatus, int stepId, string userName)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();
            
            using var trans = conn.BeginTransaction();

            try
            {
                var internalName = Guid.NewGuid().ToString();
                
                // If batchName is empty or a placeholder, we will generate it from the ID
                bool autoGenerate = string.IsNullOrEmpty(batchName) || batchName == "AUTOGENERATED";
                string initialName = autoGenerate ? "NAME_PENDING_" + internalName : batchName;

                string sql;
                if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    sql = "INSERT INTO Batch (BatchName, CreatedOn, BatchTypeId, BatchStatus, StepID, InternalName, userName) " +
                          "OUTPUT INSERTED.ID " +
                          "VALUES (@initialName, @createdOn, @batchTypeId, @batchStatus, @stepId, @internalName, @userName)";
                }
                else
                {
                    sql = "INSERT INTO Batch (BatchName, CreatedOn, BatchTypeId, BatchStatus, StepID, InternalName, userName) " +
                          "VALUES (@initialName, @createdOn, @batchTypeId, @batchStatus, @stepId, @internalName, @userName) " +
                          "RETURNING ID";
                }

                var batchId = await conn.QueryFirstAsync<int>(sql,
                    new { initialName, createdOn, batchTypeId, batchStatus, stepId, internalName, userName }, trans);

                if (autoGenerate)
                {
                    // Fetch prefix from configuration
                    var prefix = await conn.ExecuteScalarAsync<string>(
                        "SELECT ConfigValue FROM Configuration WHERE ConfigName = 'Batch Prefix'", null, trans) ?? "BATCH-";
                    
                    // Support for dynamic batch naming templates
                    var finalName = await ParseBatchNameTemplate(prefix, batchId, createdOn, conn, trans);
                    
                    await conn.ExecuteAsync(
                        "UPDATE Batch SET BatchName = @finalName WHERE ID = @batchId",
                        new { finalName, batchId }, trans);
                }

                await conn.ExecuteAsync(@"
                    INSERT INTO BatchActions (BatchID, ActionName, UserName) VALUES (@batchId, 'CREATE', @userName)",
                    new { batchId, userName }, trans);

                if (!_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    await conn.ExecuteAsync($"NOTIFY batch_step_{stepId}", null, trans);
                }

                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                else trans.Commit();
                return batchId;
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        private async Task<string> ParseBatchNameTemplate(string template, int batchId, DateTime createdOn, IDbConnection conn, IDbTransaction trans)
        {
            if (string.IsNullOrWhiteSpace(template)) return batchId.ToString();

            var result = template;
            result = result.Replace("{YYYY}", createdOn.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
                           .Replace("{MM}", createdOn.ToString("MM"), StringComparison.OrdinalIgnoreCase)
                           .Replace("{DD}", createdOn.ToString("dd"), StringComparison.OrdinalIgnoreCase);

            // Handle {SEQ:N} placeholder (Daily sequence)
            if (result.Contains("{SEQ:", StringComparison.OrdinalIgnoreCase))
            {
                var match = SeqRegex().Match(result);
                if (match.Success)
                {
                    var padding = int.Parse(match.Groups[1].Value);
                    
                    // Lock the table to prevent concurrent daily sequence duplicates
                    if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                    {
                        var countSql = "SELECT COUNT(*) FROM Batch WITH (UPDLOCK, HOLDLOCK) WHERE CAST(CreatedOn AS DATE) = CAST(@createdOn AS DATE)";
                        var dailyCount = await conn.ExecuteScalarAsync<int>(countSql, new { createdOn }, trans);
                        var dailySeq = dailyCount.ToString().PadLeft(padding, '0');
                        result = SeqRegex().Replace(result, dailySeq);
                    }
                    else
                    {
                        await conn.ExecuteAsync("LOCK TABLE Batch IN EXCLUSIVE MODE", null, trans);
                        var countSql = "SELECT COUNT(*) FROM Batch WHERE CreatedOn::date = @createdOn::date";
                        var dailyCount = await conn.ExecuteScalarAsync<int>(countSql, new { createdOn }, trans);
                        var dailySeq = dailyCount.ToString().PadLeft(padding, '0');
                        result = SeqRegex().Replace(result, dailySeq);
                    }
                }
            }

            // Handle {ID:N} placeholder (Global Batch ID)
            if (result.Contains("{ID:", StringComparison.OrdinalIgnoreCase))
            {
                var match = IdRegex().Match(result);
                if (match.Success)
                {
                    var padding = int.Parse(match.Groups[1].Value);
                    var paddedId = batchId.ToString().PadLeft(padding, '0');
                    result = IdRegex().Replace(result, paddedId);
                }
            }

            // If no placeholders were used, append the ID to ensure uniqueness (Legacy behavior)
            if (result == template)
            {
                result = $"{template}{batchId}";
            }

            return result;
        }

        public async Task<BatchDto> GetBatchByIdAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql =
                "SELECT ID, BatchName, BatchTypeId, CreatedOn, BatchStatus, StepID, userName as CreatedBy " +
                "FROM Batch  " +
                "WHERE ID = @batchId";

            var result = await conn.QuerySingleOrDefaultAsync<BatchDto>(sql, new { batchId });
            return result ?? new BatchDto();
        }

        public async Task<Batch?> GetBatchInternalAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT b.ID, b.BatchName, b.BatchTypeId, b.CreatedOn, b.BatchStatus, b.StepId, 
                       ot.Name, b.LockedOn, b.LockedBy
                FROM Batch b
                LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id
                WHERE b.ID = @batchId";
            return await conn.QueryFirstOrDefaultAsync<Batch>(sql, new { batchId });
        }

        public async Task UpdateBatchAsync(int batchid, int appid)
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(
                "UPDATE Batch SET BatchTypeId = @appid WHERE ID = @batchid",
                new { appid, batchid });
        }

        public async Task CreateBatchAsync(string batchName, int appId, string username)
        {
            using var conn = CreateConnection();
            var internalName = Guid.NewGuid().ToString();

            string sql = _provider == "SqlServer"
                ? "INSERT INTO Batch (BatchName, BatchTypeId, BatchStatus, StepID, InternalName, CreatedBy) " +
                  "OUTPUT INSERTED.ID " +
                  "VALUES (@batchName, @appId, 'A', 1, @internalName, @username)"
                : "INSERT INTO Batch (BatchName, BatchTypeId, BatchStatus, StepID, InternalName, CreatedBy) " +
                  "VALUES (@batchName, @appId, 'A', 1, @internalName, @username) " +
                  "RETURNING ID";

            var batchId = await conn.QueryFirstAsync<int>(sql, new { batchName, appId, internalName, username });

            await conn.ExecuteAsync(
                "INSERT INTO BatchActions (BatchID, ActionName, UserName) VALUES (@batchId, 'CREATE', @username)",
                new { batchId, username });

            if (!_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                await conn.ExecuteAsync("NOTIFY batch_step_1");
            }
        }

        public async Task MoveToNextStepAsync(int batchId, string username)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();
            // #2: Wrap in transaction for atomicity
            using var trans = conn.BeginTransaction();
            try
            {
                var batchInfo = await conn.QueryFirstOrDefaultAsync<BatchInfo>(
                    "SELECT b.BatchName, b.BatchTypeId, b.StepID as CurrentStepId, ot.Name as BatchTypeName, ot.OcrMode, ot.SeparationMode " +
                    "FROM Batch b  " +
                    "LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id " +
                    "WHERE b.ID = @batchId",
                    new { batchId }, trans);

                if (batchInfo == null)
                {
                    throw new KeyNotFoundException($"Batch with ID {batchId} not found");
                }

                var documentCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(DISTINCT DocName) FROM BatchDetail WHERE BatchID = @batchId AND Status = 'A' AND DocName IS NOT NULL",
                    new { batchId }, trans));

                var pageCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(*) FROM BatchDetail WHERE BatchID = @batchId",
                    new { batchId }, trans));

                int nextStep = 0;
                int lastEvaluatedStep = batchInfo.CurrentStepId;

                while (true)
                {
                    var nextStepSql = _provider == "SqlServer"
                        ? "SELECT TOP 1 ID, Status FROM Steps WHERE ID > @lastEvaluatedStep AND (Status = 'A' OR ID IN (3, 4)) ORDER BY ID"
                        : "SELECT ID, Status FROM Steps WHERE ID > @lastEvaluatedStep AND (Status = 'A' OR ID IN (3, 4)) ORDER BY ID LIMIT 1";

                    var stepRecord = await conn.QueryFirstOrDefaultAsync<Step>(nextStepSql, new { lastEvaluatedStep }, trans);
                    
                    if (stepRecord == null) 
                    {
                        nextStep = 98; // Complete
                        break;
                    }

                    nextStep = stepRecord.ID;
                    string stepStatus = stepRecord.Status;

                    // Separation Step Logic (Step ID 3)
                    if (nextStep == 3)
                    {
                        string sepMode = batchInfo.SeparationMode;
                        if (!string.IsNullOrEmpty(sepMode) && sepMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                        {
                            // Skip if application mode is Manual
                            lastEvaluatedStep = 3;
                            continue;
                        }
                        else if (!string.IsNullOrEmpty(sepMode) && sepMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                        {
                            // Run if application mode is Auto (regardless of global status)
                            break;
                        }
                        else 
                        {
                            // Global fallback
                            if (stepStatus != "A")
                            {
                                lastEvaluatedStep = 3;
                                continue;
                            }
                        }
                    }

                    // OCR Step Logic (Step ID 4)
                    if (nextStep == 4)
                    {
                        string ocrMode = batchInfo.OcrMode;
                        if (!string.IsNullOrEmpty(ocrMode) && ocrMode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                        {
                            // Skip if application mode is Manual
                            lastEvaluatedStep = 4;
                            continue;
                        }
                        else if (!string.IsNullOrEmpty(ocrMode) && ocrMode.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
                        {
                            // Run if application mode is Automatic (regardless of global status)
                            break;
                        }
                        else
                        {
                            // Global fallback
                            if (stepStatus != "A")
                            {
                                lastEvaluatedStep = 4;
                                continue;
                            }
                        }
                    }

                    if (stepStatus != "A")
                    {
                        // Any other step that was returned but is inactive, skip it
                        lastEvaluatedStep = nextStep;
                        continue;
                    }

                    break;
                }

                await conn.ExecuteAsync("UPDATE Batch SET StepID = @nextStep, BatchStatus = 'A' WHERE ID = @batchId",
                    new { nextStep, batchId }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchLog (BatchId, BatchType, DocumentCount, CompletedOn, StepId, StationId, PageCount, BatchName) " +
                    "VALUES (@batchId, @batchType, @documentCount, @completedOn, @stepId, @stationId, @pageCount, @batchName)",
                    new { batchId, batchType = batchInfo.BatchTypeName, documentCount, completedOn = DateTime.Now, stepId = batchInfo.CurrentStepId, stationId = 0, pageCount, batchName = batchInfo.BatchName }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'MOVED_STEP', @username, @actionStamp)",
                    new { batchId, username, actionStamp = DateTime.Now }, trans);

                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                else trans.Commit();

                if (!_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    await conn.ExecuteAsync($"NOTIFY batch_step_{nextStep}");
                }
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        public async Task MoveToSpecificStepAsync(int batchId, int stepId, string username)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();
            // #2: Wrap in transaction for atomicity
            using var trans = conn.BeginTransaction();
            try
            {
                var batchInfo = await conn.QueryFirstOrDefaultAsync<BatchInfo>(
                    "SELECT b.BatchName, b.BatchTypeId, b.StepID as CurrentStepId, ot.Name as BatchTypeName " +
                    "FROM Batch b " +
                    "LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id " +
                    "WHERE b.ID = @batchId",
                    new { batchId }, trans);

                if (batchInfo == null)
                {
                    throw new KeyNotFoundException($"Batch with ID {batchId} not found");
                }

                var documentCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(DISTINCT DocName) FROM BatchDetail WHERE BatchID = @batchId AND Status = 'A' AND DocName IS NOT NULL",
                    new { batchId }, trans));

                var pageCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(*) FROM BatchDetail WHERE BatchID = @batchId",
                    new { batchId }, trans));

                await conn.ExecuteAsync("UPDATE Batch SET StepID = @stepId, BatchStatus = 'A' WHERE ID = @batchId",
                    new { stepId, batchId }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchLog (BatchId, BatchType, DocumentCount, CompletedOn, StepId, StationId, PageCount, BatchName) " +
                    "VALUES (@batchId, @batchType, @documentCount, @completedOn, @stepId, @stationId, @pageCount, @batchName)",
                    new { batchId, batchType = batchInfo.BatchTypeName, documentCount, completedOn = DateTime.Now, stepId = batchInfo.CurrentStepId, stationId = 0, pageCount, batchName = batchInfo.BatchName }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'MOVED_TO_STEP', @username, @actionStamp)",
                    new { batchId, username, actionStamp = DateTime.Now }, trans);

                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                else trans.Commit();

                if (!_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    await conn.ExecuteAsync($"NOTIFY batch_step_{stepId}");
                }
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        public async Task CompleteBatchAsync(int batchId, string username)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();
            // #2: Wrap in transaction for atomicity
            using var trans = conn.BeginTransaction();
            try
            {
                var batchInfo = await conn.QueryFirstOrDefaultAsync<BatchInfo>(
                    "SELECT b.BatchName, b.BatchTypeId, b.StepID as CurrentStepId, ot.Name as BatchTypeName " +
                    "FROM Batch b " +
                    "LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id " +
                    "WHERE b.ID = @batchId",
                    new { batchId }, trans);

                if (batchInfo == null)
                {
                    throw new KeyNotFoundException($"Batch with ID {batchId} not found");
                }

                var documentCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(DISTINCT DocName) FROM BatchDetail WHERE BatchID = @batchId AND Status = 'A' AND DocName IS NOT NULL",
                    new { batchId }, trans));

                var pageCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(*) FROM BatchDetail WHERE BatchID = @batchId",
                    new { batchId }, trans));

                await conn.ExecuteAsync("UPDATE Batch SET StepID = 98, BatchStatus = 'C' WHERE ID = @batchId",
                    new { batchId }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchLog (BatchId, BatchType, DocumentCount, CompletedOn, StepId, StationId, PageCount, BatchName) " +
                    "VALUES (@batchId, @batchType, @documentCount, @completedOn, @stepId, @stationId, @pageCount, @batchName)",
                    new { batchId, batchType = batchInfo.BatchTypeName, documentCount, completedOn = DateTime.Now, stepId = batchInfo.CurrentStepId, stationId = 0, pageCount, batchName = batchInfo.BatchName }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'COMPLETED', @username, @actionStamp)",
                    new { batchId, username, actionStamp = DateTime.Now }, trans);

                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                else trans.Commit();
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        public async Task<bool> HoldBatchAsync(int batchId, string username)
        {
            using var conn = CreateConnection();

            // Update batch status to 'H' (Hold)
            var result = await conn.ExecuteAsync(
                "UPDATE Batch SET BatchStatus = 'H' WHERE ID = @batchId",
                new { batchId });

            // Insert action log
            await conn.ExecuteAsync(
                "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'HOLD', @username, @actionStamp)",
                new { batchId, username, actionStamp = DateTime.Now });

            return result > 0;
        }

        public async Task MoveToExceptionStepAsync(int batchId, string username)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();
            // #2: Wrap in transaction for atomicity
            using var trans = conn.BeginTransaction();
            try
            {
                var batchInfo = await conn.QueryFirstOrDefaultAsync<BatchInfo>(
                    "SELECT b.BatchName, b.BatchTypeId, b.StepID as CurrentStepId, ot.Name as BatchTypeName " +
                    "FROM Batch b " +
                    "LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id " +
                    "WHERE b.ID = @batchId",
                    new { batchId }, trans);

                if (batchInfo == null)
                {
                    throw new KeyNotFoundException($"Batch with ID {batchId} not found");
                }

                var documentCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(DISTINCT DocName) FROM BatchDetail WHERE BatchID = @batchId AND Status = 'A' AND DocName IS NOT NULL",
                    new { batchId }, trans));

                var pageCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                    "SELECT COUNT(*) FROM BatchDetail WHERE BatchID = @batchId",
                    new { batchId }, trans));

                await conn.ExecuteAsync("UPDATE Batch SET StepID = 99, BatchStatus = 'A' WHERE ID = @batchId",
                    new { batchId }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchLog (BatchId, BatchType, DocumentCount, CompletedOn, StepId, StationId, PageCount, BatchName) " +
                    "VALUES (@batchId, @batchType, @documentCount, @completedOn, @stepId, @stationId, @pageCount, @batchName)",
                    new { batchId, batchType = batchInfo.BatchTypeName, documentCount, completedOn = DateTime.Now, stepId = batchInfo.CurrentStepId, stationId = 0, pageCount, batchName = batchInfo.BatchName }, trans);

                await conn.ExecuteAsync(
                    "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'EXCEPTION', @username, @actionStamp)",
                    new { batchId, username, actionStamp = DateTime.Now }, trans);

                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                else trans.Commit();

                if (!_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    await conn.ExecuteAsync("NOTIFY batch_step_99");
                }
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        public async Task<bool> UpdateBatchStatusAsync(int batchId, string status, string username)
        {
            try
            {
                using var conn = CreateConnection();



                // Update batch status
                var result = await conn.ExecuteAsync(
                    "UPDATE Batch SET BatchStatus = @status WHERE ID = @batchId",
                    new { status, batchId });

                // Insert action log
                await conn.ExecuteAsync(
                    "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'UPDATE_STATUS', @username, @actionStamp)",
                    new { batchId, username, actionStamp = DateTime.Now });



                return result > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task InsertBatchLogAsync(int batchId, string batchType, int documentCount, int stepId, int stationId, int pageCount, string batchName)
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync(
                "INSERT INTO BatchLog (BatchId, BatchType, DocumentCount, CompletedOn, StepId, StationId, PageCount, BatchName)  " +
                "VALUES (@batchId, @batchType, @documentCount, @completedOn, @stepId, @stationId, @pageCount, @batchName)",
                new { batchId, batchType, documentCount, completedOn = DateTime.Now, stepId, stationId, pageCount, batchName });
        }

        // ========== APPLICATION & DOCUMENT TYPES ==========
        public async Task<IEnumerable<ApplicationNameDto>> GetApplicationNamesAsync()
        {
            using var conn = CreateConnection();
            var sql = _provider == "SqlServer"
                ? "SELECT CAST(Id AS NVARCHAR(MAX)) as id, Name as name, SeparationMode as SeparationMode FROM ObjectTypes WHERE Type = 'B' "
                : "SELECT Id::text as id, Name as name, SeparationMode as SeparationMode FROM ObjectTypes WHERE Type = 'B' ";
            return await conn.QueryAsync<ApplicationNameDto>(sql);
        }

        public async Task<IEnumerable<DocumentNameDto>> GetDocumentTypesAsync(GetDocumentRequest gd)
        {
            using var conn = CreateConnection();
            if (gd.AppId == 0) return new List<DocumentNameDto>();

            var sql =
                "SELECT ot.Id, ot.Name as Name, ot.Type as Type, orr.Id as NDocId " +
                "FROM ObjectTypes ot " +
                "INNER JOIN ObjectRelation orr ON ot.Id = orr.ChildObjectId " +
                "WHERE orr.ParentObjectId = @AppId AND ot.Type = 'D'";

            var docs = (await conn.QueryAsync<DocumentNameDto>(sql, new { AppId = gd.AppId })).ToList();
            docs.Add(new DocumentNameDto { Id = 0, Name = "Uncategorized", Type = "U", NDocId = -1 });
            return docs;
        }

        public async Task<IEnumerable<ObjectTypeDto>> GetObjectTypesAsync(int appId)
        {
            using var conn = CreateConnection();
            var sql =
                "SELECT ot.Id, ot.Name " +
                "FROM ObjectTypes ot " +
                "JOIN ObjectRelation orr ON ot.Id = orr.ChildObjectId " +
                "WHERE orr.ParentObjectId = @appId AND ot.Type = 'D'";
            return await conn.QueryAsync<ObjectTypeDto>(sql, new { appId });
        }

        // ========== IMAGE OPERATIONS ==========
        public async Task<int> GetMaxPageNumberAsync(int batchId)
        {
            using var conn = CreateConnection();
            return await conn.ExecuteScalarAsync<int>(
                "SELECT COALESCE(MAX(PageNo), 0) FROM BatchDetail WHERE BatchId = @batchId", new { batchId });
        }

        public Task<int> GetNextDocumentIdAsync(int batchId)
        {
            // Placeholder: Returning 0 as DocumentId column is removed
            return Task.FromResult(0);
        }

        public async Task<int> InsertBatchDetailAsync(BatchDetailDto detail)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();

            using var trans = conn.BeginTransaction();
            try
            {
                int pageNo;
                int id;
                if (_provider == "SqlServer")
                {
                    // Lock the batch row to serialize inserts for this batch
                    await conn.ExecuteAsync("SELECT 1 FROM Batch WITH (UPDLOCK) WHERE ID = @BatchId", new { detail.BatchId }, trans);
                    pageNo = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(PageNo), 0) + 1 FROM BatchDetail WHERE BatchId = @BatchId", new { detail.BatchId }, trans);
                    
                    detail.PageNo = pageNo;
                    var sql = "INSERT INTO BatchDetail (BatchId, PageNo, FileName, Format, DocPage, Status, DocTypeId, originalfilename, DocName, InternalName, DocCreatedOn) " +
                              "OUTPUT INSERTED.ID " +
                              "VALUES (@BatchId, @PageNo, @FileName, @Format, @DocPage, @Status, @DocTypeId, @PageName, @DocName, @InternalName, @DocCreatedOn)";
                    
                    id = await conn.QueryFirstAsync<int>(sql, detail, trans);
                }
                else
                {
                    // Lock the batch row in PostgreSQL
                    await conn.ExecuteAsync("SELECT 1 FROM Batch WHERE ID = @BatchId FOR UPDATE", new { detail.BatchId }, trans);
                    pageNo = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(PageNo), 0) + 1 FROM BatchDetail WHERE BatchId = @BatchId", new { detail.BatchId }, trans);
                    
                    detail.PageNo = pageNo;
                    var sql = "INSERT INTO BatchDetail (BatchId, PageNo, FileName, Format, DocPage, Status, DocTypeId, originalfilename, DocName, InternalName, DocCreatedOn) " +
                              "VALUES (@BatchId, @PageNo, @FileName, @Format, @DocPage, @Status, @DocTypeId, @PageName, @DocName, @InternalName, @DocCreatedOn) " +
                              "RETURNING ID";
                              
                    id = await conn.QueryFirstAsync<int>(sql, detail, trans);
                }

                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                else trans.Commit();
                return id;
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        // Obsolete Document methods removed as the table is merged into BatchDetail.

        public async Task<int> InsertBatchDetailsBulkAsync(IEnumerable<BatchDetailDto> details)
        {
            if (!details.Any()) return 0;

            if (_provider == "SqlServer")
            {
                using var dt = new DataTable();
                dt.Columns.Add("BatchId", typeof(long));
                dt.Columns.Add("PageNo", typeof(int));
                dt.Columns.Add("FileName", typeof(string));
                dt.Columns.Add("Format", typeof(string));
                dt.Columns.Add("DocPage", typeof(int));
                dt.Columns.Add("Status", typeof(int));
                dt.Columns.Add("DocTypeId", typeof(int));
                dt.Columns.Add("originalfilename", typeof(string));
                dt.Columns.Add("DocName", typeof(string));
                dt.Columns.Add("InternalName", typeof(string));
                dt.Columns.Add("DocCreatedOn", typeof(DateTime));

                foreach (var detail in details)
                {
                    dt.Rows.Add(
                        detail.BatchId,
                        detail.PageNo,
                        detail.FileName,
                        detail.Format,
                        detail.DocPage,
                        detail.Status,
                        detail.DocTypeId,
                        detail.PageName, // Mapped to originalfilename in DB
                        detail.DocName,
                        detail.InternalName,
                        detail.DocCreatedOn == default ? DateTime.UtcNow : detail.DocCreatedOn
                    );
                }

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var bulkCopy = new SqlBulkCopy(conn);
                bulkCopy.DestinationTableName = "BatchDetail";
                bulkCopy.ColumnMappings.Add("BatchId", "BatchId");
                bulkCopy.ColumnMappings.Add("PageNo", "PageNo");
                bulkCopy.ColumnMappings.Add("FileName", "FileName");
                bulkCopy.ColumnMappings.Add("Format", "Format");
                bulkCopy.ColumnMappings.Add("DocPage", "DocPage");
                bulkCopy.ColumnMappings.Add("Status", "Status");
                bulkCopy.ColumnMappings.Add("DocTypeId", "DocTypeId");
                bulkCopy.ColumnMappings.Add("originalfilename", "originalfilename");
                bulkCopy.ColumnMappings.Add("DocName", "DocName");
                bulkCopy.ColumnMappings.Add("InternalName", "InternalName");
                bulkCopy.ColumnMappings.Add("DocCreatedOn", "DocCreatedOn");

                await bulkCopy.WriteToServerAsync(dt);
                return dt.Rows.Count;
            }
            else
            {
                using var conn = CreateConnection();
                if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
                else conn.Open();

                using var trans = conn.BeginTransaction();
                try
                {
                    var sql =
                        "INSERT INTO BatchDetail (BatchId, PageNo, FileName, Format, DocPage, Status, DocTypeId, originalfilename, DocName, InternalName, DocCreatedOn) " +
                        "VALUES (@BatchId, @PageNo, @FileName, @Format, @DocPage, @Status, @DocTypeId, @PageName, @DocName, @InternalName, @DocCreatedOn)";

                    var result = await conn.ExecuteAsync(sql, details, trans);
                    if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                    else trans.Commit();
                    return result;
                }
                catch
                {
                    if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                    else trans.Rollback();
                    throw;
                }
            }
        }

        public async Task<bool> HasUncategorizedImagesAsync(int batchId)
        {
            using var conn = CreateConnection();
            var sql = _provider == "SqlServer"
                ? "SELECT CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END FROM BatchDetail WHERE BatchId = @batchId AND (DocName = '' OR DocName IS NULL)"
                : "SELECT COUNT(*) > 0 FROM BatchDetail WHERE BatchId = @batchId AND (DocName = '' OR DocName IS NULL)";
            return await conn.ExecuteScalarAsync<bool>(sql, new { batchId });
        }

        public async Task<bool> DeleteBatchDetailAsync(string fileName, long batchId)
        {
            using var conn = CreateConnection();
            var sql = "UPDATE BatchDetail SET Status = 'D' WHERE FileName = @fileName AND BatchId = @batchId";
            var result = await conn.ExecuteAsync(sql, new { fileName, batchId });
            return result > 0;
        }

        public async Task<bool> UpdateDocumentStatusAsync(string fileName, long batchId, string status)
        {
            using var conn = CreateConnection();
            var documentSql = "UPDATE BatchDetail SET Status = @status WHERE InternalName = @fileName AND BatchId = @batchId";
            var result = await conn.ExecuteAsync(documentSql, new { fileName, batchId, status });
            return result > 0;
        }

        public async Task<bool> SaveBatchDetailsAsync(SaveBatchModel model)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();
            // #3: Wrap all updates in a single transaction for atomicity
            using var trans = conn.BeginTransaction();
            try
            {
                // Optimization: Fetch all existing IDs for the batch once if any are missing in the model
                var missingIds = model.Images.Where(img => img.Id <= 0).ToList();
                var idMap = new Dictionary<string, int>();
                
                if (missingIds.Any())
                {
                    var existingDetails = await conn.QueryAsync<(int Id, string FileName)>(
                        "SELECT ID, FileName FROM BatchDetail WHERE BatchId = @BatchId",
                        new { BatchId = model.BatchId }, trans);
                    
                    foreach (var detail in existingDetails)
                    {
                        idMap[detail.FileName] = detail.Id;
                    }
                }

                // Prepare data for bulk-like execution via Dapper
                var updateList = model.Images.Select(image =>
                {
                    int id = image.Id > 0 ? image.Id : (idMap.TryGetValue(image.FileName, out int mappedId) ? mappedId : 0);
                    
                    if (id <= 0) return null;

                    string docName;
                    if (!string.IsNullOrEmpty(image.DisplayId)) docName = image.DisplayId;
                    else if (!string.IsNullOrEmpty(image.PageName)) docName = image.PageName;
                    else docName = $"IMG{image.PageNo:D6}";

                    return new
                    {
                        DocPage = image.DocPage,
                        doctypeid = image.DocTypeId,
                        DocName = docName,
                        BatchId = model.BatchId,
                        Id = id
                    };
                }).Where(x => x != null).ToList();

                if (updateList.Any())
                {
                    var updateBatchDetailSql = "UPDATE BatchDetail " +
                                                "SET DocPage = @DocPage, DocTypeId = @doctypeid, DocName = @DocName " +
                                                "WHERE BatchId = @BatchId AND ID = @Id";
                    
                    // Dapper executes this for each item in the collection, which is more efficient 
                    // than a manual loop because it can reuse the prepared command.
                    await conn.ExecuteAsync(updateBatchDetailSql, updateList, trans);
                }

                var batchInfo = await conn.QueryFirstOrDefaultAsync<BatchInfo>(
                    "SELECT b.BatchName, b.BatchTypeId, b.StepID as CurrentStepId, ot.Name as BatchTypeName FROM Batch b LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id WHERE b.ID = @batchId",
                    new { batchId = model.BatchId }, trans);

                if (batchInfo != null)
                {
                    var documentCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                        "SELECT COUNT(DISTINCT DocName) FROM BatchDetail WHERE BatchID = @batchId AND Status = 'A' AND DocName IS NOT NULL",
                        new { batchId = model.BatchId }, trans));

                    var pageCount = Convert.ToInt32(await conn.ExecuteScalarAsync<object>(
                        "SELECT COUNT(*) FROM BatchDetail WHERE BatchID = @batchId",
                        new { batchId = model.BatchId }, trans));

                    // Insert batch log within the same transaction
                    await conn.ExecuteAsync(
                        "INSERT INTO BatchLog (BatchId, BatchType, DocumentCount, CompletedOn, StepId, StationId, PageCount, BatchName) " +
                        "VALUES (@batchId, @batchType, @documentCount, @completedOn, @stepId, @stationId, @pageCount, @batchName)",
                        new { batchId = model.BatchId, batchType = batchInfo.BatchTypeName, documentCount, completedOn = DateTime.Now, stepId = batchInfo.CurrentStepId, stationId = 0, pageCount, batchName = batchInfo.BatchName }, trans);

                    await conn.ExecuteAsync(
                        "INSERT INTO BatchActions (BatchID, ActionName, UserName, ActionStamp) VALUES (@batchId, 'SAVED_BATCH_DETAILS', @username, @actionStamp)",
                        new { batchId = model.BatchId, username = model.Username, actionStamp = DateTime.Now }, trans);
                }

                if (trans is System.Data.Common.DbTransaction dbTrans) await dbTrans.CommitAsync();
                else trans.Commit();
                return true;
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        // ========== MONITORING ==========
        public async Task<IEnumerable<JobMonitorDto>> GetJobDataAsync(UserDto job)
        {
            using var conn = CreateConnection();
            // #25: Add server-side limit to prevent unbounded result sets
            string sql;
            if (_provider == "SqlServer")
            {
                sql = job.UserType == "admin"
                    ? "SELECT TOP 5000 * FROM JobMonitorReportQuery ORDER BY ID DESC"
                    : "SELECT TOP 5000 * FROM JobMonitorReportQuery WHERE UserName = @username ORDER BY ID DESC";
            }
            else
            {
                sql = job.UserType == "admin"
                    ? "SELECT * FROM JobMonitorReportQuery ORDER BY ID DESC LIMIT 5000"
                    : "SELECT * FROM JobMonitorReportQuery WHERE UserName = @username ORDER BY ID DESC LIMIT 5000";
            }
            return await conn.QueryAsync<JobMonitorDto>(sql, job);
        }

        public async Task<IEnumerable<Batch>> GetBatchesByStepIdAsync(int stepId)
        {
            using var conn = CreateConnection();
            var sql = "SELECT * FROM AllBatchesWithTypeName WHERE StepId = @stepId AND BatchStatus = 'A' ORDER BY CreatedOn";
            return await conn.QueryAsync<Batch>(sql, new { stepId });
        }

        public async Task<IEnumerable<ColumnTypeDto>> GetFilterColumnsAsync()
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider == "SqlServer")
            {
                sql = @"SELECT COLUMN_NAME as column_name, DATA_TYPE as datatype, 
                        CASE WHEN DATA_TYPE = 'int' THEN '1' WHEN DATA_TYPE IN ('nvarchar', 'varchar', 'text') THEN '2' 
                        WHEN DATA_TYPE IN ('datetime', 'datetime2') THEN '3' ELSE '' END as dtid 
                        FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'JobMonitorReportQuery' 
                        ORDER BY ORDINAL_POSITION";
            }
            else
            {
                sql = @"SELECT COLUMN_NAME as column_name, DATA_TYPE as datatype, 
                        CASE WHEN DATA_TYPE = 'integer' THEN '1' WHEN DATA_TYPE IN ('varchar', 'text') THEN '2' 
                        WHEN DATA_TYPE = 'timestamp' THEN '3' ELSE '' END as dtid 
                        FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'public' 
                        AND TABLE_NAME = 'jobmonitorreportquery' ORDER BY ORDINAL_POSITION";
            }
            return await conn.QueryAsync<ColumnTypeDto>(sql);
        }

        // #5: Column allowlist to prevent SQL injection — only allow columns from the view schema
        private static readonly HashSet<string> _allowedFilterColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "ID", "BatchName", "BatchType", "StepName", "BatchStatus", "UserName",
            "CreatedOn", "DocumentCount", "PageCount", "CompletedOn", "StationId"
        };

        public async Task<IEnumerable<JobMonitorDto>> GetFilteredDataAsync(FilterInputDto fi)
        {
            using var conn = CreateConnection();
            var sqlBuilder = new System.Text.StringBuilder("SELECT * FROM JobMonitorReportQuery WHERE 1=1 ");
            var parameters = new DynamicParameters();

            // Fetch all column datatypes in a single query instead of per-filter (#5 perf)
            var columnTypes = await GetAllFilterDatatypesAsync(conn);

            for (int i = 0; i < fi.filterdata.filter.Count; i++)
            {
                var rule = fi.filterdata.filter[i];

                // #5: Validate column name against allowlist to prevent SQL injection
                if (!_allowedFilterColumns.Contains(rule.column))
                {
                    throw new ArgumentException($"Invalid filter column: {rule.column}");
                }

                var dataType = columnTypes.TryGetValue(rule.column, out var dt) ? dt : "varchar";
                var op = GetOperator(fi.filterdata.filter[i].condition);

                if (dataType == "timestamp" && op == "BETWEEN")
                {
                    sqlBuilder.Append($" AND CAST({rule.column} AS DATE) BETWEEN @start{i} AND @end{i}");
                    parameters.Add($"start{i}", rule.start);
                    parameters.Add($"end{i}", rule.end);
                }
                else
                {
                    var param = op == "LIKE" ? $"%{rule.value}%" : rule.value;
                    sqlBuilder.Append($" AND {rule.column} {op} @value{i}");
                    parameters.Add($"value{i}", param);
                }
            }

            return await conn.QueryAsync<JobMonitorDto>(sqlBuilder.ToString(), parameters);
        }

        // #5: Fetch all column datatypes in one query instead of N queries
        private async Task<Dictionary<string, string>> GetAllFilterDatatypesAsync(IDbConnection conn)
        {
            var sql = _provider == "SqlServer"
                ? "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'JobMonitorReportQuery'"
                : "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'public' AND TABLE_NAME = 'jobmonitorreportquery'";
            
            var results = await conn.QueryAsync<(string COLUMN_NAME, string DATA_TYPE)>(sql);
            return results.ToDictionary(r => r.COLUMN_NAME, r => r.DATA_TYPE, StringComparer.OrdinalIgnoreCase);
        }

        // ========== PURGING ==========
        public async Task<IEnumerable<Batch>> GetBatchesInDateRangeAsync(DateTime startDate, DateTime endDate, int applicationId = 0, int stepId = 0)
        {
            using var conn = CreateConnection();
            var sql = "SELECT * FROM Batch WHERE CreatedOn >= @startDate AND CreatedOn <= @endDate AND (Ispurging IS NULL OR Ispurging = false)";
            
            if (applicationId > 0)
                sql += " AND BatchTypeId = @applicationId";
                
            if (stepId > 0)
                sql += " AND StepId = @stepId";
                
            return await conn.QueryAsync<Batch>(sql, new { startDate, endDate, applicationId, stepId });
        }

        public async Task UpdateBatchPurgeStatusAsync(int batchId, bool isPurging)
        {
            using var conn = CreateConnection();
            var sql = "UPDATE Batch SET Ispurging = @isPurging WHERE ID = @batchId";
            await conn.ExecuteAsync(sql, new { isPurging, batchId });
        }

        public async Task<bool> DeleteBatchRecordAsync(int batchId)
        {
            using var conn = CreateConnection();
            if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
            else conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                await conn.ExecuteAsync("DELETE FROM BatchDetail WHERE BatchId = @batchId", new { batchId }, trans);
                await conn.ExecuteAsync("DELETE FROM BatchLog WHERE BatchId = @batchId", new { batchId }, trans);
                await conn.ExecuteAsync("DELETE FROM BatchActions WHERE BatchID = @batchId", new { batchId }, trans);
                var result = await conn.ExecuteAsync("DELETE FROM Batch WHERE ID = @batchId", new { batchId }, trans);
                if (trans is System.Data.Common.DbTransaction tDbTrans) await tDbTrans.CommitAsync();
                else trans.Commit();
                return result > 0;
            }
            catch
            {
                if (trans is System.Data.Common.DbTransaction errorTrans) await errorTrans.RollbackAsync();
                else trans.Rollback();
                throw;
            }
        }

        private static string GetOperator(string condition) => condition.ToLower() switch
        {
            "equals" => "=",
            "like" => "LIKE",
            "isgreaterthan" => ">",
            "islessthan" => "<",
            "range" => "BETWEEN",
            _ => "="
        };



        public async Task<IEnumerable<BatchImageWithDocNameDto>> GetBatchImagesAsync(int batchId)
        {
            using var conn = CreateConnection();

            var sql =
                "SELECT  " +
                "bd.ID, " +
                "bd.PageNo, " +
                "bd.FileName, " +
                "bd.Format, " +
                "bd.DocPage, " +
                "bd.Status, " +
                "bd.DocTypeId, " +
                "bd.PageName, " +
                "COALESCE(bd.DocName, '') as DocName, " +
                "ot.Name as DocTypeName, " +
                "dwt.ID as DocumentId " +
                "FROM BatchDetail bd " +
                "LEFT JOIN DocumentWithTypeName dwt ON bd.BatchID = dwt.BatchID AND bd.DocName = dwt.DocName " +
                "LEFT JOIN ObjectTypes ot ON bd.DocTypeId = ot.Id " +
                "WHERE bd.BatchId = @batchId and  bd.status='A' " +
                "ORDER BY bd.PageNo";

            var result = await conn.QueryAsync<BatchImageWithDocNameDto>(sql, new { batchId });
            return result;
        }

        public async Task<bool> UpdateBatchOcrTypeAsync(int batchId, string ocrType)
        {
            try
            {
                using var conn = CreateConnection();
                if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
                else conn.Open();

                var updateSql = "UPDATE Batch SET OcrType = @OcrType WHERE ID = @batchId";
                var result = await conn.ExecuteAsync(updateSql, new { OcrType = ocrType, batchId });

                return result > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetBatchOcrTypeAsync(int batchId)
        {
            try
            {
                using var conn = CreateConnection();
                if (conn is System.Data.Common.DbConnection dbConn) await dbConn.OpenAsync();
                else conn.Open();

                var sql = "SELECT OcrType FROM Batch WHERE ID = @batchId";
                var ocrType = await conn.QueryFirstOrDefaultAsync<string>(sql, new { batchId });

                return ocrType ?? "tesseract"; // Default to tesseract if not set
            }
            catch
            {
                return "tesseract"; // Default to tesseract on error
            }
        }

        public async Task<IEnumerable<Batch>> GetLatestBatchesAsync(int count = 100)
        {
            using var conn = CreateConnection();
            var sql = _provider == "SqlServer"
                ? $@"
                SELECT TOP {count}
                    b.ID,
                    b.BatchName,
                    b.BatchTypeId,
                    b.CreatedOn,
                    b.BatchStatus,
                    b.StepId,
                    ot.Name as Name
                FROM Batch b
                LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id
                ORDER BY b.CreatedOn DESC"
                : @"
                SELECT 
                    b.ID,
                    b.BatchName,
                    b.BatchTypeId,
                    b.CreatedOn,
                    b.BatchStatus,
                    b.StepId,
                    ot.Name as Name
                FROM Batch b
                LEFT JOIN ObjectTypes ot ON b.BatchTypeId = ot.Id
                ORDER BY b.CreatedOn DESC
                LIMIT @count";
            
            return await conn.QueryAsync<Batch>(sql, new { count });
        }

        public async Task<bool> ClaimBatchAsync(int batchId, int stepId, string workerId, int timeoutMinutes = 60)
        {
            using var conn = CreateConnection();
            var now = DateTime.Now;
            var threshold = now.AddMinutes(-timeoutMinutes);

            var sql = @"
                UPDATE Batch 
                SET LockedBy = @workerId, 
                    LockedOn = @now,
                    BatchStatus = 'P'
                WHERE ID = @batchId 
                  AND StepId = @stepId 
                  AND (LockedBy IS NULL OR LockedOn < @threshold)";

            var affectedRows = await conn.ExecuteAsync(sql, new 
            { 
                batchId, 
                stepId, 
                workerId, 
                now,
                threshold
            });

            return affectedRows > 0;
        }

        public async Task<bool> ReleaseBatchAsync(int batchId, string workerId)
        {
            using var conn = CreateConnection();
            var sql = @"
                UPDATE Batch 
                SET LockedBy = NULL, 
                    LockedOn = NULL
                WHERE ID = @batchId AND LockedBy = @workerId";

            var affectedRows = await conn.ExecuteAsync(sql, new { batchId, workerId });
            return affectedRows > 0;
        }

        public async Task<int> RecoverStaleLocksAsync(int timeoutMinutes)
        {
            using var conn = CreateConnection();
            var threshold = DateTime.Now.AddMinutes(-timeoutMinutes);
            var sql = @"
                UPDATE Batch 
                SET LockedBy = NULL, 
                    LockedOn = NULL,
                    BatchStatus = 'A'
                WHERE LockedBy IS NOT NULL AND LockedOn < @threshold";

            return await conn.ExecuteAsync(sql, new { threshold });
        }

        public async Task<Batch?> PickNextBatchAsync(int stepId, string workerId, int timeoutMinutes = 60)
        {
            using var conn = CreateConnection();
            var now = DateTime.Now;
            var threshold = now.AddMinutes(-timeoutMinutes);

            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                // #10: Added UPDLOCK for stronger concurrency safety on SQL Server
                sql = @"
                    WITH CTE AS (
                        SELECT TOP (1) ID, BatchTypeId
                        FROM Batch WITH (UPDLOCK, ROWLOCK, READPAST)
                        WHERE StepId = @stepId 
                          AND BatchStatus = 'A' 
                          AND (LockedBy IS NULL OR LockedOn < @threshold)
                        ORDER BY CreatedOn
                    )
                    UPDATE CTE 
                    SET LockedBy = @workerId, 
                        LockedOn = @now, 
                        BatchStatus = 'P'
                    OUTPUT INSERTED.ID, INSERTED.BatchName, INSERTED.BatchTypeId, INSERTED.CreatedOn, 
                           INSERTED.BatchStatus, INSERTED.StepId, INSERTED.LockedOn, INSERTED.LockedBy,
                           (SELECT Name FROM ObjectTypes WHERE Id = INSERTED.BatchTypeId) as Name";
            }
            else
            {
                sql = @"
                    UPDATE Batch
                    SET LockedBy = @workerId, 
                        LockedOn = @now, 
                        BatchStatus = 'P'
                    WHERE ID = (
                        SELECT ID FROM Batch
                        WHERE StepId = @stepId 
                          AND BatchStatus = 'A' 
                          AND (LockedBy IS NULL OR LockedOn < @threshold)
                        ORDER BY CreatedOn
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                    )
                    RETURNING ID, BatchName, BatchTypeId, CreatedOn, BatchStatus, StepId, LockedOn, LockedBy,
                    (SELECT Name FROM ObjectTypes WHERE Id = BatchTypeId) as Name";
            }

            return await conn.QueryFirstOrDefaultAsync<Batch>(sql, new 
            { 
                stepId, 
                workerId, 
                now, 
                threshold 
            });
        }
    }
}