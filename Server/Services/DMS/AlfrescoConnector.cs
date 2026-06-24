using Server.Models;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Text;

namespace Server.Services.DMS
{
    public class AlfrescoConnector : IDmsConnector
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AlfrescoConnector(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public string GetConnectorType() => "Alfresco";

        public async Task<bool> ConnectAsync(DmsConfigDto config)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                
                // Set up authentication
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                // Test connection by getting repository info
                var response = await client.GetAsync($"{config.Url}/alfresco/api/-default-/public/alfresco/versions/1/networks");
                
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UploadDocumentAsync(DmsConfigDto config, string localFilePath, string documentName, Dictionary<string, string> metadata)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                
                // Set up authentication
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                // Read the file to upload as a stream to avoid OOM exceptions on large files
                await using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf"); // Adjust based on file type

                // Prepare form data
                var formData = new MultipartFormDataContent();
                formData.Add(fileContent, "\"file\"", documentName);

                // Add metadata as form fields
                foreach (var kvp in metadata)
                {
                    var content = new StringContent(kvp.Value);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
                    formData.Add(content, $"\"{kvp.Key}\"");
                }

                // Upload to Alfresco
                var response = await client.PostAsync($"{config.Url}/alfresco/api/-default-/public/alfresco/versions/1/nodes/{config.DMSCabinetName}/children", formData);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Alfresco upload error: {ex.Message}", ex);
            }
        }

        public async Task<bool> TestConnectionAsync(DmsConfigDto config)
        {
            return await ConnectAsync(config);
        }
    }

    public class AlfrescoConfig
    {
        public string? SiteId { get; set; }
        public string? FolderPath { get; set; }
        public string? DocumentTypeId { get; set; }
    }
}