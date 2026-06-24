namespace Server.Models
{
    public class EmailConfiguration
    {
        public int Id { get; set; }
        public string EmailId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty; // In production, encrypt this!
        public int AppId { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string ImapServer { get; set; } = "imap.gmail.com";
        public int ImapPort { get; set; } = 993;
        public bool IsEnabled { get; set; }
        public DateTime? LastChecked { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
    }
}
