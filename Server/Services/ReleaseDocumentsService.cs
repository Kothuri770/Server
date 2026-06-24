using Microsoft.Extensions.Hosting;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using Server.Services.DMS;
using System.Text.Encodings.Web;
using System.Text.Json;
using TrueCapture.Services;
using Microsoft.AspNetCore.SignalR;
using Server.Hubs;
using Microsoft.Extensions.Configuration;

namespace Server.Services
{
    public class ReleaseDocumentsService : BackgroundService
    {
        private readonly ILogger<ReleaseDocumentsService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly DmsConnectorManager _dmsConnectorManager;
        private readonly IHubContext<BatchLockHub> _lockHubContext;
        private readonly string _workerId;
        private readonly TimeSpan _interval;
        private readonly bool _isEnabled;

        public ReleaseDocumentsService(
            ILogger<ReleaseDocumentsService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            DmsConnectorManager dmsConnectorManager,
            IHubContext<BatchLockHub> lockHubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _dmsConnectorManager = dmsConnectorManager;
            _configuration = configuration;
            _lockHubContext = lockHubContext;
            _workerId = $"Release_Worker_{Guid.NewGuid().ToString().Substring(0, 8)}";

            var intervalSeconds = configuration.GetValue<int>("ReleaseDocuments:IntervalSeconds", 5);
            _interval = TimeSpan.FromSeconds(intervalSeconds);
            _isEnabled = configuration.GetValue<bool>("ReleaseDocuments:Enabled", true);
        }

        // ─── Main Service Loop ───────────────────────────────────────────────────────

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled)
            {
                _logger.LogInformation("Release Documents Service is disabled by configuration and will not start.");
                return;
            }

            _logger.LogInformation("Release Documents Service is starting. WorkerId={WorkerId}", _workerId);

            Npgsql.NpgsqlConnection? listenConn = null;
            var connStr = _configuration.GetConnectionString("DefaultConnection") ?? "";
            bool isPostgres = connStr.Contains("Host=", StringComparison.OrdinalIgnoreCase);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    listenConn = await EnsureListenConnectionAsync(listenConn, connStr, isPostgres, stoppingToken);

                    await ProcessReleaseBatches(stoppingToken);

                    await WaitForNextBatchAsync(listenConn, isPostgres, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Release Documents Service received cancellation. Shutting down cleanly.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in Release Documents Service main loop. Will retry in 10 seconds.");
                    await LogToFileAsync("SYSTEM", "RELEASE_SERVICE_ERROR", "Unhandled error in Release service main loop", "ERROR", ex.StackTrace, ex.Message);
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
            _logger.LogInformation("Release Documents Service has stopped.");
        }

        private async Task<Npgsql.NpgsqlConnection?> EnsureListenConnectionAsync(Npgsql.NpgsqlConnection? listenConn, string connStr, bool isPostgres, CancellationToken stoppingToken)
        {
            if (isPostgres && listenConn == null)
            {
                listenConn = new Npgsql.NpgsqlConnection(connStr);
                await listenConn.OpenAsync(stoppingToken);
                await using var cmd = new Npgsql.NpgsqlCommand("LISTEN batch_step_8", listenConn);
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

        // ─── Batch Orchestration ─────────────────────────────────────────────────────

        private async Task ProcessReleaseBatches(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();

            var maxParallel = _configuration.GetValue<int>("ReleaseDocuments:MaxParallelWorkers", 5);
            var lockTimeout = _configuration.GetValue<int>("ReleaseDocuments:LockTimeoutMinutes", 60);

            using var semaphore = new SemaphoreSlim(maxParallel);
            var tasks = new HashSet<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                await semaphore.WaitAsync(stoppingToken);

                Batch? batch;
                try
                {
                    // Atomically pick-and-lock the next release-ready batch
                    batch = await batchRepository.PickNextBatchAsync(8, _workerId, lockTimeout);
                }
                catch (OperationCanceledException)
                {
                    semaphore.Release();
                    throw; // Bubble up to ExecuteAsync for clean shutdown
                }
                catch (Exception ex)
                {
                    semaphore.Release();
                    _logger.LogError(ex, "Error picking next Release batch. Will retry next cycle.");
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
                        _logger.LogError(ex, "Release Documents batch {BatchId} task faulted at top level.", capturedBatch.ID);
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

        // ─── Per-Batch Processing ────────────────────────────────────────────────────

        private async Task ProcessSingleBatchWithLock(Batch batch, int lockTimeout, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
            var verifyRepository = scope.ServiceProvider.GetRequiredService<IVerifyRepository>();
            var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();

            int batchId = batch.ID;

            try
            {
                _logger.LogInformation("Worker {WorkerId} starting Release for Batch {BatchId}", _workerId, batchId);

                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batchId,
                    Status = "Processing",
                    WorkerId = _workerId,
                    Step = "Release"
                }, CancellationToken.None);

                await batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 8, Status = "Start" });

                await ProcessBatchReleaseInternalAsync(
                    batchId, verifyRepository, configurationService, batchLogService, batchRepository, stoppingToken);

                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batchId,
                    Status = "Completed",
                    WorkerId = _workerId,
                    Step = "Release"
                }, CancellationToken.None);

