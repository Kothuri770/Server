// Server/Models/DTOs.cs

using System.ComponentModel.DataAnnotations;

namespace Server.Models;

// ================= AUTHENTICATION =================
public class LoginRequest
{
    [Required] public string UserName { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public string LicenseStatus { get; set; } = string.Empty;
    public int? LicenseDaysRemaining { get; set; }
    public string? LicenseMessage { get; set; }
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CreateUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string UserType { get; set; } = "user";
    public string ViewLimit { get; set; } = "All";
    public bool IsEnabled { get; set; } = true;
}

public class UpdateUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string UserType { get; set; } = "user";
    public string ViewLimit { get; set; } = "All";
    public bool IsEnabled { get; set; } = true;
}


    public class ObjectTypeDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int? OcrConnectorId { get; set; }
        public string OcrMode { get; set; } = "Manual";
        public string SeparationMode { get; set; } = "Global";
    }

    // Represents the global definition of a property (Table: Property)
    public class PropertyDto
    {
        public int Id { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public string PropertyDesc { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public int? PropertyLength { get; set; }
        public int? LookupId { get; set; }
        public int PropertyOrder { get; set; } = 0;
    }

    // Represents the mapping between an App and a Property (Table: DocumentClassDetail)
    public class DocTypePropertyDto
    {
        public int Id { get; set; }
        public int DocTypeId { get; set; }
        public int PropertyId { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public string PropertyDesc { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public int PropertyOrder { get; set; }
        public int Length { get; set; }
        public bool IsBatchProperty { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int? ZoneId { get; set; }
        public int? LookupId { get; set; }
        public string? UIDesignId { get; set; }
        public bool IsLookup { get; set; }
        public string? DefaultValue { get; set; }
        public bool IsPreIndex { get; set; }
    }

    public class FormIdSettings
    {
        public int DocTypeId { get; set; }
        public int PropertyId { get; set; }
        public bool IsEnabled { get; set; }
    }

    public class IdentificationRuleDto
    {
        public int ID { get; set; }
        public string RuleType { get; set; } = string.Empty; // Maps to IDType
        public string Method { get; set; } = string.Empty;   // Maps to IDMethod
        public string Value { get; set; } = string.Empty;    // Maps to IDValue
        public int DocTypeId { get; set; }   // Maps to ParentObjectId
        public int? ZoneId { get; set; }
        public bool DiscardPage { get; set; }
    }

    public class LookupConfigDto
    {
        public int Id { get; set; }
        public string LookupString { get; set; } = string.Empty;
        public string LookupType { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
    }

    public class DmsProviderDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime ModifiedOn { get; set; }
    }

    public class DmsOutputFormatDto
    {
        public int Id { get; set; }
        public string FormatCode { get; set; } = string.Empty;
        public string FormatName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime ModifiedOn { get; set; }
    }

    public class DmsConfigDto
    {
        public int ConfigId { get; set; }
        public int DocTypeID { get; set; }
        public int ProviderId { get; set; }
        public int? OutputFormatId { get; set; }
        public string DMSClassName { get; set; } = string.Empty;
        public string DMSCabinetName { get; set; } = string.Empty;
        public string ReleaseFolder { get; set; } = string.Empty;
        public string NameExpression { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? AdditionalConfig { get; set; }
        
        // Navigation properties for display
        public string? ProviderName { get; set; }
        public string? ProviderDisplayName { get; set; }
        public string? OutputFormatCode { get; set; }
        public string? OutputFormatName { get; set; }
    }
    public class ZoneDto
    {
        public int ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? LeftX { get; set; }
        public int? TopY { get; set; }
        public int? RightX { get; set; }
        public int? BottomY { get; set; }
        public int? DisplayedWidth { get; set; }
        public int? DisplayedHeight { get; set; }
        public int DocTypeID { get; set; }
        public int PageNo { get; set; }
        public string Type { get; set; } = string.Empty;
        public int? StartPosition { get; set; }
        public int? Length { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class ZoneExtractionRequest
    {
        public string ImageBase64 { get; set; } = string.Empty;
        public int JsLeft { get; set; }
        public int JsTop { get; set; }
        public int JsRight { get; set; }
        public int JsBottom { get; set; }
        public int DisplayedWidth { get; set; }
        public int DisplayedHeight { get; set; }
        public string PropertyType { get; set; } = "string";
    }
    public class CharacterZoneDto
    {
        public int ID { get; set; }
        public int ZoneId { get; set; }
        public int Order { get; set; }
        public int? LeftX { get; set; }
        public int? TopY { get; set; }
        public int? RightX { get; set; }
        public int? BottomY { get; set; }
        public string DataType { get; set; } = "String";
        public string OCRType { get; set; } = "O";
    }

    public class TemplateInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime Created { get; set; }
    }


    public class ConfigurationDtos
{
        public int ID { get; set; }
        public string ConfigName { get; set; } = "";
        public string ConfigValue { get; set; } = "";
    }

    public class ConfigurationSettings
    {
        public string BatchFolder { get; set; } = "";
        public string DocumentFolder { get; set; } = "";
        public string TempFolder { get; set; } = "";
        public string SamplesFolder { get; set; } = "";
        public string TemplatesFolder { get; set; } = "";
        public string BatchPrefix { get; set; } = "BATCH";
        public string LocationName { get; set; } = "";
        public int BatchType { get; set; } = 0;
        public int ScanProfile { get; set; } = 0;
        public int MaxBatchesDisplayed { get; set; } = 100;
        public bool ShowBatchWindow { get; set; } = false;
        public string OcrLanguage { get; set; } = "ENGLISH";
        public string TessDataPath { get; set; } = "./Tessract";
        public string SeparationMode { get; set; } = "Manual"; // Manual or Auto
    }

    public class PathValidationRequest
    {
        public string Path { get; set; } = "";
        public bool CreateIfNotExists { get; set; } = true;
    }

    public class PathValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = "";
        public string FullPath { get; set; } = "";
    }
    public class ObjectRelationDto
    {
        public int ParentObjectId { get; set; }
        public int ChildObjectId { get; set; }
    }

    public class DocumentSampleDto
    {
        public int DocTypeID { get; set; }
        public string SampleFile { get; set; } = string.Empty;
    }

    public class PropertyZoneMappingRequest
    {
        public int PropertyId { get; set; }
        public int? ZoneId { get; set; }
        public int DocTypeId { get; set; }
    }

    public class BatchLockInfo
    {
        public int BatchId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public DateTime LockAcquired { get; set; }
        public DateTime ExpirationTime { get; set; }
        public string Status { get; set; } = "Active";
    }

    public class AcquireLockRequest
    {
        public int BatchId { get; set; }
        public string SessionId { get; set; } = string.Empty;
    }

    public class AcquireLockResponse
    {
        public string Result { get; set; } = string.Empty;
        public DateTime? ExpirationTime { get; set; }
        public string? CurrentLockHolder { get; set; }
        public DateTime? LockExpiration { get; set; }
    }

    public class ReleaseLockRequest
    {
        public int BatchId { get; set; }
    }

    // User Roles and Permissions DTOs
    public class UserRoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class PermissionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class UserPermissionsDto
    {
        public int UserId { get; set; }
        public List<int> RoleIds { get; set; } = new();
        public List<int> PermissionIds { get; set; } = new();
    }



    public class UserCreationRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserType { get; set; } = "user";
        public string ViewLimit { get; set; } = "All";
        public bool IsEnabled { get; set; } = true;
        public List<int> RoleIds { get; set; } = new();
    }

    public class RolePermissionAssignmentRequest
    {
        public int RoleId { get; set; }
        public List<int> PermissionIds { get; set; } = new();
    }
public class PropertyMappingDto
{
    public int Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public int DoctypeId { get; set; }
    public string? JsonProperty { get; set; }
}

public class PropertyMappingRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public int DoctypeId { get; set; }
    public string JsonProperty { get; set; } = string.Empty;
}

public class MappingRow
{
    public string SourceName { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
}




