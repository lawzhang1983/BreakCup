using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BreakCup.Core;

namespace BreakCup.UI
{
    /// <summary>一次休息的共享状态；多块屏幕上的休息屏观察同一个会话</summary>
    internal sealed class RestSession
    {
        private Tip tip;
        private bool finished;

        public RestSession(Tip tip, DateTime startedAt, DateTime until)
        {
            this.tip = tip;
            StartedAt = startedAt;
            Until = until;
        }

        public DateTime StartedAt { get; }
        public DateTime Until { get; }

        public event Action? TipChanged;
        public event Action? Finished;

        public Tip Tip
        {
            get => tip;
            set
            {
                tip = value;
                TipChanged?.Invoke();
            }
        }

        public bool IsFinished
        {
            get => finished;
            set
            {
                if (finished == value) return;
                finished = value;
                if (value) Finished?.Invoke();
            }
        }
    }

    /// <summary>
    /// 休息屏内容。排版以 16:9 高度为基准单位，带鱼屏和竖屏也不至于失衡；尺寸比例与 Mac 版 RestView 一致。
    /// 背景层（模糊壁纸的缓慢推拉）由 <see cref="DriftingBackground"/> 提供，这里只画渐变遮罩和文字。
    /// </summary>
    internal sealed class RestView : Grid
    {
        private static readonly Duration TipFade = new Duration(TimeSpan.FromMilliseconds(400));
        private readonly RestSession session;
        private readonly double unit;
        private readonly TextBlock clock;
        private readonly SlimProgress progress;
        private readonly Border tipCard;
        private readonly Path tipIcon;
        private readonly TextBlock tipTitle;
        private readonly TextBlock tipText;
        private readonly TranslateTransform tipOffset = new TranslateTransform();
        private readonly FrameworkElement content;
        private readonly FrameworkElement welcome;
        private readonly FrameworkElement footer;

