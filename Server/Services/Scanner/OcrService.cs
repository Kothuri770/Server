using Microsoft.AspNetCore.Components.Forms;
using Server.Models;
using Server.Services.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Tesseract;
using TrueCapture.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Server.Services
{
    public interface IOcrService
    {
        Task ProcessPageAsync(int pageId, int batchId);
        Task<OcrResult> ExtractFromZoneAsync(int pageId, ZoneDto zone, int batchId);
        Task<OcrExtractionResult> ExtractTextFromZoneAsync(ZoneExtractionRequest request, Dictionary<string, object>? configData = null);
        Task<string> ProcessWithAzureDocIntelAsync(string inputPath, string outputDirectory, int batchId = 0, Dictionary<string, object>? configData = null);
        Task<string> ProcessWithGoogleDocAiAsync(string inputPath, string outputDirectory, string endpoint, string processorId, string apiKey, Dictionary<string, object>? configData = null);
        Task<string> ProcessWithAmazonTextractAsync(string inputPath, string outputDirectory, string region, string accessKey, string secretKey, Dictionary<string, object>? configData = null);
        Task<string> ExtractWordsFromPageAsync(string filePath, Dictionary<string, object>? configData = null);
        Task<string> ExtractFullPageTextAsync(string filePath, Dictionary<string, object>? configData = null);
        Task<Dictionary<int, string>> ExtractZonesFromAzureAnalysisAsync(string analysisJson, IEnumerable<ZoneDto> zones, int batchId = 0);
        Task<string> ProcessWithOllamaAsync(string extractedText, IEnumerable<IndexFieldDto> indexFields, Dictionary<string, object>? configData = null);
    }

    public partial class OcrService : IOcrService
    {
        [GeneratedRegex(@"[|~^_{}\[\]<>`*=•·]")]
        private static partial Regex NoiseRegex();

        [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
        private static partial Regex ControlCharsRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        [GeneratedRegex("[^0-9-]")]
        private static partial Regex NonIntegerRegex();

        [GeneratedRegex("[^0-9.-]")]
        private static partial Regex NonDecimalRegex();

        [GeneratedRegex(@"\d{1,4}[/\-\.\s]\d{1,2}[/\-\.\s]\d{2,4}|\d{1,2}[/\-\.\s]\d{1,2}[/\-\.\s]\d{2}")]
        private static partial Regex DatePatternRegex();

        private readonly IFileStorageService _fileService;
        private readonly IConfiguration _config;
        private readonly ILogger<OcrService> _logger;
        private readonly IConfigurationService _configurationService;
        private readonly IOcrConnectorService _ocrConnectorService;
        private readonly IBatchLogService _batchLogService;
        private static readonly SemaphoreSlim _ollamaSemaphore = new SemaphoreSlim(1);

        public OcrService(IFileStorageService fileService, IConfiguration config, ILogger<OcrService> logger, IConfigurationService configurationService, IOcrConnectorService ocrConnectorService, IBatchLogService batchLogService)
        {
            _fileService = fileService;
            _config = config;
            _logger = logger;
            _configurationService = configurationService;
            _ocrConnectorService = ocrConnectorService;
            _batchLogService = batchLogService;
        }

        public async Task ProcessPageAsync(int pageId, int batchId)
        {
            // Full page OCR processing
            await Task.CompletedTask;
        }

        public async Task<OcrResult> ExtractFromZoneAsync(int pageId, ZoneDto zone, int batchId)
        {
            // Simulate OCR extraction
            return new OcrResult
            {
                ZoneId = zone.ID,
                OCRValue = $"Extracted_{zone.Name}_{Guid.NewGuid().ToString().Substring(0, 8)}",
                Confidence = 95.5
            };
        }

        public async Task<OcrExtractionResult> ExtractTextFromZoneAsync(ZoneExtractionRequest request, Dictionary<string, object>? configData = null)
        {
            try
            {
                // Decode the base64 image
                byte[] imageBytes = Convert.FromBase64String(request.ImageBase64);
                
                // Check if this is a PDF file
                if (IsPdfFile(imageBytes))
                {
                    // Handle PDF by converting to image first
                    return new OcrExtractionResult 
                    { 
                        Text = await ExtractTextFromPdfZoneAsync(imageBytes, request.JsLeft, request.JsTop, request.JsRight, request.JsBottom, request.DisplayedWidth, request.DisplayedHeight, request.PropertyType),
                        Confidence = 0
                    };
                }
                
                // Use provided configData or fall back to default connector
                var effectiveConfig = configData;
                if (effectiveConfig == null)
                {
                    var defaultConnector = await _ocrConnectorService.GetDefaultOcrConnectorAsync();
                    effectiveConfig = defaultConnector?.ConfigData;
                }
                
                string tesseractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tessract"); // Default to app-local folder
                string tesseractLanguage = "eng"; // Default language
                
                if (effectiveConfig != null)
                {
                    // Extract configuration values from effectiveConfig
                    if (effectiveConfig.ContainsKey("tessdatapath"))
                    {
                        var configuredPath = effectiveConfig["tessdatapath"]?.ToString();
                        if (!string.IsNullOrEmpty(configuredPath) && !configuredPath.StartsWith("./"))
                        {
                            tesseractPath = configuredPath;
                        }
                    }
                    if (effectiveConfig.ContainsKey("language"))
                    {
                        tesseractLanguage = effectiveConfig["language"]?.ToString() ?? "eng";
                    }
                }

                // Check if we should use Azure for this manual extraction
                bool useAzure = false;
                if (effectiveConfig != null && effectiveConfig.ContainsKey("endpoint") && effectiveConfig.ContainsKey("key"))
                {
                    // If endpoint and key are present, it's likely an Azure config
                    useAzure = true;
                }

                if (useAzure)
                {
                    try
                    {
                        // For manual zone extraction with Azure, we crop the image first then send to Azure
                        // or we could use the full page analysis if it was already performed.
                        // Let's use the cropping approach for precision on manual clicks.
                        using var ms = new MemoryStream(imageBytes);
                        using var fullImage = await SixLabors.ImageSharp.Image.LoadAsync(ms);
                        
                        double scaleX = (double)fullImage.Width / request.DisplayedWidth;
                        double scaleY = (double)fullImage.Height / request.DisplayedHeight;

                        int realX = (int)(request.JsLeft * scaleX);
                        int realY = (int)(request.JsTop * scaleY);
                        int realW = (int)((request.JsRight - request.JsLeft) * scaleX);
                        int realH = (int)((request.JsBottom - request.JsTop) * scaleY);

                        // Ensure bounds are valid
                        realX = Math.Max(0, Math.Min(realX, fullImage.Width - 1));
                        realY = Math.Max(0, Math.Min(realY, fullImage.Height - 1));
                        realW = Math.Max(1, Math.Min(realW, fullImage.Width - realX));
                        realH = Math.Max(1, Math.Min(realH, fullImage.Height - realY));

                        fullImage.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(realX, realY, realW, realH)));
                        
                        using var cropMs = new MemoryStream();
                        await fullImage.SaveAsJpegAsync(cropMs);
                        
                        // Create a temporary file for the crop
                        string tempCrop = Path.Combine(Path.GetTempPath(), $"tc_crop_{Guid.NewGuid()}.jpg");
                        await File.WriteAllBytesAsync(tempCrop, cropMs.ToArray());

                        var azureResultJson = await ProcessWithAzureDocIntelAsync(tempCrop, Path.GetTempPath(), 0, effectiveConfig);
                        
                        // Extract text from the result
                        var root = JsonDocument.Parse(azureResultJson).RootElement;
                        string content = "";
                        if (root.TryGetProperty("content", out var contentProp))
                        {
                            content = contentProp.GetString() ?? "";
                        }

                        if (File.Exists(tempCrop)) File.Delete(tempCrop);

                        return new OcrExtractionResult { Text = content, Confidence = 99.0 };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Azure manual extraction failed, falling back to Tesseract");
                    }
                }

                try
                { 
                    using var engine = new TesseractEngine(tesseractPath, tesseractLanguage, EngineMode.TesseractOnly);
                    byte[] compatibleBytes = await DecodeToCompatibleFormatAsync(imageBytes);
                    using var img = Pix.LoadFromMemory(compatibleBytes);

                    double scaleX = 1.0;
                    double scaleY = 1.0;

                    if (request.DisplayedWidth > 0 && request.DisplayedHeight > 0)
                    {
                        scaleX = (double)img.Width / request.DisplayedWidth;
                        scaleY = (double)img.Height / request.DisplayedHeight;
                    }

                    int realX = (int)(request.JsLeft * scaleX);
                    int realY = (int)(request.JsTop * scaleY);
                    
                    // If width/height are 0 (full page request), use the image dimensions
                    int realW = (request.JsRight - request.JsLeft) > 0 ? (int)((request.JsRight - request.JsLeft) * scaleX) : img.Width;
                    int realH = (request.JsBottom - request.JsTop) > 0 ? (int)((request.JsBottom - request.JsTop) * scaleY) : img.Height;

                    // Ensure bounds are valid
                    realX = Math.Max(0, realX);
                    realY = Math.Max(0, realY);
                    realW = Math.Min(realW, img.Width - realX);
                    realH = Math.Min(realH, img.Height - realY);

                    if (realW <= 0 || realH <= 0)
                    {
                        // Fallback to full page if calculations result in invalid zone
                        realX = 0;
                        realY = 0;
                        realW = img.Width;
                        realH = img.Height;
                    }

                    var segMode = (request.JsRight - request.JsLeft) > 0 ? PageSegMode.SingleBlock : PageSegMode.Auto;
                    var zone = new Tesseract.Rect(realX, realY, realW, realH);
                    using var page = engine.Process(img, zone, segMode);
                    string text = page.GetText();
                    float confidence = page.GetMeanConfidence() * 100; // Convert 0-1 to 0-100 percentage
                    return new OcrExtractionResult
                    {
                        Text = CleanExtractedTextByType(text, request.PropertyType),
                        Confidence = Math.Round(confidence, 1)
                    };
                }
                finally
                {
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from zone at coordinates");
                return new OcrExtractionResult { Text = string.Empty, Confidence = 0 };
            }
        }
        
        private bool IsPdfFile(byte[] fileBytes)
        {
            if (fileBytes.Length < 4) return false;
            
            // Check for PDF magic number: %PDF
            return fileBytes[0] == 0x25 && 
                   fileBytes[1] == 0x50 && 
                   fileBytes[2] == 0x44 && 
                   fileBytes[3] == 0x46;
        }
        
        private async Task<string> ExtractTextFromPdfZoneAsync(byte[] pdfBytes, int jsLeft, int jsTop, int jsRight, int jsBottom, int displayedWidth, int displayedHeight, string propertyType)
        {
            // For now, return empty string for PDFs since we don't have a reliable PDF-to-image conversion method
            // using a library like PdfiumViewer, iTextSharp, or similar
            _logger.LogWarning("PDF files are not supported for OCR processing. The image data should be converted to an image format first.");
            return string.Empty;
        }

        private static string CleanExtractedText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // Clean common OCR noise artifacts (tables/borders/speckles interpreted as weird symbols)
            text = NoiseRegex().Replace(text, " ");

            // Remove non-printable control characters
            text = ControlCharsRegex().Replace(text, string.Empty);

            // Remove extra whitespace and normalize line breaks
            text = WhitespaceRegex().Replace(text, " ").Trim();
            
            return text;
        }

        private static string CleanFullPageText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // Clean common OCR noise artifacts
            text = NoiseRegex().Replace(text, " ");

            // Remove non-printable control characters (but keep newlines)
            text = ControlCharsRegex().Replace(text, string.Empty);

            // Replace horizontal whitespace (spaces, tabs) with a single space, but preserve newlines
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanedLines = lines.Select(line => 
            {
                return System.Text.RegularExpressions.Regex.Replace(line, @"[ \t]+", " ").Trim();
            });

            // Filter out multiple consecutive empty lines to keep it compact
            var resultLines = new List<string>();
            bool lastWasEmpty = false;
            foreach (var line in cleanedLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (!lastWasEmpty)
                    {
                        resultLines.Add(string.Empty);
                        lastWasEmpty = true;
                    }
                }
                else
                {
                    resultLines.Add(line);
                    lastWasEmpty = false;
                }
            }

            return string.Join("\n", resultLines).Trim();
        }
        
        private static string CleanExtractedTextByType(string text, string propertyType)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // Clean based on property type
            string cleanedText = propertyType?.ToLower() switch
            {
                "integer" or "int" or "int32" => CleanIntegerText(text),
                "decimal" or "numeric" or "float" or "double" => CleanDecimalText(text),
                "date" or "datetime" => CleanDateText(text),
                "boolean" or "bool" => CleanBooleanText(text),
                _ => CleanGeneralText(text) // Default cleaning for string/text types
            };
            
            return cleanedText;
        }
        
        private static string CleanGeneralText(string text)
        {
            return CleanExtractedText(text);
        }
        
        private static string CleanIntegerText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // Extract only digits and sign
            var cleaned = NonIntegerRegex().Replace(text, "");
            
            // If it starts with multiple signs, keep only the last one
            if (cleaned.Length > 1)
            {
                var chars = cleaned.ToCharArray();
                var result = new System.Text.StringBuilder();
                bool hasSign = false;
                
                for (int i = 0; i < chars.Length; i++)
                {
                    if (chars[i] == '-' && i == 0) // Only allow negative sign at the beginning
                    {
                        result.Append(chars[i]);
                        hasSign = true;
                    }
                    else if (char.IsDigit(chars[i]))
                    {
                        result.Append(chars[i]);
                    }
                }
                
                cleaned = result.ToString();
            }
            
            // Validate that it's a proper integer
            if (int.TryParse(cleaned, out int value))
            {
                return value.ToString();
            }
            
            // If parsing fails, return empty string or a default value
            return string.Empty;
        }
        
        private static string CleanDecimalText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // Extract digits, decimal point, and sign
            var cleaned = NonDecimalRegex().Replace(text, "");
            
            // Ensure only one decimal point
            var parts = cleaned.Split('.');
            if (parts.Length > 2)
            {
                // Keep the first part and join the rest as decimal part
                cleaned = parts[0] + "." + string.Join("", parts.Skip(1));
            }
            
            // Ensure only one negative sign at the beginning
            if (cleaned.Contains('-'))
            {
                var withoutSign = cleaned.Replace("-", "");
                cleaned = "-" + withoutSign;
            }
            
            // Validate that it's a proper decimal
            if (decimal.TryParse(cleaned, out decimal value))
            {
                return value.ToString();
            }
            
            // If parsing fails, return empty string or a default value
            return string.Empty;
        }
        
        private static string CleanDateText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            // Try different possible date patterns
            var datePatterns = new[] {
                "MM/dd/yy",
                "dd/MM/yy",
                "MM/dd/yyyy",
                "dd/MM/yyyy",
                "yyyy/MM/dd",
                "yyyy-MM-dd",
                "MM-dd-yy",
                "dd-MM-yy",
                "MM-dd-yyyy",
                "dd-MM-yyyy"
            };
            
            // First try to find a date pattern in the text (supports /, -, . and spaces)
            var match = DatePatternRegex().Match(text);
            if (match.Success)
            {
                var dateString = match.Value;
                
                // Try to parse with various formats
                foreach (var format in datePatterns)
                {
                    if (DateTime.TryParseExact(dateString, format, null, System.Globalization.DateTimeStyles.None, out DateTime dateValue))
                    {
                        return dateValue.ToString("yyyy-MM-dd");
                    }
                }
                
                // If specific formats fail, try general parsing
                if (DateTime.TryParse(dateString, out DateTime generalDateValue))
                {
                    return generalDateValue.ToString("yyyy-MM-dd");
                }
            }
            
            // If no match or parsing fails, return empty
            return string.Empty;
        }
        
        private static string CleanBooleanText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "false"; // Default to false for boolean
            
            var lowerText = text.ToLowerInvariant().Trim();
            
            // Common true values
            if (lowerText == "true" || lowerText == "1" || lowerText == "yes" || lowerText == "y" || lowerText == "on")
            {
                return "true";
            }
            
            // Common false values
            if (lowerText == "false" || lowerText == "0" || lowerText == "no" || lowerText == "n" || lowerText == "off")
            {
                return "false";
            }
            
            // If it doesn't match known boolean values, default to false
            return "false";
        }
        
        private async Task<string> GetTempFolderPathFromConfig()
        {
            try
            {
                // Get Temp folder path from configuration
                var configValue = await _configurationService.GetConfigurationsValue("Temp Folder");
                return string.IsNullOrEmpty(configValue) ? Path.GetTempPath() : configValue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get Temp folder path from configuration, using system default");
                return Path.GetTempPath();
            }
        }
        
        private async Task<byte[]> DecodeToCompatibleFormatAsync(byte[] inputBytes)
        {
            try
            {
                using var ms = new MemoryStream(inputBytes);
                using var image = await SixLabors.ImageSharp.Image.LoadAsync(ms);
                using var outMs = new MemoryStream();
                await image.SaveAsync(outMs, new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder());
                return outMs.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode image with ImageSharp. Falling back to original bytes.");
                return inputBytes;
            }
        }

        public async Task<string> ProcessWithAzureDocIntelAsync(string inputPath, string outputDirectory, int batchId = 0, Dictionary<string, object>? configData = null)
        {
            try
            {
                if (batchId > 0)
                {
                    await _batchLogService.LogBatchTaskAsync(batchId, "OCR_AZURE_START", $"Starting Azure OCR for {Path.GetFileName(inputPath)}", null, "system");
                }

                // Use provided configData or fall back to finding an active Azure connector
                var effectiveConfig = configData;
                if (effectiveConfig == null)
                {
                    var connectors = await _ocrConnectorService.GetAllOcrConnectorsAsync();
                    var azureConnector = connectors.FirstOrDefault(c => c.Provider?.Name?.ToLower() == "azuredocintel" && c.IsDefault)
                                      ?? connectors.FirstOrDefault(c => c.Provider?.Name?.ToLower() == "azuredocintel" && c.IsActive);
                    effectiveConfig = azureConnector?.ConfigData;
                }
                
                if (effectiveConfig == null)
                {
                    var msg = "No active Azure Document Intelligence connector found.";
                    _logger.LogWarning(msg);
                    if (batchId > 0)
                    {
                        await _batchLogService.LogBatchTaskAsync(batchId, "OCR_AZURE_ERROR", msg, null, "system");
                    }
                    return string.Empty;
                }
                
                // Extract configuration values from effectiveConfig
                var endpoint = effectiveConfig.ContainsKey("endpoint") ? effectiveConfig["endpoint"]?.ToString()?.Trim() : "";
                var apiKey = effectiveConfig.ContainsKey("apikey") ? effectiveConfig["apikey"]?.ToString()?.Trim() : "";
                var modelId = effectiveConfig.ContainsKey("modelid") ? effectiveConfig["modelid"]?.ToString()?.Trim() : "prebuilt-layout";
                
                // Sanitize endpoint (remove trailing slash if present)
                if (!string.IsNullOrEmpty(endpoint) && endpoint.EndsWith("/"))
                {
                    endpoint = endpoint.TrimEnd('/');
                }

                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    var availableKeys = effectiveConfig != null ? string.Join(", ", effectiveConfig.Keys) : "null";
                    var msg = $"Azure configuration incomplete. Endpoint: {!string.IsNullOrEmpty(endpoint)}, Key: {!string.IsNullOrEmpty(apiKey)}. Available keys: [{availableKeys}]";
                    _logger.LogWarning(msg);
                    if (batchId > 0)
                    {
                        await _batchLogService.LogBatchTaskAsync(batchId, "OCR_AZURE_ERROR", msg, null, "system");
                    }
                    return string.Empty;
                }

                _logger.LogInformation("Processing Azure Document Intelligence. Endpoint: {Endpoint}, Model: {ModelId}", endpoint, modelId);
                
                var analyzer = new AzureDocIntelAnalyzer(endpoint, apiKey, modelId);
                _logger.LogInformation("Analyzing document: {InputPath}", inputPath);
                
                try
                {
                    var success = await analyzer.AnalyzeAndSaveAsync(inputPath, outputDirectory);
                    
                    if (success)
                    {
                        var baseName = Path.GetFileNameWithoutExtension(inputPath);
                        var outputPath = Path.Combine(outputDirectory, $"{baseName}_analysis.json");
                        
                        if (File.Exists(outputPath))
                        {
                            var jsonContent = await File.ReadAllTextAsync(outputPath);
                            _logger.LogInformation("Successfully analyzed document. Result length: {Length}", jsonContent.Length);
                            return jsonContent;
                        }
                        _logger.LogWarning("Azure analysis succeeded but output file not found: {OutputPath}", outputPath);
                    }
                }
                catch (Exception ex)
                {
                    var msg = $"Azure analysis failed: {ex.Message}";
                    _logger.LogError(ex, msg);
                    if (batchId > 0)
                    {
                        await _batchLogService.LogBatchTaskAsync(batchId, "OCR_AZURE_ERROR", msg, null, "system");
                    }
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document with Azure Document Intelligence");
                return string.Empty;
            }
        }
        
        public async Task<string> ProcessWithGoogleDocAiAsync(string inputPath, string outputDirectory, string endpoint, string processorId, string apiKey, Dictionary<string, object>? configData = null)
        {
            try
            {
                string googleEndpoint = endpoint;
                string googleProcessorId = processorId;
                string googleApiKey = apiKey;

                var effectiveConfig = configData;
                if (effectiveConfig == null)
                {
                    // Get the default Google Document AI connector from OCR connectors table
                    var connectors = await _ocrConnectorService.GetAllOcrConnectorsAsync();
                    var googleConnector = connectors.FirstOrDefault(c => c.Provider?.Name?.ToLower() == "googledocai" && c.IsDefault)
                                       ?? connectors.FirstOrDefault(c => c.Provider?.Name?.ToLower() == "googledocai" && c.IsActive);
                    effectiveConfig = googleConnector?.ConfigData;
                }
                
                if (effectiveConfig != null)
                {
                    // Extract configuration values from effectiveConfig
                    googleEndpoint = effectiveConfig.ContainsKey("endpoint") ? effectiveConfig["endpoint"]?.ToString() : endpoint;
                    googleProcessorId = effectiveConfig.ContainsKey("processorid") ? effectiveConfig["processorid"]?.ToString() : processorId;
                    googleApiKey = effectiveConfig.ContainsKey("apikey") ? effectiveConfig["apikey"]?.ToString() : apiKey;
                }
                
                if (string.IsNullOrEmpty(googleEndpoint) || string.IsNullOrEmpty(googleProcessorId))
                {
                    _logger.LogWarning("Google Document AI configuration not complete. Please provide endpoint and processorId.");
                    return string.Empty;
                }
                
                // Build the processor name from components
                var fullProcessorName = $"projects/{GetProjectIdFromEndpoint(googleEndpoint)}/locations/{GetLocationFromEndpoint(googleEndpoint)}/processors/{googleProcessorId}";
                
                var analyzer = new GoogleDocAiAnalyzer(googleEndpoint, googleApiKey, googleProcessorId);
                var success = await analyzer.AnalyzeAndSaveAsync(inputPath, outputDirectory);
                
                if (success)
                {
                    var baseName = Path.GetFileNameWithoutExtension(inputPath);
                    var outputPath = Path.Combine(outputDirectory, $"{baseName}_analysis.json");
                    
                    if (File.Exists(outputPath))
                    {
                        var jsonContent = await File.ReadAllTextAsync(outputPath);
                        return jsonContent;
                    }
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document with Google Document AI");
                return string.Empty;
            }
        }
        
        public async Task<string> ProcessWithAmazonTextractAsync(string inputPath, string outputDirectory, string region, string accessKey, string secretKey, Dictionary<string, object>? configData = null)
        {
            try
            {
                string amazonRegion = region;
                string amazonAccessKey = accessKey;
                string amazonSecretKey = secretKey;

                var effectiveConfig = configData;
                if (effectiveConfig == null)
                {
                    // Get the default Amazon Textract connector from OCR connectors table
                    var connectors = await _ocrConnectorService.GetAllOcrConnectorsAsync();
                    var amazonConnector = connectors.FirstOrDefault(c => c.Provider?.Name?.ToLower() == "amazontextract" && c.IsDefault)
                                       ?? connectors.FirstOrDefault(c => c.Provider?.Name?.ToLower() == "amazontextract" && c.IsActive);
                    effectiveConfig = amazonConnector?.ConfigData;
                }
                
                if (effectiveConfig != null)
                {
                    // Extract configuration values from effectiveConfig
                    amazonRegion = effectiveConfig.ContainsKey("region") ? effectiveConfig["region"]?.ToString() : region;
                    amazonAccessKey = effectiveConfig.ContainsKey("accesskey") ? effectiveConfig["accesskey"]?.ToString() : accessKey;
                    amazonSecretKey = effectiveConfig.ContainsKey("secretkey") ? effectiveConfig["secretkey"]?.ToString() : secretKey;
                }
                
                if (string.IsNullOrEmpty(amazonRegion) || string.IsNullOrEmpty(amazonAccessKey) || string.IsNullOrEmpty(amazonSecretKey))
                {
                    _logger.LogWarning("Amazon Textract configuration not complete. Please provide region, accessKey, and secretKey.");
                    return string.Empty;
                }
                
                var analyzer = new AmazonTextractAnalyzer(amazonRegion, amazonAccessKey, amazonSecretKey);
                var success = await analyzer.AnalyzeAndSaveAsync(inputPath, outputDirectory);
                
                if (success)
                {
                    var baseName = Path.GetFileNameWithoutExtension(inputPath);
                    var outputPath = Path.Combine(outputDirectory, $"{baseName}_analysis.json");
                    
                    if (File.Exists(outputPath))
                    {
                        var jsonContent = await File.ReadAllTextAsync(outputPath);
                        return jsonContent;
                    }
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document with Amazon Textract");
                return string.Empty;
            }
        }
        
        private string GetProjectIdFromEndpoint(string endpoint)
        {
            // Extract project ID from the endpoint
            // For now, we'll use a placeholder - in a real implementation this would come from config
            return "placeholder-project"; 
        }
        
        private string GetLocationFromEndpoint(string endpoint)
        {
            // Extract location from the endpoint
            // For now, we'll use a placeholder - in a real implementation this would come from config
            return "us"; 
        }

        public async Task<string> ExtractWordsFromPageAsync(string filePath, Dictionary<string, object>? configData = null)
        {
            try
            {
                if (!File.Exists(filePath)) return string.Empty;
                
                byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
                
                // Use provided configData or fall back to default connector
                var effectiveConfig = configData;
                if (effectiveConfig == null)
                {
                    var defaultConnector = await _ocrConnectorService.GetDefaultOcrConnectorAsync();
                    effectiveConfig = defaultConnector?.ConfigData;
                }
                
                string tesseractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tessract");
                string tesseractLanguage = "eng";
                
                if (effectiveConfig != null)
                {
                    if (effectiveConfig.ContainsKey("tessdatapath"))
                    {
                        tesseractPath = effectiveConfig["tessdatapath"]?.ToString(); if (string.IsNullOrEmpty(tesseractPath) || tesseractPath.StartsWith("./")) tesseractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tessract");
                    }
                    if (effectiveConfig.ContainsKey("language"))
                    {
                        tesseractLanguage = effectiveConfig["language"]?.ToString() ?? "eng";
                    }
                }
                
                // Use Default engine mode (LSTM neural network) for better accuracy
                using (var engine = new TesseractEngine(tesseractPath, tesseractLanguage, EngineMode.Default))
                {
                    byte[] compatibleBytes = await DecodeToCompatibleFormatAsync(imageBytes);
                    var rawImg = Pix.LoadFromMemory(compatibleBytes);
                    
                    // --- Image Preprocessing for better recognition ---
                    // 1. Convert to grayscale if color
                    Pix processedImg;
                    try
                    {
                        processedImg = rawImg.ConvertRGBToGray();
                    }
                    catch
                    {
                        // Already grayscale or 1bpp — use as-is
                        processedImg = rawImg.Clone();
                    }
                    rawImg.Dispose();
                    
                    // 2. Binarize using Otsu's adaptive thresholding
                    try
                    {
                        var binarized = processedImg.BinarizeOtsuAdaptiveThreshold(2000, 2000, 0, 0, 0.0f);
                        processedImg.Dispose();
                        processedImg = binarized;
                    }
                    catch
                    {
                        // If binarization fails, continue with grayscale
                        _logger.LogWarning("Binarization failed, using grayscale image for OCR");
                    }
                    
                    // 3. Deskew the image
                    try
                    {
                        var deskewed = processedImg.Deskew();
                        processedImg.Dispose();
                        processedImg = deskewed;
                    }
                    catch
                    {
                        // If deskew fails, continue without it
                        _logger.LogWarning("Deskew failed, continuing without deskew");
                    }
                    
                    using (processedImg)
                    using (var page = engine.Process(processedImg, PageSegMode.Auto))
                    {
                        var analysis = new AzureDocIntelAnalysis();
                        var ocrData = new AzureDocIntelOcrData
                        {
                            PageNumber = 1,
                            Dimensions = new AzureDocIntelDimensions
                            {
                                // Use original image dimensions for coordinate mapping
                                Width = processedImg.Width,
                                Height = processedImg.Height,
                                Unit = "pixel"
                            }
                        };
                        
                        using (var iter = page.GetIterator())
                        {
                            iter.Begin();
                            do
                            {
                                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                                {
                                    var text = iter.GetText(PageIteratorLevel.Word);
                                    text = CleanExtractedText(text);
                                    if (string.IsNullOrWhiteSpace(text)) continue;
                                    
                                    var confidence = iter.GetConfidence(PageIteratorLevel.Word) / 100.0; // Normalize to 0-1
                                    
                                    ocrData.Words.Add(new AzureDocIntelWord
                                    {
                                        Text = text,
                                        Confidence = confidence,
                                        Polygon = new double[] { 
                                            rect.X1, rect.Y1, 
                                            rect.X2, rect.Y1, 
                                            rect.X2, rect.Y2, 
                                            rect.X1, rect.Y2 
                                        }
                                    });
                                }
                            } while (iter.Next(PageIteratorLevel.Word));
                        }
                        
                        analysis.OcrData.Add(ocrData);
                        analysis.Metadata.TotalPages = 1;
                        analysis.Metadata.AnalyzedAt = DateTime.UtcNow;
                        analysis.Metadata.InputFile = Path.GetFileName(filePath);
                        
                        return System.Text.Json.JsonSerializer.Serialize(analysis);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting words from page using Tesseract");
                return string.Empty;
            }
        }

        public async Task<string> ExtractFullPageTextAsync(string filePath, Dictionary<string, object>? configData = null)
        {
            try
            {
                if (!File.Exists(filePath)) return string.Empty;
                
                byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
                
                // Use provided configData or fall back to default connector
                var effectiveConfig = configData;
                if (effectiveConfig == null)
                {
                    var defaultConnector = await _ocrConnectorService.GetDefaultOcrConnectorAsync();
                    effectiveConfig = defaultConnector?.ConfigData;
                }
                
                string tesseractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tessract");
                string tesseractLanguage = "eng";
                
                if (effectiveConfig != null)
                {
                    if (effectiveConfig.ContainsKey("tessdatapath"))
                    {
                        tesseractPath = effectiveConfig["tessdatapath"]?.ToString();
                        if (string.IsNullOrEmpty(tesseractPath) || tesseractPath.StartsWith("./"))
                        {
                            tesseractPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tessract");
                        }
                    }
                    if (effectiveConfig.ContainsKey("language"))
                    {
                        tesseractLanguage = effectiveConfig["language"]?.ToString() ?? "eng";
                    }
                }
                
                // Use Default engine mode for better accuracy
                using (var engine = new TesseractEngine(tesseractPath, tesseractLanguage, EngineMode.Default))
                {
                    byte[] compatibleBytes = await DecodeToCompatibleFormatAsync(imageBytes);
                    using (var img = Pix.LoadFromMemory(compatibleBytes))
                    {
                        // Image preprocessing can be added here if needed, 
                        // but for full-page text, Auto segmentation is usually enough
                        using (var page = engine.Process(img, PageSegMode.Auto))
                        {
                            return CleanFullPageText(page.GetText());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting full page text using Tesseract");
                return string.Empty;
            }
        }

        public async Task<Dictionary<int, string>> ExtractZonesFromAzureAnalysisAsync(string analysisJson, IEnumerable<ZoneDto> zones, int batchId = 0)
        {
            var results = new Dictionary<int, string>();
            if (string.IsNullOrEmpty(analysisJson) || zones == null || !zones.Any())
                return results;

            try
            {
                using var doc = JsonDocument.Parse(analysisJson);
                var root = doc.RootElement;

                // 1. First, let's try to find text for each zone based on coordinates
                if (root.TryGetProperty("ocrData", out var ocrData) && ocrData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var zone in zones)
                    {
                        var zoneText = new List<string>();
                        
                        // Search in all pages (or specific page if zone.PageNo is set)
                        foreach (var page in ocrData.EnumerateArray())
                        {
                            int pageNum = page.TryGetProperty("pageNumber", out var p) ? p.GetInt32() : 1;
                            if (zone.PageNo > 0 && zone.PageNo != pageNum) continue;

                            if (page.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array)
                            {
                                int wordCount = 0;
                                foreach (var word in words.EnumerateArray())
                                {
                                    wordCount++;
                                    if (IsWordInZone(word, zone, page))
                                    {
                                        zoneText.Add(word.GetProperty("text").GetString() ?? "");
                                    }
                                    
                                    // Log first 2 words for the first zone to debug scaling
                                    if (results.Count == 0 && zoneText.Count == 0 && wordCount <= 2)
                                    {
                                        _logger.LogInformation("DEBUG: Zone '{Name}' [{L},{T},{R},{B}] vs Word '{Text}'", 
                                            zone.Name, zone.LeftX, zone.TopY, zone.RightX, zone.BottomY, word.GetProperty("text").GetString());
                                    }
                                }
                            }
                        }

                        if (zoneText.Any())
                        {
                            results[zone.ID] = string.Join(" ", zoneText);
                        }
                    }
                }

                // 2. Second, try to match by name from KeyValuePairs if zone is still empty
                if (root.TryGetProperty("keyValuePairs", out var kvps) && kvps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var zone in zones)
                    {
                        if (results.ContainsKey(zone.ID) && !string.IsNullOrEmpty(results[zone.ID])) continue;

                        foreach (var kvp in kvps.EnumerateArray())
                        {
                            if (kvp.TryGetProperty("key", out var key) && key.TryGetProperty("text", out var keyText))
                            {
                                var kText = keyText.GetString() ?? "";
                                // Check if the zone name matches the Azure key name (case-insensitive fuzzy match)
                                if (IsMatch(kText, zone.Name))
                                {
                                    if (kvp.TryGetProperty("value", out var val) && val.ValueKind != JsonValueKind.Null)
                                    {
                                        results[zone.ID] = val.GetProperty("text").GetString() ?? "";
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting zones from Azure analysis JSON");
            }

            return results;
        }

        private bool IsWordInZone(JsonElement word, ZoneDto zone, JsonElement page)
        {
            if (!word.TryGetProperty("polygon", out var poly) || poly.ValueKind != JsonValueKind.Array || poly.GetArrayLength() < 8)
                return false;

            // Get page dimensions and units
            string unit = "pixel";
            if (page.TryGetProperty("dimensions", out var dims))
            {
                unit = dims.TryGetProperty("unit", out var u) ? u.GetString()?.ToLower() ?? "pixel" : "pixel";
            }

            // Scale factor if unit is inches (Azure usually uses 72 DPI for its coordinate system in some modes)
            float scale = 1.0f;
            if (unit == "inch") scale = 72.0f; 

            // Azure polygon is [x1, y1, x2, y2, x3, y3, x4, y4]
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < poly.GetArrayLength(); i += 2)
            {
                float x = poly[i].GetSingle() * scale;
                float y = poly[i+1].GetSingle() * scale;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
            
            // Log for the first few checks
            if (zone.LeftX < 500 && minX < 500) // Just a filter to avoid flooding
            {
                 var msg = $"DEBUG: Word '{word.GetProperty("text").GetString()}' at [{minX},{minY},{maxX},{maxY}] in {unit}. Page: {(page.TryGetProperty("dimensions", out var d) ? d.GetProperty("width").GetRawText() : "0")}x{(page.TryGetProperty("dimensions", out var d2) ? d2.GetProperty("height").GetRawText() : "0")}";
                 _logger.LogInformation(msg);
                 // If batchId is provided, also log to the batch log table
                 // This will be handled in the caller to avoid too many DB calls
            }
            
            // Check for overlap
            float overlapX1 = Math.Max(minX, (float)(zone.LeftX ?? 0));
            float overlapY1 = Math.Max(minY, (float)(zone.TopY ?? 0));
            float overlapX2 = Math.Min(maxX, (float)(zone.RightX ?? 0));
            float overlapY2 = Math.Min(maxY, (float)(zone.BottomY ?? 0));

            if (overlapX1 < overlapX2 && overlapY1 < overlapY2)
            {
                float overlapArea = (overlapX2 - overlapX1) * (overlapY2 - overlapY1);
                float wordArea = (maxX - minX) * (maxY - minY);
                return overlapArea > (wordArea * 0.4); 
            }

            return false;
        }

        private bool IsMatch(string azureKey, string zoneName)
        {
            if (string.IsNullOrEmpty(azureKey) || string.IsNullOrEmpty(zoneName)) return false;
            
            var s1 = Clean(azureKey);
            var s2 = Clean(zoneName);
            
            return s1.Contains(s2) || s2.Contains(s1);
        }

        private string Clean(string s) => new string(s.ToLower().Where(char.IsLetterOrDigit).ToArray());

        public async Task<string> ProcessWithOllamaAsync(string extractedText, IEnumerable<IndexFieldDto> indexFields, Dictionary<string, object>? configData = null)
        {
            if (string.IsNullOrWhiteSpace(extractedText))
                return string.Empty;

            string endpoint = "http://localhost:11434/api/generate";
            string modelId = "gemma4:e4b";

            if (configData != null)
            {
                if (configData.ContainsKey("endpoint") && !string.IsNullOrEmpty(configData["endpoint"]?.ToString()))
                    endpoint = configData["endpoint"]?.ToString() ?? endpoint;
                if (configData.ContainsKey("modelid") && !string.IsNullOrEmpty(configData["modelid"]?.ToString()))
                    modelId = configData["modelid"]?.ToString() ?? modelId;
            }

            var fieldNames = indexFields.Select(f => f.Label).Where(f => !string.IsNullOrEmpty(f)).ToList();
            string fieldsList = fieldNames.Any() ? string.Join(", ", fieldNames) : "all relevant data";

            string prompt = $@"You are an OCR data extraction assistant. Here is the raw text extracted from a document using Tesseract OCR:

{extractedText}

IMPORTANT: Extract ALL key-value pairs and data fields found in the document. The following fields are high priority: [{fieldsList}]. For each high-priority field name in the list [{fieldsList}] that you extract, you MUST use the exact field name from the list as the key in the ""Metadata"" JSON object. Do not change the key names (e.g. if the list has 'Name', do not use 'Applicant/Person Name' or 'Person Name').

OCR CORRECTION & CLEANUP RULES:
1. DATE CORRECTION: OCR processes frequently misrecognize slashes ('/') or dashes ('-') as the digit '1', 'I', 'l', or '|', or merge them with adjacent digits (e.g., merging a slash and the leading '1' of a year like '1990' into a single '1').
   - For example, if you see a digit string like '151081990' for Date of Birth, do NOT parse it as '15/10/1981'. Instead, reconstruct it logically as '15/08/1990' by recognizing that the '1's represent slashes ('/') and the leading '1' of '1990' was merged with the slash.
   - If a date is '231111974', reconstruct it as '23/11/1974'.
   - Validate that all extracted dates are logically valid calendar dates. Format all extracted dates as 'DD/MM/YYYY'.
2. GENERAL CLEANUP: Clean up OCR noise, weird characters, and misrecognized labels. Ensure name fields contain only alphabetical characters and spaces, removing any stray punctuation or digits that were clearly OCR artifacts.

TABLE / LINE ITEMS EXTRACTION RULES:
1. Identify any table or grid of items in the raw text.
2. Determine the column headers for the table (e.g., ""Description"", ""Quantity"", ""UnitPrice"", ""Total"").
3. Represent the table as a JSON array named ""LineItems"".
4. Each row in the table must be an object in the ""LineItems"" array.
5. All row objects in the array MUST use the exact same keys (representing the column headers). Do NOT use dynamic keys or cell values (e.g. do NOT output key names like ""Left quarter panel"" or ""Tail lights""). Every object in the list must share identical key names.
6. For cell values in ""LineItems"", do NOT include confidence scores or nested objects; output the cell values strictly as simple strings.

Output the result strictly as a valid JSON object with exactly this structure:
{{
  ""Metadata"": {{
    ""FieldName"": {{
      ""Value"": ""extracted value"",
      ""Confidence"": 0.99
    }}
  }},
  ""LineItems"": [
    {{
      ""Description"": ""item description"",
      ""Quantity"": ""1"",
      ""UnitPrice"": ""10.00"",
      ""Total"": ""10.00""
    }},
    {{
      ""Description"": ""another item"",
      ""Quantity"": ""2"",
      ""UnitPrice"": ""5.00"",
      ""Total"": ""10.00""
    }}
  ]
}}

Put ALL extracted key-value pairs inside the Metadata object. If a field is not found, omit it. Output only valid JSON without any markdown formatting or extra text.";

            var requestData = new OllamaRequest
            {
                Model = modelId,
                Prompt = prompt,
                Stream = false,
                Format = "json"
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            string jsonString = JsonSerializer.Serialize(requestData, options);
            var content = new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json");

            await _ollamaSemaphore.WaitAsync();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                var response = await client.PostAsync(endpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var resultData = JsonSerializer.Deserialize<OllamaResponse>(responseBody, options);
                    return resultData?.Response ?? string.Empty;
                }
                else
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Ollama API Error ({StatusCode}): {ErrorBody}", response.StatusCode, errorBody);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to Ollama at {Endpoint}", endpoint);
                return string.Empty;
            }
            finally
            {
                _ollamaSemaphore.Release();
            }
        }
    }

    public class OllamaRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public bool Stream { get; set; }
        public string? Format { get; set; }
    }

    public class OllamaResponse
    {
        public string Model { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public bool Done { get; set; }
    }
}
