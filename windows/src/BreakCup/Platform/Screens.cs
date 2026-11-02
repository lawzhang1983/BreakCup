using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace BreakCup.Platform
{
    /// <summary>
    /// 屏幕与窗口摆放。进程按每显示器 DPI 感知运行，屏幕坐标一律是物理像素；
    /// WPF 窗口内部按 DIP 排版，跨显示器摆放时由这里换算。
    /// </summary>
    internal static class Screens
    {
        public static Forms.Screen[] All => Forms.Screen.AllScreens;

        /// <summary>屏幕布局指纹：块数、位置、分辨率任一变化都会不同</summary>
        public static string Fingerprint() =>
            string.Join(";", All.Select(s => $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}"));

        public static double Scale(Forms.Screen screen) =>
            Native.ScaleAt(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y + screen.Bounds.Height / 2);

        /// <summary>
        /// 把窗口放到某块屏幕的物理像素矩形上。移到另一块缩放比例不同的屏幕时，
        /// 系统发 WM_DPICHANGED、WPF 按比例改窗口尺寸，所以先移过去、再按目标矩形摆一次。
        /// </summary>
        public static void Place(Window window, System.Drawing.Rectangle pixels, bool resize = true)
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var flags = Native.SWP_NOACTIVATE | Native.SWP_NOZORDER | (resize ? 0 : Native.SWP_NOSIZE);
            Native.SetWindowPos(hwnd, IntPtr.Zero, pixels.X, pixels.Y, pixels.Width, pixels.Height, flags);
            Native.SetWindowPos(hwnd, IntPtr.Zero, pixels.X, pixels.Y, pixels.Width, pixels.Height, flags);
        }

        public static void BringToTop(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public static System.Drawing.Rectangle WindowRect(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect)) return System.Drawing.Rectangle.Empty;
            return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        /// <summary>截下某块屏幕当前的画面，休息屏从它淡入淡出，效果和 Mac 版窗口渐显一样</summary>
        public static ImageSource? Capture(Forms.Screen screen)
        {
            try
            {
                var bounds = screen.Bounds;
                using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                }
                var hbitmap = bitmap.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    Native.DeleteObject(hbitmap);
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is ArgumentException || ex is OutOfMemoryException)
            {
                Log.Write($"截取屏幕画面失败：{ex.Message}");
                return null;
            }
        }
    }
}