        public RestView(RestSession session, Size size)
        {
            this.session = session;
            unit = Math.Min(size.Height, size.Width * 0.5625);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextElement.SetFontFamily(this, Theme.UiFont);
            TextElement.SetForeground(this, Brushes.White);

            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x8C, 0, 0, 0), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x4D, 0, 0, 0), 0.5));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x9E, 0, 0, 0), 1));
            shade.Freeze();
            Children.Add(new Rectangle { Fill = shade });

            // 标题行
            var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var labelBrush = Theme.Brush(0xC7, Colors.White);
            label.Children.Add(new Path
            {
                Data = Icons.CoffeeFilled,
                Fill = labelBrush,
                Stretch = Stretch.Uniform,
                Height = unit * 0.024,
                Width = unit * 0.024,
                Margin = new Thickness(0, 0, unit * 0.008, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            label.Children.Add(new TextBlock { Text = "歇一杯 · 起身休息中", FontSize = unit * 0.021, FontWeight = FontWeights.Medium, Foreground = labelBrush, VerticalAlignment = VerticalAlignment.Center });

            // 大号倒计时和进度条
            clock = new TextBlock
            {
                FontFamily = Theme.DigitFont,
                FontSize = unit * 0.15,
                FontWeight = FontWeights.Thin,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Typography.SetNumeralAlignment(clock, FontNumeralAlignment.Tabular);
            progress = new SlimProgress
            {
                Width = unit * 0.56,
                Height = Math.Max(4, unit * 0.0045),
                TrackBrush = Theme.Brush(0x29, Colors.White),
                FillBrush = Theme.Brush(Theme.Latte),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, unit * 0.022, 0, 0)
            };

            // 提示卡
            tipIcon = new Path { Fill = Theme.Brush(Theme.Latte), Stretch = Stretch.Uniform, Width = unit * 0.02, Height = unit * 0.02, Margin = new Thickness(0, 0, unit * 0.007, 0), VerticalAlignment = VerticalAlignment.Center };
            tipTitle = new TextBlock { FontSize = unit * 0.019, FontWeight = FontWeights.SemiBold, Foreground = Theme.Brush(Theme.Latte), VerticalAlignment = VerticalAlignment.Center };
            var pillRow = new StackPanel { Orientation = Orientation.Horizontal };
            pillRow.Children.Add(tipIcon);
            pillRow.Children.Add(tipTitle);
            var pill = new Border
            {
                Child = pillRow,
                Background = Theme.Brush(0x1A, Colors.White),
                CornerRadius = new CornerRadius(unit),
                Padding = new Thickness(unit * 0.016, unit * 0.007, unit * 0.016, unit * 0.007),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            tipText = new TextBlock
            {
                FontSize = unit * 0.032,
                Foreground = Theme.Brush(0xF2, Colors.White),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = unit * 0.032 * 1.3 + unit * 0.009,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, unit * 0.02, 0, 0)
            };
            var cardStack = new StackPanel();
            cardStack.Children.Add(pill);
            cardStack.Children.Add(tipText);
            tipCard = new Border
            {
                Child = cardStack,
                Width = unit * 1.1,
                Padding = new Thickness(unit * 0.05, unit * 0.036, unit * 0.05, unit * 0.036),
                Background = Theme.Brush(0x4D, Colors.Black),
                BorderBrush = Theme.Brush(0x1A, Colors.White),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(unit * 0.026),
                Margin = new Thickness(0, unit * 0.034 + unit * 0.02, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransform = tipOffset
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, unit * 0.04) };
            stack.Children.Add(label);
            clock.Margin = new Thickness(0, unit * 0.034 - unit * 0.025, 0, 0);
            stack.Children.Add(clock);
            stack.Children.Add(progress);
            stack.Children.Add(tipCard);
            content = stack;
            Children.Add(content);

            // 休息结束的欢迎画面
            var welcomeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
            welcomeStack.Children.Add(new Path { Data = Icons.CoffeeFilled, Fill = Theme.Brush(Theme.Latte), Stretch = Stretch.Uniform, Height = unit * 0.09, Width = unit * 0.09 });
            welcomeStack.Children.Add(new TextBlock { Text = "休息结束，欢迎回来", FontSize = unit * 0.05, FontWeight = FontWeights.Light, Margin = new Thickness(0, unit * 0.03, 0, 0) });
            welcome = welcomeStack;
            Children.Add(welcome);

            var footerText = new TextBlock
            {
                Text = $"{session.Until:HH:mm} 自动结束 · 休息期间键盘和鼠标暂停使用",
                FontSize = unit * 0.016,
                Foreground = Theme.Brush(0x80, Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, unit * 0.05)
            };
            footer = footerText;
            Children.Add(footer);

            ShowTip(session.Tip);
            session.TipChanged += OnTipChanged;
            session.Finished += OnFinished;
            if (session.IsFinished) ApplyFinished(animated: false);
            Update(DateTime.Now);
        }

        public void Update(DateTime now)
        {
            var total = Math.Max(1, (session.Until - session.StartedAt).TotalSeconds);
            var remaining = Math.Max(0, (session.Until - now).TotalSeconds);
            clock.Text = BreakFormat.Clock(remaining);
            progress.Value = 1 - remaining / total;
        }

        public void Detach()
        {
            session.TipChanged -= OnTipChanged;
            session.Finished -= OnFinished;
        }

        private void ShowTip(Tip tip)
        {
            tipIcon.Data = Icons.For(tip.Category);
            tipTitle.Text = tip.Category.Title();
            tipText.Text = tip.Text;
        }

        /// <summary>换提示：旧卡片淡出，新卡片从略低处淡入</summary>
        private void OnTipChanged()
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var fadeOut = new DoubleAnimation(0, TipFade) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                ShowTip(session.Tip);
                tipCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TipFade) { EasingFunction = ease });
                tipOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(unit * 0.01, 0, TipFade) { EasingFunction = ease });
            };
            tipCard.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void OnFinished() => ApplyFinished(animated: true);

        private void ApplyFinished(bool animated)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(animated ? 500 : 0));
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
            footer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
            welcome.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        }
    }

    /// <summary>
    /// 壁纸模糊图做底，40 秒一个来回缓慢推拉平移；没有壁纸时退回咖啡色渐变。
    /// 动画交给 WPF 合成线程，帧率限到 30，全屏也不怎么占资源。
    /// </summary>
    internal sealed class DriftingBackground : Grid
    {
        public static readonly Color FallbackTop = Color.FromRgb(0x2B, 0x1F, 0x1A);
        public static readonly Color FallbackBottom = Color.FromRgb(0x5C, 0x40, 0x30);

        public DriftingBackground(ImageSource? image, Size size, bool animate)
        {
            ClipToBounds = true;
            Background = Theme.Brush(FallbackTop);
            FrameworkElement layer;
            if (image != null)
            {
                layer = new Image { Source = image, Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(layer, BitmapScalingMode.Linear);
            }
            else
            {
                var gradient = new LinearGradientBrush(FallbackTop, FallbackBottom, new Point(0, 0), new Point(1, 1));
                gradient.Freeze();
                layer = new Rectangle { Fill = gradient };
            }
            var scale = new ScaleTransform(1.03, 1.03);
            var shift = new TranslateTransform(size.Width * 0.015, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(shift);
            layer.RenderTransform = group;
            layer.RenderTransformOrigin = new Point(0.5, 0.5);
            Children.Add(layer);
            if (!animate) return;

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            Timeline Drift(double from, double to)
            {
                var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(40))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = ease
                };
                Timeline.SetDesiredFrameRate(animation, 30);
                return animation;
            }
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, (DoubleAnimation)Drift(1.03, 1.12));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, (DoubleAnimation)Drift(1.03, 1.12));
            shift.BeginAnimation(TranslateTransform.XProperty, (DoubleAnimation)Drift(size.Width * 0.015, -size.Width * 0.015));
        }
    }
}
