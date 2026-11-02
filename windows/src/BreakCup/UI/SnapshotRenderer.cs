using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BreakCup.Core;
using BreakCup.Platform;
using Forms = System.Windows.Forms;

namespace BreakCup.UI
{
    /// <summary>界面验收用：离屏渲染各界面为 PNG，不启动托盘应用，也不需要登录桌面</summary>
    internal static class SnapshotRenderer
    {
        public static void Run(string folder)
        {
            Directory.CreateDirectory(folder);
            var primary = Forms.Screen.PrimaryScreen;
            var scale = Screens.Scale(primary);
            var size = new Size(Math.Round(primary.Bounds.Width / scale), Math.Round(primary.Bounds.Height / scale));
            var background = Wallpaper.Background();
            var now = DateTime.Now;

            var tip = TipLibrary.All.FirstOrDefault(t => t.Category == TipCategory.Eyes) ?? TipLibrary.All[0];
            var resting = new RestSession(tip, now.AddSeconds(-28), now.AddSeconds(272));
            Save(Rest(resting, size, background), size, 1, Path.Combine(folder, "rest.png"));

            var longTip = TipLibrary.All.OrderByDescending(t => t.Text.Length).First();
            var laptop = new Size(1366, 768);
            Save(Rest(new RestSession(longTip, now.AddSeconds(-200), now.AddSeconds(100)), laptop, background), laptop, 1,
                Path.Combine(folder, "rest-laptop-longtip.png"));

            var finished = new RestSession(tip, now.AddSeconds(-300), now) { IsFinished = true };
            Save(Rest(finished, size, background), size, 1, Path.Combine(folder, "rest-finished.png"));

            var warning = new WarningView(now.AddSeconds(42), now.AddSeconds(-18), 5);
            var warningSize = new Size(WarningToast.ToastWidth + WarningView.ShadowMargin * 2, WarningToast.ToastHeight + WarningView.ShadowMargin * 2);
            Save(warning, warningSize, scale, Path.Combine(folder, "warning.png"));

            var model = new AppModel(BreakSettings.Default);
            var start = now.AddSeconds(-(12 * 60 + 20));
            var scheduler = new BreakScheduler(BreakSettings.Default, start);
            for (var second = 1; second <= (int)(now - start).TotalSeconds; second++)
            {
                scheduler.Tick(start.AddSeconds(second), new ActivityEnvironment());
            }
            model.Refresh(scheduler, new Core.MediaState(microphoneUsers: new[] { "Microsoft Teams" }), now);
            model.TodayRestCount = 3;
            var hints = new List<string>();
            foreach (var (name, dark) in new[] { ("settings-light.png", false), ("settings-dark.png", true) })
            {
                var (panel, view) = Settings(model, dark);
                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Save(panel, panel.DesiredSize, scale, Path.Combine(folder, name));
                if (hints.Count == 0) hints.AddRange(view.HintTexts());
            }

            SaveTraySheet(Path.Combine(folder, "tray.png"));

            var report = new StringBuilder();
            foreach (var hint in hints) report.AppendLine("悬停说明：" + hint.Replace("\n", " / "));
            report.AppendLine($"主屏 {primary.Bounds.Width}×{primary.Bounds.Height} 像素，缩放 {scale:P0}，工作区 {primary.WorkingArea}");
            report.AppendLine($"壁纸：{(background == null ? "未读到，使用咖啡色渐变" : "已读取")}");
            report.AppendLine($"托盘小图标 {Forms.SystemInformation.SmallIconSize.Width} 像素，任务栏{(Theme.TaskbarIsDark ? "深色" : "浅色")}，应用{(Theme.AppsUseDark ? "深色" : "浅色")}");
            File.WriteAllText(Path.Combine(folder, "hints.txt"), report.ToString(), new UTF8Encoding(false));
            Console.WriteLine(report.ToString());
            Console.WriteLine($"snapshots -> {folder}");
        }

