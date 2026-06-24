using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class LookupTable
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string TableName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public string? CreatedBy { get; set; }
        
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        
        public string? UpdatedBy { get; set; }
        
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual List<LookupTableValue> LookupTableValues { get; set; } = new();
    }

    public class LookupTableValue
    {
        public int Id { get; set; }
        
        [Required]
        public int LookupTableId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string DisplayValue { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string ValueCode { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public int SortOrder { get; set; } = 0;
        
        public bool IsActive { get; set; } = true;
        
        public string? CreatedBy { get; set; }
        
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        
        public string? UpdatedBy { get; set; }
        
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual LookupTable? LookupTable { get; set; }
    }

    public class PropertyValidationRule
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string ValidationType { get; set; } = string.Empty; // 'Regex', 'Lookup', 'Range', etc.
        
        public string? ValidationRule { get; set; } // For regex: pattern, for lookup: table name, for range: min/max values
        
        public string? ErrorMessage { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public string? CreatedBy { get; set; }
        
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        
        public string? UpdatedBy { get; set; }
        
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
    }

    public class PropertyLookupMapping
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        public int LookupTableId { get; set; }
        
        public bool IsRequired { get; set; } = false;
        
        public string? DefaultValue { get; set; }
        
        public string? CreatedBy { get; set; }
        
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        
        public string? UpdatedBy { get; set; }
        
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual LookupTable? LookupTable { get; set; }
    }

    // DTOs for API responses
    public class LookupTableDto
    {
        public int Id { get; set; }
        
        public string TableName { get; set; } = string.Empty;
        
        public string DisplayName { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public bool IsActive { get; set; }
        
        public List<LookupTableValueDto> Values { get; set; } = new();
    }

    public class LookupTableValueDto
    {
        public int Id { get; set; }
        
        public int LookupTableId { get; set; }
        
        public string DisplayValue { get; set; } = string.Empty;
        
        public string ValueCode { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public int SortOrder { get; set; }
        
        public bool IsActive { get; set; }
    }

    public class PropertyValidationRuleDto
    {
        public int Id { get; set; }
        
        public string PropertyName { get; set; } = string.Empty;
        
        public string DisplayName { get; set; } = string.Empty;
        
        public string ValidationType { get; set; } = string.Empty;
        
        public string? ValidationRule { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public bool IsActive { get; set; }
    }

    public class PropertyLookupMappingDto
    {
        public int Id { get; set; }
        
        public string PropertyName { get; set; } = string.Empty;
        
        public int LookupTableId { get; set; }
        
        public string LookupTableName { get; set; } = string.Empty;
        
        public string LookupTableDisplayName { get; set; } = string.Empty;
        
        public bool IsRequired { get; set; }
        
        public string? DefaultValue { get; set; }
    }

    // Request models for API
    public class CreateLookupTableRequest
    {
        [Required]
        [StringLength(100)]
        public string TableName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public bool IsActive { get; set; } = true;
    }

    public class UpdateLookupTableRequest
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string TableName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public bool IsActive { get; set; }
    }

    public class CreateLookupTableValueRequest
    {
        [Required]
        public int LookupTableId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string DisplayValue { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string ValueCode { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public int SortOrder { get; set; } = 0;
        
        public bool IsActive { get; set; } = true;
    }

    public class UpdateLookupTableValueRequest
    {
        public int Id { get; set; }
        
        [Required]
        public int LookupTableId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string DisplayValue { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string ValueCode { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        public int SortOrder { get; set; }
        
        public bool IsActive { get; set; }
    }

    public class CreatePropertyValidationRuleRequest
    {
        [Required]
        [StringLength(100)]
        public string PropertyName { get; set; } = string.Empty;
        
        public string DisplayName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string ValidationType { get; set; } = string.Empty;
        
        public string? ValidationRule { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public bool IsActive { get; set; } = true;
    }

    public class UpdatePropertyValidationRuleRequest
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string ValidationType { get; set; } = string.Empty;
        
        public string? ValidationRule { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public bool IsActive { get; set; }
    }

    public class CreatePropertyLookupMappingRequest
    {
        [Required]
        [StringLength(100)]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        public int LookupTableId { get; set; }
        
        public bool IsRequired { get; set; } = false;
        
        public string? DefaultValue { get; set; }
    }

    public class UpdatePropertyLookupMappingRequest
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        public int LookupTableId { get; set; }
        
        public bool IsRequired { get; set; }
        
        public string? DefaultValue { get; set; }
    }

    // Response models
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class LookupValueResponse
    {
        public int Id { get; set; }
        public string DisplayValue { get; set; } = string.Empty;
        public string ValueCode { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }

    // ================= DATABASE LOOKUPS =================

    public enum DatabaseType
    {
        SqlServer = 0,
        MySql = 1,
        Oracle = 2,
        PostgreSQL = 3
    }

    public class DatabaseConnection
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string ConnectionName { get; set; } = string.Empty;
        
        [Required]
        public DatabaseType DbType { get; set; }
        
        [Required]
        public string ConnectionString { get; set; } = string.Empty;
        
        public bool IsActive { get; set; } = true;
        
        public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string? UpdatedBy { get; set; }
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
    }

    public class DatabaseLookupMapping
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        public int ConnectionId { get; set; }
        
        [Required]
        public string SqlQuery { get; set; } = string.Empty;
        
        public string ColumnMappingsJson { get; set; } = "[]"; // Serialized List of mappings
        
        public bool IsActive { get; set; } = true;
        
        public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string? UpdatedBy { get; set; }
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual DatabaseConnection? Connection { get; set; }
    }

    public class DatabaseConnectionDto
    {
        public int Id { get; set; }
        public string ConnectionName { get; set; } = string.Empty;
        public DatabaseType DbType { get; set; }
        public string ConnectionString { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedOn { get; set; }
    }

    public class DatabaseLookupMappingDto
    {
        public int Id { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public int ConnectionId { get; set; }
        public string ConnectionName { get; set; } = string.Empty;
        public string SqlQuery { get; set; } = string.Empty;
        public List<DatabaseColumnMappingDto> ColumnMappings { get; set; } = new();
        public bool IsActive { get; set; }
    }

    public class DatabaseColumnMappingDto
    {
        public string DbColumn { get; set; } = string.Empty;
        public string AppProperty { get; set; } = string.Empty;
    }

    public class CreateDatabaseConnectionRequest
    {
        [Required] public string ConnectionName { get; set; } = string.Empty;
        [Required] public DatabaseType DbType { get; set; }
        [Required] public string ConnectionString { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class UpdateDatabaseConnectionRequest
    {
        public int Id { get; set; }
        [Required] public string ConnectionName { get; set; } = string.Empty;
        [Required] public DatabaseType DbType { get; set; }
        [Required] public string ConnectionString { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class CreateDatabaseLookupMappingRequest
    {
        [Required] public string PropertyName { get; set; } = string.Empty;
        [Required] public int ConnectionId { get; set; }
        [Required] public string SqlQuery { get; set; } = string.Empty;
        public List<DatabaseColumnMappingDto> ColumnMappings { get; set; } = new();
        public bool IsActive { get; set; } = true;
    }

    public class UpdateDatabaseLookupMappingRequest
    {
        public int Id { get; set; }
        [Required] public string PropertyName { get; set; } = string.Empty;
        [Required] public int ConnectionId { get; set; }
        [Required] public string SqlQuery { get; set; } = string.Empty;
        public List<DatabaseColumnMappingDto> ColumnMappings { get; set; } = new();
        public bool IsActive { get; set; } = true;
    }

    public class DatabaseLookupSearchRequest
    {
        [Required] public string PropertyName { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    public class DatabaseLookupResult
    {
        public List<string> Columns { get; set; } = new();
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
        public string? Message { get; set; }
        public bool Success { get; set; } = true;
    }
}
