namespace Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // ==================== BATCH DTOs ====================
    public class BatchDto
    {
        public int ID { get; set; }
        public string BatchName { get; set; } = string.Empty;
        public int BatchTypeId { get; set; }
        public DateTime CreatedOn { get; set; }
        public string BatchStatus { get; set; } = "A";
        public int StepID { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    public class MoveStepRequest
    {
        public long BatchId { get; set; }
        public int StepId { get; set; }
        public string Username { get; set; } = string.Empty;
    }

    public class PurgeConfigDto
    {
        public bool IsEnabled { get; set; }
        public string StartTime { get; set; } = "00:00";
        public int DurationHours { get; set; } = 1;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int DeletionMode { get; set; } // 0: Local Folder only, 1: DB Only, 2: Both
        public int ApplicationId { get; set; } = 0; // 0 = All
        public int StepId { get; set; } = 0; // 0 = All
    }

    public class PurgeResponseDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UpdateBatchStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public class BatchSubmitModel
    {
        public long BatchId { get; set; }
        public string Username { get; set; } = string.Empty;
        public int AppId { get; set; }
        public List<DocTypeSubmitModel> DocumentTypes { get; set; } = new();
    }

    public class DocTypeSubmitModel
    {
        public string DocName { get; set; } = string.Empty;
        public int DocId { get; set; }
        public int NDocId { get; set; }
        public List<string> Images { get; set; } = new();
    }

    public class SaveBatchModel
    {
        public int BatchId { get; set; }
        public List<DocTypeInfo> DocumentTypes { get; set; } = new();
        public List<ImageInfo> Images { get; set; } = new();
        public string Username { get; set; } = string.Empty;
    }

    public class DocTypeInfo
    {
        public int DocId { get; set; }
        public string DocName { get; set; } = string.Empty;
    }

    public class ImageInfo
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int DocumentId { get; set; }
        public int DocTypeId { get; set; }
        public int DocPage { get; set; }
        public int PageNo { get; set; }
        public string PageName { get; set; } = string.Empty;
        public string DisplayId { get; set; } = string.Empty;
    }

    public class BatchInfoDto
    {
        public int ID { get; set; }
        public string BatchName { get; set; } = string.Empty;
        public int BatchTypeId { get; set; }
        public DateTime CreatedOn { get; set; }
        public string BatchStatus { get; set; } = "A";
        public int StepId { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    public class BatchDetailDto
    {
        public int BatchId { get; set; }
        public int PageNo { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int DocPage { get; set; }
        public string Status { get; set; } = "A";
        public int DocTypeId { get; set; }
        public string PageName { get; set; } = string.Empty;
        public string DocName { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public DateTime DocCreatedOn { get; set; }
    }

    public class BatchImageWithDocNameDto
    {
        public int ID { get; set; }
        public int PageNo { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int DocPage { get; set; }
        public string Status { get; set; } = "A";
        public int DocTypeId { get; set; }
        public string PageName { get; set; } = string.Empty;
        public string DocName { get; set; } = string.Empty; // Document name from documentwithtypename view
        public string DocTypeName { get; set; } = string.Empty; // Document type name
        public int? DocumentId { get; set; } // The logical document handle (MIN(ID))
    }

    public class BatchDetailJsonDto
    {
        public BatchInfoJsonDto Batch { get; set; } = new();
        public List<DocumentJsonDto> Documents { get; set; } = new();
    }

    public class Batch
    {
        public int ID { get; set; }
        public string BatchName { get; set; } = string.Empty;
        public int BatchTypeId { get; set; }
        public DateTime CreatedOn { get; set; }
        public string BatchStatus { get; set; } = "A";
        public int StepId { get; set; }
        public string Name { get; set; } = string.Empty; // From ObjectTypes table
        public DateTime? LockedOn { get; set; }
        public string? LockedBy { get; set; }
    }

    public class BatchInfoJsonDto
    {
        public string BatchType { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public int TotalDocuments { get; set; }
        public int TotalPages { get; set; }
        public string Source { get; set; } = string.Empty;
        public Dictionary<string, string> Properties { get; set; } = new();
        public List<DocumentJsonDto> Documents { get; set; } = new();
    }

    public class DocumentJsonDto
    {
        public string DocumentType { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public List<PageJsonDto> Pages { get; set; } = new();
    }

    public class PageJsonDto
    {
        public string PageId { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public string batchfileName { get; set; } = string.Empty;
        public string originalFileName { get; set; } = string.Empty;
        public int fileSize { get; set; }
        public string mimeType { get; set; } = string.Empty;
        public Dictionary<string, string> Properties { get; set; } = new();
        public StorageInfo Storage { get; set; } = new();
    }

    public class StorageInfo
    {
        public string ImagePath { get; set; } = string.Empty;
    }

    public class CreateBatchRequest
    {
        public string BatchName { get; set; } = string.Empty;
        public int BatchTypeId { get; set; }
    }

    // ==================== DOCUMENT DTOs ====================
    [Obsolete("Document table is merged into BatchDetail. Use BatchDetailDto.")]
    public class DocumentDto
    {
        public int ID { get; set; }
        public int BatchID { get; set; }
        public string DocName { get; set; } = string.Empty;
        public int DocTypeId { get; set; }
        public string Status { get; set; } = "A";
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public string Format { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
    }

    public class PageDto
    {
        public int ID { get; set; }
        public int BatchID { get; set; }
        public int PageNo { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int DocPage { get; set; }
        public string DocPageType { get; set; } = "Page";
    }

    // ==================== APPLICATION DTOs ====================
    public class ApplicationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SeparationMode { get; set; } = string.Empty;
    }

    public class DocTypeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int NDocId { get; set; }
    }

    public class ApplicationNameDto
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string SeparationMode { get; set; } = string.Empty;
    }

    public class DocumentNameDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int NDocId { get; set; }
    }

    public class GetDocumentRequest
    {
        public int AppId { get; set; }
        public int BatchId { get; set; }
        public string UserName { get; set; } = string.Empty;
    }

    // ==================== MONITORING DTOs ====================
    public class JobMonitorDto
    {
        public string id { get; set; } = string.Empty;
        public string batchname { get; set; } = string.Empty;
        public string batchtype { get; set; } = string.Empty;
        public string task { get; set; } = string.Empty;
        public string batchstatus { get; set; } = string.Empty;
        public string createdOn { get; set; } = string.Empty;
        public string documentcount { get; set; } = "0";
        public string pagecount { get; set; } = "0";
        public string username { get; set; } = string.Empty;
    }

    public class PagedJobMonitorDto
    {
        public IEnumerable<JobMonitorDto> Items { get; set; } = new List<JobMonitorDto>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }

    // ==================== BATCH LOGGING DTOs ====================
    public class BatchLogEntry
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // SUCCESS, ERROR, IN_PROGRESS
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? Details { get; set; }
    }

    public class BatchLogSummary
    {
        public int BatchId { get; set; }
        public string BatchName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string OverallStatus { get; set; } = string.Empty; // COMPLETED, ERROR, IN_PROGRESS
        public List<BatchLogEntry> LogEntries { get; set; } = new List<BatchLogEntry>();
    }

    public class UpdateBatchLogStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class BatchTaskLog
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public string TaskType { get; set; } = string.Empty; // SCAN, UPLOAD, VERIFY, OCR, RELEASE, etc.
        public string TaskDescription { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // SUCCESS, FAILED, IN_PROGRESS
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Details { get; set; }
        public string? UserId { get; set; }
    }

    public class UserDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserType { get; set; } = "user";
        public DateTime CreatedOn { get; set; }
        public string ViewLimit { get; set; } = "All";
        public bool IsEnabled { get; set; } = true;
        public List<int> RoleIds { get; set; } = new();
    }

    public class UserAssignmentRequest
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public List<int> RoleIds { get; set; } = new();
    }

    public class ColumnTypeDto
    {
        public string column_name { get; set; } = string.Empty;
        public string datatype { get; set; } = string.Empty;
        public string dtid { get; set; } = string.Empty;
    }

    public class FilterInputDto
    {
        public string username { get; set; } = string.Empty;
        public string usertype { get; set; } = string.Empty;
        public FilterDataDto filterdata { get; set; } = new();
    }
    public class ProcessingStatisticsDto
    {
        public int TotalBatches { get; set; }
        public int ActiveBatches { get; set; }
        public int CompletedBatches { get; set; }
        public int HeldBatches { get; set; }
        public int UsersProcessed { get; set; }
        public double AvgProcessingTime { get; set; }
    }

    public class MonitoringDashboardDto
    {
        public ProcessingStatisticsDto TodayStats { get; set; } = new();
        public ProcessingStatisticsDto WeekStats { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
    public class FilterDataDto
    {
        public List<FilterCriteriaDto> filter { get; set; } = new();
    }

    public class FilterCriteriaDto
    {
        public string column { get; set; } = string.Empty;
        public string condition { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
        public string start { get; set; } = string.Empty;
        public string date { get; set; } = string.Empty;
        public string end { get; set; } = string.Empty;
    }

    // ==================== UPGRADE LEGACY DTOs ====================
    public class UpgradeDto
    {
        public string batchname { get; set; } = string.Empty;
        public int batchid { get; set; }
        public string username { get; set; } = string.Empty;
        public int appname { get; set; }
        public int currentdocid { get; set; }
        public int pagecount { get; set; }
        public List<DocTNDto> docTN { get; set; } = new();
        public List<DocsDto> filename { get; set; } = new();
    }

    public class DocTNDto
    {
        public string docname { get; set; } = string.Empty;
        public int? pagecount { get; set; }
        public int ndocid { get; set; }
        public List<DocsDto> files { get; set; } = new();
    }

    public class DocsDto
    {
        public string filename { get; set; } = string.Empty;
        public byte[] filebytes { get; set; } = Array.Empty<byte>();
    }

    public class PageModel
    {
        public int PageId { get; set; }
        public int DocId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string OriginalFilename { get; set; } = string.Empty;
        public string Thumbnail { get; set; } = string.Empty;
        public double Rotation { get; set; }
        public int DocPage { get; set; }
        public string PageType { get; set; } = "Page";
        public string DocName { get; set; } = string.Empty;
        public string Base64Image { get; set; } = string.Empty;
        public string ContentType { get; set; } = "image/jpeg";
        public string Format { get; set; } = string.Empty;
    }

    // ==================== INDEXING DTOs ====================
    public class IndexFieldDto
    {
        public string ColumnId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = "String";
        public bool Required { get; set; }
        public bool IsEditable { get; set; } = true;
        public bool IsLookup { get; set; }
        public int? ZoneId { get; set; }
        public int Length { get; set; } = 100;
    }

    public class SaveIndexDto
    {
        public int BatchId { get; set; }
        public int DocId { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new();
    }

    public class SaveIndexDataDto
    {
        public int BatchId { get; set; }
        public int DocId { get; set; }
        public string Username { get; set; } = string.Empty;
        public List<FieldValueDto> Fields { get; set; } = new();
    }

    public class BulkSaveIndexDto
    {
        public int BatchId { get; set; }
        public string Username { get; set; } = string.Empty;
        public List<SaveIndexDataDto> DocumentSaves { get; set; } = new();
        public List<FieldValueDto> BatchFields { get; set; } = new();
    }

    public class BatchVerificationContextDto
    {
        public int BatchId { get; set; }
        public string BatchName { get; set; } = string.Empty;
        public int BatchTypeId { get; set; }
        public List<DocumentModel> Documents { get; set; } = new();
        public List<PageModel> Pages { get; set; } = new();
        public List<IndexFieldDto> BatchIndexFields { get; set; } = new();
        public Dictionary<string, string> BatchIndexValues { get; set; } = new();
        public Dictionary<int, List<IndexFieldDto>> DocTypeFields { get; set; } = new();
        public Dictionary<int, Dictionary<string, string>> DocumentIndexValues { get; set; } = new();
    }

    public class FieldValueDto
    {
        public string ColumnId { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    // ==================== OCR DTOs ====================
    public class OcrResult
    {
        public int ZoneId { get; set; }
        public string OCRValue { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public class OcrExtractionResult
    {
        public string Text { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public class UpdateStepRequest
    {
        public int StepId { get; set; }
    }

    public class Step
    {
        public int ID { get; set; }
        public string StepName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int StepOrder { get; set; }
    }

    public class AzureDocIntelResult
    {
        public int DocIntelId { get; set; }
        public int DocId { get; set; }
        public string AnalysisResult { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class AzureDocIntelAnalysis
    {
        public AzureDocIntelMetadata Metadata { get; set; } = new();
        public List<AzureDocIntelTable> Tables { get; set; } = new();
        public List<AzureDocIntelKeyValuePair> KeyValuePairs { get; set; } = new();
        public List<AzureDocIntelOcrData> OcrData { get; set; } = new();
    }

    public class AzureDocIntelMetadata
    {
        public string InputFile { get; set; } = string.Empty;
        public DateTime AnalyzedAt { get; set; }
        public int TotalPages { get; set; }
        public int TableCount { get; set; }
        public int KvpCount { get; set; }
    }

    public class AzureDocIntelTable
    {
        public int TableIndex { get; set; }
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public List<AzureDocIntelTableCell> Cells { get; set; } = new();
    }

    public class AzureDocIntelTableCell
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int SpanCount { get; set; }
    }

    public class AzureDocIntelKeyValuePair
    {
        public int PairIndex { get; set; }
        public AzureDocIntelTextElement Key { get; set; } = new();
        public AzureDocIntelTextElement? Value { get; set; }
        public double Confidence { get; set; }
    }

    public class AzureDocIntelTextElement
    {
        public string Text { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public List<AzureDocIntelRegion> Regions { get; set; } = new();
    }

    public class AzureDocIntelRegion
    {
        public int Page { get; set; }
        public double[]? Polygon { get; set; }
    }

    public class AzureDocIntelOcrData
    {
        public int PageNumber { get; set; }
        public double Angle { get; set; }
        public AzureDocIntelDimensions Dimensions { get; set; } = new();
        public List<AzureDocIntelLine> Lines { get; set; } = new();
        public List<AzureDocIntelWord> Words { get; set; } = new();
    }

    public class AzureDocIntelDimensions
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public string Unit { get; set; } = string.Empty;
    }

    public class AzureDocIntelLine
    {
        public string Text { get; set; } = string.Empty;
        public double[]? Polygon { get; set; }
    }

    public class AzureDocIntelWord
    {
        public string Text { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public double[]? Polygon { get; set; }
    }

    // ==================== OCR Connector DTOs ====================
    public class OcrProviderDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime ModifiedOn { get; set; }
    }

    public class OcrConnectorDto
    {
        public int Id { get; set; }
        public int ProviderId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public Dictionary<string, object>? ConfigData { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime ModifiedOn { get; set; }

        // Navigation property
        public OcrProviderDto? Provider { get; set; }
    }

    public class OcrConfigurationDto
    {
        public int Id { get; set; }
        public string ConfigName { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public DateTime ModifiedOn { get; set; }
    }

    public class UpdateOcrModeRequest
    {
        public string OcrMode { get; set; } = "Manual"; // "Manual" or "Automatic"
    }

    public class UpdateStepStatusRequest
    {
        public string SeparationMode { get; set; } = string.Empty;
    }

    public class OcrConnectorConfig
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public Dictionary<string, object>? ConfigData { get; set; }
        public bool IsActive { get; set; }
        public bool IsDefault { get; set; }
    }

    // ==================== DOCUMENT MODEL ====================
    public class DocumentModel
    {
        public int DocId { get; set; }
        public string DocName { get; set; } = string.Empty;
        public int DocTypeId { get; set; }
        public string DocTypeName { get; set; } = string.Empty;
        public int Status { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string InternalName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
    }

    public class DocumentInfoDto
    {
        public int DocId { get; set; }
        public string DocName { get; set; } = string.Empty;
        public int DocTypeId { get; set; }
        public int BatchId { get; set; }
    }

    public class ErrorLogDto
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Exception { get; set; } = string.Empty;
    }

    public class BatchReportDto
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string BatchName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public string Status { get; set; } = string.Empty;
        public int DocTypeCount { get; set; }
        public int PageCount { get; set; }
    }

    public class LogFile
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public long FileSize { get; set; }
    }

    public class ImageUploadResult
    {
        public int Id { get; set; }
        public string? ImageId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int DocId { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public string? ThumbnailBase64Data { get; set; }
        public bool IsPdf { get; set; }
        public List<string> PageUrls { get; set; } = new();
        public string PageName { get; set; } = string.Empty;
    }

    public class TemplateUploadModel
    {
        public Microsoft.AspNetCore.Http.IFormFile? File { get; set; }
    }

    // ==================== ZONE CONFIG DTOs ====================
    public class TemplateComparisonModel
    {
        public string ExistingTemplate { get; set; } = string.Empty;
        public Microsoft.AspNetCore.Http.IFormFile? File { get; set; }
    }

    public class TemplateComparisonResult
    {
        public bool Success { get; set; }
        public double Similarity { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class ExtractZonesRequest
    {
        public int DocumentTypeId { get; set; }
        public string ImageBase64 { get; set; } = string.Empty;
        public int DisplayedWidth { get; set; }
        public int DisplayedHeight { get; set; }
    }

    public class ZoneConfig
    {
        public int ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ZoneName => Name; // Alias for compatibility
        public string ZoneType => Type; // Alias for compatibility
        public int? LeftX { get; set; }
        public int? TopY { get; set; }
        public int? RightX { get; set; }
        public int? BottomY { get; set; }
        public int? X => LeftX; // Alias for compatibility
        public int? Y => TopY; // Alias for compatibility
        public int? Width => RightX.HasValue && LeftX.HasValue ? RightX.Value - LeftX.Value : (int?)null; // Alias for compatibility
        public int? Height => BottomY.HasValue && TopY.HasValue ? BottomY.Value - TopY.Value : (int?)null; // Alias for compatibility
        public int DocTypeID { get; set; }
        public int PageNo { get; set; } = 1;
        public string Type { get; set; } = "Text";
        public int? StartPosition { get; set; }
        public int? Length { get; set; }
        public bool Required { get; set; } = false; // For compatibility
        public int? MaxLength { get; set; } // For compatibility
        public string RegexPattern { get; set; } = string.Empty; // For compatibility
        public string ErrorMessage { get; set; } = string.Empty; // For compatibility
        public bool Enabled { get; set; } = true; // For compatibility
        public int? DisplayedWidth { get; set; }
        public int? DisplayedHeight { get; set; }
    }

    public class CreateZoneRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; } = string.Empty;
        public int LeftX { get; set; }
        public int TopY { get; set; }
        public int RightX { get; set; }
        public int BottomY { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int DocTypeID { get; set; }
        public int PageNo { get; set; } = 1;
        [System.ComponentModel.DataAnnotations.Required]
        public string Type { get; set; } = "Text";
        public int? StartPosition { get; set; }
        public int? Length { get; set; }
        public int? DisplayedWidth { get; set; }
        public int? DisplayedHeight { get; set; }
    }

    public class UpdateZoneRequest
    {
        public int ID { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; } = string.Empty;
        public int LeftX { get; set; }
        public int TopY { get; set; }
        public int RightX { get; set; }
        public int BottomY { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public int DocTypeID { get; set; }
        public int PageNo { get; set; } = 1;
        [System.ComponentModel.DataAnnotations.Required]
        public string Type { get; set; } = "Text";
        public int? StartPosition { get; set; }
        public int? Length { get; set; }
        public int? DisplayedWidth { get; set; }
        public int? DisplayedHeight { get; set; }
    }

    public class ZoneExtractionResult
    {
        public string ZoneName { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty;
        public string ZoneType { get; set; } = string.Empty;
        public Rectangle ZoneCoordinates { get; set; } = new Rectangle();
        public bool Success { get; set; } = false;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class Rectangle
    {
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int Width { get; set; } = 0;
        public int Height { get; set; } = 0;
    }
}
