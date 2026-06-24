using Microsoft.Extensions.Hosting;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueCapture.Services;
using Microsoft.AspNetCore.SignalR;
using Server.Hubs;

namespace Server.Services
{
    public class OcrProcessingService : BackgroundService
    {
        private readonly ILogger<OcrProcessingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<BatchLockHub> _lockHubContext;
        private readonly string _workerId;
        private readonly TimeSpan _interval;
        private readonly bool _isEnabled;

        public OcrProcessingService(
            ILogger<OcrProcessingService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IHubContext<BatchLockHub> lockHubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _lockHubContext = lockHubContext;
            _workerId = $"OCR_Worker_{Guid.NewGuid().ToString().Substring(0, 8)}";

            var intervalSeconds = configuration.GetValue<int>("OcrProcessing:IntervalSeconds", 10);
            _interval = TimeSpan.FromSeconds(intervalSeconds);
            _isEnabled = configuration.GetValue<bool>("OcrProcessing:Enabled", true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled)
            {
                _logger.LogInformation("OCR Processing Service is disabled by configuration and will not start.");
                return;
            }

            _logger.LogInformation("OCR Processing Service is starting. WorkerId={WorkerId}", _workerId);

            Npgsql.NpgsqlConnection? listenConn = null;
            using var scope = _serviceProvider.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var connStr = configuration.GetConnectionString("DefaultConnection") ?? "";
            bool isPostgres = connStr.Contains("Host=", StringComparison.OrdinalIgnoreCase);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    listenConn = await EnsureListenConnectionAsync(listenConn, connStr, isPostgres, stoppingToken);

                    await ProcessOcrBatches(stoppingToken);

                    await WaitForNextBatchAsync(listenConn, isPostgres, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("OCR Processing Service received cancellation signal. Shutting down cleanly.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in OCR Processing Service main loop. Will retry in 10 seconds.");
                    await SafeLogAsync("SYSTEM", "OCR_SERVICE_ERROR", "Unhandled error in OCR service main loop", "ERROR", ex.StackTrace, ex.Message, null);
                    if (listenConn != null) { await listenConn.DisposeAsync(); listenConn = null; }

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            if (listenConn != null) await listenConn.DisposeAsync();
            _logger.LogInformation("OCR Processing Service has stopped.");
        }

        private async Task<Npgsql.NpgsqlConnection?> EnsureListenConnectionAsync(Npgsql.NpgsqlConnection? listenConn, string connStr, bool isPostgres, CancellationToken stoppingToken)
        {
            if (isPostgres && listenConn == null)
            {
                listenConn = new Npgsql.NpgsqlConnection(connStr);
                await listenConn.OpenAsync(stoppingToken);
                await using var cmd = new Npgsql.NpgsqlCommand("LISTEN batch_step_4", listenConn);
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
            return listenConn;
        }

        private async Task WaitForNextBatchAsync(Npgsql.NpgsqlConnection? listenConn, bool isPostgres, CancellationToken stoppingToken)
        {
            if (isPostgres && listenConn != null && listenConn.State == System.Data.ConnectionState.Open)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
                try
                {
                    await listenConn.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown or timeout — satisfy SonarQube with a comment
                }
            }
            else
            {
                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task ProcessOcrBatches(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var maxParallel = configuration.GetValue<int>("OcrProcessing:MaxParallelWorkers", 5);
            var lockTimeout = configuration.GetValue<int>("OcrProcessing:LockTimeoutMinutes", 60);

            using var semaphore = new SemaphoreSlim(maxParallel);
            var tasks = new HashSet<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                // If shutdown is requested, stop picking new batches
                if (stoppingToken.IsCancellationRequested) break;

                await semaphore.WaitAsync(stoppingToken);

                Batch? batch;
                try
                {
                    // Atomically pick and lock the next available batch
                    batch = await batchRepository.PickNextBatchAsync(4, _workerId, lockTimeout);
                }
                catch (OperationCanceledException)
                {
                    semaphore.Release();
                    throw; // Let it bubble to ExecuteAsync
                }
                catch (Exception ex)
                {
                    semaphore.Release();
                    _logger.LogError(ex, "Error picking next OCR batch. Will retry next cycle.");
                    break;
                }

                if (batch == null)
                {
                    semaphore.Release();
                    break; // No more batches this cycle
                }

                var capturedBatch = batch;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessSingleBatchWithLock(capturedBatch, lockTimeout, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        // Per-batch exception is already handled & logged inside ProcessSingleBatchWithLock.
                        // We swallow here to prevent Task.WhenAll from propagating it and killing other tasks.
                        _logger.LogError(ex, "OCR batch {BatchId} task faulted unexpectedly at top level.", capturedBatch.ID);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                
                lock (tasks) { tasks.Add(task); }
                _ = task.ContinueWith(t => { lock (tasks) { tasks.Remove(t); } });
            }

            // Wait for all in-flight tasks regardless of individual failures
            Task[] inFlight;
            lock (tasks) { inFlight = tasks.ToArray(); }
            if (inFlight.Length > 0)
            {
                await Task.WhenAll(inFlight);
            }
        }

        private async Task ProcessSingleBatchWithLock(Batch batch, int lockTimeout, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
            var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();

            int batchId = batch.ID;

            try
            {
                _logger.LogInformation("Worker {WorkerId} starting OCR for Batch {BatchId}", _workerId, batchId);

                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batchId,
                    Status = "Running",
                    WorkerId = _workerId,
                    Step = "OCR"
                }, CancellationToken.None);

                await ProcessBatchInternal(batchId, stoppingToken, scope, batchLogService);

                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batchId,
                    Status = "Completed",
                    WorkerId = _workerId,
                    Step = "OCR"
                }, CancellationToken.None);

                _logger.LogInformation("Worker {WorkerId} completed OCR for Batch {BatchId}", _workerId, batchId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OCR for Batch {BatchId}. Moving to exception step.", batchId);

                // Belt-and-suspenders: also move to exception here in case ProcessBatchInternal
                // threw BEFORE its own try block (e.g. from StartTaskTimingAsync).
                try { await batchRepository.MoveToExceptionStepAsync(batchId, "system"); }
                catch (Exception moveEx) { _logger.LogError(moveEx, "Failed to move Batch {BatchId} to exception step", batchId); }

                await SafeLogAsync(batchId, "OCR_BATCH_FAILED", $"Batch {batchId} OCR failed", "ERROR", ex.StackTrace, ex.Message, batchLogService);

                try
                {
                    await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                    {
                        BatchId = batchId,
                        Status = "Error",
                        Message = ex.Message,
                        WorkerId = _workerId,
                        Step = "OCR"
                    }, CancellationToken.None);
                }
                catch (Exception sigEx)
                {
                    _logger.LogWarning(sigEx, "Failed to send SignalR error notification for Batch {BatchId}", batchId);
                }
            }
            finally
            {
                // Always release the distributed lock — even if everything above fails
                try
                {
                    await batchRepository.ReleaseBatchAsync(batchId, _workerId);
                }
                catch (Exception releaseEx)
                {
                    _logger.LogError(releaseEx, "CRITICAL: Failed to release lock for Batch {BatchId}.", batchId);
                }
            }
        }

