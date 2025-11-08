using System.Collections.Generic;

namespace AutoCopy.Models
{
    public class FileOperationResult
    {
        public int TotalFiles { get; set; }
        public int FoundFiles { get; set; }
        public int SkippedFiles { get; set; }
        public int NotFoundFiles { get; set; }
        public List<string> NotFoundList { get; set; } = new List<string>();
        public string ElapsedTime { get; set; } = "";
    }

    public enum DuplicateHandling
    {
        Skip = 0,
        Rename = 1,
        Overwrite = 2
    }
}
