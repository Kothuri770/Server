using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services.DMS;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileNetController : ControllerBase
    {
        private readonly ILogger<FileNetController> _logger;

        public FileNetController(ILogger<FileNetController> logger)
        {
            _logger = logger;
        }

        [HttpPost("test-connection")]
        public async Task<ActionResult<FileNetTestConnectionResult>> TestConnection([FromBody] FileNetConnectionConfig config)
        {
            try
            {
                var connector = new FileNetConnector();
                
                // Create a temporary DmsConfigDto for testing
                var dmsConfig = new DmsConfigDto
                {
                    Url = config.CeUri,
                    Username = config.Username,
                    Password = config.Password,
                    AdditionalConfig = System.Text.Json.JsonSerializer.Serialize(new 
                    { 
                        ObjectStore = config.ObjectStoreName,
                        DomainName = config.DomainName
                    })
                };

                var isConnected = await connector.TestConnectionAsync(dmsConfig);
                
                var result = new FileNetTestConnectionResult
                {
                    Connected = isConnected,
                    ObjectStoreName = config.ObjectStoreName,
                    ErrorMessage = isConnected ? "" : "Failed to connect to FileNet server"
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing FileNet connection");
                return BadRequest(new FileNetTestConnectionResult
                {
                    Connected = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        [HttpPost("upload-document")]
        public async Task<ActionResult<FileNetUploadResult>> UploadDocument(
            [FromForm] string localFilePath,
            [FromForm] string documentName,
            [FromForm] string metadataJson,
            [FromBody] FileNetConnectionConfig config)
        {
            try
            {
                var connector = new FileNetConnector();
                
                // Parse metadata
                var metadata = string.IsNullOrEmpty(metadataJson) 
                    ? new Dictionary<string, string>() 
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) 
                      ?? new Dictionary<string, string>();

                // Create DmsConfigDto
                var dmsConfig = new DmsConfigDto
                {
                    Url = config.CeUri,
                    Username = config.Username,
                    Password = config.Password,
                    DMSClassName = "Document", // Default document class
                    AdditionalConfig = System.Text.Json.JsonSerializer.Serialize(new 
                    { 
                        ObjectStore = config.ObjectStoreName,
                        DocumentClass = "Document"
                    })
                };

                var success = await connector.UploadDocumentAsync(dmsConfig, localFilePath, documentName, metadata);
                
                var result = new FileNetUploadResult
                {
                    Success = success,
                    DocumentId = success ? "Generated_ID" : "",
                    ErrorMessage = success ? "" : "Failed to upload document to FileNet"
                };

                if (success)
                {
                    result.PropertiesSet = metadata;
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document to FileNet");
                return BadRequest(new FileNetUploadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        [HttpGet("document-classes")]
        public ActionResult<List<string>> GetDocumentClasses([FromQuery] FileNetConnectionConfig config)
        {
            try
            {
                // This would query FileNet for available document classes
                // For now, returning common document classes
                var documentClasses = new List<string>
                {
                    "Document",
                    "APT_Doc",
                    "Invoice",
                    "Contract",
                    "Email",
                    "Report"
                };

                return Ok(documentClasses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving document classes");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("property-names")]
        public ActionResult<List<string>> GetPropertyNames(
            [FromQuery] string documentClass,
            [FromQuery] FileNetConnectionConfig config)
        {
            try
            {
                // This would query FileNet for properties of a specific document class
                // For now, returning common properties
                var properties = new List<string>
                {
                    "DocumentTitle",
                    "InvoiceNumber", 
                    "InvoiceDate",
                    "PO_Number",
                    "TotalAmount",
                    "Address",
                    "ApplicationName",
                    "Description",
                    "Author",
                    "Keywords"
                };

                return Ok(properties);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving property names");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("validate-config")]
        public ActionResult<object> ValidateConfiguration([FromBody] FileNetConnectionConfig config)
        {
            try
            {
                var errors = new List<string>();

                if (string.IsNullOrWhiteSpace(config.CeUri))
                    errors.Add("Content Engine URI is required");

                if (string.IsNullOrWhiteSpace(config.Username))
                    errors.Add("Username is required");

                if (string.IsNullOrWhiteSpace(config.Password))
                    errors.Add("Password is required");

                if (string.IsNullOrWhiteSpace(config.ObjectStoreName))
                    errors.Add("Object Store name is required");

                if (!Uri.IsWellFormedUriString(config.CeUri, UriKind.Absolute))
                    errors.Add("Invalid Content Engine URI format");

                return Ok(new 
                { 
                    IsValid = errors.Count == 0,
                    Errors = errors
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating FileNet configuration");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}