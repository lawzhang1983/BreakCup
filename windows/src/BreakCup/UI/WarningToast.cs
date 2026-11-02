using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BreakCup.Core;
using BreakCup.Platform;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BreakCup.UI
{
    /// <summary>到点前的预告条：每块屏幕顶部一条，不抢焦点、不挡点击</summary>
    internal sealed class WarningToast
    {
        public const double ToastWidth = 470;
        public const double ToastHeight = 76;

        private readonly List<Window> windows = new List<Window>();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly List<WarningView> views = new List<WarningView>();
        private DateTime? restAt;
        private DateTime shownAt;
        private double restMinutes = 5;
        private string screens = "";

        public WarningToast()
        {
            timer.Tick += (_, _) => Update();
            SystemEvents.DisplaySettingsChanged += (_, _) =>
            {
                if (restAt != null) Application.Current.Dispatcher.BeginInvoke(new Action(() => Rebuild(fadeIn: false)));
            };
        }

        public bool IsShowing => restAt != null;

        public void Show(DateTime restAt, double restMinutes)
        {
            if (this.restAt == restAt && screens == Screens.Fingerprint()) return;
            if (this.restAt != restAt) shownAt = DateTime.Now;
            this.restAt = restAt;
            this.restMinutes = restMinutes;
            Rebuild(fadeIn: windows.Count == 0);
            timer.Start();
        }

        /// <summary>immediate：进入休息时直接收掉，休息屏淡入用的屏幕截图里不会留下半透明的预告条</summary>
        public void Hide(bool immediate = false)
        {
            if (restAt == null) return;
            restAt = null;
            timer.Stop();
            var closing = new List<Window>(windows);
            windows.Clear();
            views.Clear();
            foreach (var window in closing)
            {
                if (immediate)
                {
                    window.Close();
                    continue;
                }
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) => window.Close();
                window.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private void Rebuild(bool fadeIn)
        {
            if (restAt is not DateTime at) return;
            foreach (var window in windows) window.Close();
            windows.Clear();
            views.Clear();
            screens = Screens.Fingerprint();
            foreach (var screen in Screens.All)
            {
                var view = new WarningView(at, shownAt, restMinutes);
                views.Add(view);
                var window = MakeWindow(view);
                var scale = Screens.Scale(screen);
                var width = (int)Math.Round(ToastWidth * scale);
                var height = (int)Math.Round(ToastHeight * scale);
                var work = screen.WorkingArea;
                // 窗口四周留出阴影的位置
                var margin = (int)Math.Round(WarningView.ShadowMargin * scale);
                var rect = new System.Drawing.Rectangle(
                    work.Left + (work.Width - width) / 2 - margin,
                    work.Top + (int)Math.Round(14 * scale) - margin,
                    width + margin * 2,
                    height + margin * 2);
                window.Opacity = fadeIn ? 0 : 1;
                Screens.Place(window, rect);
                window.Show();
                Screens.Place(window, rect);
                Screens.BringToTop(window);
                if (fadeIn) window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                windows.Add(window);
            }
        }

        private void Update()
        {
            foreach (var view in views) view.Update(DateTime.Now);
            foreach (var window in windows) Screens.BringToTop(window);
        }

        private static Window MakeWindow(WarningView view)
        {
            var window = new Window
            {
                Title = "歇一杯预告",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Focusable = false,
                IsHitTestVisible = false,
                Content = view
            };
            // 鼠标穿透、不进 Alt+Tab、不抢焦点
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            Native.AddExStyle(hwnd, Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            return window;
        }
    }

    /// <summary>预告条内容：倒计时圆环、说明文字和冒热气的咖啡杯，固定深色</summary>
    internal sealed class WarningView : Grid
    {
        public const double ShadowMargin = 12;
        private readonly DateTime restAt;
        private readonly DateTime shownAt;
        private readonly double restMinutes;
        private readonly CountdownRing ring = new CountdownRing { Width = 44, Height = 44 };
        private readonly TextBlock ringText;
        private readonly TextBlock title;

        public WarningView(DateTime restAt, DateTime shownAt, double restMinutes)
        {
            this.restAt = restAt;
            this.shownAt = shownAt;
            this.restMinutes = restMinutes;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            var white = Theme.Brush(Colors.White);
            ringText = new TextBlock
            {
                FontFamily = Theme.DigitFont,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = white,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(ringText, FontNumeralAlignment.Tabular);
            var ringBox = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
            ringBox.Children.Add(ring);
            ringBox.Children.Add(ringText);

            title = new TextBlock { FontFamily = Theme.UiFont, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = white };
            Typography.SetNumeralAlignment(title, FontNumeralAlignment.Tabular);
            var subtitle = new TextBlock
            {
                Text = "先保存手头的工作，准备起身走一走",
                FontFamily = Theme.UiFont,
                FontSize = 12,
                Foreground = Theme.Brush(0xA6, Colors.White),
                Margin = new Thickness(0, 3, 0, 0)
            };
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            texts.Children.Add(title);
            texts.Children.Add(subtitle);

            var cup = new Path { Data = Icons.CoffeeFilled, Fill = new SolidColorBrush(Theme.Latte), Stretch = Stretch.Uniform, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };

            var row = new DockPanel { Margin = new Thickness(16, 0, 16, 0), LastChildFill = true };
            DockPanel.SetDock(ringBox, Dock.Left);
            DockPanel.SetDock(cup, Dock.Right);
            row.Children.Add(ringBox);
            row.Children.Add(cup);
            row.Children.Add(texts);

            var card = new Border
            {
                Width = WarningToast.ToastWidth,
                Height = WarningToast.ToastHeight,
                CornerRadius = new CornerRadius(18),
                Background = Theme.Brush(0xF0, Color.FromRgb(0x1C, 0x1C, 0x1C)),
                BorderBrush = Theme.Brush(0x1A, Colors.White),
                BorderThickness = new Thickness(1),
                Child = row,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.35 }
            };
            Margin = new Thickness(0);
            Children.Add(card);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            Update(DateTime.Now);
        }

        public void Update(DateTime now)
        {
            var total = Math.Max(1, (restAt - shownAt).TotalSeconds);
            var remaining = Math.Max(0, (restAt - now).TotalSeconds);
            var seconds = (int)Math.Ceiling(remaining);
            ring.Fraction = remaining / total;
            ringText.Text = seconds.ToString();
            title.Text = $"{seconds} 秒后开始休息 {BreakFormat.Minutes(restMinutes)}";
        }
    }
}
