namespace AutoCopy.Models
{
    public class AppConfig
    {
        public string? SourceFolder { get; set; }
        public string? DestinationFolder { get; set; }
        public string? FileListPath { get; set; }
        public bool IgnoreExtension { get; set; }
        public bool CaseInsensitive { get; set; }
        public int DuplicateHandling { get; set; }
        
        // v1.1.0 new settings
        public int ParallelThreads { get; set; } = 4;
        public bool EnableParallel { get; set; } = true;
        public bool EnableFilters { get; set; } = false;
        public string ExtensionFilter { get; set; } = "";
        public string MinSizeMB { get; set; } = "0";
        public string MaxSizeMB { get; set; } = "1000";
        
        // v1.2.0 theme setting
        public bool IsDarkMode { get; set; } = false;

        // v1.3.0 verification settings
        public bool EnableVerification { get; set; } = true;
        public int VerificationMethod { get; set; } = 2; // Standard by default
        public int VerificationFailureAction { get; set; } = 0; // RetryAuto by default
    }
}
