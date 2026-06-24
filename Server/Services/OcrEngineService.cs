using Microsoft.Extensions.Logging;
using Server.Models;
using Server.Repositories;
using TrueCapture.Services;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace Server.Services
{
    public class OcrEngineService : IOcrEngineService
    {
        private readonly ILogger<OcrEngineService> _logger;
        private readonly IVerifyRepository _verifyRepository;
        private readonly IOcrService _ocrService;
        private readonly IBatchLogService _batchLogService;
        private readonly IFileStorageService _fileStorageService;

        public OcrEngineService(
            ILogger<OcrEngineService> logger,
            IVerifyRepository verifyRepository,
            IOcrService ocrService,
            IBatchLogService batchLogService,
            IFileStorageService fileStorageService)
        {
            _logger = logger;
            _verifyRepository = verifyRepository;
            _ocrService = ocrService;
            _batchLogService = batchLogService;
            _fileStorageService = fileStorageService;
        }

        public async Task ProcessDocumentAsync(string providerName, DocumentModel document, List<PageModel> documentPages, int batchId, Dictionary<string, object>? configData = null)
        {
            try
            {
                providerName = providerName.ToLower().Replace(" ", "").Trim();
                var configKeys = configData?.Keys != null ? string.Join(", ", configData.Keys) : "none";
                _logger.LogInformation("ENGINE_DIAGNOSTIC: Processing Document {DocId} with Provider '{Provider}'. ConfigKeys: [{ConfigKeys}]", 
                    document.DocId, providerName, configKeys);
                
                await SafeLogAsync(batchId, "OCR_ENGINE_START", $"Document {document.DocId} starting with provider: {providerName}", "INFO");

                switch (providerName)
                {
                    case "azuredocintel":
                    case "azure":
                        var firstPage = documentPages.First();
                        var (firstPagePath, _) = await _verifyRepository.GetPageFilePathAsync(firstPage.PageId);
                        await ProcessWithAzureDocIntelAsync(document, firstPagePath, batchId, configData);
                        break;

                    case "googledocai":
                    case "google":
                        var gp = documentPages.First();
                        var (gpPath, _) = await _verifyRepository.GetPageFilePathAsync(gp.PageId);
                        await ProcessWithGoogleDocAiAsync(document, gpPath, batchId, configData);
                        break;

                    case "amazontextract":
                    case "amazon":
                        var ap = documentPages.First();
                        var (apPath, _) = await _verifyRepository.GetPageFilePathAsync(ap.PageId);
                        await ProcessWithAmazonTextractAsync(document, apPath, batchId, configData);
                        break;

                    case "ollama":
                        await ProcessWithOllamaAsync(document, documentPages, batchId, configData);
                        break;

                    case "tesseract":
                        await ProcessWithTesseractAsync(document, documentPages, batchId, configData);
                        break;
                    default:
                        await ProcessWithTesseractAsync(document, documentPages, batchId, configData);
                        break;
                }

                // Word-level extraction for Point-and-Shoot
                await ExtractWordLevelDataAsync(providerName, document, documentPages, configData);
                
                // Full-page text extraction
                await ExtractFullPageTextAsync(providerName, documentPages, batchId, configData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR failed for Document {DocId} in Batch {BatchId} using provider {Provider}", document.DocId, batchId, providerName);
                await SafeLogAsync(batchId, "OCR_DOCUMENT_FAILED", $"Document {document.DocId} OCR failed: {ex.Message}", "ERROR", ex.StackTrace);
                throw;
            }
        }

        private async Task ProcessWithTesseractAsync(DocumentModel document, List<PageModel> documentPages, int batchId, Dictionary<string, object>? configData = null)
        {
            await SafeLogAsync(batchId, "TESSERACT_OCR_START", $"Starting Tesseract OCR for document {document.DocId}", "INFO");

            var zones = await _verifyRepository.GetZonesForDocumentTypeAsync(document.DocTypeId);
            var docTypeProperties = await _verifyRepository.GetIndexFieldsAsync(document.DocTypeId);

            foreach (var zone in zones)
            {
                try
                {
                    if (!zone.Enabled) continue;
                    await ProcessZoneWithTesseractInternalAsync(document, documentPages, zone, docTypeProperties, configData);
                }
                catch (Exception zoneEx)
                {
                    _logger.LogError(zoneEx, "Error processing zone '{ZoneName}' in Doc {DocId}", zone.Name, document.DocId);
                    await SafeLogAsync(batchId, "TESSERACT_ZONE_FAILED", $"Zone {zone.Name} failed in doc {document.DocId}", "ERROR", zoneEx.Message);
                }
            }

        }

        public async Task ProcessZoneWithTesseractAsync(DocumentModel document, List<PageModel> documentPages, ZoneDto zone)
        {
            var docTypeProperties = await _verifyRepository.GetIndexFieldsAsync(document.DocTypeId);
            await ProcessZoneWithTesseractInternalAsync(document, documentPages, zone, docTypeProperties);
        }

        private async Task ProcessZoneWithTesseractInternalAsync(DocumentModel document, List<PageModel> documentPages, ZoneDto zone, IEnumerable<IndexFieldDto> docTypeProperties, Dictionary<string, object>? configData = null)
        {
            var targetPage = documentPages.FirstOrDefault(p => p.DocPage == zone.PageNo) ?? documentPages.First();
            var (pagePath, _) = await _verifyRepository.GetPageFilePathAsync(targetPage.PageId);

            if (string.IsNullOrEmpty(pagePath) || !File.Exists(pagePath))
            {
                _logger.LogWarning("File not found for page {PageId} at '{Path}'", targetPage.PageId, pagePath);
                return;
            }

            var imageBytes = await File.ReadAllBytesAsync(pagePath);
            var imageBase64 = Convert.ToBase64String(imageBytes);

            var mappedProperty = docTypeProperties.FirstOrDefault(p => p.ZoneId == zone.ID);
            var propertyType = mappedProperty?.Type ?? "string";

            var ocrResult = await _ocrService.ExtractTextFromZoneAsync(new ZoneExtractionRequest
            {
                ImageBase64 = imageBase64,
                JsLeft = zone.LeftX ?? 0,
                JsTop = zone.TopY ?? 0,
                JsRight = zone.RightX ?? 0,
                JsBottom = zone.BottomY ?? 0,
                DisplayedWidth = zone.DisplayedWidth ?? 0,
                DisplayedHeight = zone.DisplayedHeight ?? 0,
                PropertyType = propertyType
            }, configData);

            await _verifyRepository.SaveOcrResultAsync(document.DocId, zone.ID, ocrResult.Text, ocrResult.Confidence);
            await _verifyRepository.UpdateDocumentFieldFromOcrAsync(document.DocId, zone.ID, ocrResult.Text);
        }

        private async Task ExtractWordLevelDataAsync(string providerName, DocumentModel document, List<PageModel> documentPages, Dictionary<string, object>? configData = null)
        {
            if (providerName == "azuredocintel" || providerName == "azure")
            {
                // Azure already handles word-level mapping in ProcessWithAzureDocIntelAsync
                return;
            }
            if (providerName == "ollama")
            {
                // Ollama handles word-level data, KVPs, and Tables together in ProcessWithOllamaAsync
                return;
            }
            // FETCH EXISTING AZURE RESULT TO PRESERVE KVP AND TABLES
            var existingResult = await _verifyRepository.GetAzureDocIntelResultsByDocumentIdAsync(document.DocId);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            AzureDocIntelAnalysis fullAnalysis = null;
            if (!string.IsNullOrEmpty(existingResult))
            {
                try { fullAnalysis = JsonSerializer.Deserialize<AzureDocIntelAnalysis>(existingResult, options); } catch { }
            }
            if (fullAnalysis == null) fullAnalysis = new AzureDocIntelAnalysis();

            // Clear existing OcrData so we don't duplicate
            fullAnalysis.OcrData.Clear();

            foreach (var page in documentPages)
            {
                var (pagePath, _) = await _verifyRepository.GetPageFilePathAsync(page.PageId);
                if (string.IsNullOrEmpty(pagePath) || !File.Exists(pagePath)) continue;

                var pageJson = await _ocrService.ExtractWordsFromPageAsync(pagePath, configData);
                if (!string.IsNullOrEmpty(pageJson))
                {
                    var pageAnalysis = JsonSerializer.Deserialize<AzureDocIntelAnalysis>(pageJson, options);
                    if (pageAnalysis?.OcrData.Any() == true)
                    {
                        var ocrData = pageAnalysis.OcrData.First();
                        ocrData.PageNumber = page.DocPage;
                        fullAnalysis.OcrData.Add(ocrData);
                    }
                }
            }

            if (fullAnalysis.OcrData.Any() || fullAnalysis.KeyValuePairs.Any() || fullAnalysis.Tables.Any())
            {
                fullAnalysis.Metadata.TotalPages = documentPages.Count;
                fullAnalysis.Metadata.AnalyzedAt = DateTime.UtcNow;
                var combinedJson = JsonSerializer.Serialize(fullAnalysis, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await _verifyRepository.SaveAzureDocIntelResultAsync(document.DocId, combinedJson);
            }
        }

        private async Task ExtractFullPageTextAsync(string providerName, List<PageModel> documentPages, int batchId, Dictionary<string, object>? configData = null)
        {
            // If we are using Azure, we should have already saved the full text in the JSON
            // This method is mainly for Tesseract fallback or legacy search indexing
            
            var batchDirectory = await _fileStorageService.GetBatchPathAsync(batchId);
            if (string.IsNullOrEmpty(batchDirectory)) return;

            Directory.CreateDirectory(batchDirectory);

            foreach (var page in documentPages)
            {
                var (pagePath, _) = await _verifyRepository.GetPageFilePathAsync(page.PageId);
                if (string.IsNullOrEmpty(pagePath) || !File.Exists(pagePath)) continue;

                // Check if we have an Azure result for this document first
                // For now, let's just make sure we don't force Tesseract if Azure was intended
                if (providerName == "azuredocintel" || providerName == "azure")
                {
                    // Azure text is already handled in ProcessWithAzureDocIntelAsync
                    continue; 
                }

                var fullText = await _ocrService.ExtractFullPageTextAsync(pagePath, configData);
                if (!string.IsNullOrEmpty(fullText))
                {
                    var imageFileName = Path.GetFileNameWithoutExtension(pagePath);
                    var outputFilePath = Path.Combine(batchDirectory, $"{imageFileName}_analysis.txt");
                    await File.WriteAllTextAsync(outputFilePath, fullText);
                }
            }
        }

        private async Task ProcessWithAzureDocIntelAsync(DocumentModel document, string filePath, int batchId, Dictionary<string, object>? configData = null)
        {
            var outputDirectory = await _fileStorageService.GetBatchPathAsync(batchId);
            if (string.IsNullOrEmpty(outputDirectory)) outputDirectory = Path.GetTempPath();

            Directory.CreateDirectory(outputDirectory);
            var analysisResult = await _ocrService.ProcessWithAzureDocIntelAsync(filePath, outputDirectory, batchId, configData);

            if (!string.IsNullOrEmpty(analysisResult))
            {
                // Save the raw JSON result for the Verify page to use
                await _verifyRepository.SaveAzureDocIntelResultAsync(document.DocId, analysisResult);

                // Now, map the Azure results back into the document's fields
                // We try Zones first, but if no zones exist, we use the Index Fields directly (Key-Value matching)
                var zones = (await _verifyRepository.GetZonesForDocumentTypeAsync(document.DocTypeId))?.ToList();
                var indexFields = (await _verifyRepository.GetIndexFieldsAsync(document.DocTypeId))?.ToList();

                _logger.LogInformation("DEBUG: Found {ZCount} zones and {FCount} fields for DocTypeId {DocTypeId}", 
                    zones?.Count ?? 0, indexFields?.Count ?? 0, document.DocTypeId);
                
                await _batchLogService.LogBatchTaskAsync(batchId, "OCR_MAPPING_DEBUG", 
                    $"Found {zones?.Count ?? 0} zones and {indexFields?.Count ?? 0} fields for document type {document.DocTypeId}", null, "system");

                // If we have no zones but have index fields, let's create "virtual zones" from the fields for mapping
                if ((zones == null || !zones.Any()) && indexFields != null && indexFields.Any())
                {
                    zones = indexFields.Select(f => new ZoneDto 
                    { 
                        ID = f.ZoneId ?? 0, // This might be 0, we need to handle mapping to fieldId
                        Name = f.Label,
                        DocTypeID = document.DocTypeId,
                        Enabled = true
                    }).ToList();
                }

                if (zones != null && zones.Any())
                {
                    _logger.LogInformation("Mapping Azure results to {Count} fields for Document {DocId}", zones.Count, document.DocId);
                    var zoneResults = await _ocrService.ExtractZonesFromAzureAnalysisAsync(analysisResult, zones, batchId);
                    
                    int mappedCount = 0;
                    foreach (var zoneResult in zoneResults)
                    {
                        if (!string.IsNullOrEmpty(zoneResult.Value))
                        {
                            mappedCount++;
                            // We need to find the correct field ID if the zone ID is missing/virtual
                            var field = indexFields?.FirstOrDefault(f => f.Label == zones.FirstOrDefault(z => z.ID == zoneResult.Key)?.Name || f.ZoneId == zoneResult.Key);
                            
                            if (field != null)
                            {
                                await _verifyRepository.SaveOcrResultAsync(document.DocId, field.ZoneId ?? 0, zoneResult.Value, 99.0);
                                await _verifyRepository.UpdateDocumentFieldFromOcrAsync(document.DocId, field.ZoneId ?? 0, zoneResult.Value);
                            }
                        }
                    }

                    await _batchLogService.LogBatchTaskAsync(batchId, "OCR_AZURE_MAPPING", 
                        $"Successfully mapped {mappedCount} fields using Azure Key-Value pairs for {document.FileName}", null, "system");
                }
            }
        }

        private async Task ProcessWithGoogleDocAiAsync(DocumentModel document, string filePath, int batchId, Dictionary<string, object>? configData)
        {
            var endpoint = configData?.ContainsKey("endpoint") == true ? configData["endpoint"]?.ToString() : "";
            var apiKey = configData?.ContainsKey("apikey") == true ? configData["apikey"]?.ToString() ?? string.Empty : string.Empty;
            var processorId = configData?.ContainsKey("processorid") == true ? configData["processorid"]?.ToString() ?? string.Empty : string.Empty;

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(processorId) || string.IsNullOrEmpty(apiKey)) return;

            var outputDirectory = await _fileStorageService.GetBatchPathAsync(batchId);
            if (string.IsNullOrEmpty(outputDirectory)) outputDirectory = Path.GetTempPath();

            Directory.CreateDirectory(outputDirectory);
            var analysisResult = await _ocrService.ProcessWithGoogleDocAiAsync(filePath, outputDirectory, endpoint, processorId, apiKey);

            if (!string.IsNullOrEmpty(analysisResult))
            {
                await _verifyRepository.SaveAzureDocIntelResultAsync(document.DocId, analysisResult);
            }
        }

        private async Task ProcessWithAmazonTextractAsync(DocumentModel document, string filePath, int batchId, Dictionary<string, object>? configData)
        {
            var region = configData?.ContainsKey("region") == true ? configData["region"]?.ToString() ?? "us-east-1" : "us-east-1";
            var accessKey = configData?.ContainsKey("accesskey") == true ? configData["accesskey"]?.ToString() ?? string.Empty : string.Empty;
            var secretKey = configData?.ContainsKey("secretkey") == true ? configData["secretkey"]?.ToString() ?? string.Empty : string.Empty;

            if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(region)) return;

            var outputDirectory = await _fileStorageService.GetBatchPathAsync(batchId);
            if (string.IsNullOrEmpty(outputDirectory)) outputDirectory = Path.GetTempPath();

            Directory.CreateDirectory(outputDirectory);
            var analysisResult = await _ocrService.ProcessWithAmazonTextractAsync(filePath, outputDirectory, region, accessKey, secretKey);

            if (!string.IsNullOrEmpty(analysisResult))
            {
                await _verifyRepository.SaveAzureDocIntelResultAsync(document.DocId, analysisResult);
            }
        }

        private async Task SafeLogAsync(int batchId, string task, string message, string status, string? details = null)
        {
            try
            {
                await _batchLogService.LogToFile(batchId.ToString(), task, message, status, details, null);
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "Failed to write batch log for batch {BatchId} task {Task}", batchId, task);
            }
        }

        private async Task ProcessWithOllamaAsync(DocumentModel document, List<PageModel> documentPages, int batchId, Dictionary<string, object>? configData)
        {
            await SafeLogAsync(batchId, "OLLAMA_OCR_START", $"Starting Ollama OCR for document {document.DocId}", "INFO");

            var indexFields = (await _verifyRepository.GetIndexFieldsAsync(document.DocTypeId))?.ToList() ?? new List<IndexFieldDto>();

            var targetPage = documentPages.First();
            var (pagePath, _) = await _verifyRepository.GetPageFilePathAsync(targetPage.PageId);

            if (string.IsNullOrEmpty(pagePath) || !File.Exists(pagePath))
            {
                _logger.LogWarning("File not found for page {PageId} at '{Path}'", targetPage.PageId, pagePath);
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // ============================================================
            // STEP 1: ALWAYS extract word-level OCR data using Tesseract
            // This runs regardless of whether Ollama succeeds or fails
            // ============================================================
            var azureFormatAnalysis = new AzureDocIntelAnalysis();

            // Fetch any existing result to preserve data from previous runs
            var existingResult = await _verifyRepository.GetAzureDocIntelResultsByDocumentIdAsync(document.DocId);
            if (!string.IsNullOrEmpty(existingResult))
            {
                try { azureFormatAnalysis = JsonSerializer.Deserialize<AzureDocIntelAnalysis>(existingResult, options) ?? new AzureDocIntelAnalysis(); } catch { }
            }

            // Clear and rebuild OcrData (word-level)
            azureFormatAnalysis.OcrData.Clear();
            foreach (var page in documentPages)
            {
                var (wordPagePath, _) = await _verifyRepository.GetPageFilePathAsync(page.PageId);
                if (string.IsNullOrEmpty(wordPagePath) || !File.Exists(wordPagePath)) continue;

                var pageJson = await _ocrService.ExtractWordsFromPageAsync(wordPagePath, configData);
                if (!string.IsNullOrEmpty(pageJson))
                {
                    var pageAnalysis = JsonSerializer.Deserialize<AzureDocIntelAnalysis>(pageJson, options);
                    if (pageAnalysis?.OcrData.Any() == true)
                    {
                        var ocrData = pageAnalysis.OcrData.First();
                        ocrData.PageNumber = page.DocPage;
                        azureFormatAnalysis.OcrData.Add(ocrData);
                    }
                }
            }
            _logger.LogInformation("Ollama: Extracted {WordPages} word-level OCR pages for Document {DocId}", azureFormatAnalysis.OcrData.Count, document.DocId);

            // ============================================================
            // STEP 2: Extract KVPs and Tables using Ollama (best-effort)
            // If this fails, we still have the OCR data from Step 1
            // ============================================================
            int mappedCount = 0;
            var extractedDocValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string fullText = await _ocrService.ExtractFullPageTextAsync(pagePath, configData);

            if (!string.IsNullOrEmpty(fullText))
            {
                var ollamaResult = await _ocrService.ProcessWithOllamaAsync(fullText, indexFields, configData);
                if (!string.IsNullOrEmpty(ollamaResult))
                {
                    try
                    {
                        // Some LLMs wrap JSON in markdown blocks like ```json ... ```, so clean it first
                        string cleanedResult = ollamaResult.Trim();
                        if (cleanedResult.StartsWith("```json"))
                            cleanedResult = cleanedResult.Substring(7);
                        if (cleanedResult.StartsWith("```"))
                            cleanedResult = cleanedResult.Substring(3);
                        if (cleanedResult.EndsWith("```"))
                            cleanedResult = cleanedResult.Substring(0, cleanedResult.Length - 3);
                        cleanedResult = cleanedResult.Trim();
                        
                        // DEBUG: Write raw cleaned output to file to see what Ollama returned!
                        System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ollama_debug.txt"), cleanedResult);

                        cleanedResult = cleanedResult.Trim();
                        if (!cleanedResult.EndsWith("}")) cleanedResult += "}";

                        var rootObj = JsonSerializer.Deserialize<JsonElement>(cleanedResult, options);

                        // Clear out existing KVPs and Tables so we don't duplicate them on re-run
                        azureFormatAnalysis.KeyValuePairs.Clear();
                        azureFormatAnalysis.Tables.Clear();

                        int kvpIndex = 0;

                        if (rootObj.ValueKind == JsonValueKind.Object)
                        {
                            if (rootObj.TryGetProperty("metadata", out var metadataObj) || rootObj.TryGetProperty("Metadata", out metadataObj))
                            {
                                if (metadataObj.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (var kvp in metadataObj.EnumerateObject())
                                    {
                                        string fieldKey = kvp.Name;
                                        string valStr = "";
                                        double conf = 0.99;

                                        if (kvp.Value.ValueKind == JsonValueKind.Object)
                                        {
                                            if (kvp.Value.TryGetProperty("value", out var v) || kvp.Value.TryGetProperty("Value", out v))
                                                valStr = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText();
                                            if (kvp.Value.TryGetProperty("confidence", out var c) || kvp.Value.TryGetProperty("Confidence", out c))
                                                conf = c.TryGetDouble(out var parsedConf) ? parsedConf : 0.99;
                                        }
                                        else
                                        {
                                            valStr = kvp.Value.ValueKind == JsonValueKind.String ? kvp.Value.GetString() ?? "" : kvp.Value.GetRawText();
                                        }

                                        if (valStr == "null") valStr = ""; // Clear out null literals

                                        azureFormatAnalysis.KeyValuePairs.Add(new AzureDocIntelKeyValuePair
                                        {
                                            PairIndex = kvpIndex++,
                                            Key = new AzureDocIntelTextElement { Text = fieldKey, Confidence = conf },
                                            Value = new AzureDocIntelTextElement { Text = valStr, Confidence = conf },
                                            Confidence = conf
                                        });

                                        // Match Ollama key to a configured index field
                                        var matchingField = indexFields.FirstOrDefault(f => string.Equals(f.Label, fieldKey, StringComparison.OrdinalIgnoreCase) || 
                                                                                        string.Equals(f.Label?.Replace(" ", ""), fieldKey.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                                                                                        (f.Label != null && fieldKey.Contains(f.Label, StringComparison.OrdinalIgnoreCase)) ||
                                                                                        (f.Label != null && f.Label.Contains(fieldKey, StringComparison.OrdinalIgnoreCase)));
                                        if (matchingField != null && !string.IsNullOrEmpty(valStr))
                                        {
                                            // Collect matched values for bulk save via SaveDocumentIndexDataAsync
                                            // Use the PropertyName (Label) as the key, which maps to column_X in doctable
                                            if (!extractedDocValues.ContainsKey(matchingField.Label))
                                            {
                                                extractedDocValues[matchingField.Label] = valStr;
                                            }
                                            mappedCount++;
                                            _logger.LogInformation("Ollama: Matched field '{OllamaKey}' -> property '{Label}' = '{Value}'", fieldKey, matchingField.Label, valStr);
                                        }
                                    }
                                }
                            }

                            if (rootObj.TryGetProperty("lineItems", out var lineItemsArr) || rootObj.TryGetProperty("LineItems", out lineItemsArr))
                            {
                                if (lineItemsArr.ValueKind == JsonValueKind.Array && lineItemsArr.GetArrayLength() > 0)
                                {
                                    var columns = new List<string>();
                                    foreach (var item in lineItemsArr.EnumerateArray())
                                    {
                                        if (item.ValueKind == JsonValueKind.Object)
                                        {
                                            foreach (var prop in item.EnumerateObject())
                                            {
                                                if (!columns.Contains(prop.Name))
                                                {
                                                    columns.Add(prop.Name);
                                                }
                                            }
                                        }
                                    }

                                    if (columns.Any())
                                    {
                                        var table = new AzureDocIntelTable
                                        {
                                            TableIndex = azureFormatAnalysis.Tables.Count,
                                            RowCount = 1 + lineItemsArr.GetArrayLength(),
                                            ColumnCount = columns.Count
                                        };

                                        // Row 0 is column headers
                                        for (int colIndex = 0; colIndex < columns.Count; colIndex++)
                                        {
                                            table.Cells.Add(new AzureDocIntelTableCell
                                            {
                                                Row = 0,
                                                Col = colIndex,
                                                Content = columns[colIndex],
                                                Kind = "columnHeader"
                                            });
                                        }

                                        // Row 1+ are data cells
                                        int dataRowIndex = 1;
                                        foreach (var item in lineItemsArr.EnumerateArray())
                                        {
                                            if (item.ValueKind == JsonValueKind.Object)
                                            {
                                                for (int colIndex = 0; colIndex < columns.Count; colIndex++)
                                                {
                                                    string columnName = columns[colIndex];
                                                    string valStr = "";

                                                    if (item.TryGetProperty(columnName, out var cellValue))
                                                    {
                                                        if (cellValue.ValueKind == JsonValueKind.Object)
                                                        {
                                                            if (cellValue.TryGetProperty("value", out var v) || cellValue.TryGetProperty("Value", out v))
                                                            {
                                                                valStr = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText();
                                                            }
                                                            else
                                                            {
                                                                valStr = cellValue.GetRawText();
                                                            }
                                                        }
                                                        else if (cellValue.ValueKind == JsonValueKind.Null)
                                                        {
                                                            valStr = "";
                                                        }
                                                        else
                                                        {
                                                            valStr = cellValue.ValueKind == JsonValueKind.String ? cellValue.GetString() ?? "" : cellValue.GetRawText();
                                                        }
                                                    }

                                                    if (valStr == "null") valStr = "";

                                                    table.Cells.Add(new AzureDocIntelTableCell
                                                    {
                                                        Row = dataRowIndex,
                                                        Col = colIndex,
                                                        Content = valStr
                                                    });
                                                }
                                                dataRowIndex++;
                                            }
                                        }

                                        if (table.Cells.Any())
                                        {
                                            azureFormatAnalysis.Tables.Add(table);
                                        }
                                    }
                                }
                            }
                        }

                        _logger.LogInformation("Ollama: Parsed {KvpCount} KVPs and {TableCount} tables for Document {DocId}", 
                            azureFormatAnalysis.KeyValuePairs.Count, azureFormatAnalysis.Tables.Count, document.DocId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse Ollama JSON response for Document {DocId}. OCR word data will still be saved.", document.DocId);
                        await SafeLogAsync(batchId, "OLLAMA_PARSE_ERROR", "Failed to parse Ollama JSON response (OCR data preserved)", "ERROR", ex.Message);
                    }
                }
            }

            // Persist matched Ollama metadata into the doctable via property-name mapping
            if (extractedDocValues.Count > 0)
            {
                try
                {
                    _logger.LogInformation("Ollama: Saving {Count} extracted fields for Document {DocId}: [{Fields}]", 
                        extractedDocValues.Count, document.DocId, string.Join(", ", extractedDocValues.Keys));
                    // Ensure the doctable_X exists (auto-create if needed)
                    await _verifyRepository.EnsureDoctableExistsAsync(document.DocTypeId);
                    await _verifyRepository.SaveDocumentIndexDataAsync(document.DocId, extractedDocValues);
                    _logger.LogInformation("Ollama: Successfully persisted {Count} fields to doctable for Document {DocId}", extractedDocValues.Count, document.DocId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ollama: Failed to save extracted fields to doctable for Document {DocId}", document.DocId);
                    await SafeLogAsync(batchId, "OLLAMA_SAVE_ERROR", $"Failed to save {extractedDocValues.Count} extracted fields to doctable", "ERROR", ex.Message);
                }
            }

            // ============================================================
            // STEP 3: ALWAYS save the combined result (OCR + KVPs + Tables)
            // Even if Ollama failed, we still save the word-level OCR data
            // ============================================================
            azureFormatAnalysis.Metadata.TotalPages = documentPages.Count;
            azureFormatAnalysis.Metadata.AnalyzedAt = DateTime.UtcNow;
            azureFormatAnalysis.Metadata.KvpCount = azureFormatAnalysis.KeyValuePairs.Count;
            azureFormatAnalysis.Metadata.TableCount = azureFormatAnalysis.Tables.Count;

            if (azureFormatAnalysis.OcrData.Any() || azureFormatAnalysis.KeyValuePairs.Any() || azureFormatAnalysis.Tables.Any())
            {
                string formattedJson = JsonSerializer.Serialize(azureFormatAnalysis, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await _verifyRepository.SaveAzureDocIntelResultAsync(document.DocId, formattedJson);
                _logger.LogInformation("Ollama: Saved {KvpCount} KVPs, {TableCount} tables, {OcrCount} OCR pages for Document {DocId}", 
                    azureFormatAnalysis.KeyValuePairs.Count, azureFormatAnalysis.Tables.Count, azureFormatAnalysis.OcrData.Count, document.DocId);
            }

            await _batchLogService.LogBatchTaskAsync(batchId, "OCR_OLLAMA_MAPPING", $"Mapped {mappedCount} fields, {azureFormatAnalysis.KeyValuePairs.Count} KVPs, {azureFormatAnalysis.Tables.Count} tables for document {document.DocId}", null, "system");
        }
    }
}
