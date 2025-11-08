using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AutoCopy.Models
{
    /// <summary>
    /// Represents a checkpoint during copy operation for resume capability
    /// Thread-safe and serializable for persistence
    /// </summary>
    public class CopyCheckpoint
    {
        /// <summary>
        /// Unique identifier for this copy session
        /// </summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Timestamp when checkpoint was created
        /// </summary>
        public DateTime CheckpointTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when copy operation started
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Source folder path
        /// </summary>
        public string SourceFolder { get; set; } = string.Empty;

        /// <summary>
        /// Destination folder path
        /// </summary>
        public string DestinationFolder { get; set; } = string.Empty;

        /// <summary>
        /// Original file list to process
        /// </summary>
        public List<string> OriginalFileList { get; set; } = new List<string>();

        /// <summary>
        /// Files that have been successfully processed
        /// </summary>
        public List<string> ProcessedFiles { get; set; } = new List<string>();

        /// <summary>
        /// Files that failed processing
        /// </summary>
        public List<string> FailedFiles { get; set; } = new List<string>();

        /// <summary>
        /// Files that were skipped
        /// </summary>
        public List<string> SkippedFiles { get; set; } = new List<string>();

        /// <summary>
        /// Total bytes processed so far
        /// </summary>
        public long ProcessedBytes { get; set; } = 0;

        /// <summary>
        /// Total bytes to process (estimated)
        /// </summary>
        public long TotalBytes { get; set; } = 0;

        /// <summary>
        /// Copy operation settings at time of checkpoint
        /// </summary>
        public CopySettings Settings { get; set; } = new CopySettings();

        /// <summary>
        /// Current operation mode (Paste or File mode)
        /// </summary>
        public bool IsPasteMode { get; set; } = false;

        /// <summary>
        /// Check if this checkpoint is valid for resuming
        /// </summary>
        [JsonIgnore]
        public bool IsValidForResume
        {
            get
            {
                try
                {
                    // Basic validation checks
                    if (string.IsNullOrWhiteSpace(SessionId) ||
                        string.IsNullOrWhiteSpace(SourceFolder) ||
                        string.IsNullOrWhiteSpace(DestinationFolder) ||
                        OriginalFileList == null ||
                        OriginalFileList.Count == 0)
                    {
                        return false;
                    }

                    // Check if checkpoint is not too old (older than 7 days)
                    if (DateTime.UtcNow.Subtract(CheckpointTime).TotalDays > 7)
                    {
                        return false;
                    }

                    // Check if there are remaining files to process
                    int remainingFiles = OriginalFileList.Count - 
                                       (ProcessedFiles?.Count ?? 0) - 
                                       (SkippedFiles?.Count ?? 0) - 
                                       (FailedFiles?.Count ?? 0);

                    return remainingFiles > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Get remaining files to process
        /// </summary>
        [JsonIgnore]
        public List<string> RemainingFiles
        {
            get
            {
                try
                {
                    var processed = new HashSet<string>(ProcessedFiles ?? new List<string>());
                    var skipped = new HashSet<string>(SkippedFiles ?? new List<string>());
                    var failed = new HashSet<string>(FailedFiles ?? new List<string>());

                    var remaining = new List<string>();
                    
                    foreach (var file in OriginalFileList ?? new List<string>())
                    {
                        if (!processed.Contains(file) && 
                            !skipped.Contains(file) && 
                            !failed.Contains(file))
                        {
                            remaining.Add(file);
                        }
                    }

                    return remaining;
                }
                catch
                {
                    return new List<string>();
                }
            }
        }

        /// <summary>
        /// Calculate progress percentage
        /// </summary>
        [JsonIgnore]
        public double ProgressPercentage
        {
            get
            {
                try
                {
                    if (OriginalFileList == null || OriginalFileList.Count == 0)
                        return 0.0;

                    int totalProcessed = (ProcessedFiles?.Count ?? 0) + 
                                       (SkippedFiles?.Count ?? 0) + 
                                       (FailedFiles?.Count ?? 0);

                    return Math.Min(100.0, Math.Max(0.0, (totalProcessed * 100.0) / OriginalFileList.Count));
                }
                catch
                {
                    return 0.0;
                }
            }
        }

        /// <summary>
        /// Get human-readable summary
        /// </summary>
        [JsonIgnore]
        public string Summary
        {
            get
            {
                try
                {
                    int total = OriginalFileList?.Count ?? 0;
                    int processed = ProcessedFiles?.Count ?? 0;
                    int remaining = RemainingFiles.Count;
                    double progress = ProgressPercentage;

                    return $"{processed}/{total} files completed ({progress:F1}%), {remaining} remaining";
                }
                catch
                {
                    return "Invalid checkpoint data";
                }
            }
        }
    }

    /// <summary>
    /// Copy operation settings for checkpoint
    /// </summary>
    public class CopySettings
    {
        public bool IgnoreExtension { get; set; } = false;
        public bool CaseInsensitive { get; set; } = false;
        public int DuplicateHandling { get; set; } = 0; // Skip, Overwrite, Rename
        public bool EnableParallel { get; set; } = true;
        public int ParallelThreads { get; set; } = 4;
        public bool EnableFilters { get; set; } = false;
        public string ExtensionFilter { get; set; } = string.Empty;
        public string MinSizeMB { get; set; } = "0";
        public string MaxSizeMB { get; set; } = "1000";
        public bool EnableVerification { get; set; } = true;
        public int VerificationMethod { get; set; } = 2; // Standard
        public int VerificationFailureAction { get; set; } = 0; // RetryAuto
    }
}
