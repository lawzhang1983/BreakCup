using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using BreakCup.Core;

namespace BreakCup.UI
{
    /// <summary>托盘弹出的参数设置面板，结构与 Mac 版 SettingsView 一一对应</summary>
    internal sealed class SettingsView : StackPanel
    {
        public const double PanelWidth = 360;
        private const double BodySize = 13;
        private const double CaptionSize = 12;

        private readonly AppModel model;
        private readonly List<(TipCategory Category, CategoryChip Chip)> chips = new List<(TipCategory, CategoryChip)>();
        private readonly List<HintMark> hints = new List<HintMark>();

        public SettingsView(AppModel model)
        {
            this.model = model;
            Width = PanelWidth;
            Margin = new Thickness(0);
            TextElement.SetFontFamily(this, Theme.UiFont);
            TextElement.SetFontSize(this, BodySize);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(TextElement.ForegroundProperty, Keys.Text);

            var body = new StackPanel { Margin = new Thickness(16) };
            Add(body, Header(), 0);
            Add(body, StatusCard(), 14);
            Add(body, Section("时间",
                StepperRow("可使用时间", "work", s => BreakFormat.Minutes(s.WorkMinutes)),
                StepperRow("休息时间", "rest", s => BreakFormat.Minutes(s.RestMinutes)),
                StepperRow("到点前预告", "warning", s => s.WarningSeconds == 0 ? "不预告" : $"{(int)s.WarningSeconds} 秒")), 14);
            Add(body, Section("智能判断",
                SwitchRow("离开电脑视为已休息", nameof(AppModel.AwayHint), false,
                    s => s.Settings.ResetWhenAway, model.SetResetWhenAway),
                SwitchRow("开会时推迟休息", nameof(AppModel.MeetingHint), false,
                    s => s.Settings.DeferWhenMediaInUse, model.SetDeferWhenMediaInUse)), 14);
            Add(body, Section("休息屏",
                StepperRow("提示轮换间隔", "tip", s => $"{(int)s.TipIntervalSeconds} 秒"),
                CategoryGrid(),
                SwitchRow("休息结束后启动屏幕保护", nameof(AppModel.ScreenSaverHint), false,
                    s => s.Settings.ScreenSaverAfterRest, model.SetScreenSaverAfterRest)), 14);
            Add(body, Section("其他",
                SwitchRow("托盘图标显示剩余时间", null, false,
                    s => s.Settings.ShowCountdownInTray, model.SetShowCountdown),
                SwitchRow("登录时自动启动", nameof(AppModel.LoginItemNote), true,
                    s => s.LaunchAtLogin, model.SetLaunchAtLogin)), 14);
            Add(body, Footer(), 14);
            Children.Add(body);

            model.PropertyChanged += OnModelChanged;
            RefreshChips();
        }

