using System;

namespace Server.Models
{
    public class BatchUpdate
    {
        public int BatchId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Step { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}