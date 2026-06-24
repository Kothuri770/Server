using Server.Repositories;
using Server.Services.Configuration;
using Npgsql;
using Dapper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Server.Services.Scanner
{
    public interface IScannerService
    {
        Task<List<ScannerDevice>> GetScannerDevicesAsync();
        Task<List<ImageScanResult>> ScanFromDeviceAsync(long batchId, int docId, string deviceId);
        Task<bool> UpdateStepStatusForSeparationModeAsync(string separationMode);
    }
    public class ScannerService : BaseRepository, IScannerService
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<ScannerService> _logger;
        private readonly IBatchRepository _batchRepo;
        private readonly IFileStorageService _fileService;
        private string _basePath = string.Empty;

        public ScannerService(
            IConfigurationService configService, 
            ILogger<ScannerService> logger, 
            IBatchRepository batchRepo, 
            IFileStorageService fileService,
            IConfiguration configuration, 
            string provider)
            : base(configuration.GetConnectionString("TrueCaptureDb") ?? throw new InvalidOperationException("Connection string not configured"), provider)
        {
            _configService = configService;
            _logger = logger;
            _batchRepo = batchRepo;
            _fileService = fileService;
        }

        public async Task<List<ScannerDevice>> GetScannerDevicesAsync()
        {
            try
            {
                var devices = new List<ScannerDevice>();
                
                // Try WIA first
                var wiaDevices = await GetWiaDevicesAsync();
                devices.AddRange(wiaDevices);
                
                // If no WIA devices found, try TWAIN fallback
                if (!devices.Any())
                {
                    var twainDevices = await GetTwainDevicesAsync();
                    devices.AddRange(twainDevices);
                }
                
                // If still no devices, add a helpful message
                if (!devices.Any())
                {
                    devices.Add(new ScannerDevice 
                    { 
                        DeviceId = "none", 
                        DeviceName = "No scanners found - Use File System", 
                        Status = "Offline" 
                    });
                }
                
                return devices;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scanner devices");
                throw;
            }
        }

        public async Task<List<ImageScanResult>> ScanFromDeviceAsync(long batchId, int docId, string deviceId)
        {
            try
            {
                _logger.LogInformation("Initiating scan from device {DeviceId} for batch {BatchId}", deviceId, batchId);

                // MOCK IMPLEMENTATION
                return await SimulateScanAsync(batchId, docId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning from device {DeviceId}", deviceId);
                throw new Exception($"Scanner error: {ex.Message}");
            }
        }

        private async Task<List<ScannerDevice>> GetWiaDevicesAsync()
        {
            var devices = new List<ScannerDevice>();
            try
            {
                var wiaClassId = new Guid("E1C5D730-7E97-11D2-9B0C-0060083E862D");
                var deviceManager = Activator.CreateInstance(Type.GetTypeFromCLSID(wiaClassId));
                if (deviceManager != null)
                {
                    var deviceInfos = deviceManager.GetType().InvokeMember("DeviceInfos", System.Reflection.BindingFlags.GetProperty, null, deviceManager, new object[] { 1 });
                    if (deviceInfos != null)
                    {
                        var count = (int)deviceInfos.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, deviceInfos, null);
                        for (int i = 1; i <= count; i++)
                        {
                            try
                            {
                                var deviceInfo = deviceInfos.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, deviceInfos, new object[] { i });
                                if (deviceInfo != null)
                                {
                                    var deviceId = deviceInfo.GetType().InvokeMember("DeviceID", System.Reflection.BindingFlags.GetProperty, null, deviceInfo, null)?.ToString();
                                    var name = deviceInfo.GetType().InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, deviceInfo, null)?.ToString();
                                    if (!string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(name))
                                    {
                                        devices.Add(new ScannerDevice { DeviceId = deviceId, DeviceName = name, Status = "Online" });
                                    }
                                }
                            }
                            catch (Exception deviceEx)
                            {
                                _logger.LogWarning(deviceEx, "Error processing WIA device {Index}", i);
                                continue;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WIA device detection failed, falling back to TWAIN");
            }
            return devices;
        }

        private async Task<List<ScannerDevice>> GetTwainDevicesAsync()
        {
            var devices = new List<ScannerDevice>();
            try 
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"))
                {
                    if (key != null)
                    {
                        var twainKeys = new[]
                        {
                            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\twain_32.dll",
                            @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\twain_32.dll"
                        };
                        foreach (var keyPath in twainKeys)
                        {
                            try
                            {
                                using (var twainKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
                                {
                                    if (twainKey != null)
                                    {
                                        var valueNames = twainKey.GetValueNames();
                                        foreach (var valueName in valueNames)
                                        {
                                            if (valueName.Contains("Scanner") || valueName.Contains("TWAIN"))
                                            {
                                                var devicePath = twainKey.GetValue(valueName)?.ToString();
                                                if (!string.IsNullOrEmpty(devicePath))
                                                {
                                                    devices.Add(new ScannerDevice { DeviceId = $"TWAIN_{valueName}", DeviceName = $"TWAIN Scanner ({valueName})", Status = "Online" });
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception registryEx)
                            {
                                _logger.LogWarning(registryEx, "Error accessing registry key: {KeyPath}", keyPath);
                            }
                        }
                    }
                }
                using (var twainKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\TWAIN Working Group\TWAIN Data Sources"))
                {
                    if (twainKey != null)
                    {
                        var subKeys = twainKey.GetSubKeyNames();
                        foreach (var subKeyName in subKeys)
                        {
                            devices.Add(new ScannerDevice { DeviceId = $"TWAIN_DS_{subKeyName}", DeviceName = $"TWAIN Data Source: {subKeyName}", Status = "Online" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving TWAIN devices");
            }
            return devices;
        }

        private async Task<List<ImageScanResult>> SimulateScanAsync(long batchId, int docId)
        {
            await Task.Delay(2000);
            var results = new List<ImageScanResult>();
            // Get the centralized batch path
            var uploadPath = await _fileService.GetBatchPathAsync((int)batchId);
            
            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            for (int i = 0; i < 3; i++)
            {
                var fileName = $"scanned_{DateTime.Now:yyyyMMddHHmmss}_{i:D3}.jpg";
                var filePath = Path.Combine(uploadPath, fileName);
                CreateDummyImage(filePath, 2550, 3300);
                var pageId = await _batchRepo.GetMaxPageNumberAsync((int)batchId) + 1;
                var imageId = $"IMG{pageId:D6}";
                results.Add(new ImageScanResult { ImageId = imageId, FileName = fileName, DocId = docId, ImageUrl = $"/api/batch/{batchId}/file/{Uri.EscapeDataString(fileName)}", ThumbnailUrl = $"/api/batch/{batchId}/file/thumb_{Uri.EscapeDataString(fileName)}", IsPdf = false });
            }

            // Update metadata JSON
            await _fileService.UpdateBatchMetadataAsync((int)batchId);
            
            return results;
        }

        public async Task<bool> UpdateStepStatusForSeparationModeAsync(string separationMode)
        {
            try
            {
                using var conn = CreateConnection();
                
                // Determine status based on separation mode
                var autoStatus = separationMode?.Trim().ToLower() == "auto" ? "A" : "I"; // A = Active, I = Inactive
                var manualStatus = separationMode?.ToLower() == "manual" ? "A" : "I"; // A = Active, I = Inactive
                
                // Update Auto Separation step (ID=3)
                var updateAutoSql = "UPDATE Steps SET Status = @Status WHERE ID = 3";
                await conn.ExecuteAsync(updateAutoSql, new { Status = autoStatus });
                
                // Update Manual Separation step (ID=2) 
                var updateManualSql = "UPDATE Steps SET Status = @Status WHERE ID = 2";
                await conn.ExecuteAsync(updateManualSql, new { Status = manualStatus });
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating separation step statuses");
                return false;
            }
        }

        private string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return sanitized.Trim();
        }

        private void CreateDummyImage(string path, int width, int height)
        {
            using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height))
            {
                image.Save(path, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());

                using (var thumbnail = image.Clone(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions { Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max, Size = new SixLabors.ImageSharp.Size(150, 150) })))
                {
                    var thumbPath = Path.Combine(Path.GetDirectoryName(path), $"thumb_{Path.GetFileName(path)}");
                    thumbnail.Save(thumbPath, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
                }
            }
        }
    }

    public class ScannerDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Status { get; set; } = "Online";
    }

    public class ImageScanResult
    {
        public string ImageId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int DocId { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public bool IsPdf { get; set; }
        public List<string> PageUrls { get; set; } = new();
    }
}