        private async Task ProcessBatchInternal(int batchId, CancellationToken stoppingToken, IServiceScope scope, IBatchLogService batchLogService)
        {
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
            var verifyRepository = scope.ServiceProvider.GetRequiredService<IVerifyRepository>();
            var ocrConnectorService = scope.ServiceProvider.GetRequiredService<IOcrConnectorService>();

            // ALL code is inside the try block so any exception always reaches MoveToExceptionStepAsync
            try
            {
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                _logger.LogInformation("Processing OCR for Batch {BatchId}", batchId);
                await batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 4, Status = "Start" });
                await SafeLogAsync(batchId, "OCR_PROCESSING_START", $"Starting OCR for batch {batchId}", "INFO", null, null, batchLogService);

                var batch = await batchRepository.GetBatchByIdAsync(batchId);
                if (batch == null)
                {
                    _logger.LogWarning("Batch {BatchId} not found during OCR processing.", batchId);
                    return;
                }

                // Hierarchical Connector Resolution (2-tier: Application -> Global Default)
                // 1. Check Application (Batch Type) Level
                var activeConnector = await ocrConnectorService.GetOcrConnectorByApplicationIdAsync(batch.BatchTypeId);
                
                // 2. Fallback to Global Default
                if (activeConnector == null)
                {
                    _logger.LogInformation("No Application-level OCR selection for BatchType {BatchTypeId}. Checking Global Default.", batch.BatchTypeId);
                    activeConnector = await ocrConnectorService.GetDefaultOcrConnectorAsync();
                }

                var providerName = activeConnector?.Provider?.Name ?? "tesseract";
                var configData = activeConnector?.ConfigData;

                _logger.LogInformation("ENGINE_DIAGNOSTIC: Resolved Provider '{Provider}' for Batch {BatchId} (Application {AppName})", 
                    providerName, batchId, batch.BatchName);

                var documents = (await verifyRepository.GetDocumentsForVerifyAsync(batchId)).ToList();
                var allPages = (await verifyRepository.GetPagesForVerifyAsync(batchId)).ToList();

