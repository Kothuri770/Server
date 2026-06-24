using Microsoft.Extensions.Logging;
using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;
using Server.Services.Scanner;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrueCapture.Services;
using ZXing;
using ZXing.Common;

namespace Server.Services
{
    public class SeparationService : ISeparationService
    {
        private readonly ILogger<SeparationService> _logger;
        private readonly IOcrService _ocrService;
        private readonly IImageTransformationService _imageTransformationService;

        public SeparationService(ILogger<SeparationService> logger, IOcrService ocrService, IImageTransformationService imageTransformationService)
        {
            _logger = logger;
            _ocrService = ocrService;
            _imageTransformationService = imageTransformationService;
        }

        public async Task ProcessBatchSeparationAsync(
            int batchId,
            int batchTypeId,
            IVerifyRepository verifyRepository,
            IConfigurationService configService,
            IBatchLogService batchLogService)
        {
            try
            {
                _logger.LogInformation("Starting auto separation logic for Batch {BatchId}", batchId);

                var allDocs = await verifyRepository.GetDocumentsInBatchAsync(batchId);
                if (!allDocs.Any())
                {
                    _logger.LogInformation("No documents found in Batch {BatchId} — nothing to separate", batchId);
                    return;
                }

                // Load all identification rules up front
                var documentTypes = await configService.GetChildObjectsAsync(batchTypeId);
                var allIdentificationRules = new List<IdentificationRuleDto>();
                foreach (var docType in documentTypes)
                {
                    var rules = await configService.GetIdentificationRulesAsync(docType.Id);
                    allIdentificationRules.AddRange(rules);
                }



                int activeDocTypeId = 0;
                string activeDocName = "";
                int currentPageInDoc = 0;
                string lastIdentificationMethod = "";
                int currentBatchSequence = 0;

                foreach (var doc in allDocs)
                {
                    _logger.LogDebug("Processing Page {PageId} (File: {File}). State: Type={DocType}, Doc={DocName}", 
                        doc.DocId, doc.FileName, activeDocTypeId, activeDocName);
                    try
                    {
                        int pageId = doc.DocId;

                        var matchingRule = await ProcessDocumentSeparationAsync(
                            batchId, doc, allIdentificationRules, verifyRepository, configService);

                        if (matchingRule != null)
                        {
                            // Reset sequence only if DocType or Method changed
                            if (activeDocTypeId != matchingRule.DocTypeId || lastIdentificationMethod != matchingRule.Method)
                            {
                                currentBatchSequence = 0;
                            }

                            activeDocTypeId = matchingRule.DocTypeId;
                            lastIdentificationMethod = matchingRule.Method;
                            activeDocName = string.Empty; 
                            currentPageInDoc = 0;

                            _logger.LogInformation("New identification (Rule) for Page {PageId} using {Method} rule for DocType {DocTypeId}. Discard={Discard}", 
                                pageId, lastIdentificationMethod, activeDocTypeId, matchingRule.DiscardPage);

                            if (matchingRule.DiscardPage)
                            {
                                await verifyRepository.UpdateBatchDetailStatusAsync(pageId, "I");
                                continue;
                            }
                        }

                        if (activeDocTypeId > 0)
                        {
                            // Initialize sequence on first match
                            if (currentBatchSequence == 0)
                            {
                                currentBatchSequence = await verifyRepository.GetNextDocumentSequenceAsync(batchId, activeDocTypeId, pageId);
                            }
                            else
                            {
                                currentBatchSequence++;
                            }

                            // Force every page to be a new document (DOC-0001, DOC-0002, etc.) to satisfy "Sequential Naming" requirement
                            currentPageInDoc = 1;
                            
                            var docTypeName = await verifyRepository.GetDocumentTypeNameByIdAsync(activeDocTypeId);
                            var prefix = !string.IsNullOrEmpty(docTypeName) && docTypeName.Length >= 3 ? docTypeName.Substring(0, 3).ToUpper() : (docTypeName ?? "DOC").PadRight(3, 'X').ToUpper();
                            
                            activeDocName = $"{prefix}-{currentBatchSequence:D4}";
                                
                            _logger.LogInformation("Creating independent document {DocName} for Page {PageId} (DocType {DocTypeId})", 
                                activeDocName, pageId, activeDocTypeId);

                            await verifyRepository.UpdateDocumentTypeAsync(pageId, activeDocTypeId);
                            await verifyRepository.UpdateDocumentNameAsync(pageId, activeDocName);
                            await verifyRepository.UpdateBatchDetailMappingAsync(pageId, activeDocTypeId, currentBatchSequence, activeDocName);
                        }
                        else
                        {
                            activeDocTypeId = 0;
                            currentPageInDoc = 1;

                            if (currentBatchSequence == 0)
                            {
                                currentBatchSequence = await verifyRepository.GetNextDocumentSequenceAsync(batchId, 0, pageId);
                            }
                            else
                            {
                                currentBatchSequence++;
                            }
                            
                            activeDocName = $"IMG-{currentBatchSequence:D4}";

                            await verifyRepository.UpdateDocumentTypeAsync(pageId, 0);
                            await verifyRepository.UpdateDocumentNameAsync(pageId, activeDocName);
                            await verifyRepository.UpdateBatchDetailMappingAsync(pageId, 0, 1, activeDocName);
                            _logger.LogInformation("Page {PageId} not matched by any rule and no active document. Marked as {DocName}", pageId, activeDocName);
                        }
                    }
                    catch (Exception docEx)
                    {
                        _logger.LogError(docEx, "Error processing Page {PageId} during separation for Batch {BatchId}", doc.DocId, batchId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in ProcessBatchSeparationAsync for Batch {BatchId}", batchId);
                throw;
            }
        }

        private async Task<IdentificationRuleDto?> ProcessDocumentSeparationAsync(
            int batchId,
            DocumentModel document,
            List<IdentificationRuleDto> identificationRules,
            IVerifyRepository verifyRepository,
            IConfigurationService configService)
        {
            try
            {
                string physicalFileName = !string.IsNullOrEmpty(document.InternalName) ? document.InternalName : document.FileName;

                // 1. Barcode rules
                var barcodeValue = await ReadBarcodeFromImageAsync(document.DocId, verifyRepository);
                if (!string.IsNullOrEmpty(barcodeValue))
                {
                    var matchingRule = identificationRules.FirstOrDefault(rule =>
                        rule.Method?.ToLower() == "barcode" &&
                        rule.Value?.Equals(barcodeValue, StringComparison.OrdinalIgnoreCase) == true);

                    if (matchingRule != null) return matchingRule;
                }

                // 2. Keyword rules
                var keywordRules = identificationRules.Where(rule => rule.Method?.ToLower() == "keyword").ToList();
                foreach (var rule in keywordRules)
                {
                    try
                    {
                        string extractedText = string.Empty;
                        if (rule.ZoneId.HasValue && rule.ZoneId > 0)
                        {
                            var zoneInfo = await configService.GetZonesForDocTypeAsync(rule.DocTypeId);
                            var zone = zoneInfo.FirstOrDefault(z => z.ID == rule.ZoneId);
                            if (zone != null) extractedText = await PerformOcrOnDocumentAsync(document, zone, verifyRepository);
                        }
                        else
                        {
                            extractedText = await PerformOcrOnDocumentAsync(document, null, verifyRepository);
                        }

                        if (!string.IsNullOrEmpty(extractedText) && !string.IsNullOrEmpty(rule.Value))
                        {
                            if (extractedText.Contains(rule.Value, StringComparison.OrdinalIgnoreCase)) return rule;
                        }
                    }
                    catch (Exception ruleEx)
                    {
                        _logger.LogError(ruleEx, "Error evaluating keyword rule {RuleId} for Doc {DocId}", rule.ID, document.DocId);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessDocumentSeparationAsync for Doc {DocId}", document.DocId);
                return null;
            }
        }

        private async Task<string> PerformOcrOnDocumentAsync(
            DocumentModel document,
            ZoneDto? zone,
            IVerifyRepository verifyRepository)
        {
            try
            {
                var (fullPath, _) = await verifyRepository.GetPageFilePathAsync(document.DocId);
                if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath)) return string.Empty;

                string imageBase64 = Convert.ToBase64String(await System.IO.File.ReadAllBytesAsync(fullPath));

                if (zone != null)
                {
                    var ocrResult = await _ocrService.ExtractTextFromZoneAsync(new ZoneExtractionRequest
                    {
                        ImageBase64 = imageBase64,
                        JsLeft = zone.LeftX ?? 0,
                        JsTop = zone.TopY ?? 0,
                        JsRight = zone.RightX ?? 0,
                        JsBottom = zone.BottomY ?? 0,
                        DisplayedWidth = zone.DisplayedWidth ?? 0,
                        DisplayedHeight = zone.DisplayedHeight ?? 0
                    });
                    return ocrResult.Text;
                }
                else
                {
                    var ocrResult = await _ocrService.ExtractTextFromZoneAsync(new ZoneExtractionRequest
                    {
                        ImageBase64 = imageBase64,
                        JsLeft = 0,
                        JsTop = 0,
                        JsRight = 0,
                        JsBottom = 0,
                        DisplayedWidth = 0,
                        DisplayedHeight = 0
                    });
                    return ocrResult.Text;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing OCR for Doc {DocId}", document.DocId);
                return string.Empty;
            }
        }

        private async Task<string?> ReadBarcodeFromImageAsync(int docId, IVerifyRepository verifyRepository)
        {
            try
            {
                var (fullPath, fileName) = await verifyRepository.GetPageFilePathAsync(docId);

                if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
                {
                    _logger.LogWarning("Barcode extraction failed: File not found or path empty. DocId: {DocId}, Path: {Path}", docId, fullPath ?? "NULL");
                    return null;
                }

                var fileInfo = new FileInfo(fullPath);
                _logger.LogInformation("Reading barcode from: {Path} ({Size} bytes)", fullPath, fileInfo.Length);

                using var image = Image.Load<Rgba32>(fullPath);
                var pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);

                var luminanceSource = new RGBLuminanceSource(pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.RGBA32);
                var barcodeReader = new BarcodeReader<RGBLuminanceSource>(source => source)
                {
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        TryInverted = true,
                        PureBarcode = false,
                        PossibleFormats = new List<BarcodeFormat>
                        {
                            BarcodeFormat.CODE_128,
                            BarcodeFormat.CODE_39,
                            BarcodeFormat.CODE_93,
                            BarcodeFormat.QR_CODE,
                            BarcodeFormat.DATA_MATRIX,
                            BarcodeFormat.EAN_13,
                            BarcodeFormat.EAN_8,
                            BarcodeFormat.ITF,
                            BarcodeFormat.PDF_417,
                            BarcodeFormat.UPC_A
                        }
                    }
                };

                var result = barcodeReader.Decode(luminanceSource);
                if (result != null)
                {
                    _logger.LogInformation("Barcode detected: {Text} (Format: {Format})", result.Text, result.BarcodeFormat);
                    return result.Text;
                }

                _logger.LogWarning("No barcode detected in image: {Path}", fullPath);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading barcode for DocId {DocId}", docId);
                return null;
            }
        }

        private async Task<string> GetDocumentTypeNameById(int docTypeId, IVerifyRepository verifyRepository)
        {
            try
            {
                return await verifyRepository.GetDocumentTypeNameByIdAsync(docTypeId) ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting document type name for DocTypeId {DocTypeId}", docTypeId);
                return string.Empty;
            }
        }
    }
}
