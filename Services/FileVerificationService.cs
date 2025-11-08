using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AutoCopy.Models;

namespace AutoCopy.Services
{
    /// <summary>
    /// Service for verifying file integrity after copy operations
    /// </summary>
    public class FileVerificationService
    {
        /// <summary>
        /// Verify that destination file matches source file
        /// </summary>
        public async Task<VerificationResult> VerifyFileAsync(
            string sourcePath,
            string destPath,
            VerificationMethod method,
            CancellationToken cancellationToken = default)
        {
            var result = new VerificationResult
            {
                FilePath = destPath,
                Method = method,
                IsValid = false
            };

            try
            {
                // Check if both files exist
                if (!File.Exists(sourcePath))
                {
                    result.ErrorMessage = "Source file not found";
                    return result;
                }

                if (!File.Exists(destPath))
                {
                    result.ErrorMessage = "Destination file not found";
                    return result;
                }

                var sourceInfo = new FileInfo(sourcePath);
                var destInfo = new FileInfo(destPath);

                result.SourceSize = sourceInfo.Length;
                result.DestSize = destInfo.Length;
                result.SourceModified = sourceInfo.LastWriteTime;
                result.DestModified = destInfo.LastWriteTime;

                switch (method)
                {
                    case VerificationMethod.None:
                        result.IsValid = true;
                        return result;

                    case VerificationMethod.SizeOnly:
                        result.IsValid = VerifySizeOnly(sourceInfo, destInfo, result);
                        return result;

                    case VerificationMethod.Standard:
                        result.IsValid = VerifyStandard(sourceInfo, destInfo, result);
                        return result;

                    case VerificationMethod.MD5Hash:
                        result.IsValid = await VerifyMD5Async(sourcePath, destPath, result, cancellationToken).ConfigureAwait(false);
                        return result;

                    default:
                        result.ErrorMessage = "Unknown verification method";
                        return result;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Verification error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Verify file size only (fastest)
        /// </summary>
        private bool VerifySizeOnly(FileInfo sourceInfo, FileInfo destInfo, VerificationResult result)
        {
            if (sourceInfo.Length != destInfo.Length)
            {
                result.ErrorMessage = $"Size mismatch: Source={sourceInfo.Length}, Dest={destInfo.Length}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verify size and modified date (good balance)
        /// </summary>
        private bool VerifyStandard(FileInfo sourceInfo, FileInfo destInfo, VerificationResult result)
        {
            // Check size first
            if (sourceInfo.Length != destInfo.Length)
            {
                result.ErrorMessage = $"Size mismatch: Source={sourceInfo.Length}, Dest={destInfo.Length}";
                return false;
            }

            // Check if modified times are within 2 seconds (file copy may have slight time diff)
            var timeDiff = Math.Abs((destInfo.LastWriteTime - sourceInfo.LastWriteTime).TotalSeconds);
            if (timeDiff > 2)
            {
                result.ErrorMessage = $"Modified time mismatch: Source={sourceInfo.LastWriteTime}, Dest={destInfo.LastWriteTime}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verify using MD5 hash (100% accurate but slower)
        /// </summary>
        private async Task<bool> VerifyMD5Async(
            string sourcePath,
            string destPath,
            VerificationResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                string sourceHash = await ComputeMD5Async(sourcePath, cancellationToken).ConfigureAwait(false);
                string destHash = await ComputeMD5Async(destPath, cancellationToken).ConfigureAwait(false);

                result.SourceHash = sourceHash;
                result.DestHash = destHash;

                if (sourceHash != destHash)
                {
                    result.ErrorMessage = $"Hash mismatch: Source={sourceHash}, Dest={destHash}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Hash computation error: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Compute MD5 hash of a file
        /// </summary>
        private async Task<string> ComputeMD5Async(string filePath, CancellationToken cancellationToken)
        {
            using (var md5 = MD5.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
            {
                var hashBytes = await Task.Run(() => md5.ComputeHash(stream), cancellationToken).ConfigureAwait(false);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Verify file synchronously (for compatibility)
        /// </summary>
        public VerificationResult VerifyFile(
            string sourcePath,
            string destPath,
            VerificationMethod method)
        {
            var result = new VerificationResult
            {
                FilePath = destPath,
                Method = method,
                IsValid = false
            };

            try
            {
                // Check if both files exist
                if (!File.Exists(sourcePath))
                {
                    result.ErrorMessage = "Source file not found";
                    return result;
                }

                if (!File.Exists(destPath))
                {
                    result.ErrorMessage = "Destination file not found";
                    return result;
                }

                // Get file info for verification methods
                var sourceInfo = new FileInfo(sourcePath);
                var destInfo = new FileInfo(destPath);

                // Perform verification based on method
                switch (method)
                {
                    case VerificationMethod.None:
                        result.IsValid = true;
                        result.ErrorMessage = "No verification performed";
                        break;

                    case VerificationMethod.SizeOnly:
                        result.IsValid = VerifySizeOnly(sourceInfo, destInfo, result);
                        break;

                    case VerificationMethod.Standard:
                        result.IsValid = VerifyStandard(sourceInfo, destInfo, result);
                        break;

                    case VerificationMethod.MD5Hash:
                        result = VerifyMD5(sourcePath, destPath);
                        break;

                    default:
                        result.ErrorMessage = "Unknown verification method";
                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Verification error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Synchronous MD5 verification
        /// </summary>
        private VerificationResult VerifyMD5(string sourcePath, string destPath)
        {
            var result = new VerificationResult
            {
                FilePath = destPath,
                Method = VerificationMethod.MD5Hash,
                IsValid = false
            };

            try
            {
                string sourceHash = ComputeMD5Sync(sourcePath);
                string destHash = ComputeMD5Sync(destPath);

                result.IsValid = string.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase);
                result.ErrorMessage = result.IsValid ? "MD5 hashes match" : $"MD5 mismatch: source={sourceHash.Substring(0,8)}..., dest={destHash.Substring(0,8)}...";

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"MD5 verification failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Synchronous MD5 computation
        /// </summary>
        private string ComputeMD5Sync(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
