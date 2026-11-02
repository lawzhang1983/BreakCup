using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;

namespace BreakCup.Core
{
    /// <summary>休息屏提示的类别；<see cref="TipCategories.All"/> 的顺序就是休息屏轮播的类别顺序。</summary>
    public enum TipCategory
    {
        Exercise,
        Eyes,
        Hydration,
        Sedentary,
        Breathing
    }

    public static class TipCategories
    {
        public static readonly IReadOnlyList<TipCategory> All = new[]
        {
            TipCategory.Exercise, TipCategory.Eyes, TipCategory.Hydration, TipCategory.Sedentary, TipCategory.Breathing
        };

        public static string Title(this TipCategory category) => category switch
        {
            TipCategory.Exercise => "简单运动",
            TipCategory.Eyes => "远眺护眼",
            TipCategory.Hydration => "喝水",
            TipCategory.Sedentary => "久坐危害",
            _ => "呼吸放松"
        };

        /// <summary>存储和提示 ID 用的英文键，与 Mac 版一致</summary>
        public static string Key(this TipCategory category) => category switch
        {
            TipCategory.Exercise => "exercise",
            TipCategory.Eyes => "eyes",
            TipCategory.Hydration => "hydration",
            TipCategory.Sedentary => "sedentary",
            _ => "breathing"
        };

        public static TipCategory? FromKey(string key)
        {
            foreach (var category in All)
            {
                if (category.Key() == key) return category;
            }
            return null;
        }
    }

    public sealed class BreakSettings : IEquatable<BreakSettings>
    {
        /// <summary>连续可使用时间（分钟）</summary>
        public double WorkMinutes { get; set; }
        /// <summary>强制休息时间（分钟）</summary>
        public double RestMinutes { get; set; }
        /// <summary>到点前预告（秒），0 表示不预告</summary>
        public double WarningSeconds { get; set; }
        /// <summary>离开电脑足够久时视为已经休息过</summary>
        public bool ResetWhenAway { get; set; }
        /// <summary>麦克风或摄像头使用中（开会）时推迟休息</summary>
        public bool DeferWhenMediaInUse { get; set; }
        /// <summary>休息屏提示轮换间隔（秒）</summary>
        public double TipIntervalSeconds { get; set; }
        /// <summary>休息屏展示哪些类别的提示</summary>
        public List<TipCategory> EnabledCategories { get; set; } = new List<TipCategory>();
        /// <summary>托盘图标显示剩余时间</summary>
        public bool ShowCountdownInTray { get; set; }
        /// <summary>休息结束后启动系统屏幕保护</summary>
        public bool ScreenSaverAfterRest { get; set; }

        public static BreakSettings Default => new BreakSettings
        {
            WorkMinutes = 45,
            RestMinutes = 5,
            WarningSeconds = 60,
            ResetWhenAway = true,
            DeferWhenMediaInUse = true,
            TipIntervalSeconds = 20,
            EnabledCategories = TipCategories.All.ToList(),
            ShowCountdownInTray = true,
            ScreenSaverAfterRest = true
        };

        /// <summary>设置界面上步进器的取值范围</summary>
        public static readonly (double Min, double Max) WorkRange = (5, 180);
        public static readonly (double Min, double Max) RestRange = (1, 30);
        public static readonly (double Min, double Max) WarningRange = (0, 180);
        public static readonly (double Min, double Max) TipIntervalRange = (10, 120);

        /// <summary>
        /// 只因键鼠空闲判定「已离开」时的最短空闲时长：
        /// 看文档、想问题时几分钟不碰键鼠很常见，休息时长设得很短也不能据此清零计时。
        /// </summary>
        public const double MinimumAwaySeconds = 5 * 60;

        public double WorkSeconds => WorkMinutes * 60;
        public double RestSeconds => RestMinutes * 60;
        public double AwaySeconds => Math.Max(RestSeconds, MinimumAwaySeconds);

        public BreakSettings Clone()
        {
            var copy = (BreakSettings)MemberwiseClone();
            copy.EnabledCategories = EnabledCategories.ToList();
            return copy;
        }

        /// <summary>
        /// 兜底校正。下限比界面范围宽，方便直接改注册表设小数分钟做实测；
        /// 休息上限 60 分钟，保证强制休息一定有尽头。
        /// </summary>
        public BreakSettings Sanitized()
        {
            var s = Clone();
            s.WorkMinutes = Clamp(WorkMinutes, 0.5, 240, Default.WorkMinutes);
            s.RestMinutes = Clamp(RestMinutes, 0.25, 60, Default.RestMinutes);
            s.WarningSeconds = Math.Min(Clamp(WarningSeconds, 0, 300, 60), s.WorkSeconds / 2);
            s.TipIntervalSeconds = Clamp(TipIntervalSeconds, 5, 300, 20);
            s.EnabledCategories = TipCategories.All.Where(EnabledCategories.Contains).ToList();
            if (s.EnabledCategories.Count == 0)
            {
                s.EnabledCategories = TipCategories.All.ToList();
            }
            return s;
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return Math.Min(Math.Max(value, min), max);
        }

