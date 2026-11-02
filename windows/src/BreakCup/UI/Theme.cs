using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace BreakCup.UI
{
    /// <summary>主题资源键。控件用 SetResourceReference 引用，切换深浅色时自动刷新</summary>
    internal static class Keys
    {
        public const string Panel = "Brush.Panel";
        public const string PanelBorder = "Brush.PanelBorder";
        public const string Card = "Brush.Card";
        public const string CardBorder = "Brush.CardBorder";
        public const string StatusCard = "Brush.StatusCard";
        public const string Text = "Brush.Text";
        public const string SecondaryText = "Brush.SecondaryText";
        public const string TertiaryText = "Brush.TertiaryText";
        public const string Coffee = "Brush.Coffee";
        public const string CoffeeSoft = "Brush.CoffeeSoft";
        public const string CoffeeBorder = "Brush.CoffeeBorder";
        public const string OnCoffee = "Brush.OnCoffee";
        public const string Control = "Brush.Control";
        public const string ControlHover = "Brush.ControlHover";
        public const string ControlPressed = "Brush.ControlPressed";
        public const string ControlBorder = "Brush.ControlBorder";
        public const string Neutral = "Brush.Neutral";
        public const string Attention = "Brush.Attention";
        public const string Track = "Brush.Track";
        public const string Green = "Brush.Green";
        public const string GreenSoft = "Brush.GreenSoft";
        public const string Orange = "Brush.Orange";
        public const string OrangeSoft = "Brush.OrangeSoft";
        public const string Blue = "Brush.Blue";
        public const string BlueSoft = "Brush.BlueSoft";
    }

    /// <summary>深浅色跟随系统「选择应用模式」；咖啡色主色与 Mac 版一致，深色模式下换成拿铁色</summary>
    internal static class Theme
    {
        private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>休息屏和预告条（固定深色底）上的强调色，同 Mac 版 latte</summary>
        public static readonly Color Latte = Color.FromRgb(0xDE, 0xB3, 0x85);

        public static readonly FontFamily UiFont = new FontFamily("Segoe UI, Microsoft YaHei UI");
        public static readonly FontFamily DigitFont = new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI");

        public static bool IsDark { get; private set; }

        /// <summary>应用是否用深色模式</summary>
        public static bool AppsUseDark => ReadPersonalize("AppsUseLightTheme") == 0;

        /// <summary>任务栏是否深色：决定托盘图标画白色还是黑色</summary>
        public static bool TaskbarIsDark => ReadPersonalize("SystemUsesLightTheme") == 0;

        public static void Apply(ResourceDictionary resources, bool dark)
        {
            IsDark = dark;
            var coffee = dark ? Color.FromRgb(0xD6, 0xA6, 0x78) : Color.FromRgb(0x8C, 0x59, 0x38);
            Set(resources, Keys.Panel, dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
            Set(resources, Keys.PanelBorder, dark ? Color.FromArgb(0x55, 0x5A, 0x5A, 0x5A) : Color.FromArgb(0x33, 0x00, 0x00, 0x00));
            Set(resources, Keys.Card, dark ? Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
            Set(resources, Keys.CardBorder, dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00));
            Set(resources, Keys.StatusCard, WithAlpha(coffee, 0x1A));
            Set(resources, Keys.Text, dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
            Set(resources, Keys.SecondaryText, dark ? Color.FromRgb(0xC5, 0xC5, 0xC5) : Color.FromRgb(0x5F, 0x5F, 0x5F));
            Set(resources, Keys.TertiaryText, dark ? Color.FromRgb(0x8A, 0x8A, 0x8A) : Color.FromRgb(0x8E, 0x8E, 0x8E));
            Set(resources, Keys.Coffee, coffee);
            Set(resources, Keys.CoffeeSoft, WithAlpha(coffee, dark ? (byte)0x29 : (byte)0x1F));
            Set(resources, Keys.CoffeeBorder, WithAlpha(coffee, 0x99));
            Set(resources, Keys.OnCoffee, dark ? Color.FromRgb(0x1C, 0x13, 0x0D) : Colors.White);
            Set(resources, Keys.Control, dark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
            Set(resources, Keys.ControlHover, dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0xF9, 0xF9, 0xF9));
            Set(resources, Keys.ControlPressed, dark ? Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x4D, 0xF9, 0xF9, 0xF9));
            Set(resources, Keys.ControlBorder, dark ? Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x29, 0x00, 0x00, 0x00));
            Set(resources, Keys.Neutral, dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
            Set(resources, Keys.Attention, dark ? Color.FromRgb(0xFF, 0xA9, 0x4D) : Color.FromRgb(0xC8, 0x6A, 0x00));
            Set(resources, Keys.Track, dark ? Color.FromRgb(0x9A, 0x9A, 0x9A) : Color.FromRgb(0x73, 0x73, 0x73));

            // 阶段标签的颜色，对应 Mac 版的系统绿、橙、蓝
            var green = dark ? Color.FromRgb(0x6C, 0xCB, 0x5F) : Color.FromRgb(0x10, 0x7C, 0x10);
            var orange = dark ? Color.FromRgb(0xFF, 0x9F, 0x0A) : Color.FromRgb(0xE8, 0x80, 0x0C);
            var blue = dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
            Set(resources, Keys.Green, green);
            Set(resources, Keys.GreenSoft, WithAlpha(green, 0x26));
            Set(resources, Keys.Orange, orange);
            Set(resources, Keys.OrangeSoft, WithAlpha(orange, 0x26));
            Set(resources, Keys.Blue, blue);
            Set(resources, Keys.BlueSoft, WithAlpha(blue, 0x26));

            // 悬停提示跟随主题；系统默认的提示框在深色面板旁边很突兀
            var tooltip = new Style(typeof(ToolTip));
            tooltip.Setters.Add(new Setter(Control.BackgroundProperty, Brush(dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xF9, 0xF9, 0xF9))));
            tooltip.Setters.Add(new Setter(Control.ForegroundProperty, Brush(dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A))));
            tooltip.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(dark ? Color.FromRgb(0x45, 0x45, 0x45) : Color.FromRgb(0xD0, 0xD0, 0xD0))));
            tooltip.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 6)));
            tooltip.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
            tooltip.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            resources[typeof(ToolTip)] = tooltip;
        }

        public static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static SolidColorBrush Brush(byte a, Color color) => Brush(WithAlpha(color, a));

        public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

        private static void Set(ResourceDictionary resources, string key, Color color) => resources[key] = Brush(color);

        private static int ReadPersonalize(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizePath);
            return key?.GetValue(name) is int value ? value : 1;
        }
    }
}
