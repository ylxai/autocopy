using System.Collections.Generic;

namespace AutoCopy.Models
{
    public static class PhotoFormats
    {
        // RAW Formats - All major camera brands
        public static readonly Dictionary<string, string> RawFormats = new Dictionary<string, string>
        {
            // Nikon
            { ".nef", "Nikon RAW" },
            { ".nrw", "Nikon RAW (Coolpix)" },
            
            // Canon
            { ".cr2", "Canon RAW" },
            { ".cr3", "Canon RAW (New)" },
            { ".crw", "Canon RAW (Old)" },
            
            // Sony
            { ".arw", "Sony RAW" },
            { ".srf", "Sony RAW (Old)" },
            { ".sr2", "Sony RAW" },
            
            // Fujifilm
            { ".raf", "Fujifilm RAW" },
            
            // Olympus
            { ".orf", "Olympus RAW" },
            
            // Panasonic
            { ".rw2", "Panasonic RAW" },
            { ".raw", "Panasonic RAW" },
            
            // Pentax
            { ".pef", "Pentax RAW" },
            { ".ptx", "Pentax RAW" },
            
            // Leica
            { ".rwl", "Leica RAW" },
            { ".dng", "Digital Negative (Adobe)" },
            
            // Phase One
            { ".iiq", "Phase One RAW" },
            
            // Hasselblad
            { ".3fr", "Hasselblad RAW" },
            { ".fff", "Hasselblad RAW" },
            
            // Sigma
            { ".x3f", "Sigma RAW" },
            
            // Kodak
            { ".dcr", "Kodak RAW" },
            { ".kdc", "Kodak RAW" },
            
            // Minolta
            { ".mrw", "Minolta RAW" },
            
            // Samsung
            { ".srw", "Samsung RAW" },
            
            // Epson
            { ".erf", "Epson RAW" },
            
            // Mamiya
            { ".mef", "Mamiya RAW" },
            { ".mos", "Mamiya RAW" },
            
            // Generic
            { ".raw", "Generic RAW" },
            { ".rwz", "Rawzor RAW" }
        };

        // JPEG/JPG Formats
        public static readonly List<string> JpegFormats = new List<string>
        {
            ".jpg", ".jpeg", ".jpe", ".jfif"
        };

        // Other Image Formats (for photography)
        public static readonly Dictionary<string, string> OtherImageFormats = new Dictionary<string, string>
        {
            { ".png", "PNG Image" },
            { ".tif", "TIFF Image" },
            { ".tiff", "TIFF Image" },
            { ".bmp", "Bitmap Image" },
            { ".gif", "GIF Image" },
            { ".webp", "WebP Image" },
            { ".heic", "HEIC (iPhone)" },
            { ".heif", "HEIF Image" },
            { ".psd", "Photoshop" },
            { ".psb", "Photoshop Big" },
            { ".ai", "Adobe Illustrator" }
        };

        // Video Formats (for photographers who also shoot video)
        public static readonly Dictionary<string, string> VideoFormats = new Dictionary<string, string>
        {
            { ".mp4", "MP4 Video" },
            { ".mov", "QuickTime Video" },
            { ".avi", "AVI Video" },
            { ".mkv", "MKV Video" },
            { ".mts", "AVCHD Video" },
            { ".m2ts", "AVCHD Video" },
            { ".mpg", "MPEG Video" },
            { ".mpeg", "MPEG Video" },
            { ".wmv", "Windows Media Video" }
        };

        // Get all photo extensions (RAW + JPEG + Images)
        public static List<string> GetAllPhotoExtensions()
        {
            var list = new List<string>();
            list.AddRange(RawFormats.Keys);
            list.AddRange(JpegFormats);
            list.AddRange(OtherImageFormats.Keys);
            return list;
        }

        // Get all extensions including video
        public static List<string> GetAllMediaExtensions()
        {
            var list = GetAllPhotoExtensions();
            list.AddRange(VideoFormats.Keys);
            return list;
        }

        // Check if file is RAW format
        public static bool IsRawFormat(string extension)
        {
            return RawFormats.ContainsKey(extension.ToLower());
        }

        // Check if file is JPEG
        public static bool IsJpegFormat(string extension)
        {
            return JpegFormats.Contains(extension.ToLower());
        }

        // Get format description
        public static string GetFormatDescription(string extension)
        {
            extension = extension.ToLower();
            
            if (RawFormats.ContainsKey(extension))
                return RawFormats[extension];
            
            if (JpegFormats.Contains(extension))
                return "JPEG Image";
            
            if (OtherImageFormats.ContainsKey(extension))
                return OtherImageFormats[extension];
            
            if (VideoFormats.ContainsKey(extension))
                return VideoFormats[extension];
            
            return "Unknown Format";
        }

        // Photography presets for quick selection
        public static class Presets
        {
            public static string AllPhotos => string.Join(",", GetAllPhotoExtensions());
            public static string AllMedia => string.Join(",", GetAllMediaExtensions());
            public static string RawOnly => string.Join(",", RawFormats.Keys);
            public static string JpegOnly => string.Join(",", JpegFormats);
            public static string NikonRAW => ".nef,.nrw";
            public static string CanonRAW => ".cr2,.cr3,.crw";
            public static string SonyRAW => ".arw,.srf,.sr2";
            public static string VideoOnly => string.Join(",", VideoFormats.Keys);
        }
    }
}