                _logger.LogInformation("Worker {WorkerId} completed Release for Batch {BatchId}", _workerId, batchId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing Batch {BatchId}. Moving to exception step.", batchId);

                try { await batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 8, Status = "Error", ErrorMessage = ex.Message }); }
                catch (Exception logEx) { _logger.LogError(logEx, "Failed to log Error task timing for Batch {BatchId}", batchId); }

                try { await LogToFileAsync(batchId.ToString(), "RELEASE_FAILED", $"Fatal error releasing batch {batchId}", "ERROR", ex.StackTrace, ex.Message, batchLogService); }
                catch (Exception logEx)
                {
                    _logger.LogWarning(logEx, "Failed to log final failure for Batch {BatchId}", batchId);
                }

                try { await batchRepository.UpdateBatchStatusAsync(batchId, "E", "system"); }
                catch (Exception statusEx) { _logger.LogError(statusEx, "Failed to update status to E for Batch {BatchId}", batchId); }

                try { await batchRepository.MoveToExceptionStepAsync(batchId, "system"); }
                catch (Exception moveEx) { _logger.LogError(moveEx, "Failed to move Batch {BatchId} to exception step", batchId); }

                try
                {
                    await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                    {
                        BatchId = batchId,
                        Status = "Error",
                        Error = ex.Message,
                        WorkerId = _workerId,
                        Step = "Release"
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
                    _logger.LogError(releaseEx, "CRITICAL: Failed to release lock for Batch {BatchId}. Batch may stay locked until timeout.", batchId);
                }
            }
        }

        private async Task ProcessBatchReleaseInternalAsync(
            int batchId,
            IVerifyRepository verifyRepository,
            IConfigurationService configurationService,
            IBatchLogService batchLogService,
            IBatchRepository batchRepository,
            CancellationToken stoppingToken)
        {
            var batch = await batchRepository.GetBatchInternalAsync(batchId);
            if (batch == null)
            {
                _logger.LogError("Batch {BatchId} not found in database for release", batchId);
                return;
            }

            try
            {
                _logger.LogInformation("Processing release for Batch {BatchId}", batch.ID);

                await batchRepository.UpdateBatchStatusAsync(batchId, "P", "system");
                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batchId,
                    Status = "Running",
                    WorkerId = _workerId,
                    Step = "Release"
                }, CancellationToken.None);

                await LogToFileAsync(batch.ID.ToString(), "RELEASE_START", $"Starting release for batch {batch.ID}", "INFO", batchLogService: batchLogService);

                // Get all documents and pages for the batch upfront (single DB round-trip each)
                var allBatchDocuments = (await verifyRepository.GetDocumentsForVerifyAsync(batch.ID)).ToList();
                var allBatchPages = (await verifyRepository.GetPagesForVerifyAsync(batch.ID)).ToList();

                // Handle orphan pages (pages without a matching document record)
                var orphanPages = allBatchPages
                    .Where(p => !allBatchDocuments.Any(d => d.DocId == p.DocId && p.DocId > 0) && p.DocId >= 0)
                    .ToList();

                if (orphanPages.Any())
                {
                    _logger.LogInformation("Found {Count} orphan pages in Batch {BatchId}. Creating virtual documents.", orphanPages.Count, batch.ID);
                    var orphanGroups = orphanPages
                        .GroupBy(p => p.DocId > 0 ? p.DocId.ToString() : "orphan_page_" + p.PageId)
                        .ToList();

                    foreach (var group in orphanGroups)
                    {
                        var firstPage = group.First();
                        allBatchDocuments.Add(new DocumentModel
                        {
                            DocId = firstPage.DocId,
                            DocName = !string.IsNullOrEmpty(firstPage.DocName) ? firstPage.DocName :
                                      !string.IsNullOrEmpty(firstPage.OriginalFilename) ? Path.GetFileNameWithoutExtension(firstPage.OriginalFilename) :
                                      "IMG_" + firstPage.PageId,
                            DocTypeId = 0,
                            InternalName = firstPage.FileName,
                            Status = 1
                        });
                    }
                }

                var localFallbackDocuments = new List<DocumentModel>();
                var dmsProcessingGroups = new Dictionary<string, (DmsConfigDto Config, List<DocumentModel> Documents)>();
                var successfullyProcessedDmsGroups = new List<(DmsConfigDto Config, IDmsConnector? Connector)>();

                // Get all DMS providers once (avoid repeated DB calls inside foreach)
                var allProviders = await configurationService.GetDmsProvidersAsync();

                // #17: Cache DMS configs per DocTypeId to avoid querying per document
                var dmsConfigCache = new Dictionary<int, IEnumerable<DmsConfigDto>>();

                foreach (var doc in allBatchDocuments)
                {
                    if (doc.DocTypeId > 0)
                    {
                        if (!dmsConfigCache.TryGetValue(doc.DocTypeId, out var dmsConfigs))
                        {
                            dmsConfigs = await configurationService.GetDmsConfigsForDocTypeAsync(doc.DocTypeId);
                            dmsConfigCache[doc.DocTypeId] = dmsConfigs;
                        }
                        var dmsConfig = dmsConfigs.FirstOrDefault(c => c.IsActive);

                        if (dmsConfig != null && dmsConfig.IsActive)
                        {
                            var key = $"{dmsConfig.ProviderId}|{dmsConfig.DMSCabinetName}|{dmsConfig.DMSClassName}|{dmsConfig.ReleaseFolder}|{dmsConfig.Url}";
                            if (!dmsProcessingGroups.ContainsKey(key))
                            {
                                dmsProcessingGroups[key] = (dmsConfig, new List<DocumentModel>());
                            }
                            dmsProcessingGroups[key].Documents.Add(doc);
                            continue;
                        }
                    }
                    localFallbackDocuments.Add(doc);
                }

                // Process DMS groups — pass allProviders so no new scope is needed inside
                foreach (var group in dmsProcessingGroups.Values)
                {
                    try
                    {
                        var providerName = GetProviderNameById(group.Config.ProviderId, allProviders);
                        await LogToFileAsync(batch.ID.ToString(), "DMS_GROUP_PROCESSING",
                            $"Processing {providerName} group with {group.Documents.Count} documents", "INFO", batchLogService: batchLogService);

                        if (_dmsConnectorManager.IsConnectorSupported(providerName))
                        {
                            var connector = await ProcessDocumentsWithDmsConnector(
                                batch, group.Config, group.Documents, allBatchPages,
                                verifyRepository, configurationService, batchLogService, stoppingToken);

                            if (connector != null)
                                successfullyProcessedDmsGroups.Add((Config: group.Config, Connector: connector));
                        }
                        else
                        {
                            _logger.LogWarning("Connector '{Provider}' not supported. Falling back to local.", providerName);
                            localFallbackDocuments.AddRange(group.Documents);
                        }
                    }
                    catch (Exception groupEx)
                    {
                        _logger.LogError(groupEx, "Error processing DMS group for Batch {BatchId}.", batchId);
                        throw;
                    }
                }

                // Consolidated JSON generation
                var batchTypeName = await verifyRepository.GetBatchTypeNameAsync(batch.ID);
                var releaseFolderMain = await configurationService.GetConfigurationsValue("Batch Folder");
                if (string.IsNullOrEmpty(releaseFolderMain)) releaseFolderMain = Path.GetTempPath();

                var releaseFolderLocal = await configurationService.GetConfigurationsValue("Document Folder");
                if (string.IsNullOrEmpty(releaseFolderLocal)) releaseFolderLocal = "releases";

                var batchTypeFolder = Path.Combine(releaseFolderLocal, batchTypeName);
                var fullBatchFolderPath = Path.GetFullPath(Path.Combine(batchTypeFolder, batch.BatchName));

                var batchDetailJson = await CreateBatchDetailJsonDto(
                    batch.ID, batchTypeName, allBatchDocuments, allBatchPages,
                    verifyRepository, configurationService, batchRepository, fullBatchFolderPath);

                var jsonString = JsonSerializer.Serialize(new { batch = batchDetailJson }, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                var jsonFileName = $"{batchTypeName}_{batch.BatchName}.json";
                // When a DMS connector is configured for this batch, stage the JSON in the system
                // temp directory only (for DMS upload) — the temp folder is deleted afterwards.
                // No ID-based folder (e.g. "1") is ever written to local storage when DMS is active.
                // When there is NO DMS connector, the JSON is written to the local Batch Folder
                // (releaseFolderMain) under a sub-folder named by batch ID, as before.
                bool isUsingRealTemp = dmsProcessingGroups.Any();

                string tempBatchFolderPath = isUsingRealTemp
                    ? Path.Combine(Path.GetTempPath(), "TrueCapture_Release", batch.ID.ToString())
                    : Path.Combine(releaseFolderMain, batch.ID.ToString());

                Directory.CreateDirectory(tempBatchFolderPath);
                var jsonLocalPath = Path.Combine(tempBatchFolderPath, jsonFileName);
                await File.WriteAllTextAsync(jsonLocalPath, jsonString.Replace(@"\u0022", "\""), stoppingToken);

                // Upload JSON to all DMS targets
                foreach (var (groupConfig, groupConnector) in successfullyProcessedDmsGroups)
                {
                    try
                    {
                        var safeBatchName = string.IsNullOrEmpty(batch.Name) ? "Unsorted" : batch.Name;
                        var jsonDocumentNameInDms = $"{safeBatchName}\\{batch.ID}\\{jsonFileName}";
                        _logger.LogInformation("Uploading consolidated JSON to DMS: {Name}", jsonDocumentNameInDms);

                        var jsonSuccess = await groupConnector!.UploadDocumentAsync(
                            groupConfig, jsonLocalPath, jsonDocumentNameInDms, new Dictionary<string, string>());

                        if (jsonSuccess)
                            await LogToFileAsync(batch.ID.ToString(), "DMS_JSON_UPLOAD_SUCCESS",
                                $"Uploaded JSON to {groupConfig.DMSCabinetName}", "SUCCESS", batchLogService: batchLogService);
                    }
                    catch (Exception uploadEx)
                    {
                        _logger.LogError(uploadEx, "Failed to upload JSON to DMS for Batch {BatchId} — non-fatal", batchId);
                    }
                }

                // Local fallback copy — only when there is NO DMS connector.
                // When DMS is active the JSON must NOT be written to any local folder;
                // it is uploaded to the cloud only.
                if (localFallbackDocuments.Any() && !isUsingRealTemp)
                {
                    try
                    {
                        var batchFolderPath = Path.Combine(batchTypeFolder, batch.BatchName);
                        Directory.CreateDirectory(batchFolderPath);
                        await CopyDocumentsToBatchFolders(batch.ID, batchFolderPath, localFallbackDocuments, allBatchPages, verifyRepository);

                        // Copy the JSON alongside the images — local-only batches only.
                        var finalLocalPath = Path.Combine(batchFolderPath, jsonFileName);
                        File.Copy(jsonLocalPath, finalLocalPath, true);
                    }
                    catch (Exception localEx)
                    {
                        _logger.LogError(localEx, "Failed local fallback copy for Batch {BatchId} — non-fatal", batchId);
                    }
                }

                if (isUsingRealTemp && Directory.Exists(tempBatchFolderPath))
                {
                    try { Directory.Delete(tempBatchFolderPath, true); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up temp batch folder {Path}", tempBatchFolderPath); }
                }

                await batchRepository.CompleteBatchAsync(batch.ID, "system");

                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batch.ID,
                    Status = "Completed",
                    WorkerId = _workerId,
                    Step = "Release"
                }, CancellationToken.None);

                _logger.LogInformation("Batch {BatchId} released successfully", batch.ID);
                await LogToFileAsync(batch.ID.ToString(), "RELEASE_SUCCESS", $"Batch {batch.ID} released successfully", "SUCCESS", batchLogService: batchLogService);
                await batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batch.ID, TaskId = 8, Status = "Ended" });
            }
            catch (Exception)
            {
                // Rethrow is necessary here as this is a sub-operation that must trigger 
                // the parent's error handling logic.
                throw;
            }
        }

        // ─── DMS Processing ──────────────────────────────────────────────────────────

        private async Task<IDmsConnector?> ProcessDocumentsWithDmsConnector(
            Batch batch,
            DmsConfigDto dmsConfig,
            IEnumerable<DocumentModel> documents,
            IEnumerable<PageModel> allPages,
            IVerifyRepository verifyRepository,
            IConfigurationService configurationService,
            IBatchLogService batchLogService,
            CancellationToken stoppingToken)
        {
            try
            {
                var allProviders = await configurationService.GetDmsProvidersAsync();
                var providerName = GetProviderNameById(dmsConfig.ProviderId, allProviders);
                var docList = documents.ToList();

                _logger.LogInformation("Processing {Count} docs for DocType {DocTypeId} via connector '{Provider}' for Batch {BatchId}",
                    docList.Count, docList.FirstOrDefault()?.DocTypeId, providerName, batch.ID);

                await LogToFileAsync(batch.ID.ToString(), "DMS_CONNECTOR_START",
                    $"Using connector {providerName} for batch {batch.ID}", "INFO", batchLogService: batchLogService);

                var connector = _dmsConnectorManager.GetConnector(providerName);

                if (!await connector.TestConnectionAsync(dmsConfig))
                {
                    _logger.LogError("Failed to connect to '{Provider}' for Batch {BatchId}", providerName, batch.ID);
                    await LogToFileAsync(batch.ID.ToString(), "DMS_CONNECTION_FAILED",
                        $"Failed to connect to {providerName}", "ERROR", batchLogService: batchLogService);
                    throw new InvalidOperationException($"Failed to connect to {providerName}");
                }

                await LogToFileAsync(batch.ID.ToString(), "DMS_CONNECTION_SUCCESS",
                    $"Connected to {providerName}", "SUCCESS", batchLogService: batchLogService);

                using var scope = _serviceProvider.CreateScope();
                var imageToPdfService = scope.ServiceProvider.GetRequiredService<IImageToPdfService>();
                var imageToTiffService = scope.ServiceProvider.GetRequiredService<IImageToTiffService>();

                var documentsByType = docList.GroupBy(d => d.DocTypeId).ToList();

                foreach (var docGroup in documentsByType)
                {
                    int pageCounter = 1;
                    var docTypeId = docGroup.Key;

                    var typeConfigs = await configurationService.GetDmsConfigsForDocTypeAsync(docTypeId);
                    var typeConfig = typeConfigs.FirstOrDefault(c => c.IsActive && c.ProviderId == dmsConfig.ProviderId) ?? dmsConfig;
                    var formatCode = typeConfig.OutputFormatCode?.Trim().ToUpperInvariant() ?? "ORIGINAL";

                    var docTypeName = docTypeId > 0
                        ? await verifyRepository.GetDocumentTypeNameAsync(docTypeId)
                        : "DOC";

                    var typePrefix = (docTypeName ?? "DOC").Length >= 3
                        ? (docTypeName ?? "DOC").Substring(0, 3).ToUpperInvariant()
                        : (docTypeName ?? "DOC").ToUpperInvariant();

                    _logger.LogInformation("Document type '{DocType}' — format '{Format}', prefix '{Prefix}'", docTypeName, formatCode, typePrefix);

                    foreach (var document in docGroup)
                    {
                        // Per-document isolation
                        try
                        {
                            var documentPages = allPages
                                .Where(p => p.DocId == document.DocId ||
                                            (!string.IsNullOrEmpty(document.InternalName) && p.FileName == document.InternalName))
                                .OrderBy(p => p.DocPage)
                                .ToList();

                            if (!documentPages.Any()) continue;

                            bool wasUploaded = false;

                            if (formatCode == "PDF")
                            {
                                var tempPdfPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
                                try
                                {
                                    var pagePaths = new List<string>();
                                    foreach (var p in documentPages)
                                    {
                                        var (path, _) = await verifyRepository.GetPageFilePathAsync(p.PageId);
                                        if (File.Exists(path)) pagePaths.Add(path);
                                    }

                                    if (pagePaths.Any() && await imageToPdfService.ConvertImagesToPdfAsync(pagePaths, tempPdfPath))
                                    {
                                        var targetFileName = $"{typePrefix}-{pageCounter:D4}.pdf";
                                        var documentNameInDms = $"{(string.IsNullOrEmpty(batch.Name) ? "Unsorted" : batch.Name)}\\{batch.ID}\\{targetFileName}";
                                        var props = await verifyRepository.GetIndexValuesAsync(batch.ID, document.DocId);

                                        if (await connector.UploadDocumentAsync(dmsConfig, tempPdfPath, documentNameInDms, props))
                                        {
                                            _logger.LogInformation("Uploaded PDF as '{FileName}' for Batch {BatchId}", targetFileName, batch.ID);
                                            await LogToFileAsync(batch.ID.ToString(), "DMS_DOCUMENT_UPLOAD_SUCCESS",
                                                $"Uploaded {docTypeName} PDF {targetFileName}", "SUCCESS", batchLogService: batchLogService);
                                            wasUploaded = true;
                                            pageCounter++;
                                        }
                                    }
                                }
                                catch (Exception pdfEx)
                                {
                                    _logger.LogError(pdfEx, "PDF conversion/upload error for Doc '{DocName}' in Batch {BatchId}", document.DocName, batch.ID);
                                    throw;
                                }
                                finally
                                {
                                    TryDeleteTempFile(tempPdfPath);
                                }
                            }
                            else if (formatCode == "TIFF" || formatCode == "TIF")
                            {
                                var tempTiffPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tiff");
                                try
                                {
                                    var pagePaths = new List<string>();
                                    foreach (var p in documentPages)
                                    {
                                        var (path, _) = await verifyRepository.GetPageFilePathAsync(p.PageId);
                                        if (File.Exists(path)) pagePaths.Add(path);
                                    }

                                    if (pagePaths.Any() && await imageToTiffService.ConvertImagesToTiffAsync(pagePaths, tempTiffPath))
                                    {
                                        var targetFileName = $"{typePrefix}-{pageCounter:D4}.tiff";
                                        var documentNameInDms = $"{(string.IsNullOrEmpty(batch.Name) ? "Unsorted" : batch.Name)}\\{batch.ID}\\{targetFileName}";
                                        var props = await verifyRepository.GetIndexValuesAsync(batch.ID, document.DocId);

                                        if (await connector.UploadDocumentAsync(dmsConfig, tempTiffPath, documentNameInDms, props))
                                        {
                                            _logger.LogInformation("Uploaded TIFF as '{FileName}' for Batch {BatchId}", targetFileName, batch.ID);
                                            await LogToFileAsync(batch.ID.ToString(), "DMS_DOCUMENT_UPLOAD_SUCCESS",
                                                $"Uploaded {docTypeName} TIFF {targetFileName}", "SUCCESS", batchLogService: batchLogService);
                                            wasUploaded = true;
                                            pageCounter++;
                                        }
                                    }
                                }
                                catch (Exception tiffEx)
                                {
                                    _logger.LogError(tiffEx, "TIFF conversion/upload error for Doc '{DocName}' in Batch {BatchId}", document.DocName, batch.ID);
                                    throw;
                                }
                                finally
                                {
                                    TryDeleteTempFile(tempTiffPath);
                                }
                            }

                            if (wasUploaded) continue;

                            // Individual page upload fallback
                            foreach (var page in documentPages)
                            {
                                try
                                {
                                    var (filePath, fileNameFromRepo) = await verifyRepository.GetPageFilePathAsync(page.PageId);
                                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) continue;

                                    var targetExtension = Path.GetExtension(fileNameFromRepo).ToLowerInvariant();
                                    var uploadFilePath = filePath;
                                    bool isTemp = false;

                                    if (formatCode == "TIFF" || formatCode == "TIF")
                                    {
                                        var tempTiff = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tiff");
                                        if (await imageToTiffService.ConvertImageToTiffAsync(filePath, tempTiff))
                                        {
                                            uploadFilePath = tempTiff;
                                            targetExtension = ".tiff";
                                            isTemp = true;
                                        }
                                    }
                                    else if (formatCode == "PDF")
                                    {
                                        var tempPdf = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
                                        if (await imageToPdfService.ConvertImagesToPdfAsync(new List<string> { filePath }, tempPdf))
                                        {
                                            uploadFilePath = tempPdf;
                                            targetExtension = ".pdf";
                                            isTemp = true;
                                        }
                                    }

                                    var targetFileName = $"{typePrefix}-{pageCounter:D4}{targetExtension}";
                                    var documentNameInDms = $"{(string.IsNullOrEmpty(batch.Name) ? "Unsorted" : batch.Name)}\\{batch.ID}\\{targetFileName}";
                                    var props = await verifyRepository.GetIndexValuesAsync(batch.ID, document.DocId);

                                    if (await connector.UploadDocumentAsync(dmsConfig, uploadFilePath, documentNameInDms, props))
                                    {
                                        _logger.LogInformation("Uploaded page as '{FileName}' for Batch {BatchId}", targetFileName, batch.ID);
                                        await LogToFileAsync(batch.ID.ToString(), "DMS_DOCUMENT_UPLOAD_SUCCESS",
                                            $"Uploaded {docTypeName} page {targetFileName}", "SUCCESS", batchLogService: batchLogService);
                                        pageCounter++;
                                    }

                                    if (isTemp) TryDeleteTempFile(uploadFilePath);
                                }
                                catch (Exception pageEx)
                                {
                                    _logger.LogError(pageEx, "Error uploading page for Doc '{DocName}' in Batch {BatchId}", document.DocName, batch.ID);
                                    throw;
                                }
                            }
                        }
                        catch (Exception docEx)
                        {
                            _logger.LogError(docEx, "Error processing Doc {DocId} in DMS upload for Batch {BatchId}.", document.DocId, batch.ID);
                            throw;
                        }
                    }
                }

                _logger.LogInformation("DMS processing complete for Batch {BatchId}", batch.ID);
                return connector;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in ProcessDocumentsWithDmsConnector for Batch {BatchId}", batch.ID);
                await LogToFileAsync(batch.ID.ToString(), "DMS_PROCESSING_ERROR",
                    "Critical DMS processing error", "ERROR", ex.StackTrace, ex.Message, batchLogService);
                throw;
            }
        }

        // ─── JSON DTO Construction ───────────────────────────────────────────────────

        private async Task<BatchInfoJsonDto> CreateBatchDetailJsonDto(
            int batchId,
            string batchTypeName,
            IEnumerable<DocumentModel> documents,
            IEnumerable<PageModel> allPages,
            IVerifyRepository verifyRepository,
            IConfigurationService configurationService,
            IBatchRepository batchRepository,      // <-- passed in; no scope created inside
            string batchFolderPath)
        {
            var batchInfo = await verifyRepository.GetBatchInfoAsync(batchId);
            if (batchInfo == null) throw new InvalidOperationException($"Batch info not found for batch ID {batchId}");

            var docList = documents.ToList();
            var docIds = docList.Select(d => d.DocId).ToList();
            var internalNames = docList.Select(d => d.InternalName).Where(n => !string.IsNullOrEmpty(n)).ToList();

            var pages = allPages.Where(p => docIds.Contains(p.DocId) || internalNames.Contains(p.FileName)).ToList();
            var batchProperties = await verifyRepository.GetBatchIndexValuesAsync(batchId);

            // Determine OCR provider once — reuse in the page loop (no per-page scope)
            var ocrProviderName = (await batchRepository.GetBatchOcrTypeAsync(batchId))?.ToLower() ?? "tesseract";

            var rawDocumentsByType = docList.GroupBy(d => d.DocTypeId).ToList();
            var filteredDocuments = new List<IGrouping<int, DocumentModel>>();

            foreach (var group in rawDocumentsByType)
            {
                var typeName = await verifyRepository.GetDocumentTypeNameAsync(group.Key);
                if (!string.IsNullOrEmpty(typeName) &&
                    !typeName.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase) &&
                    !typeName.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                    group.Key != 0)
                {
                    filteredDocuments.Add(group);
                }
            }

            var batchDetailJson = new BatchInfoJsonDto
            {
                BatchType = batchTypeName,
                BatchId = batchId.ToString(),
                BatchName = batchInfo.BatchName,
                CreatedDate = batchInfo.CreatedOn,
                CreatedBy = batchInfo.CreatedBy,
                TotalDocuments = filteredDocuments.Count,
                TotalPages = pages.Count,
                Source = "Inbound",
                Properties = batchProperties,
                Documents = new List<DocumentJsonDto>()
            };

            foreach (var docGroup in filteredDocuments)
            {
                var docTypeId = docGroup.Key;
                var docTypeName = await verifyRepository.GetDocumentTypeNameAsync(docTypeId);

                var documentTypeJson = new DocumentJsonDto
                {
                    DocumentType = docTypeName ?? "Uncategorized",
                    Pages = new List<PageJsonDto>()
                };

                var sortedDocsInGroup = docGroup.OrderBy(d => d.DocId).ToList();

                for (int i = 0; i < sortedDocsInGroup.Count; i++)
                {
                    var doc = sortedDocsInGroup[i];
                    var docIndexInType = i + 1;

                    var docPages = pages
                        .Where(p => p.DocId == doc.DocId ||
                                    (!string.IsNullOrEmpty(doc.InternalName) && p.FileName == doc.InternalName))
                        .OrderBy(p => p.DocPage)
                        .ToList();

                    var typeConfigs = await configurationService.GetDmsConfigsForDocTypeAsync(docTypeId);
                    var typeConfig = typeConfigs.FirstOrDefault(c => c.IsActive);
                    var formatCode = typeConfig?.OutputFormatCode?.Trim().ToUpperInvariant() ?? "ORIGINAL";

                    var typePrefix = (docTypeName ?? "DOC").Length >= 3
                        ? (docTypeName ?? "DOC").Substring(0, 3).ToUpperInvariant()
                        : (docTypeName ?? "DOC").ToUpperInvariant();

                    foreach (var page in docPages)
                    {
                        try
                        {
                            var (filePath, fileNameFromRepo) = await verifyRepository.GetPageFilePathAsync(page.PageId);

                            string targetExtension = Path.GetExtension(fileNameFromRepo).ToLowerInvariant();
                            if (formatCode == "PDF") targetExtension = ".pdf";
                            else if (formatCode == "TIFF" || formatCode == "TIF") targetExtension = ".tiff";

                            var targetFileName = $"{typePrefix}-{docIndexInType:D4}{targetExtension}";

                            // Use the ocrProviderName resolved once above — NO scope creation per page
                            Dictionary<string, string> pageProperties;
                            if (ocrProviderName == "tesseract")
                            {
                                pageProperties = await verifyRepository.GetIndexValuesAsync(batchId, page.DocId);
                            }
                            else
                            {
                                pageProperties = await GetPropertiesFromAnalysisResult(verifyRepository, page.DocId, ocrProviderName);
                            }

                            var pageJson = new PageJsonDto
                            {
                                PageId = $"{typePrefix}-{docIndexInType:D4}",
                                PageNumber = page.DocPage,
                                batchfileName = page.FileName,
                                originalFileName = page.OriginalFilename,
                                fileSize = GetFileSize(filePath),
                                mimeType = GetMimeType(targetFileName),
                                Properties = pageProperties,
                                Storage = new StorageInfo
                                {
                                    ImagePath = string.IsNullOrEmpty(batchFolderPath)
                                        ? targetFileName
                                        : Path.GetFullPath(Path.Combine(batchFolderPath, docTypeName ?? "Uncategorized", targetFileName))
                                }
                            };

                            documentTypeJson.Pages.Add(pageJson);
                        }
                        catch (Exception pageEx)
                        {
                            _logger.LogError(pageEx, "Error building JSON DTO for Page {PageId} in Batch {BatchId}. Skipping page.", page.PageId, batchId);
                        }
                    }
                }

                documentTypeJson.PageCount = documentTypeJson.Pages.Count;
                batchDetailJson.Documents.Add(documentTypeJson);
            }

            return batchDetailJson;
        }

        // ─── Local Folder Fallback ───────────────────────────────────────────────────

        private async Task CopyDocumentsToBatchFolders(
            int batchId,
            string batchFolderPath,
            IEnumerable<DocumentModel> documents,
            IEnumerable<PageModel> allPages,
            IVerifyRepository verifyRepository)
        {
            var docList = documents.ToList();

            foreach (var doc in docList)
            {
                try
                {
                    var docTypeName = await verifyRepository.GetDocumentTypeNameAsync(doc.DocTypeId);
                    var docTypeFolder = Path.Combine(batchFolderPath, docTypeName ?? "Uncategorized");
                    Directory.CreateDirectory(docTypeFolder);

                    var docPages = allPages.Where(p => p.DocId == doc.DocId).OrderBy(p => p.DocPage).ToList();

                    foreach (var page in docPages)
                    {
                        try
                        {
                            var (filePath, fileNameFromRepo) = await verifyRepository.GetPageFilePathAsync(page.PageId);
                            var fileName = doc.DocName + Path.GetExtension(fileNameFromRepo);

                            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                            {
                                var targetPath = Path.Combine(docTypeFolder, fileName);
                                File.Copy(filePath, targetPath, true);
                            }
                        }
                        catch (Exception pageEx)
                        {
                            _logger.LogError(pageEx, "Error copying page for Doc {DocId}, Page {PageId} in Batch {BatchId}", doc.DocId, page.PageId, batchId);
                        }
                    }
                }
                catch (Exception docEx)
                {
                    _logger.LogError(docEx, "Error copying Doc {DocId} to batch folder for Batch {BatchId}", doc.DocId, batchId);
                }
            }
        }

        // ─── Analysis Result Parsing ─────────────────────────────────────────────────

        private async Task<Dictionary<string, string>> GetPropertiesFromAnalysisResult(
            IVerifyRepository verifyRepository,
            int docId,
            string ocrProviderName)
        {
            var properties = new Dictionary<string, string>();

            try
            {
                var analysisResult = await verifyRepository.GetAzureDocIntelResultsByDocumentIdAsync(docId);

                if (!string.IsNullOrEmpty(analysisResult))
                {
                    var docIntelAnalysis = JsonSerializer.Deserialize<AzureDocIntelAnalysis>(
                        analysisResult,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (docIntelAnalysis != null)
                    {
                        foreach (var kvp in docIntelAnalysis.KeyValuePairs)
                        {
                            if (kvp.Key != null && !string.IsNullOrEmpty(kvp.Key.Text))
                            {
                                var key = kvp.Key.Text
                                    .Replace(" ", "_").Replace("-", "_").Replace(".", "_")
                                    .ToLower();
                                properties[key] = kvp.Value?.Text ?? "";
                            }
                        }

                        for (int i = 0; i < docIntelAnalysis.Tables.Count; i++)
                        {
                            var table = docIntelAnalysis.Tables[i];
                            if (table?.Cells.Count > 0)
                            {
                                var headerCells = new Dictionary<int, string>();
                                var firstRowCells = table.Cells.Where(c => c.Row == 0).ToList();

                                foreach (var cell in firstRowCells)
                                {
                                    if (!string.IsNullOrEmpty(cell.Content))
                                    {
                                        var headerName = cell.Content
                                            .Replace(" ", "_").Replace("-", "_").Replace(".", "_")
                                            .Replace(",", "").Replace(":", "").ToLower();
                                        headerCells[cell.Col] = headerName;
                                    }
                                }

                                var dataRows = table.Cells.Where(c => c.Row > 0).GroupBy(c => c.Row).ToList();
                                var tableObjects = new List<Dictionary<string, string>>();

                                foreach (var row in dataRows)
                                {
                                    var rowObject = new Dictionary<string, string>();
                                    foreach (var cell in row)
                                    {
                                        if (headerCells.ContainsKey(cell.Col) && !string.IsNullOrEmpty(cell.Content))
                                            rowObject[headerCells[cell.Col]] = cell.Content;
                                    }
                                    if (rowObject.Count > 0) tableObjects.Add(rowObject);
                                }

                                if (tableObjects.Count > 0)
                                {
                                    var options = new JsonSerializerOptions
                                    {
                                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                                        WriteIndented = false
                                    };
                                    properties[$"table_{i + 1}"] = JsonSerializer.Serialize(tableObjects, options);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analysis result properties for Doc {DocId}", docId);
                // Return partial/empty properties — non-fatal
            }

            return properties;
        }

        // ─── Utilities ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves provider name from a pre-fetched list — eliminates per-call DI scope creation.
        /// </summary>
        private static string GetProviderNameById(int providerId, IEnumerable<dynamic> providers)
        {
            try
            {
                foreach (var p in providers)
                {
                    // Handle both strongly-typed and anonymous types
                    var id = (int)(p.Id ?? p.id ?? 0);
                    if (id == providerId)
                    {
                        return (string)(p.Name ?? p.name ?? "LocalFolder");
                    }
                }
            }
            catch
            {
                // Fall through to default
            }
            return "LocalFolder";
        }

        private static void TryDeleteTempFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* Best-effort cleanup */ }
        }

        private int GetFileSize(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 0;
            try { return (int)new FileInfo(filePath).Length; }
            catch { return 0; }
        }

        private string GetMimeType(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "application/octet-stream";
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".pdf" => "application/pdf",
                ".tif" or ".tiff" => "image/tiff",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }

        // ─── Logging ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Preferred overload — uses the <paramref name="batchLogService"/> from the caller's scope.
        /// No new DI scope is created.
        /// </summary>
        private static async Task LogToFileAsync(
            string batchId,
            string task,
            string message,
            string status = "INFO",
            string? details = null,
            string? errorMessage = null,
            IBatchLogService? batchLogService = null)
        {
            if (batchLogService != null)
                await batchLogService.LogToFile(batchId, task, message, status, details, errorMessage);
        }

        /// <summary>
        /// Fallback overload used in <see cref="ExecuteAsync"/> error handler where no scope is available.
        /// Creates a minimal scope — use sparingly.
        /// </summary>
        private async Task LogToFileAsync(
            string batchId,
            string task,
            string message,
            string status,
            string? details,
            string? errorMessage)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();
                await batchLogService.LogToFile(batchId, task, message, status, details, errorMessage);
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "Failed to write to batch log file for batch {BatchId}", batchId);
            }
        }
    }
}
