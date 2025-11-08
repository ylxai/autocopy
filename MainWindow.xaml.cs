using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using AutoCopy.Models;
using AutoCopy.Services;
using Newtonsoft.Json;

namespace AutoCopy
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Stopwatch _stopwatch = new Stopwatch();
        private System.Windows.Threading.DispatcherTimer? _timer;
        private System.Collections.Concurrent.ConcurrentBag<string> _notFoundFiles = new System.Collections.Concurrent.ConcurrentBag<string>();
        private FileService _fileService = new FileService();
        private FileVerificationService _verificationService = new FileVerificationService();
        private ResumeService _resumeService = new ResumeService();
        private Dictionary<string, string>? _fileIndex;
        private const string CONFIG_FILE = "autocopy_config.json";
        private bool _isPasteMode = false;
        
        // Verification statistics
        private int _verifiedCount = 0;
        private int _verificationFailedCount = 0;
        private int _verificationRetriedCount = 0;
        
        // Log management
        private const int MAX_LOG_LINES = 1000;
        private int _currentLogLines = 0;
        
        // UI update batching (FIX #2)
        private const int UI_UPDATE_INTERVAL_MS = 200;
        private System.Threading.Timer? _uiBatchUpdateTimer;
        private volatile bool _uiUpdatePending = false;
        
        // Path handling constants (FIX #6)
        private const string DESTINATION_FOLDER_PLACEHOLDER = "{destination}";
        private const string SOURCE_FILENAME_PLACEHOLDER = "{filename}";
        
        // Timeout constants (FIX #11)
        private const int RETRY_DELAY_MS = 500;
        private const int DEBOUNCE_DELAY_MS = 300;
        private const int VERIFICATION_TIMEOUT_SECONDS = 2;
        
        // Resume capability
        private string? _currentSessionId;
        private DateTime _sessionStartTime;
        private readonly List<string> _sessionProcessedFiles = new List<string>();
        private readonly List<string> _sessionSkippedFiles = new List<string>();
        private readonly List<string> _sessionFailedFiles = new List<string>();
        
        // Enhanced progress tracking
        private long _totalBytes = 0;
        private long _copiedBytes = 0;
        private DateTime _startTime;
        private readonly object _progressLock = new object();
        private int _parallelThreadCount = 4;

        public MainWindow()
        {
            InitializeComponent();
            LoadConfiguration();
            InitializeTimer();
            
            // Check for resumable operations on startup
            _ = Task.Run(CheckForResumableOperationsAsync);
        }

        // FIX: Memory leak - Proper resource disposal on window close
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            try
            {
                // Stop and dispose timers
                _timer?.Stop();
                _timer = null;
                
                _uiBatchUpdateTimer?.Dispose();
                _uiBatchUpdateTimer = null;
                
                // Cancel any ongoing operations
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                
                // Stop stopwatch
                _stopwatch?.Stop();
                
                // Clear collections to help GC
                _notFoundFiles?.Clear();
                _sessionProcessedFiles?.Clear();
                _sessionSkippedFiles?.Clear();
                _sessionFailedFiles?.Clear();
                _fileIndex?.Clear();
                
                System.Diagnostics.Debug.WriteLine("✅ Resources disposed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error disposing resources: {ex.Message}");
            }
        }

        private void BtnThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(true); // Switch to Dark Mode
        }

        private void BtnThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SwitchTheme(false); // Switch to Light Mode
        }

        private void SwitchTheme(bool isDarkMode)
        {
            try
            {
                // Get application resources
                var app = Application.Current;
                
                // Clear existing theme
                var existingTheme = app.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source?.OriginalString?.Contains("Theme") == true);
                
                if (existingTheme != null)
                {
                    app.Resources.MergedDictionaries.Remove(existingTheme);
                }
                
                // Load new theme
                string themePath = isDarkMode ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
                var newTheme = new ResourceDictionary
                {
                    Source = new Uri(themePath, UriKind.Relative)
                };
                
                app.Resources.MergedDictionaries.Insert(0, newTheme);
                
                // Save preference
                SaveThemePreference(isDarkMode);
                
                string themeName = isDarkMode ? "Dark" : "Light";
                LogMessage($"🎨 Theme changed to {themeName} Mode");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error switching theme: {ex.Message}", "Theme Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveThemePreference(bool isDarkMode)
        {
            try
            {
                AppConfig? config = null;
                
                if (File.Exists(CONFIG_FILE))
                {
                    string json = File.ReadAllText(CONFIG_FILE);
                    config = JsonConvert.DeserializeObject<AppConfig>(json);
                }
                
                if (config == null)
                {
                    config = new AppConfig();
                }
                
                config.IsDarkMode = isDarkMode;
                
                string updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(CONFIG_FILE, updatedJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving theme preference: {ex.Message}");
            }
        }

        private void InitializeTimer()
        {
            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) =>
            {
                txtTimer.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                UpdateSpeedAndETA();
            };
        }

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "📸 Pilih Folder - Folder yang berisi foto yang ingin disalin",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtSourceFolder.Text = dialog.SelectedPath;
                LogMessage($"📸 Folder sumber dipilih: {dialog.SelectedPath}");
            }
        }

        private void BtnBrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "💾 Salin Ke Folder - Pilih folder tujuan untuk menyimpan hasil copy",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtDestFolder.Text = dialog.SelectedPath;
                LogMessage($"💾 Folder tujuan dipilih: {dialog.SelectedPath}");
            }
        }

        private void BtnBrowseFileList_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Pilih File List"
            };

            if (dialog.ShowDialog() == true)
            {
                txtFileList.Text = dialog.FileName;
                LogMessage($"File list dipilih: {dialog.FileName}");
                
                try
                {
                    var lines = File.ReadAllLines(dialog.FileName)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();
                    LogMessage($"✅ File list berisi {lines.Count} file");
                }
                catch (Exception ex)
                {
                    LogMessage($"❌ Error membaca file list: {ex.Message}");
                }
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            // Initialize new copy session
            _currentSessionId = Guid.NewGuid().ToString();
            _sessionStartTime = DateTime.UtcNow;
            
            _notFoundFiles.Clear();
            _sessionProcessedFiles.Clear();
            _sessionSkippedFiles.Clear();
            _sessionFailedFiles.Clear();
            
            txtFound.Text = "0";
            txtTotal.Text = "0";
            txtSkipped.Text = "0";
            txtNotFound.Text = "0";
            progressBar.Value = 0;
            txtProgress.Text = "0%";
            txtCurrentFile.Text = "";

            // Reset verification stats
            _verifiedCount = 0;
            _verificationFailedCount = 0;
            _verificationRetriedCount = 0;

            SetUIEnabled(false);
            _cancellationTokenSource = new CancellationTokenSource();
            _stopwatch.Restart();
            _timer?.Start();
            
            // Show processing status
            ShowStatusIcon("processing");

            LogMessage("========================================");
            LogMessage($"🚀 Starting new copy session: {_currentSessionId}");
            LogMessage("🚀 Memulai proses copy...");
            LogMessage($"Source: {txtSourceFolder.Text}");
            LogMessage($"Destination: {txtDestFolder.Text}");
            LogMessage($"File List: {txtFileList.Text}");
            LogMessage("========================================");

            try
            {
                await ProcessFileCopy(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                LogMessage("⏹️ Proses dibatalkan oleh user");
                HideAllStatusIcons(); // Clear status on cancellation
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error: {ex.Message}");
                ShowStatusIcon("error"); // Show error status
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _stopwatch.Stop();
                _timer?.Stop();
                StopBatchedUIUpdates(); // Stop batched updates (FIX #2)
                SetUIEnabled(true);
                btnExportNotFound.IsEnabled = _notFoundFiles.Any();
                
                // Show appropriate status icon
                if (_notFoundFiles.Count > 0)
                {
                    ShowStatusIcon("error");
                }
                else
                {
                    ShowStatusIcon("success");
                }
                
                LogMessage("========================================");
                LogMessage($"✅ Proses selesai dalam {_stopwatch.Elapsed:hh\\:mm\\:ss}");
                LogMessage("========================================");
            }
        }

        private async Task ProcessFileCopy(CancellationToken cancellationToken)
        {
            List<string> fileList;
            
            if (_isPasteMode)
            {
                // Get from paste textarea
                // FIX #10: Optimize LINQ chain to reduce allocations
                fileList = await Task.Run(() =>
                {
                    var result = new List<string>();
                    var splitLines = txtPasteList.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var line in splitLines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            result.Add(trimmed);
                        }
                    }
                    
                    return result;
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Get from file
                // FIX #10: Optimize file reading with single pass
                fileList = await Task.Run(() =>
                {
                    var result = new List<string>();
                    var allLines = File.ReadAllLines(txtFileList.Text);
                    
                    foreach (var line in allLines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            result.Add(line.Trim());
                        }
                    }
                    
                    return result;
                }, cancellationToken).ConfigureAwait(false);
            }

            if (fileList.Count == 0)
            {
                LogMessage("❌ File list is empty! No files to process.");
                MessageBox.Show(
                    "File list is empty!\n\nPlease provide a list with at least one file.",
                    "Empty File List",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            txtTotal.Text = fileList.Count.ToString();
            LogMessage($"📝 Total file dalam list: {fileList.Count}");

            LogMessage("🔍 Scanning source folder...");
            var sourceFiles = await Task.Run(() =>
                _fileService.ScanFolder(txtSourceFolder.Text, cancellationToken)
            , cancellationToken).ConfigureAwait(false);

            LogMessage($"📂 Ditemukan {sourceFiles.Count} file di source folder");

            _fileIndex = _fileService.BuildFileIndex(
                sourceFiles,
                chkIgnoreExtension.IsChecked == true,
                chkCaseInsensitive.IsChecked == true
            );

            LogMessage($"📇 Index dibuat dengan {_fileIndex.Count} entri");

            // Calculate total bytes for progress
            _totalBytes = 0;
            _copiedBytes = 0;
            _startTime = DateTime.Now;
            
            foreach (var fileName in fileList)
            {
                string searchKey = _fileService.GetSearchKey(fileName, chkIgnoreExtension.IsChecked == true, chkCaseInsensitive.IsChecked == true);
                if (_fileIndex.TryGetValue(searchKey, out var sourcePath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(sourcePath);
                        _totalBytes += fileInfo.Length;
                    }
                    catch { }
                }
            }
            
            UpdateBytesDisplay();

            int found = 0, skipped = 0, notFound = 0;
            var duplicateHandling = (DuplicateHandling)cmbDuplicateHandling.SelectedIndex;
            bool useParallel = chkParallelCopy?.IsChecked == true;
            
            LogMessage($"⚙️ Mode: {(useParallel ? $"Parallel ({_parallelThreadCount} threads)" : "Sequential")}");

            if (useParallel)
            {
                // PARALLEL COPY
                await ProcessFilesParallel(fileList, _fileIndex, duplicateHandling, cancellationToken);
            }
            else
            {
                // SEQUENTIAL COPY
                for (int i = 0; i < fileList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = fileList[i];
                string searchKey = _fileService.GetSearchKey(
                    fileName,
                    chkIgnoreExtension.IsChecked == true,
                    chkCaseInsensitive.IsChecked == true
                );

                Dispatcher.Invoke(() =>
                {
                    txtCurrentFile.Text = $"Processing: {fileName}";
                    progressBar.Value = (i + 1) * 100.0 / fileList.Count;
                    txtProgress.Text = $"{(int)progressBar.Value}%";
                });

                if (_fileIndex.TryGetValue(searchKey, out var sourcePath))
                {
                    // Apply filters if enabled (FIX #13)
                    if (!ShouldProcessFile(sourcePath))
                    {
                        Interlocked.Increment(ref skipped);
                        LogMessage($"⏭️ FILTERED: {fileName}");
                        Dispatcher.Invoke(() => txtSkipped.Text = skipped.ToString());
                        continue;
                    }
                    
                    // FIX #6: Consistent path handling
                    string destPath = BuildDestinationPath(txtDestFolder.Text, sourcePath);

                    if (File.Exists(destPath))
                    {
                        if (duplicateHandling == DuplicateHandling.Skip)
                        {
                            Interlocked.Increment(ref skipped);
                            LogMessage($"⏭️ SKIP: {fileName} (sudah ada)");
                            Dispatcher.Invoke(() => txtSkipped.Text = skipped.ToString());
                            continue;
                        }
                        else if (duplicateHandling == DuplicateHandling.Rename)
                        {
                            lock (_fileService)
                            {
                                destPath = _fileService.GetUniqueFileName(destPath);
                            }
                        }
                    }

                    try
                    {
                        var fileInfo = new FileInfo(sourcePath);
                        long fileSize = fileInfo.Length;
                        
                        // Copy with verification
                        bool copySuccess = await CopyFileWithVerificationAsync(
                            sourcePath,
                            destPath,
                            fileName,
                            duplicateHandling == DuplicateHandling.Overwrite,
                            cancellationToken
                        ).ConfigureAwait(false);
                        
                        if (copySuccess)
                        {
                            lock (_progressLock)
                            {
                                _copiedBytes += fileSize;
                            }
                            
                            Interlocked.Increment(ref found);
                            
                            LogMessage($"✅ COPIED: {fileName} -> {Path.GetFileName(destPath)}");
                            
                            // Track for resume capability
                            _sessionProcessedFiles.Add(fileName);
                            
                            // Save checkpoint periodically
                            if (_resumeService.ShouldSaveCheckpoint(_sessionProcessedFiles.Count))
                            {
                                _ = SaveCurrentCheckpointAsync();
                            }
                            
                            // UI updates now batched - no individual Dispatcher calls needed
                        }
                        else
                        {
                            // Verification failed and skipped
                            Interlocked.Increment(ref skipped);
                            _sessionSkippedFiles.Add(fileName);
                            // UI updates now batched - no individual Dispatcher calls needed
                        }
                    }
                    catch (Exception ex)
                    {
                        // FIX #7: Preserve exception context for better debugging
                        LogMessage($"❌ ERROR copying {fileName}: {ex.GetType().Name} - {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            LogMessage($"   Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                        }
                        
                        Interlocked.Increment(ref notFound);
                        _notFoundFiles.Add(fileName);
                        _sessionFailedFiles.Add(fileName);
                        // UI updates now batched - no individual Dispatcher calls needed
                    }
                }
                else
                {
                    Interlocked.Increment(ref notFound);
                    _notFoundFiles.Add(fileName);
                    _sessionFailedFiles.Add(fileName);
                    LogMessage($"❌ NOT FOUND: {fileName}");
                    // UI updates now batched - no individual Dispatcher calls needed
                }
                }
            }
        }

        private async Task ProcessFilesParallel(List<string> fileList, Dictionary<string, string> fileIndex, 
            DuplicateHandling duplicateHandling, CancellationToken cancellationToken)
        {
            int found = 0, skipped = 0, notFound = 0;
            int processed = 0;
            
            var semaphore = new SemaphoreSlim(_parallelThreadCount);
            var tasks = new List<Task>();

            foreach (var fileName in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                await semaphore.WaitAsync(cancellationToken);

                var task = Task.Run(() =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        string searchKey = _fileService.GetSearchKey(
                            fileName,
                            chkIgnoreExtension.IsChecked == true,
                            chkCaseInsensitive.IsChecked == true
                        );

                        if (_fileIndex?.TryGetValue(searchKey, out var sourcePath) == true)
                        {
                            // Apply filters if enabled
                            if (!ShouldProcessFile(sourcePath))
                            {
                                Interlocked.Increment(ref skipped);
                                LogMessage($"⏭️ FILTERED: {fileName}");
                                return;
                            }
                            
                            // FIX #6: Consistent path handling
                            string destPath = BuildDestinationPath(txtDestFolder.Text, sourcePath);

                            if (File.Exists(destPath))
                            {
                                if (duplicateHandling == DuplicateHandling.Skip)
                                {
                                    Interlocked.Increment(ref skipped);
                                    LogMessage($"⏭️ SKIP: {fileName} (sudah ada)");
                                    Dispatcher.Invoke(() => txtSkipped.Text = skipped.ToString());
                                    return;
                                }
                                else if (duplicateHandling == DuplicateHandling.Rename)
                                {
                                    lock (_fileService)
                                    {
                                        destPath = _fileService.GetUniqueFileName(destPath);
                                    }
                                }
                            }

                            var fileInfo = new FileInfo(sourcePath);
                            long fileSize = fileInfo.Length;

                            // Copy with verification (safe sync version for Parallel.ForEach)
                            bool copySuccess = CopyFileWithVerificationSync(
                                sourcePath,
                                destPath,
                                fileName,
                                duplicateHandling == DuplicateHandling.Overwrite,
                                cancellationToken
                            );

                            if (copySuccess)
                            {
                                lock (_progressLock)
                                {
                                    _copiedBytes += fileSize;
                                }
                                
                                Interlocked.Increment(ref found);

                                LogMessage($"✅ COPIED: {fileName} (Thread-{Thread.CurrentThread.ManagedThreadId})");
                            }
                            else
                            {
                                // Verification failed and skipped
                                Interlocked.Increment(ref skipped);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref notFound);
                            _notFoundFiles.Add(fileName);
                            LogMessage($"❌ NOT FOUND: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // FIX #7: Preserve exception context for better debugging
                        LogMessage($"❌ ERROR: {fileName} - {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            LogMessage($"   Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                        }
                        
                        Interlocked.Increment(ref notFound);
                        _notFoundFiles.Add(fileName);
                    }
                    finally
                    {
                        Interlocked.Increment(ref processed);
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            // Update UI in batches from main thread
            System.Threading.Timer? uiUpdateTimer = null;
            
            try
            {
                uiUpdateTimer = new System.Threading.Timer(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        txtFound.Text = found.ToString();
                        txtSkipped.Text = skipped.ToString();
                        txtNotFound.Text = notFound.ToString();
                        txtCurrentFile.Text = $"Processing: {processed}/{fileList.Count}";
                        
                        // FIX #4: Division by zero protection
                        double progress = 0.0;
                        if (fileList.Count > 0)
                        {
                            progress = Math.Min(100.0, Math.Max(0.0, processed * 100.0 / fileList.Count));
                        }
                        progressBar.Value = progress;
                        txtProgress.Text = $"{(int)progress}%";
                        UpdateBytesDisplay();
                    }));
                }, null, 0, 200); // Update every 200ms

                await Task.WhenAll(tasks);
            }
            finally
            {
                uiUpdateTimer?.Dispose();
            }
            
            // Final UI update
            Dispatcher.Invoke(() =>
            {
                txtFound.Text = found.ToString();
                txtSkipped.Text = skipped.ToString();
                txtNotFound.Text = notFound.ToString();
                progressBar.Value = 100;
                txtProgress.Text = "100%";
                UpdateBytesDisplay();
            });
        }

        private void UpdateBytesDisplay()
        {
            // FIX #9: Comprehensive null safety checks
            if (txtBytesInfo == null || progressBar == null) return;
            
            try
            {
                // Check for overflow (FIX #17)
                if (_copiedBytes < 0 || _totalBytes < 0)
                {
                    txtBytesInfo.Text = "Overflow detected!";
                    return;
                }
                
                double copiedMB = _copiedBytes / (1024.0 * 1024.0);
                double totalMB = _totalBytes / (1024.0 * 1024.0);
                txtBytesInfo.Text = $"{copiedMB:F2} MB / {totalMB:F2} MB";
            }
            catch (OverflowException)
            {
                txtBytesInfo.Text = "Size too large!";
            }
        }

        private void UpdateSpeedAndETA()
        {
            // FIX #9: Comprehensive null safety checks
            if (txtSpeed == null || txtETA == null || _stopwatch == null) return;
            
            if (_stopwatch.IsRunning && _copiedBytes > 0)
            {
                double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    double speedMBps = (_copiedBytes / (1024.0 * 1024.0)) / elapsedSeconds;
                    txtSpeed.Text = $"{speedMBps:F2} MB/s";

                    if (_totalBytes > _copiedBytes && speedMBps > 0)
                    {
                        double remainingBytes = _totalBytes - _copiedBytes;
                        double remainingSeconds = (remainingBytes / (1024.0 * 1024.0)) / speedMBps;
                        var eta = TimeSpan.FromSeconds(remainingSeconds);
                        txtETA.Text = $"{(int)eta.TotalHours:D2}:{eta.Minutes:D2}:{eta.Seconds:D2}";
                    }
                    else if (_copiedBytes >= _totalBytes)
                    {
                        txtETA.Text = "00:00:00";
                    }
                }
                else
                {
                    txtSpeed.Text = "Calculating...";
                    txtETA.Text = "Calculating...";
                }
            }
        }

        private void SliderThreads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _parallelThreadCount = (int)sliderThreads.Value;
            if (txtThreadCount != null)
            {
                txtThreadCount.Text = $"{_parallelThreadCount} threads";
            }
        }

        private void ChkEnableVerification_Changed(object sender, RoutedEventArgs e)
        {
            // FIX #9: Enhanced null safety
            if (panelVerificationSettings != null && chkEnableVerification != null)
            {
                bool isEnabled = chkEnableVerification.IsChecked == true;
                panelVerificationSettings.IsEnabled = isEnabled;
                panelVerificationSettings.Opacity = isEnabled ? 1.0 : 0.5;
            }
        }

        /// <summary>
        /// Copy file with verification and retry logic (Async version)
        /// </summary>
        private async Task<bool> CopyFileWithVerificationAsync(
            string sourcePath,
            string destPath,
            string fileName,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            bool enableVerification = chkEnableVerification?.IsChecked == true;
            var verificationMethod = (VerificationMethod)(cmbVerificationMethod?.SelectedIndex ?? 1);
            var failureAction = (VerificationFailureAction)(cmbVerificationFailure?.SelectedIndex ?? 0);
            
            int maxRetries = (failureAction == VerificationFailureAction.RetryAuto) ? 3 : 1;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Perform the copy
                    await Task.Run(() => File.Copy(sourcePath, destPath, overwrite), cancellationToken).ConfigureAwait(false);
                    
                    // Verify if enabled
                    if (enableVerification)
                    {
                        var verifyResult = await _verificationService.VerifyFileAsync(
                            sourcePath,
                            destPath,
                            verificationMethod,
                            cancellationToken
                        ).ConfigureAwait(false);
                        
                        if (verifyResult.IsValid)
                        {
                            // Verification success
                            Interlocked.Increment(ref _verifiedCount);
                            
                            if (attempt > 1)
                            {
                                LogMessage($"✅ VERIFIED after retry {attempt}: {fileName}");
                            }
                            
                            return true;
                        }
                        else
                        {
                            // Verification failed
                            Interlocked.Increment(ref _verificationFailedCount);
                            LogMessage($"⚠️ VERIFICATION FAILED (attempt {attempt}/{maxRetries}): {fileName} - {verifyResult.ErrorMessage}");
                            
                            if (attempt < maxRetries)
                            {
                                // Delete failed file and retry
                                Interlocked.Increment(ref _verificationRetriedCount);
                                try
                                {
                                    if (File.Exists(destPath))
                                        File.Delete(destPath);
                                }
                                catch { /* Ignore delete errors */ }
                                
                                LogMessage($"🔄 Retrying copy: {fileName} (attempt {attempt + 1}/{maxRetries})");
                                await Task.Delay(500, cancellationToken).ConfigureAwait(false); // Small delay before retry
                                continue;
                            }
                            else
                            {
                                // Max retries reached
                                if (failureAction == VerificationFailureAction.SkipAndLog)
                                {
                                    LogMessage($"⏭️ SKIPPING after {maxRetries} failed attempts: {fileName}");
                                    return false;
                                }
                                else if (failureAction == VerificationFailureAction.StopOperation)
                                {
                                    LogMessage($"⛔ STOPPING operation due to verification failure: {fileName}");
                                    throw new Exception($"Verification failed after {maxRetries} attempts: {verifyResult.ErrorMessage}");
                                }
                                
                                return false;
                            }
                        }
                    }
                    else
                    {
                        // No verification, copy success
                        return true;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogMessage($"❌ COPY ERROR (attempt {attempt}/{maxRetries}): {fileName} - {ex.Message}");
                    
                    if (attempt < maxRetries)
                    {
                        LogMessage($"🔄 Retrying copy: {fileName} (attempt {attempt + 1}/{maxRetries})");
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }
                    
                    throw;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Synchronous version of copy with verification for parallel operations
        /// </summary>
        private bool CopyFileWithVerificationSync(
            string sourcePath,
            string destPath,
            string fileName,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            bool enableVerification = chkEnableVerification?.IsChecked == true;
            var verificationMethod = (VerificationMethod)(cmbVerificationMethod?.SelectedIndex ?? 1);
            var failureAction = (VerificationFailureAction)(cmbVerificationFailure?.SelectedIndex ?? 0);
            
            int maxRetries = (failureAction == VerificationFailureAction.RetryAuto) ? 3 : 1;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Perform the copy (synchronous)
                    File.Copy(sourcePath, destPath, overwrite);
                    
                    // Verify if enabled (synchronous version)
                    if (enableVerification)
                    {
                        var verifyResult = _verificationService.VerifyFile(sourcePath, destPath, verificationMethod);
                        
                        if (verifyResult.IsValid)
                        {
                            // Verification success
                            Interlocked.Increment(ref _verifiedCount);
                            
                            if (attempt > 1)
                            {
                                LogMessage($"✅ VERIFIED after retry {attempt}: {fileName}");
                            }
                            
                            return true;
                        }
                        else
                        {
                            // Verification failed
                            Interlocked.Increment(ref _verificationFailedCount);
                            LogMessage($"⚠️ VERIFICATION FAILED (attempt {attempt}/{maxRetries}): {fileName} - {verifyResult.ErrorMessage}");
                            
                            if (attempt < maxRetries)
                            {
                                // Delete failed file and retry
                                Interlocked.Increment(ref _verificationRetriedCount);
                                try
                                {
                                    if (File.Exists(destPath))
                                        File.Delete(destPath);
                                }
                                catch { /* Ignore delete errors */ }
                                
                                LogMessage($"🔄 Retrying copy: {fileName} (attempt {attempt + 1}/{maxRetries})");
                                // FIX #8 & #11: Use constant for retry delay
                                Task.Delay(RETRY_DELAY_MS).Wait(); // Small delay before retry
                                continue;
                            }
                            else
                            {
                                // Max retries reached
                                if (failureAction == VerificationFailureAction.SkipAndLog)
                                {
                                    LogMessage($"⏭️ SKIPPING after {maxRetries} failed attempts: {fileName}");
                                    return false;
                                }
                                else if (failureAction == VerificationFailureAction.StopOperation)
                                {
                                    LogMessage($"⛔ STOPPING operation due to verification failure: {fileName}");
                                    throw new Exception($"Verification failed after {maxRetries} attempts: {verifyResult.ErrorMessage}");
                                }
                                
                                return false;
                            }
                        }
                    }
                    else
                    {
                        // No verification, copy success
                        return true;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogMessage($"❌ COPY ERROR (attempt {attempt}/{maxRetries}): {fileName} - {ex.Message}");
                    
                    if (attempt < maxRetries)
                    {
                        LogMessage($"🔄 Retrying copy: {fileName} (attempt {attempt + 1}/{maxRetries})");
                        // FIX #8 & #11: Use constant for retry delay (sync version)
                        System.Threading.Thread.Sleep(RETRY_DELAY_MS); // Keep sync for this synchronous method
                        continue;
                    }
                    
                    throw;
                }
            }
            
            return false;
        }

        private void ChkEnableFilters_Changed(object sender, RoutedEventArgs e)
        {
            if (panelFilters != null)
            {
                panelFilters.IsEnabled = chkEnableFilters?.IsChecked == true;
            }
        }

        // Photography Preset Buttons
        private void BtnPreset_AllPhotos(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.AllPhotos;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: All Photos (RAW + JPEG + Images)");
        }

        private void BtnPreset_RawOnly(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.RawOnly;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: RAW Only (All camera brands)");
        }

        private void BtnPreset_JpegOnly(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.JpegOnly;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: JPEG Only");
        }

        private void BtnPreset_NikonRaw(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.NikonRAW;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: Nikon RAW (.nef, .nrw)");
        }

        private void BtnPreset_CanonRaw(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.CanonRAW;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: Canon RAW (.cr2, .cr3, .crw)");
        }

        private void BtnPreset_SonyRaw(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.SonyRAW;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: Sony RAW (.arw, .srf, .sr2)");
        }

        private void BtnPreset_Video(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.VideoOnly;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: Video Files");
        }

        private void BtnPreset_AllMedia(object sender, RoutedEventArgs e)
        {
            txtExtensionFilter.Text = PhotoFormats.Presets.AllMedia;
            chkEnableFilters.IsChecked = true;
            LogMessage("📸 Preset: All Media (Photos + Videos)");
        }

        private bool ShouldProcessFile(string filePath)
        {
            // If filters not enabled, process all files
            if (chkEnableFilters?.IsChecked != true)
                return true;

            try
            {
                var fileInfo = new FileInfo(filePath);
                string extension = fileInfo.Extension.ToLower();
                long fileSizeMB = fileInfo.Length / (1024 * 1024);

                // Check extension filter
                if (!string.IsNullOrWhiteSpace(txtExtensionFilter?.Text))
                {
                    var allowedExtensions = txtExtensionFilter.Text
                        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(e => e.Trim().ToLower())
                        .Where(e => !string.IsNullOrWhiteSpace(e))
                        .ToList();

                    if (allowedExtensions.Any())
                    {
                        bool matched = false;
                        foreach (var ext in allowedExtensions)
                        {
                            string cleanExt = ext.StartsWith(".") ? ext : "." + ext;
                            if (extension == cleanExt)
                            {
                                matched = true;
                                break;
                            }
                        }
                        
                        if (!matched)
                            return false;
                    }
                }

                // Check size filter
                if (long.TryParse(txtMinSize?.Text, out long minSize))
                {
                    if (fileSizeMB < minSize)
                        return false;
                }

                if (long.TryParse(txtMaxSize?.Text, out long maxSize))
                {
                    if (fileSizeMB > maxSize)
                        return false;
                }

                return true;
            }
            catch
            {
                return true; // If error, process file anyway
            }
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(txtSourceFolder.Text))
            {
                MessageBox.Show("📸 Pilih folder sumber terlebih dahulu!\n\nFolder yang berisi foto yang ingin disalin.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(txtSourceFolder.Text))
            {
                MessageBox.Show("📸 Folder sumber tidak ditemukan!\n\nPastikan folder masih ada dan dapat diakses.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtDestFolder.Text))
            {
                MessageBox.Show("💾 Pilih folder tujuan terlebih dahulu!\n\nFolder untuk menyimpan hasil copy.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(txtDestFolder.Text))
            {
                var result = MessageBox.Show(
                    $"💾 Folder tujuan belum ada:\n\n{txtDestFolder.Text}\n\nBuat folder baru?", 
                    "Konfirmasi", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(txtDestFolder.Text);
                        LogMessage($"✅ Folder tujuan dibuat: {txtDestFolder.Text}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"❌ Tidak bisa membuat folder tujuan:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            if (_isPasteMode)
            {
                if (txtPasteList == null || string.IsNullOrWhiteSpace(txtPasteList.Text))
                {
                    MessageBox.Show("Paste daftar file terlebih dahulu di textarea!", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(txtFileList.Text) || !File.Exists(txtFileList.Text))
                {
                    MessageBox.Show("Pilih File List (.txt) yang valid!", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        private void SetUIEnabled(bool enabled)
        {
            btnStart.IsEnabled = enabled;
            btnStop.IsEnabled = !enabled;
            txtSourceFolder.IsEnabled = enabled;
            txtDestFolder.IsEnabled = enabled;
            txtFileList.IsEnabled = enabled;
            chkIgnoreExtension.IsEnabled = enabled;
            chkCaseInsensitive.IsEnabled = enabled;
            chkSkipExisting.IsEnabled = enabled;
            cmbDuplicateHandling.IsEnabled = enabled;
            btnPreview.IsEnabled = enabled;
        }

        private void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            Dispatcher.Invoke(() =>
            {
                // Check if we need to clear old logs to prevent memory growth
                if (_currentLogLines >= MAX_LOG_LINES)
                {
                    ClearOldLogs();
                }
                
                txtLog.AppendText($"[{timestamp}] {message}\n");
                txtLog.ScrollToEnd();
                _currentLogLines++;
            });
        }
        
        /// <summary>
        /// Clear old log entries to prevent memory growth (FIX #1)
        /// </summary>
        private void ClearOldLogs()
        {
            try
            {
                var lines = txtLog.Text.Split('\n');
                if (lines.Length > MAX_LOG_LINES)
                {
                    // Keep last 70% of logs + add separator
                    int keepLines = (int)(MAX_LOG_LINES * 0.7);
                    var recentLines = lines.Skip(lines.Length - keepLines).ToArray();
                    
                    txtLog.Clear();
                    txtLog.AppendText("========== LOG CLEARED TO PREVENT MEMORY GROWTH ==========\n");
                    txtLog.AppendText(string.Join("\n", recentLines));
                    
                    _currentLogLines = keepLines + 1; // +1 for separator
                }
            }
            catch (Exception ex)
            {
                // Fallback: just clear everything if string manipulation fails
                txtLog.Clear();
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] Log cleared due to memory management: {ex.Message}\n");
                _currentLogLines = 1;
            }
        }
        
        /// <summary>
        /// Start batched UI updates to improve performance (FIX #2)
        /// </summary>
        private void StartBatchedUIUpdates(List<string> fileList)
        {
            _uiBatchUpdateTimer = new System.Threading.Timer(_ =>
            {
                if (_uiUpdatePending) return; // Skip if update already pending
                
                _uiUpdatePending = true;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Batch all UI updates together
                        int currentFound = _verifiedCount; // Use verification count as proxy for found
                        int currentSkipped = 0; // Will be calculated from other counters
                        int currentNotFound = _notFoundFiles.Count;
                        int totalFiles = fileList.Count;
                        
                        // Update counters
                        txtFound.Text = currentFound.ToString();
                        txtSkipped.Text = currentSkipped.ToString(); 
                        txtNotFound.Text = currentNotFound.ToString();
                        txtTotal.Text = totalFiles.ToString();
                        
                        // Update progress (FIX #4: Division by zero protection)
                        int processed = currentFound + currentSkipped + currentNotFound;
                        if (totalFiles > 0)
                        {
                            double progress = Math.Min(100.0, Math.Max(0.0, (processed * 100.0) / totalFiles));
                            progressBar.Value = progress;
                            txtProgress.Text = $"{(int)progress}%";
                        }
                        else
                        {
                            progressBar.Value = 0;
                            txtProgress.Text = "0%";
                        }
                        
                        // Update bytes display
                        UpdateBytesDisplay();
                        
                        _uiUpdatePending = false;
                    }
                    catch
                    {
                        _uiUpdatePending = false;
                    }
                }));
            }, null, 0, UI_UPDATE_INTERVAL_MS);
        }
        
        /// <summary>
        /// Stop batched UI updates (FIX #2)
        /// </summary>
        private void StopBatchedUIUpdates()
        {
            _uiBatchUpdateTimer?.Dispose();
            _uiBatchUpdateTimer = null;
            _uiUpdatePending = false;
            
            // Final UI update
            Dispatcher.Invoke(() =>
            {
                UpdateBytesDisplay();
            });
        }
        
        /// <summary>
        /// Build consistent destination path (FIX #6)
        /// </summary>
        private string BuildDestinationPath(string destinationFolder, string sourcePath)
        {
            try
            {
                // Ensure destination folder path is clean
                var cleanDestFolder = Path.GetFullPath(destinationFolder.Trim());
                
                // Get just the filename from source
                var fileName = Path.GetFileName(sourcePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new ArgumentException("Source path does not contain a valid filename", nameof(sourcePath));
                }
                
                // Combine paths safely
                return Path.Combine(cleanDestFolder, fileName);
            }
            catch (Exception ex)
            {
                // Fallback to basic combination if path operations fail
                LogMessage($"⚠️ Path building warning: {ex.Message}. Using fallback method.");
                return Path.Combine(destinationFolder, Path.GetFileName(sourcePath) ?? "unknown_file");
            }
        }
        
        /// <summary>
        /// Check for resumable operations on application startup
        /// </summary>
        private async Task CheckForResumableOperationsAsync()
        {
            try
            {
                await Task.Delay(1000).ConfigureAwait(false); // Give UI time to fully load
                
                var availableSessions = await _resumeService.GetAvailableResumeSessionsAsync().ConfigureAwait(false);
                
                if (availableSessions?.Count > 0)
                {
                    // Show resume dialog on UI thread
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ShowResumeDialog(availableSessions.First());
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for resumable operations: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show resume dialog and handle user choice
        /// </summary>
        private async void ShowResumeDialog(CopyCheckpoint checkpoint)
        {
            try
            {
                var resumeDialog = new Windows.ResumeDialog(checkpoint)
                {
                    Owner = this
                };

                if (resumeDialog.ShowDialog() == true)
                {
                    switch (resumeDialog.UserAction)
                    {
                        case Windows.ResumeAction.Resume:
                            LogMessage($"🔄 Resuming copy session: {checkpoint.SessionId}");
                            await ResumeOperation(checkpoint).ConfigureAwait(false);
                            break;

                        case Windows.ResumeAction.Restart:
                            LogMessage($"🔄 Restarting copy session: {checkpoint.SessionId}");
                            await _resumeService.DeleteCheckpointAsync(checkpoint.SessionId).ConfigureAwait(false);
                            
                            // Restore settings and let user manually start
                            RestoreSettingsFromCheckpoint(checkpoint);
                            break;

                        case Windows.ResumeAction.Cancel:
                            LogMessage($"⏹️ Resume cancelled, checkpoint preserved: {checkpoint.SessionId}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error handling resume dialog: {ex.Message}",
                    "Resume Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
        
        /// <summary>
        /// Resume copy operation from checkpoint
        /// </summary>
        private async Task ResumeOperation(CopyCheckpoint checkpoint)
        {
            try
            {
                if (!ValidateCheckpointForResume(checkpoint))
                    return;

                // Restore session state
                _currentSessionId = checkpoint.SessionId;
                _sessionStartTime = checkpoint.StartTime;
                
                // Restore processed files lists
                _sessionProcessedFiles.Clear();
                _sessionProcessedFiles.AddRange(checkpoint.ProcessedFiles ?? new List<string>());
                
                _sessionSkippedFiles.Clear();
                _sessionSkippedFiles.AddRange(checkpoint.SkippedFiles ?? new List<string>());
                
                _sessionFailedFiles.Clear();
                _sessionFailedFiles.AddRange(checkpoint.FailedFiles ?? new List<string>());

                // Restore UI settings
                RestoreSettingsFromCheckpoint(checkpoint);
                
                // Initialize file index for resume operation
                LogMessage("🔍 Scanning source folder for resume...");
                var sourceFiles = await Task.Run(() =>
                    _fileService.ScanFolder(checkpoint.SourceFolder, _cancellationTokenSource?.Token ?? CancellationToken.None)
                , _cancellationTokenSource?.Token ?? CancellationToken.None).ConfigureAwait(false);

                _fileIndex = _fileService.BuildFileIndex(
                    sourceFiles,
                    checkpoint.Settings?.IgnoreExtension ?? false,
                    checkpoint.Settings?.CaseInsensitive ?? true
                );
                
                LogMessage($"📇 Resume index built with {_fileIndex.Count} entries");
                
                // Get remaining files to process
                var remainingFiles = checkpoint.RemainingFiles;
                
                LogMessage($"🔄 Resuming with {remainingFiles.Count} remaining files");
                LogMessage($"📊 Previous progress: {checkpoint.ProgressPercentage:F1}% completed");

                // Set UI state
                SetUIEnabled(false);
                _cancellationTokenSource = new CancellationTokenSource();
                _stopwatch.Restart();
                _timer?.Start();

                // Update totals
                txtTotal.Text = checkpoint.OriginalFileList.Count.ToString();
                txtFound.Text = checkpoint.ProcessedFiles?.Count.ToString() ?? "0";
                txtSkipped.Text = checkpoint.SkippedFiles?.Count.ToString() ?? "0";
                txtNotFound.Text = checkpoint.FailedFiles?.Count.ToString() ?? "0";
                
                _copiedBytes = checkpoint.ProcessedBytes;
                _totalBytes = checkpoint.TotalBytes;

                // Resume the copy operation
                await ProcessResumedFileCopy(remainingFiles, checkpoint).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Resume operation failed: {ex.Message}");
                MessageBox.Show(
                    $"Failed to resume operation:\n{ex.Message}",
                    "Resume Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                SetUIEnabled(true);
                _stopwatch.Stop();
                _timer?.Stop();
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            LogMessage("⚠️ Menghentikan proses...");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                FileName = $"autocopy_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, txtLog.Text);
                MessageBox.Show("Log berhasil disimpan!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExportNotFound_Click(object sender, RoutedEventArgs e)
        {
            if (!_notFoundFiles.Any())
            {
                MessageBox.Show("Tidak ada file yang tidak ditemukan.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt",
                FileName = "not_found.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllLines(dialog.FileName, _notFoundFiles);
                MessageBox.Show($"{_notFoundFiles.Count} file berhasil di-export!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                LogMessage($"📄 Exported {_notFoundFiles.Count} not found files to {dialog.FileName}");
            }
        }
        
        /// <summary>
        /// Validate checkpoint for resume capability
        /// </summary>
        private bool ValidateCheckpointForResume(CopyCheckpoint checkpoint)
        {
            try
            {
                if (checkpoint == null || !checkpoint.IsValidForResume)
                {
                    MessageBox.Show(
                        "Checkpoint is invalid or corrupted.",
                        "Resume Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                // Validate folders exist
                if (!Directory.Exists(checkpoint.SourceFolder))
                {
                    MessageBox.Show(
                        $"Source folder no longer exists:\n{checkpoint.SourceFolder}",
                        "Resume Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                if (!Directory.Exists(checkpoint.DestinationFolder))
                {
                    var result = MessageBox.Show(
                        $"Destination folder does not exist:\n{checkpoint.DestinationFolder}\n\nCreate it now?",
                        "Create Destination?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Directory.CreateDirectory(checkpoint.DestinationFolder);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"Failed to create destination folder:\n{ex.Message}",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error validating checkpoint: {ex.Message}",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return false;
            }
        }
        
        /// <summary>
        /// Restore UI settings from checkpoint
        /// </summary>
        private void RestoreSettingsFromCheckpoint(CopyCheckpoint checkpoint)
        {
            try
            {
                if (checkpoint?.Settings == null)
                    return;

                var settings = checkpoint.Settings;

                // Restore folder paths
                if (txtSourceFolder != null)
                    txtSourceFolder.Text = checkpoint.SourceFolder;
                
                if (txtDestFolder != null)
                    txtDestFolder.Text = checkpoint.DestinationFolder;

                // Restore copy settings
                if (chkIgnoreExtension != null)
                    chkIgnoreExtension.IsChecked = settings.IgnoreExtension;
                
                if (chkCaseInsensitive != null)
                    chkCaseInsensitive.IsChecked = settings.CaseInsensitive;
                
                if (cmbDuplicateHandling != null)
                    cmbDuplicateHandling.SelectedIndex = Math.Max(0, Math.Min(2, settings.DuplicateHandling));

                // Restore parallel settings
                if (chkParallelCopy != null)
                    chkParallelCopy.IsChecked = settings.EnableParallel;
                
                if (sliderThreads != null)
                    sliderThreads.Value = Math.Max(1, Math.Min(16, settings.ParallelThreads));

                // Restore filter settings
                if (chkEnableFilters != null)
                    chkEnableFilters.IsChecked = settings.EnableFilters;
                
                if (txtExtensionFilter != null)
                    txtExtensionFilter.Text = settings.ExtensionFilter ?? string.Empty;
                
                if (txtMinSize != null)
                    txtMinSize.Text = settings.MinSizeMB ?? "0";
                
                if (txtMaxSize != null)
                    txtMaxSize.Text = settings.MaxSizeMB ?? "1000";

                // Restore verification settings
                if (chkEnableVerification != null)
                    chkEnableVerification.IsChecked = settings.EnableVerification;
                
                if (cmbVerificationMethod != null)
                    cmbVerificationMethod.SelectedIndex = Math.Max(0, Math.Min(3, settings.VerificationMethod));
                
                if (cmbVerificationFailure != null)
                    cmbVerificationFailure.SelectedIndex = Math.Max(0, Math.Min(2, settings.VerificationFailureAction));

                // Set paste mode if needed
                if (checkpoint.IsPasteMode)
                {
                    _isPasteMode = true;
                    if (radioPasteMode != null)
                        radioPasteMode.IsChecked = true;
                    
                    // Restore original file list to paste text
                    if (txtPasteList != null && checkpoint.OriginalFileList != null)
                        txtPasteList.Text = string.Join("\n", checkpoint.OriginalFileList);
                }
                else
                {
                    _isPasteMode = false;
                    if (radioFileMode != null)
                        radioFileMode.IsChecked = true;
                }

                LogMessage("⚙️ Settings restored from checkpoint");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Warning: Failed to restore some settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Save current checkpoint for resume capability
        /// </summary>
        private async Task SaveCurrentCheckpointAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_currentSessionId))
                    return;

                var settings = new CopySettings
                {
                    IgnoreExtension = chkIgnoreExtension?.IsChecked == true,
                    CaseInsensitive = chkCaseInsensitive?.IsChecked == true,
                    DuplicateHandling = cmbDuplicateHandling?.SelectedIndex ?? 0,
                    EnableParallel = chkParallelCopy?.IsChecked == true,
                    ParallelThreads = _parallelThreadCount,
                    EnableFilters = chkEnableFilters?.IsChecked == true,
                    ExtensionFilter = txtExtensionFilter?.Text ?? string.Empty,
                    MinSizeMB = txtMinSize?.Text ?? "0",
                    MaxSizeMB = txtMaxSize?.Text ?? "1000",
                    EnableVerification = chkEnableVerification?.IsChecked == true,
                    VerificationMethod = cmbVerificationMethod?.SelectedIndex ?? 2,
                    VerificationFailureAction = cmbVerificationFailure?.SelectedIndex ?? 0
                };

                var checkpoint = _resumeService.CreateCheckpoint(
                    _currentSessionId,
                    _sessionStartTime,
                    txtSourceFolder?.Text ?? string.Empty,
                    txtDestFolder?.Text ?? string.Empty,
                    GetCurrentFileList(),
                    new List<string>(_sessionProcessedFiles),
                    new List<string>(_sessionFailedFiles),
                    new List<string>(_sessionSkippedFiles),
                    _copiedBytes,
                    _totalBytes,
                    settings,
                    _isPasteMode
                );

                bool saved = await _resumeService.SaveCheckpointAsync(checkpoint).ConfigureAwait(false);
                
                if (saved)
                {
                    System.Diagnostics.Debug.WriteLine($"Checkpoint saved: {_sessionProcessedFiles.Count} processed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save checkpoint: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get current file list being processed
        /// </summary>
        private List<string> GetCurrentFileList()
        {
            try
            {
                if (_isPasteMode && txtPasteList?.Text != null)
                {
                    var result = new List<string>();
                    var splitLines = txtPasteList.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var line in splitLines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed))
                        {
                            result.Add(trimmed);
                        }
                    }
                    
                    return result;
                }
                else if (!string.IsNullOrWhiteSpace(txtFileList?.Text) && File.Exists(txtFileList.Text))
                {
                    var result = new List<string>();
                    var allLines = File.ReadAllLines(txtFileList.Text);
                    
                    foreach (var line in allLines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            result.Add(line.Trim());
                        }
                    }
                    
                    return result;
                }
                
                return new List<string>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get current file list: {ex.Message}");
                return new List<string>();
            }
        }
        
        /// <summary>
        /// Process resumed file copy operation
        /// </summary>
        private async Task ProcessResumedFileCopy(List<string> remainingFiles, CopyCheckpoint checkpoint)
        {
            try
            {
                if (remainingFiles == null || remainingFiles.Count == 0)
                {
                    LogMessage("✅ No remaining files to process");
                    return;
                }

                LogMessage($"🔄 Processing {remainingFiles.Count} remaining files");
                
                // Restore progress counters from checkpoint
                int found = checkpoint.ProcessedFiles?.Count ?? 0;
                int skipped = checkpoint.SkippedFiles?.Count ?? 0;
                int notFound = checkpoint.FailedFiles?.Count ?? 0;

                // Start batched UI updates
                StartBatchedUIUpdates(checkpoint.OriginalFileList);

                // Process remaining files using existing logic
                var duplicateHandling = (DuplicateHandling)(checkpoint.Settings?.DuplicateHandling ?? 0);
                
                if (checkpoint.Settings?.EnableParallel == true && remainingFiles.Count > 1)
                {
                    LogMessage($"⚙️ Resume Mode: Parallel ({checkpoint.Settings.ParallelThreads} threads)");
                    await ProcessResumedParallelCopy(remainingFiles, duplicateHandling, _cancellationTokenSource?.Token ?? CancellationToken.None);
                }
                else
                {
                    LogMessage("⚙️ Resume Mode: Sequential");
                    await ProcessResumedSequentialCopy(remainingFiles, duplicateHandling, _cancellationTokenSource?.Token ?? CancellationToken.None);
                }

                // Clean up checkpoint on successful completion
                await _resumeService.DeleteCheckpointAsync(checkpoint.SessionId).ConfigureAwait(false);
                LogMessage($"🗑️ Checkpoint cleaned up: {checkpoint.SessionId}");
            }
            catch (OperationCanceledException)
            {
                LogMessage("⏹️ Resume operation cancelled");
                await SaveCurrentCheckpointAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Resume operation error: {ex.Message}");
                await SaveCurrentCheckpointAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                StopBatchedUIUpdates();
            }
        }
        
        /// <summary>
        /// Process resumed sequential copy with session tracking
        /// </summary>
        private Task ProcessResumedSequentialCopy(List<string> remainingFiles, DuplicateHandling duplicateHandling, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var sourceFolder = txtSourceFolder?.Text ?? string.Empty;
                var destFolder = txtDestFolder?.Text ?? string.Empty;
            
            int found = 0, skipped = 0, notFound = 0;

            foreach (var fileName in remainingFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var searchKey = _fileService.GetSearchKey(fileName, 
                        chkIgnoreExtension?.IsChecked == true,
                        chkCaseInsensitive?.IsChecked == true);

                    if (_fileIndex != null && _fileIndex.ContainsKey(searchKey))
                    {
                        var sourcePath = _fileIndex[searchKey];
                        var destPath = BuildDestinationPath(destFolder, Path.GetFileName(sourcePath));

                        // Handle existing files
                        if (File.Exists(destPath))
                        {
                            if (duplicateHandling == DuplicateHandling.Skip)
                            {
                                Interlocked.Increment(ref skipped);
                                _sessionSkippedFiles.Add(fileName);
                                continue;
                            }
                            else if (duplicateHandling == DuplicateHandling.Rename)
                            {
                                destPath = _fileService.GetUniqueFileName(destPath);
                            }
                        }

                        // Copy file
                        File.Copy(sourcePath, destPath, true);

                        // Verify if enabled
                        bool copySuccessful = true;
                        if (chkEnableVerification?.IsChecked == true)
                        {
                            var verificationMethod = (VerificationMethod)(cmbVerificationMethod?.SelectedIndex ?? 1);
                            var verifyResult = _verificationService.VerifyFile(sourcePath, destPath, verificationMethod);
                            copySuccessful = verifyResult.IsValid;
                            
                            if (!copySuccessful)
                            {
                                var failureAction = (VerificationFailureAction)(cmbVerificationFailure?.SelectedIndex ?? 0);
                                if (failureAction == VerificationFailureAction.SkipAndLog)
                                {
                                    Interlocked.Increment(ref skipped);
                                    _sessionSkippedFiles.Add(fileName);
                                    continue;
                                }
                            }
                        }

                        if (copySuccessful)
                        {
                            Interlocked.Increment(ref found);
                            _sessionProcessedFiles.Add(fileName);
                            
                            // Save checkpoint periodically
                            if (_resumeService.ShouldSaveCheckpoint(_sessionProcessedFiles.Count))
                            {
                                _ = SaveCurrentCheckpointAsync();
                            }
                            
                            LogMessage($"✅ RESUMED: {fileName} -> {Path.GetFileName(destPath)}");
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref notFound);
                        _notFoundFiles.Add(fileName);
                        _sessionFailedFiles.Add(fileName);
                        LogMessage($"❌ NOT FOUND: {fileName}");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Interlocked.Increment(ref notFound);
                    _notFoundFiles.Add(fileName);
                    _sessionFailedFiles.Add(fileName);
                    LogMessage($"❌ ERROR processing {fileName}: {ex.Message}");
                }
            }

                LogMessage($"🔄 Resume completed: {found} processed, {skipped} skipped, {notFound} failed");
            }, cancellationToken);
        }
        
        /// <summary>
        /// Process resumed parallel copy with session tracking
        /// </summary>
        private async Task ProcessResumedParallelCopy(List<string> remainingFiles, DuplicateHandling duplicateHandling, CancellationToken cancellationToken)
        {
            var sourceFolder = txtSourceFolder?.Text ?? string.Empty;
            var destFolder = txtDestFolder?.Text ?? string.Empty;
            
            int found = 0, skipped = 0, notFound = 0;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _parallelThreadCount,
                CancellationToken = cancellationToken
            };

            await Task.Run(() =>
            {
                Parallel.ForEach(remainingFiles, parallelOptions, fileName =>
                {
                    try
                    {
                        var searchKey = _fileService.GetSearchKey(fileName,
                            chkIgnoreExtension?.IsChecked == true,
                            chkCaseInsensitive?.IsChecked == true);

                        if (_fileIndex != null && _fileIndex.ContainsKey(searchKey))
                        {
                            var sourcePath = _fileIndex[searchKey];
                            var destPath = BuildDestinationPath(destFolder, Path.GetFileName(sourcePath));

                            // Handle existing files
                            if (File.Exists(destPath))
                            {
                                if (duplicateHandling == DuplicateHandling.Skip)
                                {
                                    Interlocked.Increment(ref skipped);
                                    lock (_sessionSkippedFiles)
                                    {
                                        _sessionSkippedFiles.Add(fileName);
                                    }
                                    return;
                                }
                                else if (duplicateHandling == DuplicateHandling.Rename)
                                {
                                    destPath = _fileService.GetUniqueFileName(destPath);
                                }
                            }

                            // Copy file
                            File.Copy(sourcePath, destPath, true);

                            // Verify if enabled
                            bool copySuccessful = true;
                            if (chkEnableVerification?.IsChecked == true)
                            {
                                var verificationMethod = (VerificationMethod)(cmbVerificationMethod?.SelectedIndex ?? 1);
                                var verifyResult = _verificationService.VerifyFile(sourcePath, destPath, verificationMethod);
                                copySuccessful = verifyResult.IsValid;
                                
                                if (!copySuccessful)
                                {
                                    var failureAction = (VerificationFailureAction)(cmbVerificationFailure?.SelectedIndex ?? 0);
                                    if (failureAction == VerificationFailureAction.SkipAndLog)
                                    {
                                        Interlocked.Increment(ref skipped);
                                        lock (_sessionSkippedFiles)
                                        {
                                            _sessionSkippedFiles.Add(fileName);
                                        }
                                        return;
                                    }
                                }
                            }

                            if (copySuccessful)
                            {
                                Interlocked.Increment(ref found);
                                lock (_sessionProcessedFiles)
                                {
                                    _sessionProcessedFiles.Add(fileName);
                                }
                                
                                // Update progress tracking
                                var fileInfo = new FileInfo(sourcePath);
                                Interlocked.Add(ref _copiedBytes, fileInfo.Length);
                                
                                // Periodic checkpoint save (thread-safe)
                                if (_resumeService.ShouldSaveCheckpoint(_sessionProcessedFiles.Count))
                                {
                                    _ = Task.Run(SaveCurrentCheckpointAsync);
                                }
                                
                                LogMessage($"✅ RESUMED: {fileName} -> {Path.GetFileName(destPath)}");
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref notFound);
                            _notFoundFiles.Add(fileName);
                            lock (_sessionFailedFiles)
                            {
                                _sessionFailedFiles.Add(fileName);
                            }
                            LogMessage($"❌ NOT FOUND: {fileName}");
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Interlocked.Increment(ref notFound);
                        _notFoundFiles.Add(fileName);
                        lock (_sessionFailedFiles)
                        {
                            _sessionFailedFiles.Add(fileName);
                        }
                        LogMessage($"❌ ERROR processing {fileName}: {ex.Message}");
                    }
                });
            }, cancellationToken).ConfigureAwait(false);

            LogMessage($"🔄 Resume completed: {found} processed, {skipped} skipped, {notFound} failed");
        }
        
        /// <summary>
        /// MacOS Style Status Icon Management
        /// </summary>
        private void ShowStatusIcon(string iconType)
        {
            // Hide all icons first
            iconSuccess.Visibility = Visibility.Collapsed;
            iconWarning.Visibility = Visibility.Collapsed;
            iconError.Visibility = Visibility.Collapsed;
            iconProcessing.Visibility = Visibility.Collapsed;
            
            // Show requested icon
            switch (iconType.ToLower())
            {
                case "success":
                    iconSuccess.Visibility = Visibility.Visible;
                    break;
                case "warning":
                    iconWarning.Visibility = Visibility.Visible;
                    break;
                case "error":
                    iconError.Visibility = Visibility.Visible;
                    break;
                case "processing":
                    iconProcessing.Visibility = Visibility.Visible;
                    break;
                case "none":
                default:
                    // All icons hidden
                    break;
            }
        }
        
        private void HideAllStatusIcons()
        {
            iconSuccess.Visibility = Visibility.Collapsed;
            iconWarning.Visibility = Visibility.Collapsed;
            iconError.Visibility = Visibility.Collapsed;
            iconProcessing.Visibility = Visibility.Collapsed;
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            var config = new AppConfig
            {
                SourceFolder = txtSourceFolder.Text,
                DestinationFolder = txtDestFolder.Text,
                FileListPath = txtFileList.Text,
                IgnoreExtension = chkIgnoreExtension.IsChecked == true,
                CaseInsensitive = chkCaseInsensitive.IsChecked == true,
                DuplicateHandling = cmbDuplicateHandling.SelectedIndex,
                ParallelThreads = _parallelThreadCount,
                EnableParallel = chkParallelCopy?.IsChecked == true,
                EnableFilters = chkEnableFilters?.IsChecked == true,
                ExtensionFilter = txtExtensionFilter?.Text ?? "",
                MinSizeMB = txtMinSize?.Text ?? "0",
                MaxSizeMB = txtMaxSize?.Text ?? "1000",
                IsDarkMode = btnThemeToggle?.IsChecked == true,
                EnableVerification = chkEnableVerification?.IsChecked == true,
                VerificationMethod = cmbVerificationMethod?.SelectedIndex ?? 1,
                VerificationFailureAction = cmbVerificationFailure?.SelectedIndex ?? 0
            };

            try
            {
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(CONFIG_FILE, json);
                MessageBox.Show("Konfigurasi berhasil disimpan!", "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                LogMessage("💾 Konfigurasi disimpan");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error menyimpan konfigurasi: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            if (!File.Exists(CONFIG_FILE))
                return;

            try
            {
                string json = File.ReadAllText(CONFIG_FILE);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);

                if (config != null)
                {
                    txtSourceFolder.Text = config.SourceFolder ?? "";
                    txtDestFolder.Text = config.DestinationFolder ?? "";
                    txtFileList.Text = config.FileListPath ?? "";
                    chkIgnoreExtension.IsChecked = config.IgnoreExtension;
                    chkCaseInsensitive.IsChecked = config.CaseInsensitive;
                    cmbDuplicateHandling.SelectedIndex = config.DuplicateHandling;
                    
                    // Load v1.1.0 settings
                    _parallelThreadCount = config.ParallelThreads;
                    if (sliderThreads != null)
                        sliderThreads.Value = _parallelThreadCount;
                    if (chkParallelCopy != null)
                        chkParallelCopy.IsChecked = config.EnableParallel;
                    if (chkEnableFilters != null)
                        chkEnableFilters.IsChecked = config.EnableFilters;
                    if (txtExtensionFilter != null)
                        txtExtensionFilter.Text = config.ExtensionFilter;
                    if (txtMinSize != null)
                        txtMinSize.Text = config.MinSizeMB;
                    if (txtMaxSize != null)
                        txtMaxSize.Text = config.MaxSizeMB;

                    // Load v1.2.0 theme setting
                    if (btnThemeToggle != null)
                    {
                        btnThemeToggle.IsChecked = config.IsDarkMode;
                        SwitchTheme(config.IsDarkMode);
                    }

                    // Load v1.3.0 verification settings
                    if (chkEnableVerification != null)
                        chkEnableVerification.IsChecked = config.EnableVerification;
                    
                    if (cmbVerificationMethod != null)
                        cmbVerificationMethod.SelectedIndex = config.VerificationMethod;
                    
                    if (cmbVerificationFailure != null)
                        cmbVerificationFailure.SelectedIndex = config.VerificationFailureAction;

                    LogMessage("✅ Konfigurasi dimuat dari file");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error loading config: {ex.Message}");
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Path.GetExtension(files[0]).ToLower() == ".txt")
                {
                    txtFileList.Text = files[0];
                    LogMessage($"📁 File list dropped: {files[0]}");
                }
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void TxtFileList_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && Path.GetExtension(files[0]).ToLower() == ".txt")
                {
                    txtFileList.Text = files[0];
                    LogMessage($"📁 File list dropped: {files[0]}");
                }
            }
        }

        private void TxtFileList_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void RadioFileMode_Checked(object sender, RoutedEventArgs e)
        {
            if (panelFileMode != null && panelPasteMode != null)
            {
                _isPasteMode = false;
                panelFileMode.Visibility = Visibility.Visible;
                panelPasteMode.Visibility = Visibility.Collapsed;
                LogMessage("📄 Mode: File List dari file .txt");
            }
        }

        private void RadioPasteMode_Checked(object sender, RoutedEventArgs e)
        {
            if (panelFileMode != null && panelPasteMode != null)
            {
                _isPasteMode = true;
                panelFileMode.Visibility = Visibility.Collapsed;
                panelPasteMode.Visibility = Visibility.Visible;
                LogMessage("📋 Mode: Paste langsung file list");
                UpdatePasteCount();
            }
        }

        private System.Windows.Threading.DispatcherTimer? _pasteCountDebounceTimer;
        
        private void TxtPasteList_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // FIX #3: Proper timer disposal to prevent resource leaks
            _pasteCountDebounceTimer?.Stop();
            
            // Dispose previous timer before creating new one
            if (_pasteCountDebounceTimer != null)
            {
                _pasteCountDebounceTimer.Tick -= PasteCountDebounceTimer_Tick; // Remove handler
                _pasteCountDebounceTimer = null; // Allow GC
            }
            
            _pasteCountDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DEBOUNCE_DELAY_MS) // FIX #11: Use constant
            };
            _pasteCountDebounceTimer.Tick += PasteCountDebounceTimer_Tick;
            _pasteCountDebounceTimer.Start();
        }
        
        /// <summary>
        /// Separate event handler for timer tick to enable proper cleanup (FIX #3)
        /// </summary>
        private void PasteCountDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _pasteCountDebounceTimer?.Stop();
            UpdatePasteCount();
        }

        private void UpdatePasteCount()
        {
            // FIX #9: Enhanced null safety with detailed checks
            if (txtPasteList?.Text == null || txtPasteCount == null || txtPasteInfo == null)
            {
                // Set safe defaults if controls are null
                if (txtPasteCount != null) txtPasteCount.Text = "0";
                if (txtPasteInfo != null) txtPasteInfo.Text = "No data available";
                return;
            }

            // FIX #10: Optimize LINQ chain to reduce intermediate collections
            var lines = new List<string>();
            var splitLines = txtPasteList.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in splitLines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    lines.Add(trimmed);
                }
            }

            int count = lines.Count;
            txtPasteCount.Text = count.ToString();

            // Update info with file statistics
            if (count > 0)
            {
                // FIX #5: Optimize string operations with StringBuilder and reduce LINQ chains
                var extensionCounts = new Dictionary<string, int>();
                
                // Single pass through lines instead of multiple LINQ operations
                foreach (var line in lines)
                {
                    var ext = Path.GetExtension(line)?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(ext))
                    {
                        extensionCounts[ext] = extensionCounts.GetValueOrDefault(ext, 0) + 1;
                    }
                }

                if (extensionCounts.Count > 0)
                {
                    // Get top 3 extensions efficiently
                    var topExtensions = extensionCounts
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(3);
                    
                    // Use StringBuilder for efficient string building
                    var sb = new System.Text.StringBuilder("📊 File types: ");
                    bool first = true;
                    
                    foreach (var ext in topExtensions)
                    {
                        if (!first) sb.Append(", ");
                        sb.Append($"{ext.Key} ({ext.Value})");
                        first = false;
                    }
                    
                    txtPasteInfo.Text = sb.ToString();
                }
                else
                {
                    txtPasteInfo.Text = "⚠️ No file extensions detected";
                }
            }
            else
            {
                txtPasteInfo.Text = "Paste file list atau ketik manual, satu file per baris";
            }
        }

        private void BtnPasteFromClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clipboardText = Clipboard.GetText();
                    
                    // Smart paste - append or replace
                    if (string.IsNullOrWhiteSpace(txtPasteList.Text))
                    {
                        txtPasteList.Text = clipboardText;
                        LogMessage("📋 Paste dari clipboard: " + clipboardText.Split('\n').Length + " lines");
                    }
                    else
                    {
                        var result = MessageBox.Show(
                            "Textarea sudah berisi text. Append atau Replace?\n\nYes = Append (tambah di bawah)\nNo = Replace (timpa semua)",
                            "Paste Mode",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question
                        );

                        if (result == MessageBoxResult.Yes)
                        {
                            txtPasteList.Text += "\n" + clipboardText;
                            LogMessage("📋 Append dari clipboard");
                        }
                        else if (result == MessageBoxResult.No)
                        {
                            txtPasteList.Text = clipboardText;
                            LogMessage("📋 Replace dengan clipboard");
                        }
                    }
                    
                    UpdatePasteCount();
                }
                else
                {
                    MessageBox.Show("Clipboard kosong atau tidak berisi text!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error paste dari clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearPaste_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtPasteList.Text))
            {
                var result = MessageBox.Show(
                    $"Hapus semua text ({txtPasteCount.Text} files)?",
                    "Confirm Clear",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    txtPasteList.Clear();
                    LogMessage("✂️ Paste list di-clear");
                    UpdatePasteCount();
                }
            }
        }

        private void BtnSavePasteToFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPasteList.Text))
            {
                MessageBox.Show("Paste list kosong! Tidak ada yang bisa disimpan.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = $"filelist_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Save Paste List to File"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Clean and save
                    var lines = txtPasteList.Text
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l));

                    File.WriteAllLines(dialog.FileName, lines);
                    LogMessage($"💾 Paste list saved to: {dialog.FileName} ({txtPasteCount.Text} files)");
                    MessageBox.Show($"Berhasil disimpan!\n\n{dialog.FileName}\n\nTotal: {txtPasteCount.Text} files", 
                        "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnLoadPasteFromFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Load File List"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string fileContent = File.ReadAllText(dialog.FileName);
                    
                    // Smart load - append or replace
                    if (!string.IsNullOrWhiteSpace(txtPasteList.Text))
                    {
                        var result = MessageBox.Show(
                            "Textarea sudah berisi text. Append atau Replace?\n\nYes = Append (tambah di bawah)\nNo = Replace (timpa semua)",
                            "Load Mode",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question
                        );

                        if (result == MessageBoxResult.Yes)
                        {
                            txtPasteList.Text += "\n" + fileContent;
                            LogMessage($"📂 Append dari file: {dialog.FileName}");
                        }
                        else if (result == MessageBoxResult.No)
                        {
                            txtPasteList.Text = fileContent;
                            LogMessage($"📂 Load dari file: {dialog.FileName}");
                        }
                    }
                    else
                    {
                        txtPasteList.Text = fileContent;
                        LogMessage($"📂 Load dari file: {dialog.FileName}");
                    }
                    
                    UpdatePasteCount();
                    MessageBox.Show($"Berhasil load file!\n\nTotal: {txtPasteCount.Text} files", 
                        "Sukses", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            try
            {
                List<string> fileList;
                
                if (_isPasteMode)
                {
                    fileList = await Task.Run(() =>
                        txtPasteList.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList()
                    ).ConfigureAwait(false);
                }
                else
                {
                    fileList = await Task.Run(() => 
                        File.ReadAllLines(txtFileList.Text)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Trim())
                            .ToList()
                    ).ConfigureAwait(false);
                }

                LogMessage("🔍 Memulai preview matching...");
                
                var sourceFiles = await Task.Run(() => 
                    _fileService.ScanFolder(txtSourceFolder.Text, CancellationToken.None)
                ).ConfigureAwait(false);
                
                var fileIndex = await Task.Run(() => 
                    _fileService.BuildFileIndex(
                        sourceFiles,
                        chkIgnoreExtension.IsChecked == true,
                        chkCaseInsensitive.IsChecked == true
                    )
                ).ConfigureAwait(false);

                int matchCount = 0;
                int noMatchCount = 0;

                LogMessage("========== PREVIEW RESULTS ==========");
                
                foreach (var fileName in fileList)
                {
                    string searchKey = _fileService.GetSearchKey(
                        fileName,
                        chkIgnoreExtension.IsChecked == true,
                        chkCaseInsensitive.IsChecked == true
                    );

                    if (_fileIndex?.TryGetValue(searchKey, out var sourcePath) == true)
                    {
                        matchCount++;
                        LogMessage($"✅ MATCH: {fileName} -> {sourcePath}");
                    }
                    else
                    {
                        noMatchCount++;
                        LogMessage($"❌ NO MATCH: {fileName}");
                    }
                }

                LogMessage("=====================================");
                LogMessage($"📊 Preview Summary:");
                LogMessage($"   Total: {fileList.Count}");
                LogMessage($"   Will be copied: {matchCount}");
                LogMessage($"   Not found: {noMatchCount}");
                LogMessage("=====================================");

                MessageBox.Show(
                    $"Preview selesai!\n\nTotal: {fileList.Count}\nWill be copied: {matchCount}\nNot found: {noMatchCount}",
                    "Preview Results",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnVisualFileSelector_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSourceFolder.Text))
            {
                MessageBox.Show(
                    "📸 Pilih Source Folder terlebih dahulu!\n\nKlik Browse pada '📸 Pilih Folder' untuk memilih folder sumber.",
                    "Source Folder Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            if (!Directory.Exists(txtSourceFolder.Text))
            {
                MessageBox.Show(
                    "📸 Source Folder tidak ditemukan!\n\nPastikan folder masih ada dan dapat diakses.",
                    "Source Folder Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                var visualSelector = new Windows.VisualFileSelector(txtSourceFolder.Text)
                {
                    Owner = this
                };

                if (visualSelector.ShowDialog() == true)
                {
                    var selectedFiles = visualSelector.SelectedFiles;

                    if (!selectedFiles.Any())
                    {
                        LogMessage("📸 Visual File Selector: No files selected");
                        return;
                    }

                    LogMessage($"📸 Visual File Selector: {selectedFiles.Count} files selected");

                    var result = MessageBox.Show(
                        $"📸 {selectedFiles.Count} files selected from Visual File Selector.\n\n" +
                        $"Choose action:\n\n" +
                        $"YES = Load to Paste Mode (for editing/review)\n" +
                        $"NO = Start copying immediately",
                        "What to do with selected files?",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        radioPasteMode.IsChecked = true;
                        txtPasteList.Text = string.Join("\n", selectedFiles.Select(f => f.FileName));
                        UpdatePasteCount();
                        LogMessage($"✅ {selectedFiles.Count} files loaded to Paste Mode");

                        MessageBox.Show(
                            $"✅ {selectedFiles.Count} files loaded to Paste Mode!\n\n" +
                            $"You can now:\n" +
                            $"• Review the list\n" +
                            $"• Edit if needed\n" +
                            $"• Click 'Start Copy' when ready",
                            "Loaded to Paste Mode",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        if (string.IsNullOrWhiteSpace(txtDestFolder.Text) || !Directory.Exists(txtDestFolder.Text))
                        {
                            MessageBox.Show(
                                "💾 Please select a valid Destination Folder first!",
                                "Destination Required",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning
                            );
                            return;
                        }

                        await CopyFilesDirectlyFromVisualSelector(selectedFiles);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error opening Visual File Selector: {ex.Message}");
                MessageBox.Show(
                    $"Error opening Visual File Selector:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task CopyFilesDirectlyFromVisualSelector(List<SelectableFileItem> selectedFiles)
        {
            if (selectedFiles == null || !selectedFiles.Any())
            {
                LogMessage("❌ Error: No files provided for copying");
                MessageBox.Show("No files provided for copying.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _notFoundFiles.Clear();
            txtFound.Text = "0";
            txtTotal.Text = selectedFiles.Count.ToString();
            txtSkipped.Text = "0";
            txtNotFound.Text = "0";
            progressBar.Value = 0;
            txtProgress.Text = "0%";
            txtCurrentFile.Text = "";

            // Reset verification stats
            _verifiedCount = 0;
            _verificationFailedCount = 0;
            _verificationRetriedCount = 0;

            SetUIEnabled(false);
            _cancellationTokenSource = new CancellationTokenSource();
            _stopwatch.Restart();
            _timer?.Start();

            LogMessage("========================================");
            LogMessage("🚀 Starting direct copy from Visual File Selector...");
            LogMessage($"Total selected: {selectedFiles.Count} files");
            LogMessage($"Destination: {txtDestFolder.Text}");
            LogMessage("========================================");

            try
            {
                try
                {
                    checked
                    {
                        _totalBytes = selectedFiles.Sum(f => f.FileSizeBytes);
                    }
                }
                catch (OverflowException)
                {
                    _totalBytes = long.MaxValue;
                    LogMessage("⚠️ Total file size exceeds maximum, capping at max value");
                }
                
                _copiedBytes = 0;
                _startTime = DateTime.Now;
                UpdateBytesDisplay();

                int found = 0, skipped = 0, notFound = 0;
                var duplicateHandling = (DuplicateHandling)cmbDuplicateHandling.SelectedIndex;
                bool useParallel = chkParallelCopy?.IsChecked == true;

                LogMessage($"⚙️ Mode: {(useParallel ? $"Parallel ({_parallelThreadCount} threads)" : "Sequential")}");

                if (useParallel)
                {
                    await CopyFilesParallelFromSelector(selectedFiles, duplicateHandling, _cancellationTokenSource.Token);
                }
                else
                {
                    for (int i = 0; i < selectedFiles.Count; i++)
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        var item = selectedFiles[i];
                        string sourcePath = item.FullPath;
                        // FIX #6: Consistent path handling
                        string destPath = BuildDestinationPath(txtDestFolder.Text, sourcePath);

                        Dispatcher.Invoke(() =>
                        {
                            txtCurrentFile.Text = $"Processing: {item.FileName}";
                            progressBar.Value = (i + 1) * 100.0 / selectedFiles.Count;
                            txtProgress.Text = $"{(int)progressBar.Value}%";
                        });

                        if (!ShouldProcessFile(sourcePath))
                        {
                            Interlocked.Increment(ref skipped);
                            LogMessage($"⏭️ FILTERED: {item.FileName}");
                            Dispatcher.Invoke(() => txtSkipped.Text = skipped.ToString());
                            continue;
                        }

                        if (File.Exists(destPath))
                        {
                            if (duplicateHandling == DuplicateHandling.Skip)
                            {
                                Interlocked.Increment(ref skipped);
                                LogMessage($"⏭️ SKIP: {item.FileName} (already exists)");
                                Dispatcher.Invoke(() => txtSkipped.Text = skipped.ToString());
                                continue;
                            }
                            else if (duplicateHandling == DuplicateHandling.Rename)
                            {
                                lock (_fileService)
                                {
                                    destPath = _fileService.GetUniqueFileName(destPath);
                                }
                            }
                        }

                        try
                        {
                            // Copy with verification
                            bool copySuccess = await CopyFileWithVerificationAsync(
                                sourcePath,
                                destPath,
                                item.FileName,
                                duplicateHandling == DuplicateHandling.Overwrite,
                                _cancellationTokenSource.Token
                            ).ConfigureAwait(false);

                            if (copySuccess)
                            {
                                lock (_progressLock)
                                {
                                    _copiedBytes += item.FileSizeBytes;
                                }

                                Interlocked.Increment(ref found);

                                LogMessage($"✅ COPIED: {item.FileName}");
                                Dispatcher.Invoke(() =>
                                {
                                    txtFound.Text = found.ToString();
                                    UpdateBytesDisplay();
                                });
                            }
                            else
                            {
                                // Verification failed and skipped
                                Interlocked.Increment(ref skipped);
                                Dispatcher.Invoke(() => txtSkipped.Text = skipped.ToString());
                            }
                        }
                        catch (Exception ex)
                        {
                            // FIX #7: Preserve exception context for better debugging
                            LogMessage($"❌ ERROR copying {item.FileName}: {ex.GetType().Name} - {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                LogMessage($"   Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                            }
                            
                            Interlocked.Increment(ref notFound);
                            _notFoundFiles.Add(item.FileName);
                            Dispatcher.Invoke(() => txtNotFound.Text = notFound.ToString());
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("⏹️ Copy cancelled by user");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _stopwatch.Stop();
                _timer?.Stop();
                SetUIEnabled(true);
                btnExportNotFound.IsEnabled = _notFoundFiles.Any();

                LogMessage("========================================");
                LogMessage($"✅ Copy completed in {_stopwatch.Elapsed:hh\\:mm\\:ss}");
                
                // Show verification statistics if enabled
                if (chkEnableVerification?.IsChecked == true)
                {
                    LogMessage($"🔒 Verification Summary:");
                    LogMessage($"   - Files verified: {_verifiedCount}");
                    LogMessage($"   - Verification failed: {_verificationFailedCount}");
                    LogMessage($"   - Retries attempted: {_verificationRetriedCount}");
                }
                
                LogMessage("========================================");
            }
        }

        private async Task CopyFilesParallelFromSelector(List<SelectableFileItem> selectedFiles, DuplicateHandling duplicateHandling, CancellationToken cancellationToken)
        {
            int found = 0, skipped = 0, notFound = 0;
            int processed = 0;

            var semaphore = new SemaphoreSlim(_parallelThreadCount);
            var tasks = new List<Task>();
            
            System.Threading.Timer? uiUpdateTimer = null;

            foreach (var item in selectedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await semaphore.WaitAsync(cancellationToken);

                var task = Task.Run(() =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string sourcePath = item.FullPath;
                        // FIX #6: Consistent path handling
                        string destPath = BuildDestinationPath(txtDestFolder.Text, sourcePath);

                        if (!ShouldProcessFile(sourcePath))
                        {
                            Interlocked.Increment(ref skipped);
                            LogMessage($"⏭️ FILTERED: {item.FileName}");
                            return;
                        }

                        if (File.Exists(destPath))
                        {
                            if (duplicateHandling == DuplicateHandling.Skip)
                            {
                                Interlocked.Increment(ref skipped);
                                LogMessage($"⏭️ SKIP: {item.FileName} (already exists)");
                                Dispatcher.Invoke(() => txtSkipped.Text = skipped.ToString());
                                return;
                            }
                            else if (duplicateHandling == DuplicateHandling.Rename)
                            {
                                lock (_fileService)
                                {
                                    destPath = _fileService.GetUniqueFileName(destPath);
                                }
                            }
                        }

                        try
                        {
                            // Copy with verification (safe sync version for Parallel.ForEach)
                            bool copySuccess = CopyFileWithVerificationSync(
                                sourcePath,
                                destPath,
                                item.FileName,
                                duplicateHandling == DuplicateHandling.Overwrite,
                                cancellationToken
                            );

                            if (copySuccess)
                            {
                                lock (_progressLock)
                                {
                                    _copiedBytes += item.FileSizeBytes;
                                }

                                Interlocked.Increment(ref found);
                                LogMessage($"✅ COPIED: {item.FileName} (Thread-{Thread.CurrentThread.ManagedThreadId})");
                            }
                            else
                            {
                                // Verification failed and skipped
                                Interlocked.Increment(ref skipped);
                            }
                        }
                        catch (Exception ex)
                        {
                            // FIX #7: Preserve exception context for better debugging
                            LogMessage($"❌ ERROR: {item.FileName} - {ex.GetType().Name}: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                LogMessage($"   Inner: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                            }
                            
                            Interlocked.Increment(ref notFound);
                            _notFoundFiles.Add(item.FileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"❌ ERROR: {item.FileName} - {ex.Message}");
                        Interlocked.Increment(ref notFound);
                        _notFoundFiles.Add(item.FileName);
                    }
                    finally
                    {
                        Interlocked.Increment(ref processed);
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            try
            {
                uiUpdateTimer = new System.Threading.Timer(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        txtFound.Text = found.ToString();
                        txtSkipped.Text = skipped.ToString();
                        txtNotFound.Text = notFound.ToString();
                        txtCurrentFile.Text = $"Processing: {processed}/{selectedFiles.Count}";
                        
                        // FIX #4: Division by zero protection
                        double progress = 0.0;
                        if (selectedFiles.Count > 0)
                        {
                            progress = Math.Min(100.0, Math.Max(0.0, processed * 100.0 / selectedFiles.Count));
                        }
                        progressBar.Value = progress;
                        txtProgress.Text = $"{(int)progress}%";
                        UpdateBytesDisplay();
                    }));
                }, null, 0, 200);

                await Task.WhenAll(tasks);
            }
            finally
            {
                uiUpdateTimer?.Dispose();
                semaphore?.Dispose();
            }

            Dispatcher.Invoke(() =>
            {
                txtFound.Text = found.ToString();
                txtSkipped.Text = skipped.ToString();
                txtNotFound.Text = notFound.ToString();
                progressBar.Value = 100;
                txtProgress.Text = "100%";
                UpdateBytesDisplay();
            });
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (btnThemeToggle?.IsChecked == true)
                {
                    SwitchTheme(true);
                }
                else
                {
                    SwitchTheme(false);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error toggling theme: {ex.Message}");
            }
        }

        private void ChkParallelCopy_Checked(object sender, RoutedEventArgs e)
        {
            // Enable parallel copy - already handled by the IsChecked property in code
        }

        private void ChkParallelCopy_Unchecked(object sender, RoutedEventArgs e)
        {
            // Disable parallel copy - already handled by the IsChecked property in code
        }

        private void ChkEnableFilters_Checked(object sender, RoutedEventArgs e)
        {
            if (panelFilters != null)
                panelFilters.IsEnabled = true;
        }

        private void ChkEnableFilters_Unchecked(object sender, RoutedEventArgs e)
        {
            if (panelFilters != null)
                panelFilters.IsEnabled = false;
        }

        private void ChkEnableVerification_Checked(object sender, RoutedEventArgs e)
        {
            ChkEnableVerification_Changed(sender, e);
        }

        private void ChkEnableVerification_Unchecked(object sender, RoutedEventArgs e)
        {
            ChkEnableVerification_Changed(sender, e);
        }
    }
}
