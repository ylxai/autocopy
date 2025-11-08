using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace AutoCopy.Models
{
    public class SelectableFileItem : INotifyPropertyChanged
    {
        private string _fullPath = "";
        public string FullPath
        {
            get => _fullPath;
            set => _fullPath = value ?? "";
        }

        private string _fileName = "";
        public string FileName
        {
            get => _fileName;
            set => _fileName = value ?? "";
        }

        private string _fileExtension = "";
        public string FileExtension
        {
            get => _fileExtension;
            set => _fileExtension = value ?? "";
        }

        public long FileSizeBytes { get; set; }

        private string _fileSizeFormatted = "";
        public string FileSizeFormatted
        {
            get => _fileSizeFormatted;
            set => _fileSizeFormatted = value ?? "";
        }

        public DateTime DateModified { get; set; }
        public DateTime DateCreated { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private BitmapSource? _thumbnail;
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isThumbnailLoading = true;
        public bool IsThumbnailLoading
        {
            get => _isThumbnailLoading;
            set
            {
                if (_isThumbnailLoading != value)
                {
                    _isThumbnailLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? CameraModel { get; set; }
        public string? LensModel { get; set; }
        public int? ISO { get; set; }
        public string? ShutterSpeed { get; set; }
        public string? Aperture { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
