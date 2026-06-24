using Azure.Storage.Blobs;
using Server.Models;
using System.Text.Json;

namespace Server.Services.DMS
{
    public class AzureBlobConnector : IDmsConnector
    {
        public string GetConnectorType() => "AzureBlob";

        public async Task<bool> ConnectAsync(DmsConfigDto config)
        {
            try
            {
                var additionalConfig = JsonSerializer.Deserialize<AzureBlobConfig>(config.AdditionalConfig ?? "{}");
                
                BlobContainerClient containerClient;
                if (!string.IsNullOrEmpty(additionalConfig?.ConnectionString))
                {
                    // Use connection string
                    var blobServiceClient = new BlobServiceClient(additionalConfig.ConnectionString);
                    containerClient = blobServiceClient.GetBlobContainerClient(additionalConfig.ContainerName);
                }
                else if (!string.IsNullOrEmpty(config.Url))
                {
                    // Use account URL
                    var blobServiceClient = new BlobServiceClient(new Uri(config.Username));
                    containerClient = blobServiceClient.GetBlobContainerClient(config.DMSCabinetName);
                }
                else
                {
                    // Use account name and key separately
                    var connectionString = $"DefaultEndpointsProtocol=https;AccountName={config.Username};AccountKey={config.Password};EndpointSuffix=core.windows.net";
                    var blobServiceClient = new BlobServiceClient(connectionString);
                    containerClient = blobServiceClient.GetBlobContainerClient(config.DMSCabinetName);
                }

                // Try to access the container
                await containerClient.GetPropertiesAsync();
                return true;
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
                var additionalConfig = JsonSerializer.Deserialize<AzureBlobConfig>(config.AdditionalConfig ?? "{}");
                
                BlobContainerClient containerClient;
                if (!string.IsNullOrEmpty(additionalConfig?.ConnectionString))
                {
                    var blobServiceClient = new BlobServiceClient(additionalConfig.ConnectionString);
                    containerClient = blobServiceClient.GetBlobContainerClient(additionalConfig.ContainerName);
                }
                else if (!string.IsNullOrEmpty(config.Url))
                {
                    var blobServiceClient = new BlobServiceClient(new Uri(config.Url));
                    containerClient = blobServiceClient.GetBlobContainerClient(config.DMSCabinetName);
                }
                else
                {
                    var connectionString = $"DefaultEndpointsProtocol=https;AccountName={config.Username};AccountKey={config.Password};EndpointSuffix=core.windows.net";
                    var blobServiceClient = new BlobServiceClient(connectionString);
                    containerClient = blobServiceClient.GetBlobContainerClient(config.DMSCabinetName);
                }

                // Create the container if it doesn't exist
                await containerClient.CreateIfNotExistsAsync();

                // Upload the file
                var blobClient = containerClient.GetBlobClient(documentName);
                
                using var fileStream = File.OpenRead(localFilePath);
                await blobClient.UploadAsync(fileStream, true);

                // Set metadata if provided
                if (metadata?.Any() == true)
                {
                    var blobMetadata = metadata.ToDictionary(kvp => kvp.Key.ToLower(), kvp => kvp.Value);
                    await blobClient.SetMetadataAsync(blobMetadata);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Exception is re-thrown below for the caller to handle
                throw new InvalidOperationException($"Azure Blob upload error: {ex.Message}", ex);
            }
        }

        public async Task<bool> TestConnectionAsync(DmsConfigDto config)
        {
            return await ConnectAsync(config);
        }
    }

    public class AzureBlobConfig
    {
        public string? ConnectionString { get; set; }
        public string? ContainerName { get; set; }
        public string? AccountName { get; set; }
        public string? AccountKey { get; set; }
    }
}