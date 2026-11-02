using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using BreakCup.Core;
using Microsoft.Win32;

namespace BreakCup.Platform
{
    /// <summary>
    /// 休息屏收起后接着启动系统屏保，人走开了屏幕也不会一直亮着内容；没设屏保就直接锁屏。
    /// 唤醒要不要输密码由系统的「在恢复时显示登录屏幕」决定，歇一杯只读不改。与 Mac 版 ScreenSaver 对应。
    /// </summary>
    internal static class ScreenSaver
    {
        private const string DesktopKey = @"Control Panel\Desktop";
        private const string PolicyKey = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop";
        private static HwndSource? window;

        public static ScreenSaverConfig ReadConfig()
        {
            using var policy = Registry.CurrentUser.OpenSubKey(PolicyKey);
            using var desktop = Registry.CurrentUser.OpenSubKey(DesktopKey);
            string? Read(string name) => (policy?.GetValue(name) ?? desktop?.GetValue(name))?.ToString();
            return ScreenSaverConfig.FromRegistry(Read("SCRNSAVE.EXE"), Read("ScreenSaveActive"), Read("ScreenSaverIsSecure"), Exists);
        }

        public static void Start()
        {
            var config = ReadConfig();
            Log.Write($"休息结束后：{config.Summary}");
            if (config.Action == AfterRestAction.Lock)
            {
                Lock();
                return;
            }
            // SC_SCREENSAVE 交给 DefWindowProc，系统按用户的屏保设置启动，和闲置到点时一样，「恢复时显示登录屏幕」照常生效
            window ??= new HwndSource(new HwndSourceParameters("歇一杯屏幕保护") { WindowStyle = unchecked((int)Native.WS_POPUP) });
            var sentAt = LastInputTick();
            Native.DefWindowProc(window.Handle, Native.WM_SYSCOMMAND, new IntPtr(Native.SC_SCREENSAVE), IntPtr.Zero);
            // 设置看着没问题、屏保却没起来（比如被策略拦了）：人又没碰过键鼠，就按没屏保处理，免得屏幕一直亮着
            var check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            check.Tick += (_, _) =>
            {
                check.Stop();
                if (Running() || LastInputTick() != sentAt) return;
                Log.Write("屏幕保护没有启动，改为锁屏");
                Lock();
            };
            check.Start();
        }

        private static void Lock()
        {
            if (!Native.LockWorkStation()) Log.Write($"锁屏失败：错误码 {Marshal.GetLastWin32Error()}");
        }

        private static bool Running() => Native.SystemParametersInfo(Native.SPI_GETSCREENSAVERRUNNING, 0, out int running, 0) && running != 0;

        private static uint LastInputTick()
        {
            var info = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO)) };
            return Native.GetLastInputInfo(ref info) ? info.dwTime : 0;
        }

        private static bool Exists(string program)
        {
            try
            {
                var path = Environment.ExpandEnvironmentVariables(program.Trim('"'));
                if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.SystemDirectory, path);
                return File.Exists(path);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
