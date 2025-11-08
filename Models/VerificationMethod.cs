using System;

namespace AutoCopy.Models
{
    /// <summary>
    /// Method for verifying file integrity after copy
    /// </summary>
    public enum VerificationMethod
    {
        /// <summary>
        /// No verification (fastest, not recommended for critical data)
        /// </summary>
        None = 0,

        /// <summary>
        /// Verify file size only (very fast, basic check)
        /// </summary>
        SizeOnly = 1,

        /// <summary>
        /// Verify size and last modified date (fast, good balance)
        /// </summary>
        Standard = 2,

        /// <summary>
        /// Full MD5 hash comparison (slower, 100% accurate)
        /// </summary>
        MD5Hash = 3
    }

    /// <summary>
    /// Action to take when verification fails
    /// </summary>
    public enum VerificationFailureAction
    {
        /// <summary>
        /// Retry copy automatically (up to 3 attempts)
        /// </summary>
        RetryAuto = 0,

        /// <summary>
        /// Skip file and log error
        /// </summary>
        SkipAndLog = 1,

        /// <summary>
        /// Stop entire operation
        /// </summary>
        StopOperation = 2
    }

    /// <summary>
    /// Result of file verification
    /// </summary>
    public class VerificationResult
    {
        public bool IsValid { get; set; }
        public string FilePath { get; set; } = "";
        public VerificationMethod Method { get; set; }
        public string ErrorMessage { get; set; } = "";
        public long SourceSize { get; set; }
        public long DestSize { get; set; }
        public DateTime SourceModified { get; set; }
        public DateTime DestModified { get; set; }
        public string? SourceHash { get; set; }
        public string? DestHash { get; set; }
    }
}
