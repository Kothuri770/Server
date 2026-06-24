using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;
using TrueCapture.Services;

namespace Server.Services
{
    public class LocalFolderPollingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval;
        private readonly ILogger<LocalFolderPollingService> _logger;
        private readonly bool _isEnabled;
        private readonly ILocalFolderPollingManager _pollingManager;

        public LocalFolderPollingService(IServiceProvider serviceProvider, ILogger<LocalFolderPollingService> logger, ILocalFolderPollingManager pollingManager, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _pollingManager = pollingManager;

            var intervalSeconds = configuration.GetValue<int>("LocalFolderPolling:IntervalSeconds", 10);
            _interval = TimeSpan.FromSeconds(intervalSeconds);

            _isEnabled = configuration.GetValue<bool>("LocalFolderPolling:Enabled", true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled)
            {
                _logger.LogInformation("Local Folder Polling is disabled globally by configuration.");
                return;
            }

            // Wait briefly to allow DB to be initialized if starting up
            await Task.Delay(5000, stoppingToken);

            // Read initial state from DB
            using (var scope = _serviceProvider.CreateScope())
            {
                try
                {
                    var repo = scope.ServiceProvider.GetRequiredService<ILocalFolderRepository>();
                    var config = await repo.GetConfigurationAsync();
                    _pollingManager.NotifyConfigurationChanged(config?.IsEnabled ?? false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Local Folder Polling Manager state");
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _pollingManager.WaitForEnabledAsync(stoppingToken);

                    if (stoppingToken.IsCancellationRequested) break;

                    await PollLocalFolderAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in LocalFolderPollingService");
                }

                if (_pollingManager.IsEnabled && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_interval, stoppingToken);
                    }
                    catch (TaskCanceledException) { break; }
                }
            }
        }

        private async Task PollLocalFolderAsync(CancellationToken stoppingToken)
        {
            if (!await _pollingManager.PollingLock.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken))
            {
                _logger.LogWarning("Local Folder polling timed out waiting for lock. Another polling operation might be hung.");
                return;
            }

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<ILocalFolderRepository>();
                    var batchRepo = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
                    var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                    var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();

                    var config = await repo.GetConfigurationAsync();

                    if (config == null || string.IsNullOrWhiteSpace(config.PickImagesPath) || string.IsNullOrWhiteSpace(config.BackupPath))
                    {
                        return;
                    }

                    if (!config.IsEnabled)
                    {
                        _pollingManager.NotifyConfigurationChanged(false);
                        return;
                    }

                    if (!Directory.Exists(config.PickImagesPath))
                    {
                        _logger.LogWarning("Local Folder PickImagesPath does not exist: {Path}", config.PickImagesPath);
                        return;
                    }

                    if (!Directory.Exists(config.BackupPath))
                    {
                        try
                        {
                            Directory.CreateDirectory(config.BackupPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to create BackupPath: {Path}", config.BackupPath);
                            return;
                        }
                    }

                    var allFiles = Directory.GetFiles(config.PickImagesPath);
                    var validFiles = allFiles.Where(f => IsImageOrPdf(f)).ToList();

                    if (validFiles.Any())
                    {
                        _logger.LogInformation("Found {Count} valid files in local folder {Path}. Processing...", validFiles.Count, config.PickImagesPath);
                        await ProcessLocalFolderBatchAsync(validFiles, config, batchRepo, configService, batchLogService, repo);
                    }

                    await repo.UpdateLastCheckedAsync(config.Id, DateTime.Now);
                }
            }
            finally
            {
                _pollingManager.PollingLock.Release();
            }
        }

        private bool IsImageOrPdf(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            var ext = Path.GetExtension(filePath).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".tif" || ext == ".tiff" || ext == ".pdf";
        }

        private async Task ProcessLocalFolderBatchAsync(List<string> files, LocalFolderConfiguration config,
            IBatchRepository batchRepo, IConfigurationService configService, IBatchLogService batchLogService, ILocalFolderRepository repo)
        {
            var username = "LocalFolderBot";

            // Create Batch at Scan step (1)
            var batchId = await batchRepo.InsertBatchAsync(string.Empty, DateTime.Now, config.AppId, "A", 1, username);
            await batchLogService.LogBatchTaskAsync(batchId, "CREATE_BATCH", "Created batch from Local Folder Intake", null, username);

            // Get batch folder destination
            var basePath = await configService.GetConfigurationsValue("Batch Folder") ?? "C:\\TrueCapture\\ICBatches";
            var batchFolder = Path.Combine(basePath, batchId.ToString());
            if (!Directory.Exists(batchFolder)) Directory.CreateDirectory(batchFolder);

            // Per User Requirement: Default all images to Uncategorized (DocTypeId = 0)
            int docTypeId = 0; 
            
            int pageNo = 1;
            var processedFiles = new List<string>();

            foreach (var filePath in files)
            {
                var originalFileName = Path.GetFileName(filePath);
                var fileExtension = Path.GetExtension(originalFileName) ?? ".pdf";
                var format = fileExtension.Replace(".", "").ToUpper();
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";

                var destPath = Path.Combine(batchFolder, uniqueFileName);

                try
                {
                    File.Copy(filePath, destPath);

                    // Required per TrueCapture design: ONE Document record per starting Image file to support independent Drag and Drop

                    var detail = new BatchDetailDto
                    {
                        BatchId = batchId,
                        PageNo = pageNo,
                        FileName = uniqueFileName,
                        Format = format,
                        DocPage = 1, // Originally clustered docpage sequentially; strictly 1 natively when it owns its own document
                        Status = "A",
                        DocTypeId = docTypeId,
                        PageName = originalFileName,
                        DocName = $"IMG{pageNo:D6}",
                        InternalName = uniqueFileName,
                        DocCreatedOn = DateTime.Now
                    };
                    await batchRepo.InsertBatchDetailAsync(detail);
                    pageNo++;
                    processedFiles.Add(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to copy and register file {File} into Batch {BatchId}", filePath, batchId);
                }
            }

            // Move the successfully processed files to Backup Path
            foreach (var processedFile in processedFiles)
            {
                try
                {
                    var backupFileDest = Path.Combine(config.BackupPath, Path.GetFileName(processedFile));
                    // Handle duplicate file names in backup folder
                    if (File.Exists(backupFileDest))
                    {
                        var ext = Path.GetExtension(backupFileDest);
                        var name = Path.GetFileNameWithoutExtension(backupFileDest);
                        backupFileDest = Path.Combine(config.BackupPath, $"{name}_{Guid.NewGuid()}{ext}");
                    }
                    
                    File.Move(processedFile, backupFileDest);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to move file {File} to backup path {BackupPath}", processedFile, config.BackupPath);
                }
            }

            // The user specifically requested: "in scan step just save dont submit"
            await batchLogService.LogBatchTaskAsync(batchId, "SAVE_BATCH", "Batch saved successfully in Scan step without submitting", null, username);

            // CRITICAL Fix for View visibility: The JobMonitorReportQuery requires a BatchLog record
            var appName = (await batchRepo.GetApplicationNamesAsync()).FirstOrDefault(a => a.id == config.AppId.ToString())?.name ?? config.AppId.ToString();
            await batchRepo.InsertBatchLogAsync(batchId, appName, 1, 1, 0, processedFiles.Count, batchId.ToString());

            _logger.LogInformation("Local Folder batch {BatchId} created with {Count} pages, retained in Scan step.", batchId, processedFiles.Count);
        }
    }
}
