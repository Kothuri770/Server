using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    // FileNet-specific configuration models
    public class FileNetConnectionConfig
    {
        [Required]
        public string CeUri { get; set; } = "http://localhost:9080/wsi/FNCEWS40MTOM";
        
        [Required]
        public string Username { get; set; } = "p8admin";
        
        [Required]
        public string Password { get; set; } = "";
        
        [Required]
        public string ObjectStoreName { get; set; } = "TARGET";
        
        public string DomainName { get; set; } = "";
        
        public int TimeoutSeconds { get; set; } = 30;
        
        public bool UseSSL { get; set; } = false;
    }

    public class FileNetDocumentConfig
    {
        public string DocumentClass { get; set; } = "Document";
        
        public string FolderPath { get; set; } = "/";
        
        public string CabinetName { get; set; } = "DefaultCabinet";
        
        public bool AutoClassify { get; set; } = false;
        
        public CheckinType CheckinType { get; set; } = CheckinType.MAJOR_VERSION;
        
        public RefreshMode RefreshMode { get; set; } = RefreshMode.REFRESH;
    }

    public class FileNetPropertyMapping
    {
        public string SourcePropertyName { get; set; } = "";
        
        public string TargetPropertyName { get; set; } = "";
        
        public string DataType { get; set; } = "STRING";
        
        public bool Required { get; set; } = false;
        
        public string DefaultValue { get; set; } = "";
    }

    // Enums matching FileNet API
    public enum CheckinType
    {
        MAJOR_VERSION,
        MINOR_VERSION
    }

    public enum RefreshMode
    {
        REFRESH,
        NO_REFRESH
    }

    public enum AutoClassify
    {
        DO_NOT_AUTO_CLASSIFY,
        AUTO_CLASSIFY
    }

    public enum ContentTransfer
    {
        EMBEDDED,
        EXTERNAL
    }

    // Response models
    public class FileNetUploadResult
    {
        public bool Success { get; set; }
        
        public string DocumentId { get; set; } = "";
        
        public string VersionSeriesId { get; set; } = "";
        
        public string ErrorMessage { get; set; } = "";
        
        public DateTime UploadTimestamp { get; set; } = DateTime.UtcNow;
        
        public Dictionary<string, string> PropertiesSet { get; set; } = new();
    }

    public class FileNetTestConnectionResult
    {
        public bool Connected { get; set; }
        
        public string ObjectStoreName { get; set; } = "";
        
        public int DocumentCount { get; set; }
        
        public string ErrorMessage { get; set; } = "";
        
        public DateTime TestTimestamp { get; set; } = DateTime.UtcNow;
    }
}