        private static FrameworkElement Rest(RestSession session, Size size, ImageSource? background)
        {
            var grid = new Grid { Width = size.Width, Height = size.Height, Background = Brushes.Black };
            grid.Children.Add(new DriftingBackground(background, size, animate: false));
            grid.Children.Add(new RestView(session, size));
            return grid;
        }

        /// <summary>和托盘弹出时同样的外框：圆角、描边、阴影，配色按指定深浅自带资源</summary>
        private static (FrameworkElement Panel, SettingsView View) Settings(AppModel model, bool dark)
        {
            var root = new Border { Background = dark ? Theme.Brush(Color.FromRgb(0x10, 0x10, 0x10)) : Theme.Brush(Color.FromRgb(0xDA, 0xDA, 0xDA)), Padding = new Thickness(12) };
            Theme.Apply(root.Resources, dark);
            var view = new SettingsView(model);
            var frame = new Border
            {
                Child = new ScrollHost(view),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.28 }
            };
            frame.SetResourceReference(Border.BackgroundProperty, Keys.Panel);
            frame.SetResourceReference(Border.BorderBrushProperty, Keys.PanelBorder);
            root.Child = frame;
            return (root, view);
        }

        /// <summary>托盘图标对照表：上半深色任务栏、下半浅色任务栏，每种尺寸按原像素放大 6 倍</summary>
        private static void SaveTraySheet(string path)
        {
            var glyphs = new[]
            {
                new TrayGlyph("33", false, false),
                new TrayGlyph("180", false, false),
                new TrayGlyph("42", true, true),
                new TrayGlyph(null, false, false),
                new TrayGlyph(null, false, true)
            };
            var sizes = new[] { 16, 20, 24, 32 };
            const int zoom = 6;
            const int gap = 12;
            var cell = 32 * zoom + gap;
            var width = glyphs.Length * cell + gap;
            var height = sizes.Length * 2 * cell + gap;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (var band = 0; band < 2; band++)
                {
                    var dark = band == 0;
                    var top = band * sizes.Length * cell;
                    dc.DrawRectangle(Theme.Brush(dark ? Color.FromRgb(0x1F, 0x1F, 0x1F) : Color.FromRgb(0xEE, 0xEE, 0xEE)), null,
                        new Rect(0, top, width, sizes.Length * cell + (band == 1 ? gap : 0)));
                    for (var row = 0; row < sizes.Length; row++)
                    {
                        for (var column = 0; column < glyphs.Length; column++)
                        {
                            var image = IconArt.Tray(glyphs[column], sizes[row], dark);
                            var x = gap + column * cell;
                            var y = top + gap + row * cell;
                            dc.DrawImage(NearestNeighbor(image, zoom), new Rect(x, y, sizes[row] * zoom, sizes[row] * zoom));
                        }
                    }
                }
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            File.WriteAllBytes(path, IconArt.Png(bitmap));
        }

        /// <summary>按原像素放大，看清每个像素的抗锯齿</summary>
        private static BitmapSource NearestNeighbor(BitmapSource image, int zoom)
        {
            var source = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
            int w = source.PixelWidth, h = source.PixelHeight;
            var pixels = new byte[w * h * 4];
            source.CopyPixels(pixels, w * 4, 0);
            var big = new byte[w * zoom * h * zoom * 4];
            for (var y = 0; y < h * zoom; y++)
            {
                for (var x = 0; x < w * zoom; x++)
                {
                    Buffer.BlockCopy(pixels, ((y / zoom) * w + x / zoom) * 4, big, (y * w * zoom + x) * 4, 4);
                }
            }
            var result = BitmapSource.Create(w * zoom, h * zoom, 96, 96, PixelFormats.Pbgra32, null, big, w * zoom * 4);
            result.Freeze();
            return result;
        }

        private static void Save(FrameworkElement element, Size size, double scale, string path)
        {
            element.Measure(size);
            element.Arrange(new Rect(size));
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(element);
            File.WriteAllBytes(path, IconArt.Png(bitmap));
        }
    }
}
