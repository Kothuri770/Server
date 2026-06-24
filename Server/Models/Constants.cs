namespace Server.Models
{
    public static class Constants
    {
        /// <summary>
        /// The default path for batch folders if not configured in the database.
        /// Resolved SonarQube issue S1075.
        /// </summary>
        public const string DefaultBatchFolder = @"C:\TrueCapture\ICBatches";
        
        /// <summary>
        /// The default path for templates if not configured in the database.
        /// </summary>
        public const string DefaultTemplatesFolder = @"C:\TrueCapture\Templates";
    }
}
