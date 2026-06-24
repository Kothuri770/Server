using System;

namespace Server.Models
{
    public class SftpConfiguration
    {
        public int Id { get; set; }
        public int AppId { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RemotePath { get; set; } = string.Empty;
        public string? BackupPath { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime? LastChecked { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}
