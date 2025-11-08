using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AutoCopy.Services
{
    public class FileService
    {
        public List<string> ScanFolder(string folderPath, CancellationToken cancellationToken)
        {
            var files = new List<string>();
            ScanFolderRecursive(folderPath, files, cancellationToken);
            return files;
        }

        private void ScanFolderRecursive(string folderPath, List<string> files, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Add files in current directory
                files.AddRange(Directory.GetFiles(folderPath));

                // Recursively scan subdirectories
                foreach (var directory in Directory.GetDirectories(folderPath))
                {
                    ScanFolderRecursive(directory, files, cancellationToken);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we don't have access to
            }
            catch (Exception)
            {
                // Skip problematic folders
            }
        }

        public Dictionary<string, string> BuildFileIndex(
            List<string> files,
            bool ignoreExtension,
            bool caseInsensitive)
        {
            var index = new Dictionary<string, string>(
                caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
            );

            foreach (var filePath in files)
            {
                string fileName = Path.GetFileName(filePath);
                string key = GetSearchKey(fileName, ignoreExtension, caseInsensitive);

                // Keep first occurrence (or you could implement logic to keep newest/largest)
                if (!index.ContainsKey(key))
                {
                    index[key] = filePath;
                }
            }

            return index;
        }

        public string GetSearchKey(string fileName, bool ignoreExtension, bool caseInsensitive)
        {
            string key = fileName;

            if (ignoreExtension)
            {
                key = Path.GetFileNameWithoutExtension(fileName);
            }

            if (caseInsensitive)
            {
                key = key.ToLowerInvariant();
            }

            return key;
        }

        public string GetUniqueFileName(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            int counter = 1;
            string newPath = filePath;

            while (File.Exists(newPath))
            {
                string newFileName = $"{fileNameWithoutExt}_{counter}{extension}";
                newPath = Path.Combine(directory, newFileName);
                counter++;
            }

            return newPath;
        }
    }
}
