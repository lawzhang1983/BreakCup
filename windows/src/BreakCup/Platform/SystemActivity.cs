using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BreakCup.Platform
{
    /// <summary>键鼠空闲时长与锁屏状态</summary>
    internal sealed class SystemActivity : IDisposable
    {
        private readonly Dispatcher dispatcher;
        private bool sessionLocked;
        private Action? onChange;

        public SystemActivity(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher;
        }

        /// <summary>
        /// 锁屏、会话被切走（快速用户切换、远程桌面接管）都算锁定；
        /// 输入桌面不可访问（锁屏界面、Ctrl+Alt+Del 安全桌面）也算，进程晚于锁屏启动时靠它兜底。
        /// </summary>
        public bool IsScreenLocked => sessionLocked || !InputDesktopAccessible();

        /// <summary>状态变化（锁屏、解锁、唤醒）时立刻回调一次，不必等下一秒</summary>
        public void Start(Action onChange)
        {
            this.onChange = onChange;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        /// <summary>
        /// 距最后一次键盘或鼠标输入的秒数。GetLastInputInfo 统计的是整个会话的输入，
        /// 远程桌面、远控软件注入的键鼠同样会刷新它，远程操作也算在用电脑。
        /// </summary>
        public double IdleSeconds()
        {
            var info = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO)) };
            if (!Native.GetLastInputInfo(ref info)) return 0;
            var elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
            return elapsed / 1000.0;
        }

        private static bool InputDesktopAccessible()
        {
            var desktop = Native.OpenInputDesktop(0, false, Native.DESKTOP_SWITCHDESKTOP);
            if (desktop == IntPtr.Zero) return false;
            Native.CloseDesktop(desktop);
            return true;
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                    sessionLocked = true;
                    break;
                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.SessionLogon:
                    sessionLocked = false;
                    break;
                default:
                    return;
            }
            Notify();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) Notify();
        }

        private void Notify()
        {
            var action = onChange;
            if (action != null) dispatcher.BeginInvoke(action);
        }

        public void Dispose()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }

    /// <summary>登录时自动启动：写当前用户的 Run 注册表项，和「任务管理器 › 启动应用」里看到的是同一处</summary>
    internal static class LoginItem
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "BreakCup";

        public static string ExecutablePath => Process.GetCurrentProcess().MainModule?.FileName ?? "";

        public static bool IsEnabled => Registered() != null && !DisabledInTaskManager();

        /// <summary>需要用户处理的问题，面板上以橙色感叹号提示</summary>
        public static string? Note
        {
            get
            {
                var registered = Registered();
                if (registered == null) return null;
                if (DisabledInTaskManager()) return "已在「任务管理器 › 启动应用」里被禁用，在那里重新启用即可";
                if (!string.Equals(registered.Trim('"'), ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return "开机启动指向的不是当前这份歇一杯，关掉再打开一次即可修正";
                }
                return null;
            }
        }

        public static void Set(bool enabled)
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunPath);
            if (enabled)
            {
                run.SetValue(ValueName, $"\"{ExecutablePath}\"");
                // 任务管理器里禁用过的话，这里会留一条禁用记录；用户主动打开时一并清掉
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedPath, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }

        private static string? Registered()
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunPath);
            return run?.GetValue(ValueName) as string;
        }

        /// <summary>StartupApproved 里首字节为奇数表示在任务管理器里被禁用</summary>
        private static bool DisabledInTaskManager()
        {
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedPath);
            return approved?.GetValue(ValueName) is byte[] flags && flags.Length > 0 && (flags[0] & 1) == 1;
        }
    }

    /// <summary>今日休息次数，只保留当天</summary>
    internal static class RestLog
    {
        private const string KeyPath = Core.SettingsStore.DefaultKeyPath;

        public static int TodayCount()
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("restLogDay") as string == Today && key.GetValue("restLogCount") is int count ? count : 0;
        }

        public static void Record()
        {
            var count = TodayCount() + 1;
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("restLogDay", Today);
            key.SetValue("restLogCount", count, RegistryValueKind.DWord);
        }

        private static string Today => DateTime.Now.ToString("yyyy-MM-dd");
    }
}
