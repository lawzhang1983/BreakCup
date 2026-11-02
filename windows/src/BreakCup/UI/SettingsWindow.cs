using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using BreakCup.Platform;
using Forms = System.Windows.Forms;

namespace BreakCup.UI
{
    /// <summary>
    /// 托盘弹出的设置面板：贴着任务栏、靠近点击位置弹出，点到别处就收起，
    /// 和 Mac 版菜单栏弹窗的行为一致。屏幕放不下时可以滚动。
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        private const double ShadowMargin = 12;
        private const double ScreenGap = 4;
        private readonly ScrollHost scroll;
        private readonly Border frame;
        private DateTime hiddenAt = DateTime.MinValue;
        private System.Drawing.Point anchor;

        private readonly AppModel model;

        public SettingsWindow(AppModel model)
        {
            this.model = model;
            Title = "歇一杯";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = true;

            View = new SettingsView(model);
            scroll = new ScrollHost(View);
            frame = new Border
            {
                Child = scroll,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(ShadowMargin),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.28 }
            };
            frame.SetResourceReference(Border.BackgroundProperty, Keys.Panel);
            frame.SetResourceReference(Border.BorderBrushProperty, Keys.PanelBorder);
            // 圆角裁切，滚动时内容不会盖出边框
            scroll.ClipToBounds = true;
            Content = frame;

            Deactivated += (_, _) => HidePanel();
            SizeChanged += (_, _) =>
            {
                if (IsVisible) Reposition();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) HidePanel();
            };
        }

        public SettingsView View { get; }

        public void Toggle()
        {
            if (IsVisible)
            {
                HidePanel();
                return;
            }
            // 面板打开时点托盘图标：点击先让面板失焦收起，这里不要再弹出来
            if ((DateTime.UtcNow - hiddenAt).TotalMilliseconds < 350) return;
            ShowPanel();
        }

        public void ShowPanel()
        {
            model.RefreshScreenSaverHint();
            anchor = Forms.Cursor.Position;
            var screen = Forms.Screen.FromPoint(anchor);
            var scale = Screens.Scale(screen);
            scroll.MaxViewportHeight = Math.Max(240, screen.WorkingArea.Height / scale - ShadowMargin * 2 - ScreenGap * 2 - 2);
            Opacity = 0;
            if (!IsVisible) Show();
            Reposition();
            Activate();
            Platform.Foreground.Force(new WindowInteropHelper(this).Handle);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        }

        public void HidePanel()
        {
            if (!IsVisible) return;
            hiddenAt = DateTime.UtcNow;
            BeginAnimation(OpacityProperty, null);
            Hide();
        }

        /// <summary>
        /// 贴着任务栏摆：任务栏在下（默认）就让面板底边贴工作区底边、水平方向对着点击位置，
        /// 任务栏在上、左、右时同理，最后夹回工作区内。
        /// </summary>
        private void Reposition()
        {
            var screen = Forms.Screen.FromPoint(anchor);
            var work = screen.WorkingArea;
            var bounds = screen.Bounds;
            var scale = Screens.Scale(screen);
            Screens.Place(this, new System.Drawing.Rectangle(work.Right - 10, work.Bottom - 10, 1, 1), resize: false);
            var size = Screens.WindowRect(this).Size;
            var shadow = (int)Math.Round(ShadowMargin * scale);
            var gap = (int)Math.Round(ScreenGap * scale);

            int x, y;
            if (work.Bottom < bounds.Bottom || anchor.Y >= work.Bottom)
            {
                x = anchor.X - size.Width / 2;
                y = work.Bottom - size.Height + shadow - gap;
            }
            else if (work.Top > bounds.Top)
            {
                x = anchor.X - size.Width / 2;
                y = work.Top - shadow + gap;
            }
            else if (work.Left > bounds.Left)
            {
                x = work.Left - shadow + gap;
                y = anchor.Y - size.Height / 2;
            }
            else if (work.Right < bounds.Right)
            {
                x = work.Right - size.Width + shadow - gap;
                y = anchor.Y - size.Height / 2;
            }
            else
            {
                x = anchor.X - size.Width / 2;
                y = work.Bottom - size.Height + shadow - gap;
            }
            x = Clamp(x, work.Left - shadow + gap, work.Right - size.Width + shadow - gap);
            y = Clamp(y, work.Top - shadow + gap, work.Bottom - size.Height + shadow - gap);
            Screens.Place(this, new System.Drawing.Rectangle(x, y, size.Width, size.Height), resize: false);
        }

        private static int Clamp(int value, int min, int max) => max < min ? min : Math.Min(Math.Max(value, min), max);
    }
}
