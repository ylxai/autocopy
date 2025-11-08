using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoCopy.Models;

namespace AutoCopy.Services
{
    public class ThumbnailService : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(4);
        private bool _disposed = false;

        public async Task<BitmapSource?> LoadThumbnailAsync(string filePath, CancellationToken ct)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ThumbnailService));

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                return await Task.Run(() => LoadThumbnailInternal(filePath), ct).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private BitmapSource? LoadThumbnailInternal(string filePath)
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLower();

                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif")
                {
                    return LoadImageThumbnail(filePath);
                }
                else if (PhotoFormats.IsRawFormat(ext))
                {
                    return LoadRawThumbnail(filePath);
                }
                else if (PhotoFormats.VideoFormats.ContainsKey(ext))
                {
                    return GetDefaultIcon("video");
                }
                else
                {
                    return GetDefaultIcon(ext);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading thumbnail for {filePath}: {ex.Message}");
                return GetDefaultIcon("error");
            }
        }

        private BitmapSource? LoadImageThumbnail(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                
                if (fileInfo.Length > 50 * 1024 * 1024)
                {
                    System.Diagnostics.Debug.WriteLine($"File too large for thumbnail: {filePath} ({fileInfo.Length / (1024 * 1024)} MB)");
                    return GetDefaultIcon("large");
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 120;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image thumbnail for {filePath}: {ex.Message}");
                return GetDefaultIcon(".jpg");
            }
        }

        private BitmapSource? LoadRawThumbnail(string filePath)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(
                        stream,
                        BitmapCreateOptions.DelayCreation,
                        BitmapCacheOption.OnDemand
                    );

                    if (decoder.Thumbnail != null)
                    {
                        var thumbnail = decoder.Thumbnail.Clone();
                        thumbnail.Freeze();
                        return thumbnail;
                    }

                    if (decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        var resized = CreateResizedImage(frame, 120);
                        return resized;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading RAW thumbnail for {filePath}: {ex.Message}");
            }

            return GetDefaultIcon("raw");
        }

        private BitmapSource CreateResizedImage(BitmapSource source, int maxSize)
        {
            double scale = Math.Min(maxSize / (double)source.PixelWidth, maxSize / (double)source.PixelHeight);
            var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            resized.Freeze();
            return resized;
        }

        private BitmapSource? GetDefaultIcon(string type)
        {
            int size = 120;
            int dpi = 96;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                var backgroundColor = type switch
                {
                    "raw" => new SolidColorBrush(Color.FromRgb(102, 126, 234)),
                    "video" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                    "error" => new SolidColorBrush(Color.FromRgb(158, 158, 158)),
                    "large" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    _ => new SolidColorBrush(Color.FromRgb(96, 125, 139))
                };

                context.DrawRectangle(backgroundColor, null, new Rect(0, 0, size, size));

                var text = type switch
                {
                    "raw" => "RAW",
                    "video" => "VIDEO",
                    "error" => "?",
                    "large" => "LARGE",
                    _ => Path.GetExtension(type).TrimStart('.').ToUpper()
                };

                if (text.Length > 6)
                    text = text.Substring(0, 6);

                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    size / 5,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip
                );

                context.DrawText(formattedText, new Point(
                    (size - formattedText.Width) / 2,
                    (size - formattedText.Height) / 2
                ));
            }

            var bitmap = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

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
                    _semaphore?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
