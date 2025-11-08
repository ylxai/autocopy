using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using AutoCopy.Models;
using AutoCopy.Services;

namespace AutoCopy.ViewModels
{
    public class VisualFileSelectorViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ThumbnailService _thumbnailService = new();
        private CancellationTokenSource? _loadCancellation;
        private bool _disposed = false;

        private ObservableCollection<SelectableFileItem> _files = new();
        public ObservableCollection<SelectableFileItem> Files
        {
            get => _files;
            set
            {
                _files = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalCount));
            }
        }

        private ICollectionView? _filesView;
        public ICollectionView? FilesView
        {
            get => _filesView;
            set
            {
                _filesView = value;
                OnPropertyChanged();
            }
        }

        private string _sourceFolder = "";
        public string SourceFolder
        {
            get => _sourceFolder;
            set
            {
                _sourceFolder = value;
                OnPropertyChanged();
            }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                if (!IsLoading)
                    FilesView?.Refresh();
            }
        }

        private string _selectedDateFilter = "All Dates";
        public string SelectedDateFilter
        {
            get => _selectedDateFilter;
            set
            {
                _selectedDateFilter = value;
                OnPropertyChanged();
                ApplyDateFilter();
                if (!IsLoading)
                    FilesView?.Refresh();
            }
        }

        private string _selectedSizeFilter = "All Sizes";
        public string SelectedSizeFilter
        {
            get => _selectedSizeFilter;
            set
            {
                _selectedSizeFilter = value;
                OnPropertyChanged();
                ApplySizeFilter();
                if (!IsLoading)
                    FilesView?.Refresh();
            }
        }

        private string _selectedTypeFilter = "All Types";
        public string SelectedTypeFilter
        {
            get => _selectedTypeFilter;
            set
            {
                _selectedTypeFilter = value;
                OnPropertyChanged();
                if (!IsLoading)
                    FilesView?.Refresh();
            }
        }

        private long _filterMinSize = 0;
        private long _filterMaxSize = long.MaxValue;
        private DateTime? _filterDateFrom;
        private DateTime? _filterDateTo;

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public int SelectedCount => Files.Count(f => f.IsSelected);
        public int TotalCount => Files.Count;
        public long TotalSelectedSize
        {
            get
            {
                try
                {
                    checked
                    {
                        return Files.Where(f => f.IsSelected).Sum(f => f.FileSizeBytes);
                    }
                }
                catch (OverflowException)
                {
                    System.Diagnostics.Debug.WriteLine("Overflow in TotalSelectedSize calculation");
                    return long.MaxValue;
                }
            }
        }
        public string TotalSelectedSizeFormatted => FormatFileSize(TotalSelectedSize);

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand InverseSelectionCommand { get; }

        public VisualFileSelectorViewModel()
        {
            SelectAllCommand = new RelayCommand(SelectAll);
            SelectNoneCommand = new RelayCommand(SelectNone);
            InverseSelectionCommand = new RelayCommand(InverseSelection);
        }

        public async Task LoadFilesFromFolderAsync(string folderPath)
        {
            if (!IsPathSafe(folderPath))
            {
                StatusMessage = "Error: Access to this folder is restricted for security reasons.";
                MessageBox.Show(
                    "Access to system folders is restricted for security.\n\nPlease select a different folder.",
                    "Security Restriction",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            var ct = _loadCancellation.Token;

            try
            {
                IsLoading = true;
                StatusMessage = "Scanning folder...";
                
                DetachPropertyChangedHandlers();
                Files.Clear();

                var filePaths = await Task.Run(() =>
                {
                    var files = new List<string>();
                    try
                    {
                        files.AddRange(
                            Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                     .Where(f => IsMediaFile(f))
                        );
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Access denied to folder: {folderPath} - {ex.Message}");
                        try
                        {
                            files.AddRange(
                                Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                                         .Where(f => IsMediaFile(f))
                            );
                        }
                        catch
                        {
                        }
                    }
                    catch (PathTooLongException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Path too long: {folderPath} - {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error scanning folder: {folderPath} - {ex.Message}");
                    }
                    return files;
                }, ct).ConfigureAwait(false);

                StatusMessage = $"Found {filePaths.Count} files. Loading details...";

                var items = filePaths.Select(path =>
                {
                    var fileInfo = new FileInfo(path);
                    return new SelectableFileItem
                    {
                        FullPath = path,
                        FileName = Path.GetFileName(path),
                        FileExtension = Path.GetExtension(path).ToLower(),
                        FileSizeBytes = fileInfo.Length,
                        FileSizeFormatted = FormatFileSize(fileInfo.Length),
                        DateModified = fileInfo.LastWriteTime,
                        DateCreated = fileInfo.CreationTime,
                        IsSelected = false,
                        IsThumbnailLoading = true
                    };
                }).ToList();

                Files = new ObservableCollection<SelectableFileItem>(items);
                FilesView = CollectionViewSource.GetDefaultView(Files);
                FilesView.Filter = FilterPredicate;

                AttachPropertyChangedHandlers();

                StatusMessage = $"Loaded {Files.Count} files. Loading thumbnails...";

                await LoadThumbnailsProgressivelyAsync(items, ct).ConfigureAwait(false);

                StatusMessage = $"Ready. {Files.Count} files loaded.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Loading cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadThumbnailsProgressivelyAsync(List<SelectableFileItem> items, CancellationToken ct)
        {
            const int batchSize = 100;
            long loadedCount = 0;
            int totalCount = items.Count;

            for (int i = 0; i < items.Count; i += batchSize)
            {
                if (ct.IsCancellationRequested)
                    break;

                var batch = items.Skip(i).Take(batchSize);
                var tasks = batch.Select(async item =>
                {
                    try
                    {
                        var thumbnail = await _thumbnailService.LoadThumbnailAsync(item.FullPath, ct).ConfigureAwait(false);

                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher != null)
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                item.Thumbnail = thumbnail;
                                item.IsThumbnailLoading = false;
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading thumbnail: {ex.Message}");
                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher != null)
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                item.IsThumbnailLoading = false;
                            });
                        }
                    }
                    finally
                    {
                        Interlocked.Increment(ref loadedCount);
                    }
                });

                await Task.WhenAll(tasks);

                var currentLoaded = Interlocked.Read(ref loadedCount);
                StatusMessage = $"Loading thumbnails... {currentLoaded}/{totalCount}";
            }
        }

        private bool FilterPredicate(object obj)
        {
            if (obj is not SelectableFileItem item) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                if (!item.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            long sizeMB = item.FileSizeBytes / (1024 * 1024);
            if (sizeMB < _filterMinSize)
                return false;
            if (_filterMaxSize != long.MaxValue && sizeMB > _filterMaxSize)
                return false;

            if (_filterDateFrom.HasValue && item.DateModified < _filterDateFrom.Value)
                return false;
            if (_filterDateTo.HasValue && item.DateModified > _filterDateTo.Value)
                return false;

            if (SelectedTypeFilter == "RAW Only" && !PhotoFormats.IsRawFormat(item.FileExtension))
                return false;
            if (SelectedTypeFilter == "JPEG Only" && !PhotoFormats.IsJpegFormat(item.FileExtension))
                return false;
            if (SelectedTypeFilter == "Video" && !PhotoFormats.VideoFormats.ContainsKey(item.FileExtension))
                return false;

            return true;
        }

        private void ApplyDateFilter()
        {
            _filterDateFrom = null;
            _filterDateTo = null;

            switch (SelectedDateFilter)
            {
                case "Today":
                    _filterDateFrom = DateTime.Today;
                    _filterDateTo = DateTime.Today.AddDays(1);
                    break;
                case "Last 7 days":
                    _filterDateFrom = DateTime.Today.AddDays(-7);
                    break;
                case "Last 30 days":
                    _filterDateFrom = DateTime.Today.AddDays(-30);
                    break;
            }
        }

        private void ApplySizeFilter()
        {
            _filterMinSize = 0;
            _filterMaxSize = long.MaxValue;

            switch (SelectedSizeFilter)
            {
                case "> 10 MB":
                    _filterMinSize = 10;
                    break;
                case "> 50 MB":
                    _filterMinSize = 50;
                    break;
            }
        }

        private void SelectAll()
        {
            DetachPropertyChangedHandlers();
            
            foreach (var item in Files)
                item.IsSelected = true;
            
            AttachPropertyChangedHandlers();
            
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(TotalSelectedSize));
            OnPropertyChanged(nameof(TotalSelectedSizeFormatted));
        }

        private void SelectNone()
        {
            DetachPropertyChangedHandlers();
            
            foreach (var item in Files)
                item.IsSelected = false;
            
            AttachPropertyChangedHandlers();
            
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(TotalSelectedSize));
            OnPropertyChanged(nameof(TotalSelectedSizeFormatted));
        }

        private void InverseSelection()
        {
            DetachPropertyChangedHandlers();
            
            foreach (var item in Files)
                item.IsSelected = !item.IsSelected;
            
            AttachPropertyChangedHandlers();
            
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(TotalSelectedSize));
            OnPropertyChanged(nameof(TotalSelectedSizeFormatted));
        }

        private bool IsMediaFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return PhotoFormats.IsRawFormat(ext) ||
                   PhotoFormats.IsJpegFormat(ext) ||
                   PhotoFormats.OtherImageFormats.ContainsKey(ext) ||
                   PhotoFormats.VideoFormats.ContainsKey(ext);
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F2} KB";
            if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void AttachPropertyChangedHandlers()
        {
            foreach (var item in Files)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }
        }

        private void DetachPropertyChangedHandlers()
        {
            if (Files == null) return;
            
            foreach (var item in Files)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableFileItem.IsSelected))
            {
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(TotalSelectedSize));
                OnPropertyChanged(nameof(TotalSelectedSizeFormatted));
            }
        }

        private bool IsPathSafe(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);

                var blacklist = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"C:\Windows",
                    @"C:\Program Files",
                    @"C:\Program Files (x86)"
                };

                foreach (var blocked in blacklist)
                {
                    if (!string.IsNullOrEmpty(blocked) && 
                        fullPath.StartsWith(blocked, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating path safety: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Cancel ongoing operations - tasks will stop when they check the token
                _loadCancellation?.Cancel();
                
                // Dispose cancellation token source
                // Note: Running tasks will receive OperationCanceledException when they check the token
                _loadCancellation?.Dispose();
                
                DetachPropertyChangedHandlers();
                
                _thumbnailService?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during ViewModel disposal: {ex.Message}");
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();
    }
}
