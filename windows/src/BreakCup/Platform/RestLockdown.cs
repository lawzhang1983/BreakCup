using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace BreakCup.Platform
{
    /// <summary>
    /// 休息期间拦截键鼠：低级键盘钩子吞掉所有按键（⊞ 键、Alt+Tab、Alt+F4、Ctrl+Shift+Esc 都在内），
    /// 低级鼠标钩子吞掉点击和滚轮，只放行移动。Ctrl+Alt+Del 和 ⊞+L 由系统安全桌面处理，任何程序都拦不住。
    /// 兜底：过了休息结束时间 15 秒，钩子自动放行，避免意外情况下键鼠被一直锁住。
    /// 钩子装在独立线程上：低级钩子回调超时会被系统悄悄摘掉，不能让界面线程的卡顿影响拦截。
    /// </summary>
    internal sealed class InputBlocker
    {
        private readonly Native.HookProc keyboardProc;
        private readonly Native.HookProc mouseProc;
        private IntPtr keyboardHook;
        private IntPtr mouseHook;
        private long deadlineTicks;
        private Thread? thread;
        private uint threadId;

        public InputBlocker()
        {
            // 委托必须由字段持有，否则被回收后钩子回调会崩溃
            keyboardProc = OnKeyboard;
            mouseProc = OnMouse;
        }

        public bool IsActive => thread != null;

        public void Start(DateTime untilUtc)
        {
            Interlocked.Exchange(ref deadlineTicks, untilUtc.AddSeconds(15).Ticks);
            if (IsActive) return;
            using var ready = new ManualResetEventSlim(false);
            thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "歇一杯键鼠拦截", Priority = ThreadPriority.Highest };
            thread.Start();
            ready.Wait(TimeSpan.FromSeconds(2));
        }

        public void Stop()
        {
            var running = thread;
            if (running == null) return;
            thread = null;
            Native.PostThreadMessage(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            running.Join(TimeSpan.FromSeconds(2));
        }

        private void Run(ManualResetEventSlim ready)
        {
            threadId = Native.GetCurrentThreadId();
            var module = Native.GetModuleHandle(null);
            keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, module, 0);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            {
                Log.Write($"键鼠钩子安装失败：错误码 {Marshal.GetLastWin32Error()}");
            }
            ready.Set();
            // 低级钩子的回调靠本线程的消息循环派发
            while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            keyboardHook = IntPtr.Zero;
            mouseHook = IntPtr.Zero;
        }

        private bool Blocking => DateTime.UtcNow.Ticks < Interlocked.Read(ref deadlineTicks);

        private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && Blocking) return new IntPtr(1);
            return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
        }

        private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && Blocking && IsClickOrWheel(wParam.ToInt32())) return new IntPtr(1);
            return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        private static bool IsClickOrWheel(int message) => message switch
        {
            0x0201 or 0x0202 or 0x0203 => true, // 左键
            0x0204 or 0x0205 or 0x0206 => true, // 右键
            0x0207 or 0x0208 or 0x0209 => true, // 中键
            0x020A or 0x020E => true, // 滚轮
            0x020B or 0x020C or 0x020D => true, // 侧键
            _ => false
        };
    }

    /// <summary>休息期间保持亮屏，并定期刷新系统空闲计时，避免屏保或自动锁屏盖住休息屏</summary>
    internal sealed class PowerAssertions
    {
        private bool active;
        private DateTime lastDeclared = DateTime.MinValue;

        public void Begin()
        {
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS | Native.ES_DISPLAY_REQUIRED | Native.ES_SYSTEM_REQUIRED);
            active = true;
            KeepAwake(force: true);
        }

        public void KeepAwake(bool force = false)
        {
            if (!active) return;
            if (!force && (DateTime.UtcNow - lastDeclared).TotalSeconds < 30) return;
            lastDeclared = DateTime.UtcNow;
            // 零位移的鼠标移动：光标不动，但系统的空闲计时会归零，相当于 Mac 版的「声明用户活动」
            var input = new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                mi = new Native.MOUSEINPUT { dwFlags = Native.MOUSEEVENTF_MOVE }
            };
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        public void End()
        {
            if (!active) return;
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            active = false;
            lastDeclared = DateTime.MinValue;
        }
    }

    /// <summary>把窗口拉到前台：系统限制后台进程抢焦点，先挂到当前前台线程的输入队列上再切换</summary>
    internal static class Foreground
    {
        public static void Force(IntPtr hwnd)
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground == hwnd) return;
            var foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
            var current = Native.GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != current && Native.AttachThreadInput(current, foregroundThread, true);
            Native.BringWindowToTop(hwnd);
            Native.SetForegroundWindow(hwnd);
            if (attached) Native.AttachThreadInput(current, foregroundThread, false);
        }
    }
}
