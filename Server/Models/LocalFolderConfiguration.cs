namespace Server.Models
{
    public class LocalFolderConfiguration
    {
        public int Id { get; set; }
        public int AppId { get; set; }
        public string PickImagesPath { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime? LastChecked { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
    }
}
