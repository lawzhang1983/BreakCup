using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BreakCup.Core;

namespace BreakCup.UI
{
    /// <summary>说明文字收进感叹号，鼠标指上去 0.25 秒显示，面板保持干净；需要处理的问题用橙色</summary>
    internal sealed class HintMark : Border
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(HintMark), new PropertyMetadata("", (d, _) => ((HintMark)d).Update()));

        public static readonly DependencyProperty AttentionProperty = DependencyProperty.Register(
            nameof(Attention), typeof(bool), typeof(HintMark), new PropertyMetadata(false, (d, _) => ((HintMark)d).Update()));

        private readonly Path icon = new Path { Data = Icons.Hint, Stretch = Stretch.Uniform, Width = 13, Height = 13 };
        private readonly TextBlock tipText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 300, LineHeight = 18 };

        public HintMark()
        {
            Child = icon;
            Padding = new Thickness(3);
            Background = Brushes.Transparent;
            VerticalAlignment = VerticalAlignment.Center;
            ToolTip = new ToolTip { Content = tipText };
            ToolTipService.SetInitialShowDelay(this, 250);
            ToolTipService.SetBetweenShowDelay(this, 0);
            ToolTipService.SetShowDuration(this, 60_000);
            Update();
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public bool Attention
        {
            get => (bool)GetValue(AttentionProperty);
            set => SetValue(AttentionProperty, value);
        }

        private void Update()
        {
            var text = Text ?? "";
            tipText.Text = text;
            Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            icon.SetResourceReference(Shape.FillProperty, Attention ? Keys.Attention : Keys.SecondaryText);
            AutomationProperties.SetName(this, text);
        }
    }

    /// <summary>Windows 11 样式的开关</summary>
    internal sealed class SwitchToggle : FrameworkElement
    {
        public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
            nameof(IsChecked), typeof(bool), typeof(SwitchToggle),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty OnBrushProperty = Render(nameof(OnBrush));
        public static readonly DependencyProperty OffBrushProperty = Render(nameof(OffBrush));
        public static readonly DependencyProperty ThumbOnBrushProperty = Render(nameof(ThumbOnBrush));

        private bool hover;

        public SwitchToggle()
        {
            Width = 40;
            Height = 20;
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;
            SetResourceReference(OnBrushProperty, Keys.Coffee);
            SetResourceReference(OffBrushProperty, Keys.Track);
            SetResourceReference(ThumbOnBrushProperty, Keys.OnCoffee);
        }

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        public Action<bool>? Toggled { get; set; }

        public Brush OnBrush => (Brush)GetValue(OnBrushProperty);
        public Brush OffBrush => (Brush)GetValue(OffBrushProperty);
        public Brush ThumbOnBrush => (Brush)GetValue(ThumbOnBrushProperty);

        private static DependencyProperty Render(string name) => DependencyProperty.Register(
            name, typeof(Brush), typeof(SwitchToggle), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            hover = true;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            hover = false;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
                if (IsMouseOver)
                {
                    // 由视图模型决定新值（例如开机启动可能设置失败），没接视图模型时自己翻转
                    if (Toggled != null) Toggled(!IsChecked);
                    else IsChecked = !IsChecked;
                }
            }
            e.Handled = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var track = new Rect(0.5, 0.5, 39, 19);
            if (IsChecked)
            {
                dc.DrawRoundedRectangle(OnBrush, null, track, 9.5, 9.5);
                dc.DrawEllipse(ThumbOnBrush, null, new Point(30, 10), hover ? 7.5 : 7, hover ? 7.5 : 7);
            }
            else
            {
                dc.DrawRoundedRectangle(Brushes.Transparent, new Pen(OffBrush, 1), track, 9.5, 9.5);
                dc.DrawEllipse(OffBrush, null, new Point(10, 10), hover ? 6 : 5, hover ? 6 : 5);
            }
        }
    }

    internal enum ButtonTone
    {
        Normal,
        Accent,
        Warning
    }

    /// <summary>扁平按钮：普通、咖啡色强调、橙色确认三种色调</summary>
    internal sealed class FlatButton : Border
    {
        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
            nameof(Command), typeof(ICommand), typeof(FlatButton), new PropertyMetadata(null, OnCommandChanged));

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
            nameof(CommandParameter), typeof(object), typeof(FlatButton));

        public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
            nameof(Tone), typeof(ButtonTone), typeof(FlatButton), new PropertyMetadata(ButtonTone.Normal, (d, _) => ((FlatButton)d).Restyle()));

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(FlatButton), new PropertyMetadata("", (d, e) => ((FlatButton)d).label.Text = (string)e.NewValue));

        private readonly TextBlock label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        private bool hover;
        private bool pressed;

        public FlatButton()
        {
            Child = label;
            CornerRadius = new CornerRadius(4);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(11, 3, 11, 4);
            Cursor = Cursors.Hand;
            IsEnabledChanged += (_, _) => Restyle();
            Restyle();
        }

        public ICommand? Command
        {
            get => (ICommand?)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public object? CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }

        public ButtonTone Tone
        {
            get => (ButtonTone)GetValue(ToneProperty);
            set => SetValue(ToneProperty, value);
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = (FlatButton)d;
            if (e.OldValue is ICommand old) old.CanExecuteChanged -= button.OnCanExecuteChanged;
            if (e.NewValue is ICommand command) command.CanExecuteChanged += button.OnCanExecuteChanged;
            button.OnCanExecuteChanged(null, EventArgs.Empty);
        }

        private void OnCanExecuteChanged(object? sender, EventArgs e) =>
            IsEnabled = Command?.CanExecute(CommandParameter) ?? true;

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            hover = true;
            Restyle();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            hover = false;
            pressed = false;
            Restyle();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            pressed = true;
            CaptureMouse();
            Restyle();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            var wasPressed = pressed && IsMouseCaptured;
            pressed = false;
            ReleaseMouseCapture();
            Restyle();
            e.Handled = true;
            if (wasPressed && IsMouseOver && IsEnabled && (Command?.CanExecute(CommandParameter) ?? false))
            {
                Command.Execute(CommandParameter);
            }
        }

        private void Restyle()
        {
            Opacity = IsEnabled ? 1 : 0.45;
            switch (Tone)
            {
                case ButtonTone.Accent:
                case ButtonTone.Warning:
                    if (Tone == ButtonTone.Accent)
                    {
                        SetResourceReference(BackgroundProperty, Keys.Coffee);
                        label.SetResourceReference(TextBlock.ForegroundProperty, Keys.OnCoffee);
                    }
                    else
                    {
                        SetResourceReference(BackgroundProperty, Keys.Orange);
                        label.Foreground = Brushes.White;
                    }
                    BorderBrush = Brushes.Transparent;
                    Opacity = !IsEnabled ? 0.45 : pressed ? 0.8 : hover ? 0.9 : 1;
                    break;
                default:
                    SetResourceReference(BackgroundProperty, pressed ? Keys.ControlPressed : hover ? Keys.ControlHover : Keys.Control);
                    SetResourceReference(BorderBrushProperty, Keys.ControlBorder);
                    label.SetResourceReference(TextBlock.ForegroundProperty, Keys.Text);
                    break;
            }
        }
    }

    /// <summary>竖排上下箭头的步进器，和 Mac 版一致；到了上下限对应箭头变灰</summary>
    internal sealed class Stepper : Border
    {
        public Stepper(ICommand command, string key)
        {
            Width = 18;
            Height = 24;
            CornerRadius = new CornerRadius(4);
            BorderThickness = new Thickness(1);
            SetResourceReference(BackgroundProperty, Keys.Control);
            SetResourceReference(BorderBrushProperty, Keys.ControlBorder);
            VerticalAlignment = VerticalAlignment.Center;
            var stack = new Grid();
            stack.RowDefinitions.Add(new RowDefinition());
            stack.RowDefinitions.Add(new RowDefinition());
            var up = Half(command, key + "+", "M0,4 L4,0 L8,4");
            var down = Half(command, key + "-", "M0,0 L4,4 L8,0");
            Grid.SetRow(down, 1);
            stack.Children.Add(up);
            stack.Children.Add(down);
            Child = stack;
        }

        private static Border Half(ICommand command, string parameter, string chevron)
        {
            var path = new Path
            {
                Data = Geometry.Parse(chevron),
                StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            path.SetResourceReference(Shape.StrokeProperty, Keys.SecondaryText);
            var half = new Border { Child = path, Background = Brushes.Transparent, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(3) };
            void Refresh() => half.Opacity = command.CanExecute(parameter) ? 1 : 0.35;
            command.CanExecuteChanged += (_, _) => Refresh();
            Refresh();
            half.MouseEnter += (_, _) => half.SetResourceReference(BackgroundProperty, Keys.ControlHover);
            half.MouseLeave += (_, _) => half.Background = Brushes.Transparent;
            half.MouseLeftButtonDown += (_, e) => e.Handled = true;
            half.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (command.CanExecute(parameter)) command.Execute(parameter);
            };
            return half;
        }
    }

    /// <summary>休息屏提示类别的开关胶囊</summary>
    internal sealed class CategoryChip : Border
    {
        private readonly Path icon;
        private readonly TextBlock title;
        private bool selected;

        public CategoryChip(TipCategory category, Action onClick)
        {
            icon = new Path { Data = Icons.For(category), Stretch = Stretch.Uniform, Width = 13, Height = 13, Margin = new Thickness(0, 0, 5, 0) };
            title = new TextBlock { Text = category.Title(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(icon);
            row.Children.Add(title);
            Child = row;
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(0, 4, 0, 5);
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += (_, e) => e.Handled = true;
            MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };
            Restyle();
        }

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                Restyle();
            }
        }

        private void Restyle()
        {
            var foreground = selected ? Keys.Coffee : Keys.SecondaryText;
            icon.SetResourceReference(Shape.FillProperty, foreground);
            title.SetResourceReference(TextBlock.ForegroundProperty, foreground);
            SetResourceReference(BackgroundProperty, selected ? Keys.CoffeeSoft : Keys.Neutral);
            if (selected)
            {
                SetResourceReference(BorderBrushProperty, Keys.CoffeeBorder);
            }
            else
            {
                BorderBrush = Brushes.Transparent;
            }
        }
    }

    /// <summary>细进度条，两端圆角</summary>
    internal sealed class SlimProgress : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(SlimProgress), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(SlimProgress), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
            nameof(FillBrush), typeof(Brush), typeof(SlimProgress), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush FillBrush
        {
            get => (Brush)GetValue(FillBrushProperty);
            set => SetValue(FillBrushProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth, height = ActualHeight, radius = height / 2;
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, width, height), radius, radius);
            var filled = width * Math.Min(1, Math.Max(0, Value));
            if (filled > 0)
            {
                dc.DrawRoundedRectangle(FillBrush, null, new Rect(0, 0, Math.Max(filled, height), height), radius, radius);
            }
        }
    }

    /// <summary>预告条左侧的倒计时圆环</summary>
    internal sealed class CountdownRing : FrameworkElement
    {
        public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
            nameof(Fraction), typeof(double), typeof(CountdownRing), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Fraction
        {
            get => (double)GetValue(FractionProperty);
            set => SetValue(FractionProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            const double thickness = 4;
            var radius = Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(null, new Pen(Theme.Brush(0x26, Colors.White), thickness), center, radius, radius);

            var fraction = Math.Min(1, Math.Max(0, Fraction));
            if (fraction <= 0) return;
            var pen = new Pen(new SolidColorBrush(Theme.Latte), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (fraction >= 0.999)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
                return;
            }
            var angle = fraction * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false, false);
                context.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>
    /// 可滚动区域：内容超出屏幕高度（如 1080p 屏开 150% 缩放）时用滚轮滚动，
    /// 右侧只画一条细的位置指示，不占面板宽度。
    /// </summary>
    internal sealed class ScrollHost : Grid
    {
        private readonly ScrollViewer viewer;
        private readonly Border indicator;

        public ScrollHost(UIElement content)
        {
            viewer = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false
            };
            indicator = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 3, 0),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            indicator.SetResourceReference(BackgroundProperty, Keys.Track);
            Children.Add(viewer);
            Children.Add(indicator);
            viewer.ScrollChanged += (_, _) => UpdateIndicator();
        }

        public double MaxViewportHeight
        {
            get => viewer.MaxHeight;
            set => viewer.MaxHeight = value;
        }

        private void UpdateIndicator()
        {
            double viewport = viewer.ViewportHeight, extent = viewer.ExtentHeight;
            if (extent <= viewport + 1 || viewport <= 0)
            {
                indicator.Visibility = Visibility.Collapsed;
                return;
            }
            indicator.Visibility = Visibility.Visible;
            indicator.Height = Math.Max(24, viewport * viewport / extent);
            var travel = viewport - indicator.Height;
            indicator.Margin = new Thickness(0, 4 + travel * viewer.VerticalOffset / Math.Max(1, extent - viewport) * (viewport - 8) / viewport, 3, 0);
        }
    }
}
