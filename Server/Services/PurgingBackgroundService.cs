using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Server.Models;
using Server.Repositories;
using System.IO;

namespace Server.Services
{
    public class PurgingBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly IPurgeConfigService _configService;
        private readonly ILogger<PurgingBackgroundService> _logger;
        private DateTime _lastRunDate = DateTime.MinValue;

        public PurgingBackgroundService(IServiceProvider services, IPurgeConfigService configService, ILogger<PurgingBackgroundService> logger)
        {
            _services = services;
            _configService = configService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var config = await _configService.GetConfigAsync();
                    if (config.IsEnabled)
                    {
                        var now = DateTime.Now;
                        if (TimeSpan.TryParse(config.StartTime, out var startTime))
                        {
                            var endTime = startTime.Add(TimeSpan.FromHours(config.DurationHours));
                            var currentTimeOfDay = now.TimeOfDay;

                            // Trigger if inside target time window and hasn't launched today yet
                            if (currentTimeOfDay >= startTime && currentTimeOfDay <= endTime)
                            {
                                if (_lastRunDate.Date != now.Date)
                                {
                                    _lastRunDate = now.Date; 
                                    await ExecutePurgeAsync(config, stoppingToken);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking purge configuration loop.");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task ExecutePurgeAsync(PurgeConfigDto config, CancellationToken token)
        {
            using var scope = _services.CreateScope();
            var batchRepo = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var dbConfig = scope.ServiceProvider.GetRequiredService<Server.Services.Configuration.IConfigurationService>();

            string logDir = await dbConfig.GetConfigurationsValue("Temp Folder") ?? @"C:\TrueCapture\ICTemp";
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            
            string logFile = Path.Combine(logDir, $"PurgeLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            
            async Task WriteLog(string msg) 
            { 
                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n", token); 
            }

            try 
            {
                await WriteLog($"--- Scheduled Purge Job Started ---");
                await WriteLog($"Config: DeletionMode={config.DeletionMode}, Range={config.StartDate:yyyy-MM-dd} to {config.EndDate:yyyy-MM-dd}, App={config.ApplicationId}, Step={config.StepId}");

                var batches = (await batchRepo.GetBatchesInDateRangeAsync(config.StartDate, config.EndDate, config.ApplicationId, config.StepId)).ToList();
                await WriteLog($"Found {batches.Count} batches eligible for deletion inside range constraint.");

                int successCount = 0;
                int failCount = 0;

                foreach (var batch in batches)
                {
                    if (token.IsCancellationRequested) 
                    {
                        await WriteLog("System cancellation request perceived. Halting immediately.");
                        break;
                    }

                    try
                    {
                        bool deletedLocal = false;
                        bool deletedDb = false;

                        // Local & Both Mode
                        if (config.DeletionMode == 0 || config.DeletionMode == 2)
                        {
                            var path = await fileStorage.GetBatchPathAsync(batch.ID);
                            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            {
                                Directory.Delete(path, true);
                                deletedLocal = true;
                            }
                            else 
                            {
                                await WriteLog($"[Warning] Folder path unresolved or missed for Batch {batch.ID}.");
                            }
                        }

                        // DB & Both Mode
                        if (config.DeletionMode == 1 || config.DeletionMode == 2)
                        {
                            deletedDb = await batchRepo.DeleteBatchRecordAsync(batch.ID);
                        }

                        if (deletedLocal || deletedDb)
                        {
                            if (deletedLocal && config.DeletionMode == 0)
                            {
                                await batchRepo.UpdateBatchPurgeStatusAsync(batch.ID, true);
                                await WriteLog($"Marked Batch {batch.ID} ({batch.BatchName}) as purged in database.");
                            }
                            await WriteLog($"Purged Batch {batch.ID} ({batch.BatchName}) successfully. [Local: {deletedLocal}, DB: {deletedDb}]");
                            successCount++;
                        }
                    }
                    catch (Exception e)
                    {
                        await WriteLog($"Error deleting Batch {batch.ID}: {e.Message}");
                        failCount++;
                    }
                }

                await WriteLog($"--- Purge Job Completed. Successfully purged: {successCount}, Failed: {failCount} ---");
            } 
            catch (Exception ex)
            {
                await WriteLog($"Top-level Purge Execution Exception: {ex.Message}");
            }
        }
    }
}
