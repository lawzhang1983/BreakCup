using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreakCup.UI
{
    /// <summary>托盘图标与应用图标的绘制。托盘图标按系统小图标的实际像素绘制，避免缩放发糊</summary>
    internal static class IconArt
    {
        private static readonly Color CupLight = Color.FromRgb(0x1A, 0x1A, 0x1A);
        private static readonly Color Ink = Color.FromRgb(0x2B, 0x1F, 0x1A);
        private static readonly Color Coffee = Color.FromRgb(0x8C, 0x59, 0x38);
        private static readonly Color Urgent = Color.FromRgb(0xE8, 0x80, 0x0C);

        /// <summary>
        /// 托盘图标：显示剩余时间时是一块圆角色块加数字（最后一分钟是橙色秒数），
        /// 否则是咖啡杯，按任务栏深浅画白色或黑色；预告和休息中换成实心杯。
        /// </summary>
        public static BitmapSource Tray(TrayGlyph glyph, int size, bool darkTaskbar)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (glyph.Number != null)
                {
                    Color fill, text;
                    if (glyph.IsSeconds)
                    {
                        fill = Urgent;
                        text = Colors.White;
                    }
                    else if (darkTaskbar)
                    {
                        fill = Theme.Latte;
                        text = Ink;
                    }
                    else
                    {
                        fill = Coffee;
                        text = Colors.White;
                    }
                    var inset = Math.Max(0.5, size / 32.0);
                    var box = new Rect(inset, inset, size - inset * 2, size - inset * 2);
                    dc.DrawRoundedRectangle(Theme.Brush(fill), null, box, size * 0.2, size * 0.2);
                    DrawNumber(dc, glyph.Number, box, Theme.Brush(text));
                }
                else
                {
                    var color = darkTaskbar ? Colors.White : CupLight;
                    var geometry = glyph.Steaming ? Icons.CoffeeFilled : Icons.Coffee;
                    DrawGeometry(dc, geometry, new Rect(0, 0, size, size), size * 0.04, Theme.Brush(color));
                }
            }
            return Render(visual, size);
        }

        /// <summary>应用图标：奶油色圆角底加深咖啡色杯子，与 Mac 版图标同一套配色</summary>
        public static BitmapSource App(int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var cream = new LinearGradientBrush(Color.FromRgb(0xF8, 0xEB, 0xDB), Color.FromRgb(0xE4, 0xC2, 0x9C), new Point(0, 0), new Point(0, 1));
                cream.Freeze();
                var inset = size * 0.04;
                var radius = size * 0.22;
                var box = new Rect(inset, inset, size - inset * 2, size - inset * 2);
                dc.DrawRoundedRectangle(cream, new Pen(Theme.Brush(0x33, Color.FromRgb(0x8C, 0x59, 0x38)), Math.Max(1, size / 128.0)), box, radius, radius);
                DrawGeometry(dc, Icons.CoffeeFilled, box, size * 0.15, Theme.Brush(Color.FromRgb(0x5A, 0x36, 0x22)));
            }
            return Render(visual, size);
        }

        private static void DrawNumber(DrawingContext dc, string number, Rect box, Brush brush)
        {
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var text = new FormattedText(number, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 100, brush, 1);
            // 按数字墨迹的外框排：字高占色块的 62%，三位数放不下就横向压窄
            var ink = text.BuildGeometry(new Point(0, 0));
            var bounds = ink.Bounds;
            var scale = box.Height * 0.62 / bounds.Height;
            var available = box.Width * 0.86;
            var squeeze = Math.Min(1, available / (bounds.Width * scale));
            var x = box.X + (box.Width - bounds.Width * scale * squeeze) / 2 - bounds.X * scale * squeeze;
            var y = box.Y + (box.Height - bounds.Height * scale) / 2 - bounds.Y * scale;
            dc.PushTransform(new MatrixTransform(scale * squeeze, 0, 0, scale, x, y));
            dc.DrawGeometry(brush, null, ink);
            dc.Pop();
        }

        private static void DrawGeometry(DrawingContext dc, Geometry geometry, Rect box, double padding, Brush brush)
        {
            var bounds = geometry.Bounds;
            var target = new Rect(box.X + padding, box.Y + padding, box.Width - padding * 2, box.Height - padding * 2);
            var scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
            var x = target.X + (target.Width - bounds.Width * scale) / 2 - bounds.X * scale;
            var y = target.Y + (target.Height - bounds.Height * scale) / 2 - bounds.Y * scale;
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, x, y));
            dc.DrawGeometry(brush, null, geometry);
            dc.Pop();
        }

        private static BitmapSource Render(Visual visual, int size)
        {
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        public static byte[] Png(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// 写 .ico：256 像素用 PNG 压缩，其余用带 alpha 的 32 位位图，
        /// 老版本资源管理器和 System.Drawing.Icon 都能直接读。
        /// </summary>
        public static byte[] Ico(IEnumerable<BitmapSource> images)
        {
            var entries = new List<(int Size, byte[] Data)>();
            foreach (var image in images)
            {
                var size = image.PixelWidth;
                entries.Add((size, size >= 256 ? Png(image) : Dib(image)));
            }
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)entries.Count);
            var offset = 6 + 16 * entries.Count;
            foreach (var (size, data) in entries)
            {
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(data.Length);
                writer.Write(offset);
                offset += data.Length;
            }
            foreach (var (_, data) in entries) writer.Write(data);
            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] Dib(BitmapSource image)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            var maskStride = (width + 31) / 32 * 4;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(40);
            writer.Write(width);
            writer.Write(height * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(stride * height + maskStride * height);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            // 位图按自下而上存储
            for (var row = height - 1; row >= 0; row--) writer.Write(pixels, row * stride, stride);
            writer.Write(new byte[maskStride * height]);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