                // Track total documents and successful processing
                int totalDocs = documents.Count;
                int processedDocs = 0;
                int failedDocs = 0;

                // Parallelization: Process documents within the batch in parallel
                var maxParallelDocs = configuration.GetValue<int>("OcrProcessing:MaxParallelDocsPerBatch", 3);
                var parallelOptions = new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = maxParallelDocs,
                    CancellationToken = stoppingToken 
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (document, ct) =>
                {
                    // CRITICAL: Create a NEW scope for each document to ensure absolute thread safety for repositories/services
                    using var docScope = _serviceProvider.CreateScope();
                    var docOcrEngine = docScope.ServiceProvider.GetRequiredService<IOcrEngineService>();

                    try
                    {
                        var documentPages = allPages
                            .Where(p => p.DocId == document.DocId)
                            .OrderBy(p => p.DocPage)
                            .ToList();

                        if (!documentPages.Any())
                        {
                            _logger.LogWarning("No pages found for Document {DocId} in Batch {BatchId}. Skipping OCR.", document.DocId, batchId);
                            return;
                        }

                        // Run OCR using the resolved provider and config
                        await docOcrEngine.ProcessDocumentAsync(providerName, document, documentPages, batchId, configData);
                        
                        Interlocked.Increment(ref processedDocs);
                    }
                    catch (Exception docEx)
                    {
                        Interlocked.Increment(ref failedDocs);
                        _logger.LogError(docEx, "OCR failed for Document {DocId} in Batch {BatchId}. Continuing with other documents.", document.DocId, batchId);
                        await SafeLogAsync(batchId, "OCR_DOC_ERROR", $"Document {document.DocId} OCR failed", "ERROR", docEx.StackTrace, docEx.Message, batchLogService);
                    }
                });

                _logger.LogInformation("Completed OCR processing for Batch {BatchId}. Success: {Processed}/{Total}, Failures: {Failed}", 
                    batchId, processedDocs, totalDocs, failedDocs);

                await SafeLogAsync(batchId, "OCR_BATCH_COMPLETE", $"OCR completed: {processedDocs} documents processed successfully.", "SUCCESS", null, null, batchLogService);
                
                // If EVERYTHING failed or there were fatal errors, move to exception step instead of next
                if (failedDocs > 0 && processedDocs == 0 && totalDocs > 0)
                {
                    _logger.LogError("All documents failed OCR in Batch {BatchId}. Moving to Exception step.", batchId);
                    await batchRepository.MoveToExceptionStepAsync(batchId, "system");
                }
                else
                {
                    await batchRepository.MoveToNextStepAsync(batchId, "system");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in OCR processing for Batch {BatchId}", batchId);
                await SafeLogAsync(batchId, "OCR_FATAL_ERROR", ex.Message, "FATAL", ex.StackTrace, ex.Message, batchLogService);
                
                try { await batchRepository.MoveToExceptionStepAsync(batchId, "system"); }
                catch (Exception moveEx) { _logger.LogError(moveEx, "Failed to move Batch {BatchId} to Exception step after fatal error.", batchId); }
                throw; // Re-throw so ProcessSingleBatchWithLock sends "Error" SignalR
            }
        }

        // Provider-specific methods moved to OcrEngineService

        // ─── Logging helper ──────────────────────────────────────────────────────────

        /// <summary>
        /// Single unambiguous logging helper. Uses the provided <paramref name="batchLogService"/>
        /// if available (no scope creation). Falls back to creating a minimal scope when null
        /// (e.g. in ExecuteAsync error handler where no scope exists).
        /// </summary>
        private async Task SafeLogAsync(
            int batchId,
            string task,
            string message,
            string status,
            string? details,
            string? errorMessage,
            IBatchLogService? batchLogService)
            => await SafeLogAsync(batchId.ToString(), task, message, status, details, errorMessage, batchLogService);

        private async Task SafeLogAsync(
            string batchId,
            string task,
            string message,
            string status,
            string? details,
            string? errorMessage,
            IBatchLogService? batchLogService)
        {
            try
            {
                if (batchLogService != null)
                {
                    await batchLogService.LogToFile(batchId, task, message, status, details, errorMessage);
                }
                else
                {
                    // Fallback: create a minimal scope (used only in ExecuteAsync where no scope exists)
                    using var scope = _serviceProvider.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IBatchLogService>();
                    await svc.LogToFile(batchId, task, message, status, details, errorMessage);
                }
            }
            catch (Exception logEx)
            {
                // Never let a logging failure crash the service
                _logger.LogWarning(logEx, "Failed to write batch log for batch {BatchId} task {Task}", batchId, task);
            }
        }

        private string GetContentType(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName)?.ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tiff" or ".tif" => "image/tiff",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }
    }
}
