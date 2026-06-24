
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;
using TrueCapture.Services;

namespace Server.Services
{
    public class EmailPollingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval;
        private readonly ILogger<EmailPollingService> _logger;
        private readonly bool _isEnabled;
        private readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(1);
        private readonly IEmailPollingManager _pollingManager;

        public EmailPollingService(IServiceProvider serviceProvider, ILogger<EmailPollingService> logger, IEmailPollingManager pollingManager, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _pollingManager = pollingManager;


            var intervalSeconds = configuration.GetValue<int>("EmailPolling:IntervalSeconds", 5);
            _interval = TimeSpan.FromSeconds(intervalSeconds);

            _isEnabled = configuration.GetValue<bool>("EmailPolling:Enabled", true);
        }
          
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled) 
            {
                _logger.LogInformation("Email Polling is disabled");
                return;
            }
            // Wait briefly to allow DB to be initialized if starting up
            await Task.Delay(5000, stoppingToken);

            // Read initial state from DB
            using (var scope = _serviceProvider.CreateScope())
            {
                try 
                {
                    var emailRepo = scope.ServiceProvider.GetRequiredService<IEmailRepository>();
                    var config = await emailRepo.GetConfigurationAsync();
                    _pollingManager.NotifyConfigurationChanged(config?.IsEnabled ?? false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Email Polling Manager state");
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _pollingManager.WaitForEnabledAsync(stoppingToken);

                    if (stoppingToken.IsCancellationRequested) break;

                    await PollEmailsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in EmailPollingService");
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

        private async Task PollEmailsAsync(CancellationToken stoppingToken)
        {
            // Acquire the lock to prevent concurrent polling (e.g., from background service and manual fetch)
            // Wait up to 30 seconds if busy, then skip to prevent stacking background jobs
            if (!await _pollingManager.PollingLock.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken))
            {
                _logger.LogWarning("Email polling timed out waiting for lock. Another polling operation might be hung.");
                return;
            }

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var emailRepo = scope.ServiceProvider.GetRequiredService<IEmailRepository>();
                    var batchRepo = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
                    var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                    var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();
                    var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

                    var config = await emailRepo.GetConfigurationAsync();
                    
                    if (config == null || string.IsNullOrEmpty(config.EmailId) || string.IsNullOrEmpty(config.Password))
                    {
                        _logger.LogWarning("Email configuration is missing or invalid.");
                        return;
                    }
                    
                    if (!config.IsEnabled)
                    {
                        _pollingManager.NotifyConfigurationChanged(false);
                        return;
                    }

                    _logger.LogInformation("Starting email polling for {EmailId}...", config.EmailId);

                    using (var client = new ImapClient())
                    {
                        try
                        {
                            string imapHost = config.ImapServer;
                            int imapPort = config.ImapPort;

                            if (string.IsNullOrEmpty(imapHost))
                            {
                                 imapHost = "imap.gmail.com";
                                 imapPort = 993;
                                 if (config.EmailId.Contains("outlook") || config.EmailId.Contains("hotmail") || config.EmailId.Contains("live"))
                                 {
                                     imapHost = "outlook.office365.com";
                                 }
                            }

                            await client.ConnectAsync(imapHost, imapPort, true, stoppingToken);
                            _logger.LogInformation("Connected to IMAP server: {Host}", imapHost);

                            client.AuthenticationMechanisms.Remove("XOAUTH2");
                            await client.AuthenticateAsync(config.EmailId, config.Password, stoppingToken);
                            _logger.LogInformation("Authenticated successfully for {Email}", config.EmailId);

                            var inbox = client.Inbox;
                            await inbox.OpenAsync(FolderAccess.ReadWrite, stoppingToken);

                            _logger.LogInformation("Searching for unseen emails...");
                            var uids = await inbox.SearchAsync(SearchQuery.NotSeen, stoppingToken);
                            _logger.LogInformation("Search complete. Found {Count} unseen emails.", uids.Count);

                            if (uids.Any())
                            {
                                foreach (var uid in uids)
                                {
                                    var message = await inbox.GetMessageAsync(uid, stoppingToken);
                                    _logger.LogInformation("Processing email ID: {Uid}, Subject: {Subject}", uid, message.Subject);
                                    
                                    var allAttachments = message.Attachments.ToList();
                                    _logger.LogInformation("Email {Uid} has {Count} total attachments/parts.", uid, allAttachments.Count);

                                    var attachments = new List<MimePart>();
                                    foreach (var part in allAttachments.OfType<MimePart>())
                                    {
                                        if (IsImageOrPdf(part.FileName))
                                        {
                                            attachments.Add(part);
                                        }
                                        else
                                        {
                                            _logger.LogInformation("Skipping attachment '{FileName}' - not a supported image or PDF format.", part.FileName ?? "Unknown");
                                        }
                                    }

                                    _logger.LogInformation("Found {Count} supported attachments in email {Uid}", attachments.Count, uid);

                                    if (attachments.Any())
                                    {
                                        await ProcessEmailBatchAsync(message, attachments, config, batchRepo, configService, batchLogService, stoppingToken);
                                        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, stoppingToken);
                                        _logger.LogInformation("Email {Uid} processed and marked as seen.", uid);
                                    }
                                    else
                                    {
                                        _logger.LogInformation("No supported attachments in email {Uid}. Marking as seen to skip next time.", uid);
                                        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, stoppingToken);
                                    }
                                }
                            }

                            await client.DisconnectAsync(true, stoppingToken);
                            await emailRepo.UpdateLastCheckedAsync(config.Id, DateTime.Now);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to poll emails for {EmailId}", config.EmailId);
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
            if (string.IsNullOrEmpty(fileName)) return false;
            var ext = Path.GetExtension(fileName).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".tif" || ext == ".tiff" || ext == ".pdf";
        }

        private async Task ProcessEmailBatchAsync(MimeMessage message, List<MimePart> attachments, EmailConfiguration config, 
            IBatchRepository batchRepo, IConfigurationService configService, IBatchLogService batchLogService, CancellationToken stoppingToken)
        {
            var username = "EmailBot";

            // Log configuration for debugging
            _logger.LogInformation("Processing Email Batch. Config AppId: {AppId}, DocumentType: {DocType}", config.AppId, config.DocumentType);

            // Create Batch
            var batchId = await batchRepo.InsertBatchAsync(string.Empty, DateTime.Now, config.AppId, "A", 1, username);
            await batchLogService.LogBatchTaskAsync(batchId, "CREATE_BATCH", $"Created batch from email: {message.Subject}", null, username);

            // Get batch folder
            var basePath = await configService.GetConfigurationsValue("Batch Folder") ?? "C:\\TrueCapture\\ICBatches";
            var batchFolder = Path.Combine(basePath, batchId.ToString());
            if (!Directory.Exists(batchFolder)) Directory.CreateDirectory(batchFolder);

            // Resolve Document Type
            int docTypeId = 0;
            var docTypes = (await batchRepo.GetDocumentTypesAsync(new GetDocumentRequest { AppId = config.AppId }))
                .Where(d => d.Id > 0) // Exclude Uncategorized (id=0)
                .ToList();

            if (!string.IsNullOrEmpty(config.DocumentType))
            {
                var matchedDocType = docTypes.FirstOrDefault(d => 
                    d.Name.Equals(config.DocumentType, StringComparison.OrdinalIgnoreCase));
                if (matchedDocType != null)
                {
                    docTypeId = matchedDocType.Id;
                    _logger.LogInformation("Resolved Document Type '{DocTypeName}' to ID: {DocTypeId}", config.DocumentType, docTypeId);
                }
            }

            // Auto-default: if no doctype resolved and the app has document types, use the first one
            if (docTypeId == 0 && docTypes.Count > 0)
            {
                docTypeId = docTypes[0].Id;
                _logger.LogInformation("Auto-defaulting to the first Document Type '{DocTypeName}' (ID: {DocTypeId}) for AppId {AppId}", 
                    docTypes[0].Name, docTypeId, config.AppId);
            }

            // Create a SINGLE Document record mapping for all attachments in this email
            var firstAttachment = attachments[0];
            var firstExt = Path.GetExtension(firstAttachment.FileName) ?? ".pdf";
            var formatMaster = firstExt.Replace(".", "").ToUpper();
            var internalNameMaster = $"{Guid.NewGuid()}{firstExt}";
            

            int pageNo = 1;
            foreach (var attachment in attachments)
            {
                var originalFileName = attachment.FileName;
                var fileExtension = Path.GetExtension(originalFileName) ?? ".pdf";
                var format = fileExtension.Replace(".", "").ToUpper();
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";

                // Save file
                var filePath = Path.Combine(batchFolder, uniqueFileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await attachment.Content.DecodeToAsync(stream, stoppingToken);
                }

                // Create Page (BatchDetail) linked to the single Document
                var detail = new BatchDetailDto
                {
                    BatchId = batchId,
                    PageNo = pageNo,
                    FileName = uniqueFileName,
                    Format = format,
                    DocPage = pageNo,
                    Status = "A",
                    DocTypeId = docTypeId,
                    PageName = originalFileName,
                    DocName = "EMA0001",
                    InternalName = internalNameMaster,
                    DocCreatedOn = DateTime.Now
                };
                await batchRepo.InsertBatchDetailAsync(detail);
                pageNo++;
            }

            // Finalize Batch
            await batchRepo.MoveToNextStepAsync(batchId, username);
            await batchLogService.LogBatchTaskAsync(batchId, "SUBMIT_BATCH", "Auto-submitted email batch as a single document", null, username);

            _logger.LogInformation("Email batch {BatchId} created with 1 document ({Count} pages)", batchId, attachments.Count);
        }
    }
}
