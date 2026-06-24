using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Repositories;
using MailKit.Net.Imap;
using MailKit;
using MailKit.Search;
using MimeKit;
using Server.Services;
using Server.Services.Configuration;
using TrueCapture.Services;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/email-config")]
    [Authorize(Roles = "admin,configeditor")]
    public class EmailController : ControllerBase
    {
        private readonly IEmailRepository _emailRepo;
        private readonly ILogger<EmailController> _logger;
        private readonly IEmailPollingManager _emailPollingManager;
        private readonly IServiceProvider _serviceProvider;

        public EmailController(IEmailRepository emailRepo, ILogger<EmailController> logger, 
            IEmailPollingManager emailPollingManager, IServiceProvider serviceProvider)
        {
            _emailRepo = emailRepo;
            _logger = logger;
            _emailPollingManager = emailPollingManager;
            _serviceProvider = serviceProvider;
        }

        [HttpGet]
        public async Task<ActionResult<EmailConfiguration>> GetConfiguration()
        {
            var config = await _emailRepo.GetConfigurationAsync();
            return Ok(config ?? new EmailConfiguration());
        }

        [HttpPost]
        public async Task<IActionResult> SaveConfiguration([FromBody] EmailConfiguration config)
        {
            try
            {
                _logger.LogInformation("Saving Email Configuration. AppId: {AppId}, DocumentType: {DocType}, Email: {Email}", 
                    config.AppId, config.DocumentType, config.EmailId);

                config.CreatedBy = User.Identity?.Name ?? "system";
                config.CreatedOn = DateTime.Now;
                await _emailRepo.SaveConfigurationAsync(config);

                // Notify the Email Polling Service of the config change
                _emailPollingManager.NotifyConfigurationChanged(config.IsEnabled);

                return Ok(new { message = "Email configuration saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving email configuration");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("test")]
        public async Task<IActionResult> TestConnection([FromBody] EmailConfiguration config)
        {
            try
            {
                using (var client = new ImapClient())
                {
                    // Basic validation
                    if (string.IsNullOrEmpty(config.ImapServer) || config.ImapPort <= 0)
                    {
                        return BadRequest("Invalid IMAP Server or Port.");
                    }

                    await client.ConnectAsync(config.ImapServer, config.ImapPort, true);
                    
                    // IMPORTANT: Remove XOAUTH2 to force PLAIN/LOGIN for App Passwords
                    client.AuthenticationMechanisms.Remove("XOAUTH2");
                    
                    await client.AuthenticateAsync(config.EmailId, config.Password);
                    await client.DisconnectAsync(true);
                    
                    return Ok(new { message = "Connection successful!" });
                }
            }
            catch (Exception ex)
            {
                // Return the specific error message from MailKit
                return BadRequest($"Connection failed: {ex.Message}");
            }
        }

        [HttpPost("fetch-now")]
        public async Task<IActionResult> FetchNow()
        {
            // Acquire the lock to prevent concurrent polling
            // Wait up to 30 seconds if busy
            if (!await _emailPollingManager.PollingLock.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                return Conflict("Another polling operation is already in progress.");
            }

            try
            {
                var config = await _emailRepo.GetConfigurationAsync();
                if (config == null || string.IsNullOrEmpty(config.EmailId) || string.IsNullOrEmpty(config.Password))
                {
                    return BadRequest("Email configuration is missing or incomplete.");
                }

                if (string.IsNullOrEmpty(config.ImapServer) || config.ImapPort <= 0)
                {
                    return BadRequest("Invalid IMAP Server or Port in configuration.");
                }

                // Use scoped services for batch/config operations
                using var scope = _serviceProvider.CreateScope();
                var batchRepo = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
                var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();

                int batchesCreated = 0;
                int totalAttachments = 0;

                using (var client = new ImapClient())
                {
                    await client.ConnectAsync(config.ImapServer, config.ImapPort, true);
                    client.AuthenticationMechanisms.Remove("XOAUTH2");
                    await client.AuthenticateAsync(config.EmailId, config.Password);

                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadWrite);

                    var uids = await inbox.SearchAsync(SearchQuery.NotSeen);

                    _logger.LogInformation("Fetch Now: Found {Count} unseen emails", uids.Count);

                    // Resolve doc type once for all emails
                    int docTypeId = 0;
                    var docTypes = (await batchRepo.GetDocumentTypesAsync(new GetDocumentRequest { AppId = config.AppId }))
                        .Where(d => d.Id > 0).ToList();

                    if (!string.IsNullOrEmpty(config.DocumentType))
                    {
                        var matched = docTypes.FirstOrDefault(d => 
                            d.Name.Equals(config.DocumentType, StringComparison.OrdinalIgnoreCase));
                        if (matched != null) docTypeId = matched.Id;
                    }

                    // Auto-default if no doctype resolved
                    if (docTypeId == 0 && docTypes.Count >= 1)
                    {
                        docTypeId = docTypes[0].Id;
                    }

                    foreach (var uid in uids)
                    {
                        var message = await inbox.GetMessageAsync(uid);
                        var attachments = message.Attachments.OfType<MimePart>()
                            .Where(a => IsImageOrPdf(a.FileName)).ToList();

                        if (attachments.Any())
                        {
                            await ProcessEmailBatchAsync(message, attachments, config, docTypeId,
                                batchRepo, configService, batchLogService);
                            batchesCreated++;
                            totalAttachments += attachments.Count;

                            // Mark as read
                            await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true);
                        }
                    }

                    await client.DisconnectAsync(true);
                    await _emailRepo.UpdateLastCheckedAsync(config.Id, DateTime.Now);
                }

                return Ok(new { 
                    message = $"Successfully processed {batchesCreated} email(s) with {totalAttachments} attachment(s).",
                    batchesCreated,
                    totalAttachments
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Fetch Now");
                return StatusCode(500, $"Fetch failed: {ex.Message}");
            }
            finally
            {
                _emailPollingManager.PollingLock.Release();
            }
        }

        private bool IsImageOrPdf(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            var ext = Path.GetExtension(fileName).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".tif" || ext == ".tiff" || ext == ".pdf";
        }

        private async Task ProcessEmailBatchAsync(MimeMessage message, List<MimePart> attachments, 
            EmailConfiguration config, int docTypeId,
            IBatchRepository batchRepo, IConfigurationService configService, IBatchLogService batchLogService)
        {
            var username = "EmailBot";

            // Create Batch
            var batchId = await batchRepo.InsertBatchAsync(string.Empty, DateTime.Now, config.AppId, "A", 1, username);
            // await batchRepo.UpdateBatchAsync(batchId, config.AppId); // Redundant
            await batchLogService.LogBatchTaskAsync(batchId, "CREATE_BATCH", $"Created batch from email: {message.Subject}", null, username);

            // Get batch folder
            var basePath = await configService.GetConfigurationsValue("Batch Folder") ?? Constants.DefaultBatchFolder;
            var batchFolder = Path.Combine(basePath, batchId.ToString());
            if (!Directory.Exists(batchFolder)) Directory.CreateDirectory(batchFolder);

            // Create a SINGLE Document record mapping for all attachments in this email
            var firstAttachment = attachments[0];
            var firstExt = Path.GetExtension(firstAttachment.FileName) ?? ".pdf";
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
                    await attachment.Content.DecodeToAsync(stream);
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

            _logger.LogInformation("Fetch Now: Batch {BatchId} created with 1 document ({Count} pages)", batchId, attachments.Count);
        }
    }
}