        /// <summary>面板上所有感叹号的说明文字，截图验收时核对</summary>
        public IEnumerable<string> HintTexts()
        {
            foreach (var hint in hints)
            {
                if (!string.IsNullOrEmpty(hint.Text)) yield return hint.Text;
            }
        }

        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppModel.Settings)) RefreshChips();
        }

        private void RefreshChips()
        {
            foreach (var (category, chip) in chips)
            {
                chip.Selected = model.Settings.EnabledCategories.Contains(category);
            }
        }

        private static void Add(Panel panel, UIElement child, double top)
        {
            if (child is FrameworkElement element) element.Margin = new Thickness(0, top, 0, 0);
            panel.Children.Add(child);
        }

        private UIElement Header()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Path { Data = Icons.CoffeeFilled, Stretch = Stretch.Uniform, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(Shape.FillProperty, Keys.Coffee);
            grid.Children.Add(icon);

            var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = "歇一杯", FontSize = 14, FontWeight = FontWeights.SemiBold });
            titles.Children.Add(Secondary(new TextBlock { Text = "BreakCup · 久坐强制休息", FontSize = 11, Margin = new Thickness(0, 1, 0, 0) }));
            Grid.SetColumn(titles, 1);
            grid.Children.Add(titles);

            var pill = PhasePill();
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
            return grid;
        }

        private FrameworkElement PhasePill()
        {
            var text = new TextBlock { FontSize = CaptionSize, FontWeight = FontWeights.Medium };
            var pill = new Border
            {
                Child = text,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            void Update()
            {
                text.Text = model.Phase.Title();
                var (foreground, background) = model.Phase switch
                {
                    PhaseKind.Working => (Keys.Green, Keys.GreenSoft),
                    PhaseKind.Warning => (Keys.Orange, Keys.OrangeSoft),
                    PhaseKind.Deferred => (Keys.Blue, Keys.BlueSoft),
                    _ => (Keys.Coffee, Keys.CoffeeSoft)
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, foreground);
                pill.SetResourceReference(Border.BackgroundProperty, background);
            }
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppModel.Phase)) Update();
            };
            Update();
            return pill;
        }

        private UIElement StatusCard()
        {
            var stack = new StackPanel();

            var caption = new StackPanel { Orientation = Orientation.Horizontal };
            var captionText = Secondary(new TextBlock { FontSize = CaptionSize, VerticalAlignment = VerticalAlignment.Center });
            Bind(captionText, TextBlock.TextProperty, nameof(AppModel.CountdownCaption));
            caption.Children.Add(captionText);
            caption.Children.Add(Hint(nameof(AppModel.Detail), false, new Thickness(2, 0, 0, 0)));
            stack.Children.Add(caption);

            var countdown = new TextBlock
            {
                FontFamily = Theme.DigitFont,
                FontSize = 38,
                FontWeight = FontWeights.Light,
                LineHeight = 46,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 4, 0, 8)
            };
            Typography.SetNumeralAlignment(countdown, FontNumeralAlignment.Tabular);
            TextOptions.SetTextFormattingMode(countdown, TextFormattingMode.Ideal);
            Bind(countdown, TextBlock.TextProperty, nameof(AppModel.Countdown));
            stack.Children.Add(countdown);

            var progress = new SlimProgress { Height = 4 };
            progress.SetResourceReference(SlimProgress.FillBrushProperty, Keys.Coffee);
            progress.SetResourceReference(SlimProgress.TrackBrushProperty, Keys.ControlBorder);
            Bind(progress, SlimProgress.ValueProperty, nameof(AppModel.Progress));
            stack.Children.Add(progress);

            var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            var restNow = new FlatButton { Command = model.RestNowCommand, Tone = ButtonTone.Accent, Text = "立即休息" };
            restNow.ToolTip = "开始后要等休息时间结束才能解锁，需点两次确认";
            ToolTipService.SetInitialShowDelay(restNow, 250);
            ToolTipService.SetShowOnDisabled(restNow, true);
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AppModel.ConfirmingRest)) return;
                restNow.Text = model.ConfirmingRest ? "确认开始休息" : "立即休息";
                restNow.Tone = model.ConfirmingRest ? ButtonTone.Warning : ButtonTone.Accent;
            };
            actions.Children.Add(restNow);
            actions.Children.Add(new FlatButton { Command = model.RestartWorkCommand, Text = "重新计时", Margin = new Thickness(8, 0, 0, 0) });
            var count = Secondary(new TextBlock { FontSize = CaptionSize, VerticalAlignment = VerticalAlignment.Center });
            Bind(count, TextBlock.TextProperty, nameof(AppModel.TodayRestCount), v => $"今日已休息 {v} 次");
            DockPanel.SetDock(count, Dock.Right);
            actions.Children.Add(count);
            stack.Children.Add(actions);

            var card = new Border { Child = stack, Padding = new Thickness(12), CornerRadius = new CornerRadius(8) };
            card.SetResourceReference(Border.BackgroundProperty, Keys.StatusCard);
            return card;
        }

        private UIElement CategoryGrid()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "提示类别", Margin = new Thickness(0, 0, 0, 6) });
            var grid = new Grid();
            for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            var rows = (TipCategories.All.Count + 2) / 3;
            for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < TipCategories.All.Count; i++)
            {
                var category = TipCategories.All[i];
                var chip = new CategoryChip(category, () => model.Toggle(category));
                int column = i % 3, row = i / 3;
                chip.Margin = new Thickness(column == 0 ? 0 : 3, row == 0 ? 0 : 6, column == 2 ? 0 : 3, 0);
                Grid.SetColumn(chip, column);
                Grid.SetRow(chip, row);
                grid.Children.Add(chip);
                chips.Add((category, chip));
            }
            stack.Children.Add(grid);
            return stack;
        }

        private UIElement Footer()
        {
            var dock = new DockPanel { LastChildFill = false };
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var text = new TextBlock
            {
                Text = $"v{version?.ToString(3) ?? "dev"}",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, Keys.TertiaryText);
            dock.Children.Add(text);
            var quit = new FlatButton { Command = model.QuitCommand, Text = "退出歇一杯" };
            DockPanel.SetDock(quit, Dock.Right);
            dock.Children.Add(quit);
            return dock;
        }

        private UIElement Section(string title, params UIElement[] rows)
        {
            var stack = new StackPanel();
            var heading = Secondary(new TextBlock { Text = title, FontSize = CaptionSize, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(heading);
            var content = new StackPanel();
            for (var i = 0; i < rows.Length; i++) Add(content, rows[i], i == 0 ? 0 : 10);
            var card = new Border { Child = content, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, Keys.Card);
            card.SetResourceReference(Border.BorderBrushProperty, Keys.CardBorder);
            stack.Children.Add(card);
            return stack;
        }

        private UIElement StepperRow(string title, string field, Func<BreakSettings, string> format)
        {
            var dock = new DockPanel { LastChildFill = false, MinHeight = 24 };
            dock.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            var stepper = new Stepper(model.StepCommand, field) { Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(stepper, Dock.Right);
            dock.Children.Add(stepper);
            var value = Secondary(new TextBlock { VerticalAlignment = VerticalAlignment.Center });
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            Bind(value, TextBlock.TextProperty, nameof(AppModel.Settings), s => format((BreakSettings)s));
            DockPanel.SetDock(value, Dock.Right);
            dock.Children.Add(value);
            return dock;
        }

        private UIElement SwitchRow(string title, string? hintPath, bool attention, Func<AppModel, bool> isOn, Action<bool> set)
        {
            var dock = new DockPanel { LastChildFill = false, MinHeight = 22 };
            dock.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            if (hintPath != null) dock.Children.Add(Hint(hintPath, attention, new Thickness(2, 0, 0, 0)));
            var toggle = new SwitchToggle { Toggled = set, Margin = new Thickness(12, 0, 0, 0), IsChecked = isOn(model) };
            // 开关值来自整份设置或开机启动状态，任一变化都重新取值
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppModel.Settings) || e.PropertyName == nameof(AppModel.LaunchAtLogin))
                {
                    toggle.IsChecked = isOn(model);
                }
            };
            DockPanel.SetDock(toggle, Dock.Right);
            dock.Children.Add(toggle);
            return dock;
        }

        private HintMark Hint(string path, bool attention, Thickness margin)
        {
            var hint = new HintMark { Attention = attention, Margin = margin };
            Bind(hint, HintMark.TextProperty, path, v => v as string ?? "");
            hints.Add(hint);
            return hint;
        }

        private void Bind(DependencyObject target, DependencyProperty property, string path, Func<object, object>? convert = null)
        {
            var binding = new Binding(path) { Source = model, Mode = BindingMode.OneWay };
            if (convert != null) binding.Converter = new FuncConverter(convert);
            BindingOperations.SetBinding(target, property, binding);
        }

        private static TextBlock Secondary(TextBlock text)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, Keys.SecondaryText);
            return text;
        }
    }

    internal sealed class FuncConverter : IValueConverter
    {
        private readonly Func<object, object> convert;

        public FuncConverter(Func<object, object> convert)
        {
            this.convert = convert;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => convert(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
