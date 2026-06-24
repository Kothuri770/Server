using Amazon.S3;
using Amazon.S3.Model;
using Amazon;
using Server.Models;
using System.Text.Json;

namespace Server.Services.DMS
{
    public class AwsS3Connector : IDmsConnector
    {
        public string GetConnectorType() => "AwsS3";

        public async Task<bool> ConnectAsync(DmsConfigDto config)
        {
            try
            {
                using var s3Client = GetS3Client(config);

                // Try to list objects in the bucket to test access
                var request = new ListObjectsV2Request
                {
                    BucketName = config.DMSCabinetName, // This would be the S3 bucket name
                    MaxKeys = 1
                };

                await s3Client.ListObjectsV2Async(request);
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
                using var s3Client = GetS3Client(config);

                // Ensure the bucket exists
                await EnsureBucketExistsAsync(s3Client, config.DMSCabinetName);

                // Convert backslashes to forward slashes for S3 path compatibility
                var s3Key = documentName.Replace('\\', '/');
                
                var request = new PutObjectRequest
                {
                    BucketName = config.DMSCabinetName,
                    Key = s3Key,
                    FilePath = localFilePath
                };

                // Add metadata if provided
                if (metadata?.Any() == true)
                {
                    foreach (var kvp in metadata)
                    {
                        // AWS S3 metadata keys must be lowercase and follow specific naming rules
                        var sanitizedKey = kvp.Key.ToLower().Replace(" ", "_").Replace("-", "_");
                        request.Metadata.Add(sanitizedKey, kvp.Value);
                    }
                }

                var response = await s3Client.PutObjectAsync(request);
                return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"AWS S3 upload error: {ex.Message}", ex);
            }
        }

        private static AmazonS3Client GetS3Client(DmsConfigDto config)
        {
            var additionalConfig = JsonSerializer.Deserialize<AwsS3Config>(config.AdditionalConfig ?? "{}");
            string accessKeyId = string.Empty;
            string secretAccessKey = string.Empty;
            string region = string.Empty;

            if (additionalConfig != null && !string.IsNullOrEmpty(additionalConfig.AccessKeyId) && !string.IsNullOrEmpty(additionalConfig.SecretAccessKey))
            {
                accessKeyId = additionalConfig.AccessKeyId ?? string.Empty;
                secretAccessKey = additionalConfig.SecretAccessKey ?? string.Empty;
                region = additionalConfig.Region ?? string.Empty;
            }
            else if (additionalConfig != null && !string.IsNullOrEmpty(additionalConfig.ConnectionString))
            {
                var credentials = ParseConnectionString(additionalConfig.ConnectionString);
                accessKeyId = credentials.AccessKeyId;
                secretAccessKey = credentials.SecretAccessKey;
                region = credentials.Region;
            }
            else
            {
                accessKeyId = config.Username ?? string.Empty;
                secretAccessKey = config.Password ?? string.Empty;
                region = additionalConfig?.Region ?? string.Empty;
            }

            var awsConfig = new AmazonS3Config
            {
                RegionEndpoint = string.IsNullOrEmpty(region) ? 
                    RegionEndpoint.USEast1 : RegionEndpoint.GetBySystemName(region)
            };

            return new AmazonS3Client(accessKeyId, secretAccessKey, awsConfig);
        }

        private static (string AccessKeyId, string SecretAccessKey, string Region) ParseConnectionString(string connectionString)
        {
            string accessKeyId = string.Empty;
            string secretAccessKey = string.Empty;
            string region = string.Empty;

            var parts = (connectionString ?? string.Empty).Split(';');
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                var pair = part.Split('=');
                if (pair.Length < 2) continue;

                var key = pair[0].Trim();
                var value = pair[1].Trim();

                switch (key)
                {
                    case "AccessKeyId":
                        accessKeyId = value;
                        break;
                    case "SecretAccessKey":
                        secretAccessKey = value;
                        break;
                    case "Region":
                        region = value;
                        break;
                }
            }

            return (accessKeyId, secretAccessKey, region);
        }

        private static async Task EnsureBucketExistsAsync(IAmazonS3 s3Client, string bucketName)
        {
            try
            {
                await s3Client.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = bucketName });
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                await s3Client.PutBucketAsync(new PutBucketRequest { BucketName = bucketName, UseClientRegion = true });
            }
        }

        public async Task<bool> TestConnectionAsync(DmsConfigDto config)
        {
            return await ConnectAsync(config);
        }
    }

    public class AwsS3Config
    {
        public string? ConnectionString { get; set; }
        public string? Region { get; set; }
        public string? AccessKeyId { get; set; }
        public string? SecretAccessKey { get; set; }
        public string? SessionToken { get; set; }
    }
}