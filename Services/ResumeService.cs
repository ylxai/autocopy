using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoCopy.Models;
using Newtonsoft.Json;

namespace AutoCopy.Services
{
    /// <summary>
    /// Service for managing copy operation checkpoints and resume functionality
    /// Thread-safe and robust checkpoint management
    /// </summary>
    public class ResumeService : IDisposable
    {
        private const string CHECKPOINT_FOLDER = "Checkpoints";
        private const string CHECKPOINT_EXTENSION = ".checkpoint";
        private const int MAX_CHECKPOINT_FILES = 10;
        private const int CHECKPOINT_INTERVAL_FILES = 50; // Save checkpoint every 50 files
        
        private readonly string _checkpointDirectory;
        private readonly object _checkpointLock = new object();
        private bool _disposed = false;

        public ResumeService()
        {
            // Create checkpoint directory in app data folder
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutoCopy"
            );
            
            _checkpointDirectory = Path.Combine(appDataPath, CHECKPOINT_FOLDER);
            EnsureCheckpointDirectoryExists();
        }

        /// <summary>
        /// Save checkpoint data safely with error handling
        /// </summary>
        public async Task<bool> SaveCheckpointAsync(CopyCheckpoint checkpoint)
        {
            if (checkpoint == null || _disposed)
                return false;

            try
            {
                // Validate checkpoint data before saving
                if (!IsCheckpointValid(checkpoint))
                {
                    System.Diagnostics.Debug.WriteLine("Invalid checkpoint data, skipping save");
                    return false;
                }

                string checkpointPath = GetCheckpointPath(checkpoint.SessionId);
                string tempPath = checkpointPath + ".tmp";

                // Serialize to JSON with proper formatting
                var json = JsonConvert.SerializeObject(checkpoint, Formatting.Indented, new JsonSerializerSettings
                {
                    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                    NullValueHandling = NullValueHandling.Include
                });

                // Write to temp file first, then move (atomic operation)
                await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
                
                lock (_checkpointLock)
                {
                    // Move temp file to final location (atomic)
                    if (File.Exists(checkpointPath))
                    {
                        File.Delete(checkpointPath);
                    }
                    File.Move(tempPath, checkpointPath);
                }

                // Clean up old checkpoint files
                await CleanupOldCheckpointsAsync().ConfigureAwait(false);

                System.Diagnostics.Debug.WriteLine($"Checkpoint saved successfully: {checkpoint.SessionId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save checkpoint: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load checkpoint data safely with validation
        /// </summary>
        public async Task<CopyCheckpoint?> LoadCheckpointAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || _disposed)
                return null;

            try
            {
                string checkpointPath = GetCheckpointPath(sessionId);
                
                if (!File.Exists(checkpointPath))
                    return null;

                string json = await File.ReadAllTextAsync(checkpointPath).ConfigureAwait(false);
                
                var checkpoint = JsonConvert.DeserializeObject<CopyCheckpoint>(json, new JsonSerializerSettings
                {
                    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });

                // Validate loaded checkpoint
                if (checkpoint == null || !IsCheckpointValid(checkpoint))
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid checkpoint data for session: {sessionId}");
                    return null;
                }

                return checkpoint;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load checkpoint {sessionId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all available resume sessions
        /// </summary>
        public async Task<List<CopyCheckpoint>> GetAvailableResumeSessionsAsync()
        {
            var sessions = new List<CopyCheckpoint>();
            
            if (_disposed)
                return sessions;

            try
            {
                if (!Directory.Exists(_checkpointDirectory))
                    return sessions;

                var checkpointFiles = Directory.GetFiles(_checkpointDirectory, $"*{CHECKPOINT_EXTENSION}")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(MAX_CHECKPOINT_FILES);

                foreach (var file in checkpointFiles)
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                        var checkpoint = JsonConvert.DeserializeObject<CopyCheckpoint>(json);
                        
                        if (checkpoint != null && checkpoint.IsValidForResume)
                        {
                            sessions.Add(checkpoint);
                        }
                        else
                        {
                            // Invalid checkpoint, schedule for cleanup
                            _ = Task.Run(() =>
                            {
                                try { File.Delete(file); }
                                catch { /* Ignore cleanup errors */ }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse checkpoint file {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get resume sessions: {ex.Message}");
            }

            return sessions;
        }

        /// <summary>
        /// Delete checkpoint file safely
        /// </summary>
        public async Task<bool> DeleteCheckpointAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || _disposed)
                return false;

            try
            {
                string checkpointPath = GetCheckpointPath(sessionId);
                
                await Task.Run(() =>
                {
                    lock (_checkpointLock)
                    {
                        if (File.Exists(checkpointPath))
                        {
                            File.Delete(checkpointPath);
                        }
                    }
                }).ConfigureAwait(false);

                System.Diagnostics.Debug.WriteLine($"Checkpoint deleted: {sessionId}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete checkpoint {sessionId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if we should save a checkpoint based on progress
        /// </summary>
        public bool ShouldSaveCheckpoint(int processedCount)
        {
            return processedCount > 0 && (processedCount % CHECKPOINT_INTERVAL_FILES == 0);
        }

        /// <summary>
        /// Create checkpoint from current copy state
        /// </summary>
        public CopyCheckpoint CreateCheckpoint(
            string sessionId,
            DateTime startTime,
            string sourceFolder,
            string destinationFolder,
            List<string> originalFileList,
            List<string> processedFiles,
            List<string> failedFiles,
            List<string> skippedFiles,
            long processedBytes,
            long totalBytes,
            CopySettings settings,
            bool isPasteMode)
        {
            return new CopyCheckpoint
            {
                SessionId = sessionId,
                CheckpointTime = DateTime.UtcNow,
                StartTime = startTime,
                SourceFolder = sourceFolder ?? string.Empty,
                DestinationFolder = destinationFolder ?? string.Empty,
                OriginalFileList = new List<string>(originalFileList ?? new List<string>()),
                ProcessedFiles = new List<string>(processedFiles ?? new List<string>()),
                FailedFiles = new List<string>(failedFiles ?? new List<string>()),
                SkippedFiles = new List<string>(skippedFiles ?? new List<string>()),
                ProcessedBytes = Math.Max(0, processedBytes),
                TotalBytes = Math.Max(0, totalBytes),
                Settings = settings ?? new CopySettings(),
                IsPasteMode = isPasteMode
            };
        }

        #region Private Methods

        private void EnsureCheckpointDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_checkpointDirectory))
                {
                    Directory.CreateDirectory(_checkpointDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create checkpoint directory: {ex.Message}");
            }
        }

        private string GetCheckpointPath(string sessionId)
        {
            // Sanitize session ID for filename
            string safeSessionId = string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_checkpointDirectory, $"{safeSessionId}{CHECKPOINT_EXTENSION}");
        }

        private bool IsCheckpointValid(CopyCheckpoint checkpoint)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(checkpoint.SessionId) &&
                       !string.IsNullOrWhiteSpace(checkpoint.SourceFolder) &&
                       !string.IsNullOrWhiteSpace(checkpoint.DestinationFolder) &&
                       checkpoint.OriginalFileList != null &&
                       checkpoint.ProcessedFiles != null &&
                       checkpoint.FailedFiles != null &&
                       checkpoint.SkippedFiles != null &&
                       checkpoint.Settings != null &&
                       checkpoint.OriginalFileList.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task CleanupOldCheckpointsAsync()
        {
            try
            {
                if (!Directory.Exists(_checkpointDirectory))
                    return;

                var files = Directory.GetFiles(_checkpointDirectory, $"*{CHECKPOINT_EXTENSION}")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(MAX_CHECKPOINT_FILES);

                foreach (var file in files)
                {
                    try
                    {
                        await Task.Run(() => file.Delete()).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore individual file cleanup errors
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cleanup managed resources
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
