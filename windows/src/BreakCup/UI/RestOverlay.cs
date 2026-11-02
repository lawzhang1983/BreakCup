using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BreakCup.Core;
using BreakCup.Platform;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BreakCup.UI
{
    /// <summary>
    /// 强制休息屏：每块屏幕一扇置顶的全屏窗口，键鼠钩子吞掉输入，直到休息时间结束。
    /// 流程与 Mac 版 RestOverlayController 一致：进入时淡入，结束时先显示「欢迎回来」再淡出解锁。
    /// </summary>
    internal sealed class RestOverlay
    {
        private readonly List<RestWindow> windows = new List<RestWindow>();
        private readonly InputBlocker blocker = new InputBlocker();
        private readonly PowerAssertions power = new PowerAssertions();
        private readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private DispatcherTimer? tipTimer;
        private TipDeck deck = new TipDeck(TipCategories.All);
        private RestSession? session;
        private bool finishing;
        private string screens = "";
        private IntPtr previousForeground;
        private bool screenSaverAfterRest;

        public RestOverlay()
        {
            clockTimer.Tick += (_, _) =>
            {
                foreach (var window in windows) window.View.Update(DateTime.Now);
            };
            SystemEvents.DisplaySettingsChanged += (_, _) =>
                Application.Current.Dispatcher.BeginInvoke(new Action(RebuildIfScreensChanged));
        }

        public bool IsPresented { get; private set; }

        /// <summary>预告阶段先把壁纸模糊图算好，进休息时不卡顿</summary>
        public void Prepare() => Wallpaper.Background();

        /// <summary>进入或维持休息屏；重复调用只做巡检</summary>
        public void Present(DateTime startedAt, DateTime until, BreakSettings settings)
        {
            if (IsPresented)
            {
                if (!finishing) Enforce();
                return;
            }
            IsPresented = true;
            finishing = false;
            previousForeground = Native.GetForegroundWindow();

            deck = new TipDeck(settings.EnabledCategories);
            screenSaverAfterRest = settings.ScreenSaverAfterRest;
            session = new RestSession(deck.Next(), startedAt, until);
            tipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(settings.TipIntervalSeconds) };
            tipTimer.Tick += (_, _) => AdvanceTip();
            tipTimer.Start();

            blocker.Start(until.ToUniversalTime());
            power.Begin();
            BuildWindows(fadeIn: true);
            clockTimer.Start();
            Log.Write($"休息屏已覆盖 {windows.Count} 块屏幕");

            var check = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            check.Tick += (_, _) =>
            {
                check.Stop();
                if (!IsPresented) return;
                var foreground = Native.GetForegroundWindow();
                var ours = windows.Any(w => w.Handle == foreground);
                Log.Write($"巡检：休息屏在前台={ours} 键鼠拦截={blocker.IsActive}");
            };
            check.Start();
        }

        /// <summary>休息时间到：先显示「欢迎回来」，再淡出解锁；重复调用无副作用</summary>
        public void Dismiss()
        {
            if (!IsPresented || finishing) return;
            finishing = true;
            tipTimer?.Stop();
            tipTimer = null;
            if (session != null) session.IsFinished = true;
            After(1.6, FadeOutAndTearDown);
            // 兜底：淡出动画的回调万一没来，也要按时解锁
            After(3.5, () =>
            {
                if (!finishing) return;
                Log.Write("淡出回调超时，强制收尾");
                TearDown();
            });
        }

        /// <summary>每秒巡检：窗口保持最上层并在前台、屏幕数一致、不熄屏</summary>
        public void Enforce()
        {
            if (!IsPresented || finishing) return;
            RebuildIfScreensChanged();
            foreach (var window in windows) Screens.BringToTop(window);
            var foreground = Native.GetForegroundWindow();
            if (windows.Count > 0 && windows.All(w => w.Handle != foreground))
            {
                Foreground.Force(KeyWindow()?.Handle ?? windows[0].Handle);
            }
            if (!blocker.IsActive && session != null) blocker.Start(session.Until.ToUniversalTime());
            power.KeepAwake();
        }

        private void AdvanceTip()
        {
            if (session == null || finishing) return;
            session.Tip = deck.Next();
        }

        private void RebuildIfScreensChanged()
        {
            if (!IsPresented || finishing || screens == Screens.Fingerprint()) return;
            Log.Write($"屏幕布局变化，重建休息屏（{Screens.All.Length} 块）");
            BuildWindows(fadeIn: false);
        }

        private void BuildWindows(bool fadeIn)
        {
            // 收尾阶段绝不新建窗口，否则新窗口没人回收，屏幕会被一直盖住
            if (session == null || !IsPresented || finishing) return;
            var old = windows.ToList();
            windows.Clear();
            screens = Screens.Fingerprint();
            var background = Wallpaper.Background();
            foreach (var screen in Screens.All)
            {
                var snapshot = fadeIn ? Screens.Capture(screen) : null;
                var window = new RestWindow(screen, session, background, snapshot);
                windows.Add(window);
            }
            foreach (var window in old) window.CloseNow();
            foreach (var window in windows) window.Present(fadeIn);
            Foreground.Force(KeyWindow()?.Handle ?? windows[0].Handle);
            KeyWindow()?.Activate();
        }

        /// <summary>鼠标所在屏幕上的那扇窗口拿键盘焦点</summary>
        private RestWindow? KeyWindow()
        {
            var cursor = Forms.Cursor.Position;
            return windows.FirstOrDefault(w => w.Screen.Bounds.Contains(cursor)) ?? windows.FirstOrDefault();
        }

        private void FadeOutAndTearDown()
        {
            if (!finishing) return;
            var remaining = windows.Count;
            if (remaining == 0)
            {
                TearDown();
                return;
            }
            foreach (var window in windows)
            {
                window.FadeOut(() =>
                {
                    remaining -= 1;
                    if (remaining == 0) TearDown();
                });
            }
        }

        private void TearDown()
        {
            if (!IsPresented) return;
            var closing = windows.ToList();
            windows.Clear();
            session = null;
            IsPresented = false;
            finishing = false;
            clockTimer.Stop();
            blocker.Stop();
            power.End();
            foreach (var window in closing) window.CloseNow();
            if (previousForeground != IntPtr.Zero && Native.IsWindow(previousForeground))
            {
                Native.SetForegroundWindow(previousForeground);
            }
            previousForeground = IntPtr.Zero;
            Log.Write("休息屏已解除");
            // 休息时人可能走开了：解锁后屏幕不该一直亮着内容。亮屏请求已撤，焦点也已交还，屏保起来后不会被抢走激活而退出
            if (screenSaverAfterRest) ScreenSaver.Start();
        }

        private static void After(double seconds, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }
    }

    /// <summary>
    /// 休息屏窗口：盖住整块屏幕（含任务栏），置顶，隐藏鼠标指针，拒绝关闭。
    /// 底下垫一张进入休息前的屏幕截图，内容从截图上淡入、淡出，看起来就是窗口渐显渐隐。
    /// </summary>
    internal sealed class RestWindow : Window
    {
        private static readonly Duration Fade = new Duration(TimeSpan.FromMilliseconds(600));
        private readonly Grid layers = new Grid();
        private readonly Grid scene = new Grid();
        private readonly Image? snapshot;
        private bool allowClose;

        public RestWindow(Forms.Screen screen, RestSession session, ImageSource? background, ImageSource? snapshot)
        {
            Screen = screen;
            Title = "歇一杯休息中";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = Cursors.None;
            ForceCursor = true;

            var scale = Screens.Scale(screen);
            var size = new Size(screen.Bounds.Width / scale, screen.Bounds.Height / scale);
            if (snapshot != null)
            {
                this.snapshot = new Image { Source = snapshot, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(this.snapshot, BitmapScalingMode.NearestNeighbor);
                layers.Children.Add(this.snapshot);
            }
            scene.Children.Add(new DriftingBackground(background, size, animate: true));
            View = new RestView(session, size);
            scene.Children.Add(View);
            layers.Children.Add(scene);
            Content = layers;

            // 不进 Alt+Tab
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            Native.AddExStyle(hwnd, Native.WS_EX_TOOLWINDOW);
            Handle = hwnd;
        }

        public Forms.Screen Screen { get; }
        public RestView View { get; }
        public IntPtr Handle { get; }

        public void Present(bool fadeIn)
        {
            scene.Opacity = fadeIn && snapshot != null ? 0 : 1;
            Screens.Place(this, Screen.Bounds);
            Show();
            Screens.Place(this, Screen.Bounds);
            Screens.BringToTop(this);
            if (scene.Opacity < 1)
            {
                var animation = new DoubleAnimation(0, 1, Fade);
                // 淡入完成后撤掉截图层，省得一直参与合成
                animation.Completed += (_, _) =>
                {
                    if (snapshot != null) snapshot.Visibility = Visibility.Collapsed;
                };
                scene.BeginAnimation(OpacityProperty, animation);
            }
            else if (snapshot != null)
            {
                snapshot.Visibility = Visibility.Collapsed;
            }
        }

        public void FadeOut(Action completed)
        {
            if (snapshot == null)
            {
                completed();
                return;
            }
            snapshot.Visibility = Visibility.Visible;
            var animation = new DoubleAnimation(0, Fade);
            animation.Completed += (_, _) => completed();
            scene.BeginAnimation(OpacityProperty, animation);
        }

        public void CloseNow()
        {
            View.Detach();
            allowClose = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Alt+F4 已被钩子拦下，这里再兜一层
            if (!allowClose) e.Cancel = true;
            base.OnClosing(e);
        }
    }
}
