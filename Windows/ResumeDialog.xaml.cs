using System;
using System.Windows;
using AutoCopy.Models;

namespace AutoCopy.Windows
{
    /// <summary>
    /// Dialog for handling resume copy operations
    /// Professional UI with comprehensive data display
    /// </summary>
    public partial class ResumeDialog : Window
    {
        public CopyCheckpoint? Checkpoint { get; private set; }
        public ResumeAction UserAction { get; private set; } = ResumeAction.Cancel;

        public ResumeDialog()
        {
            InitializeComponent();
        }

        public ResumeDialog(CopyCheckpoint checkpoint) : this()
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));

            Checkpoint = checkpoint;
            LoadCheckpointData(checkpoint);
        }

        /// <summary>
        /// Load checkpoint data into UI controls
        /// </summary>
        private void LoadCheckpointData(CopyCheckpoint checkpoint)
        {
            try
            {
                // Session information
                txtStartTime.Text = checkpoint.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                txtSourceFolder.Text = checkpoint.SourceFolder;
                txtDestinationFolder.Text = checkpoint.DestinationFolder;
                txtProgress.Text = checkpoint.Summary;

                // Data processed information
                double processedMB = checkpoint.ProcessedBytes / (1024.0 * 1024.0);
                double totalMB = checkpoint.TotalBytes / (1024.0 * 1024.0);
                
                if (checkpoint.TotalBytes > 0)
                {
                    txtDataProcessed.Text = $"{processedMB:F2} MB / {totalMB:F2} MB processed";
                }
                else
                {
                    txtDataProcessed.Text = $"{processedMB:F2} MB processed";
                }

                // File status counts
                txtCompletedCount.Text = (checkpoint.ProcessedFiles?.Count ?? 0).ToString();
                txtSkippedCount.Text = (checkpoint.SkippedFiles?.Count ?? 0).ToString();
                txtFailedCount.Text = (checkpoint.FailedFiles?.Count ?? 0).ToString();
                txtRemainingCount.Text = checkpoint.RemainingFiles.Count.ToString();

                // Progress bar
                progressBarResume.Value = checkpoint.ProgressPercentage;

                // Update title with session info
                Title = $"📁 Resume Copy - {checkpoint.RemainingFiles.Count} files remaining";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error loading checkpoint data: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                
                UserAction = ResumeAction.Cancel;
                DialogResult = false;
            }
        }

        private void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate checkpoint before resuming
                if (Checkpoint == null || !Checkpoint.IsValidForResume)
                {
                    MessageBox.Show(
                        "Cannot resume: checkpoint data is invalid or corrupted.",
                        "Resume Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // Validate that source and destination folders still exist
                if (!System.IO.Directory.Exists(Checkpoint.SourceFolder))
                {
                    MessageBox.Show(
                        $"Source folder no longer exists:\n{Checkpoint.SourceFolder}",
                        "Resume Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                if (!System.IO.Directory.Exists(Checkpoint.DestinationFolder))
                {
                    var result = MessageBox.Show(
                        $"Destination folder no longer exists:\n{Checkpoint.DestinationFolder}\n\nCreate it now?",
                        "Destination Missing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.IO.Directory.CreateDirectory(Checkpoint.DestinationFolder);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"Failed to create destination folder:\n{ex.Message}",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                UserAction = ResumeAction.Resume;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error during resume validation: {ex.Message}",
                    "Resume Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to restart the entire operation?\n\n" +
                "This will:\n" +
                "• Start copying from the beginning\n" +
                "• Ignore previous progress\n" +
                "• Delete the checkpoint file\n\n" +
                "This action cannot be undone.",
                "Confirm Restart",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                UserAction = ResumeAction.Restart;
                DialogResult = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Cancel will keep the checkpoint file for later resume.\n\n" +
                "You can resume this operation later by starting AutoCopy again.",
                "Confirm Cancel",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information
            );

            if (result == MessageBoxResult.OK)
            {
                UserAction = ResumeAction.Cancel;
                DialogResult = false;
            }
        }
    }

    /// <summary>
    /// User action choices for resume dialog
    /// </summary>
    public enum ResumeAction
    {
        /// <summary>
        /// Cancel operation, keep checkpoint for later
        /// </summary>
        Cancel = 0,

        /// <summary>
        /// Resume from where it left off
        /// </summary>
        Resume = 1,

        /// <summary>
        /// Restart entire operation from beginning
        /// </summary>
        Restart = 2
    }
}
