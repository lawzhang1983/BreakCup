using System;
using System.Globalization;

namespace BreakCup.Core
{
    public static class BreakFormat
    {
        /// <summary>倒计时：<c>04:32</c>，超过一小时为 <c>1:04:32</c></summary>
        public static string Clock(double seconds)
        {
            var total = Math.Max(0, (int)Math.Ceiling(seconds));
            var h = total / 3600;
            var m = total % 3600 / 60;
            var s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        }

        /// <summary>紧凑写法：最后一分钟显示秒，其余向上取整到分钟</summary>
        public static string Compact(double seconds)
        {
            var total = Math.Max(0, (int)Math.Ceiling(seconds));
            if (total < 60) return $"{total}秒";
            return $"{(int)Math.Ceiling(total / 60.0)}分";
        }

        /// <summary>托盘图标里只放得下数字：最后一分钟是秒数，其余是向上取整的分钟数</summary>
        public static (string Number, bool IsSeconds) TrayNumber(double seconds)
        {
            var total = Math.Max(0, (int)Math.Ceiling(seconds));
            if (total < 60) return (total.ToString(CultureInfo.InvariantCulture), true);
            return (((int)Math.Ceiling(total / 60.0)).ToString(CultureInfo.InvariantCulture), false);
        }

        /// <summary>设置界面用的时长说明，例如「45 分钟」「1.5 分钟」</summary>
        public static string Minutes(double value) =>
            Math.Round(value) == value
                ? $"{(int)value} 分钟"
                : value.ToString("0.0", CultureInfo.InvariantCulture) + " 分钟";
    }
}
