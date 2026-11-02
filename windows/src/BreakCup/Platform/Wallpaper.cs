using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace BreakCup.Platform
{
    /// <summary>
    /// 读取当前桌面壁纸，缩小并模糊后做休息屏背景。
    /// 优先用系统转码后的 TranscodedWallpaper：幻灯片、Windows 聚焦换过的壁纸也能读到当前这张。
    /// </summary>
    internal static class Wallpaper
    {
        private static ImageSource? cached;
        private static string? cachedKey;

        public static ImageSource? Background()
        {
            var path = CurrentPath();
            if (path == null) return null;
            var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (key == cachedKey) return cached;
            cached = Render(path);
            cachedKey = key;
            return cached;
        }

        private static string? CurrentPath()
        {
            var transcoded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(transcoded)) return transcoded;
            var buffer = new StringBuilder(520);
            if (Native.SystemParametersInfo(Native.SPI_GETDESKWALLPAPER, (uint)buffer.Capacity, buffer, 0))
            {
                var path = buffer.ToString();
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static ImageSource? Render(string path)
        {
            try
            {
                var decoder = BitmapDecoder.Create(new MemoryStream(File.ReadAllBytes(path)),
                    BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                // 缩到 640 宽再模糊，铺满屏幕时放大本身也带来柔化
                var scale = 640.0 / frame.PixelWidth;
                var small = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                int width = small.PixelWidth, height = small.PixelHeight;

                // 模糊会让边缘渐隐成透明：图片四周多铺一圈再裁掉
                const double radius = 18;
                var image = new Image
                {
                    Source = small,
                    Stretch = Stretch.Fill,
                    Width = width + radius * 4,
                    Height = height + radius * 4,
                    Margin = new Thickness(-radius * 2),
                    Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality }
                };
                var canvas = new Grid { Width = width, Height = height, ClipToBounds = true, Background = Brushes.Black };
                canvas.Children.Add(image);
                canvas.Measure(new Size(width, height));
                canvas.Arrange(new Rect(0, 0, width, height));
                canvas.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Log.Write($"读取壁纸失败：{ex.Message}");
                return null;
            }
        }
    }
}