        public bool Equals(BreakSettings? other) =>
            other != null
            && WorkMinutes == other.WorkMinutes
            && RestMinutes == other.RestMinutes
            && WarningSeconds == other.WarningSeconds
            && ResetWhenAway == other.ResetWhenAway
            && DeferWhenMediaInUse == other.DeferWhenMediaInUse
            && TipIntervalSeconds == other.TipIntervalSeconds
            && EnabledCategories.SequenceEqual(other.EnabledCategories)
            && ShowCountdownInTray == other.ShowCountdownInTray
            && ScreenSaverAfterRest == other.ScreenSaverAfterRest;

        public override bool Equals(object? obj) => Equals(obj as BreakSettings);

        public override int GetHashCode() => WorkMinutes.GetHashCode() ^ RestMinutes.GetHashCode() ^ EnabledCategories.Count;
    }

    /// <summary>
    /// 设置按字段存进注册表 HKCU\Software\BreakCup，键名与 Mac 版的 defaults 一致，
    /// 可以用 <c>reg query HKCU\Software\BreakCup</c> 直接查看和调整。数值按不变区域写成字符串，便于填小数。
    /// </summary>
    public sealed class SettingsStore
    {
        public const string DefaultKeyPath = @"Software\BreakCup";
        private readonly string keyPath;

        private static class Key
        {
            public const string WorkMinutes = "workMinutes";
            public const string RestMinutes = "restMinutes";
            public const string WarningSeconds = "warningSeconds";
            public const string ResetWhenAway = "resetWhenAway";
            public const string DeferWhenMediaInUse = "deferWhenMediaInUse";
            public const string TipIntervalSeconds = "tipIntervalSeconds";
            public const string EnabledCategories = "enabledCategories";
            public const string ShowCountdownInTray = "showCountdownInTray";
            public const string ScreenSaverAfterRest = "screenSaverAfterRest";
        }

        public SettingsStore(string keyPath = DefaultKeyPath)
        {
            this.keyPath = keyPath;
        }

        public BreakSettings Load()
        {
            var b = BreakSettings.Default;
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            if (key == null) return b.Sanitized();
            var categories = (key.GetValue(Key.EnabledCategories) as string[])?
                .Select(TipCategories.FromKey)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .ToList();
            return new BreakSettings
            {
                WorkMinutes = ReadDouble(key, Key.WorkMinutes) ?? b.WorkMinutes,
                RestMinutes = ReadDouble(key, Key.RestMinutes) ?? b.RestMinutes,
                WarningSeconds = ReadDouble(key, Key.WarningSeconds) ?? b.WarningSeconds,
                ResetWhenAway = ReadBool(key, Key.ResetWhenAway) ?? b.ResetWhenAway,
                DeferWhenMediaInUse = ReadBool(key, Key.DeferWhenMediaInUse) ?? b.DeferWhenMediaInUse,
                TipIntervalSeconds = ReadDouble(key, Key.TipIntervalSeconds) ?? b.TipIntervalSeconds,
                EnabledCategories = categories ?? b.EnabledCategories,
                ShowCountdownInTray = ReadBool(key, Key.ShowCountdownInTray) ?? b.ShowCountdownInTray,
                ScreenSaverAfterRest = ReadBool(key, Key.ScreenSaverAfterRest) ?? b.ScreenSaverAfterRest
            }.Sanitized();
        }

        public void Save(BreakSettings settings)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            WriteDouble(key, Key.WorkMinutes, settings.WorkMinutes);
            WriteDouble(key, Key.RestMinutes, settings.RestMinutes);
            WriteDouble(key, Key.WarningSeconds, settings.WarningSeconds);
            key.SetValue(Key.ResetWhenAway, settings.ResetWhenAway ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(Key.DeferWhenMediaInUse, settings.DeferWhenMediaInUse ? 1 : 0, RegistryValueKind.DWord);
            WriteDouble(key, Key.TipIntervalSeconds, settings.TipIntervalSeconds);
            key.SetValue(Key.EnabledCategories, settings.EnabledCategories.Select(c => c.Key()).ToArray(), RegistryValueKind.MultiString);
            key.SetValue(Key.ShowCountdownInTray, settings.ShowCountdownInTray ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(Key.ScreenSaverAfterRest, settings.ScreenSaverAfterRest ? 1 : 0, RegistryValueKind.DWord);
        }

        private static void WriteDouble(RegistryKey key, string name, double value) =>
            key.SetValue(name, value.ToString("R", CultureInfo.InvariantCulture), RegistryValueKind.String);

        private static double? ReadDouble(RegistryKey key, string name)
        {
            var value = key.GetValue(name);
            return value switch
            {
                int i => i,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
                _ => null
            };
        }

        private static bool? ReadBool(RegistryKey key, string name) => key.GetValue(name) switch
        {
            int i => i != 0,
            string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => null
        };
    }
}
