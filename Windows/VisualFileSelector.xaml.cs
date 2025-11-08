using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AutoCopy.Models;
using AutoCopy.ViewModels;
using Microsoft.Win32;

namespace AutoCopy.Windows
{
    public partial class VisualFileSelector : Window
    {
        private readonly VisualFileSelectorViewModel _viewModel;
        private bool _hasLoaded = false;

        public List<SelectableFileItem> SelectedFiles { get; private set; } = new();

        public VisualFileSelector()
        {
            InitializeComponent();
            _viewModel = new VisualFileSelectorViewModel();
            DataContext = _viewModel;
            
            Closed += (s, e) => _viewModel?.Dispose();
        }

        public VisualFileSelector(string sourceFolder) : this()
        {
            _viewModel.SourceFolder = sourceFolder;
            Loaded += async (s, e) =>
            {
                if (_hasLoaded) return;
                _hasLoaded = true;
                
                if (!string.IsNullOrWhiteSpace(sourceFolder) && System.IO.Directory.Exists(sourceFolder))
                {
                    await _viewModel.LoadFilesFromFolderAsync(sourceFolder);
                }
            };
        }

        private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select source folder to browse files",
                ShowNewFolderButton = false
            })
            {
                if (!string.IsNullOrWhiteSpace(_viewModel.SourceFolder))
                {
                    dialog.SelectedPath = _viewModel.SourceFolder;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _viewModel.SourceFolder = dialog.SelectedPath;
                    await _viewModel.LoadFilesFromFolderAsync(dialog.SelectedPath);
                }
            }
        }

        private void BtnExportToFileList_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = _viewModel.Files.Where(f => f.IsSelected).ToList();

            if (!selectedItems.Any())
            {
                MessageBox.Show("No files selected!\n\nPlease select files first.", 
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = $"filelist_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Export File List"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var directory = System.IO.Path.GetDirectoryName(dialog.FileName);
                    if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                    {
                        MessageBox.Show(
                            "The selected directory does not exist.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                        return;
                    }

                    var fileNames = selectedItems.Select(f => f.FileName);
                    System.IO.File.WriteAllLines(dialog.FileName, fileNames);

                    MessageBox.Show(
                        $"Successfully exported {selectedItems.Count} files!\n\n{dialog.FileName}",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show(
                        "Access denied. You don't have permission to write to this location.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
                catch (System.IO.IOException ioEx)
                {
                    MessageBox.Show(
                        $"I/O error occurred:\n\n{ioEx.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting file list:\n\n{ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnUseSelected_Click(object sender, RoutedEventArgs e)
        {
            SelectedFiles = _viewModel.Files.Where(f => f.IsSelected).ToList();

            if (!SelectedFiles.Any())
            {
                MessageBox.Show("No files selected!\n\nPlease select files first.",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
