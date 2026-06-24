using Renci.SshNet;
using Server.Models;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using TrueCapture.Services;

namespace Server.Services
{
    public class SftpPollingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval;
        private readonly ILogger<SftpPollingService> _logger;
        private readonly bool _isEnabled;
        private readonly ISftpPollingManager _pollingManager;
 
        public SftpPollingService(IServiceProvider serviceProvider, ILogger<SftpPollingService> logger, ISftpPollingManager pollingManager, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _pollingManager = pollingManager;
 
            var intervalSeconds = configuration.GetValue<int>("SftpPolling:IntervalSeconds", 60);
            _interval = TimeSpan.FromSeconds(intervalSeconds);
 
            _isEnabled = configuration.GetValue<bool>("SftpPolling:Enabled", true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled)
            {
                _logger.LogInformation("SFTP Polling is disabled globally by configuration.");
                return;
            }

            await Task.Delay(10000, stoppingToken); // Wait for DB init

            using (var scope = _serviceProvider.CreateScope())
            {
                try
                {
                    var repo = scope.ServiceProvider.GetRequiredService<ISftpRepository>();
                    var config = await repo.GetConfigurationAsync();
                    _pollingManager.NotifyConfigurationChanged(config?.IsEnabled ?? false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize SFTP Polling Manager state");
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _pollingManager.WaitForEnabledAsync(stoppingToken);
                    if (stoppingToken.IsCancellationRequested) break;
 
                    await PollSftpAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in SftpPollingService");
                }

                if (_pollingManager.IsEnabled && !stoppingToken.IsCancellationRequested)
                {
                    try { await Task.Delay(_interval, stoppingToken); }
                    catch (TaskCanceledException) { break; }
                }
            }
        }

        private async Task PollSftpAsync(CancellationToken stoppingToken)
        {
            if (!await _pollingManager.PollingLock.WaitAsync(TimeSpan.FromSeconds(10), stoppingToken)) return;

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<ISftpRepository>();
                    var config = await repo.GetConfigurationAsync();

                    if (config == null || !config.IsEnabled || string.IsNullOrWhiteSpace(config.Host)) return;

                    using (var client = new SftpClient(config.Host, config.Port, config.Username, config.Password))
                    {
                        try
                        {
                            client.Connect();
                            if (!client.IsConnected) return;

                            var remoteFiles = client.ListDirectory(config.RemotePath)
                                .Where(f => !f.IsDirectory && IsImageOrPdf(f.Name))
                                .ToList();

                            if (remoteFiles.Any())
                            {
                                var batchRepo = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
                                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                                var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();
 
                                await ProcessSftpFilesAsync(client, remoteFiles, config, batchRepo, configService, batchLogService);
                            }

                            await repo.UpdateLastCheckedAsync(config.Id, DateTime.Now);
                            client.Disconnect();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to process SFTP intake for {Host}", config.Host);
                        }
                    }
                }
            }
            finally
            {
                _pollingManager.PollingLock.Release();
            }
        }

        private bool IsImageOrPdf(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".tif" || ext == ".tiff" || ext == ".pdf";
        }

        private async Task ProcessSftpFilesAsync(SftpClient client, List<Renci.SshNet.Sftp.ISftpFile> files, SftpConfiguration config,
            IBatchRepository batchRepo, IConfigurationService configService, IBatchLogService batchLogService)
        {
            var username = "SftpBot";
            var batchId = await batchRepo.InsertBatchAsync(string.Empty, DateTime.Now, config.AppId, "A", 1, username);
            
            var basePath = await configService.GetConfigurationsValue("Batch Folder") ?? Constants.DefaultBatchFolder;
            var batchFolder = Path.Combine(basePath, batchId.ToString());
            if (!Directory.Exists(batchFolder)) Directory.CreateDirectory(batchFolder);

            int pageNo = 1;
            foreach (var file in files)
            {
                var uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.Name)}";
                var localDest = Path.Combine(batchFolder, uniqueFileName);

                using (var fs = File.OpenWrite(localDest))
                {
                    client.DownloadFile(file.FullName, fs);
                }


                await batchRepo.InsertBatchDetailAsync(new BatchDetailDto {
                    BatchId = batchId, PageNo = pageNo, FileName = uniqueFileName, Format = Path.GetExtension(file.Name).TrimStart('.').ToUpper(),
                    DocName = $"IMG{pageNo:D6}", InternalName = uniqueFileName, DocCreatedOn = DateTime.Now
                });

                // Move remote file to backup or delete
                if (!string.IsNullOrWhiteSpace(config.BackupPath))
                {
                    try { client.RenameFile(file.FullName, Path.Combine(config.BackupPath, file.Name)); }
                    catch { client.DeleteFile(file.FullName); }
                }
                else
                {
                    client.DeleteFile(file.FullName);
                }
                pageNo++;
            }

            var appName = (await batchRepo.GetApplicationNamesAsync()).FirstOrDefault(a => a.id == config.AppId.ToString())?.name ?? config.AppId.ToString();
            await batchRepo.InsertBatchLogAsync(batchId, appName, 1, 1, 0, files.Count, batchId.ToString());
            await batchLogService.LogBatchTaskAsync(batchId, "SAVE_BATCH", "Batch created from SFTP", null, username);
        }
    }
}
