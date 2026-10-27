import Foundation

public enum BreakFormat {
    /// 倒计时：`04:32`，超过一小时为 `1:04:32`
    public static func clock(_ seconds: TimeInterval) -> String {
        let total = max(0, Int(seconds.rounded(.up)))
        let h = total / 3600
        let m = (total % 3600) / 60
        let s = total % 60
        return h > 0 ? String(format: "%d:%02d:%02d", h, m, s) : String(format: "%02d:%02d", m, s)
    }

    /// 菜单栏紧凑写法：最后一分钟显示秒，其余向上取整到分钟
    public static func menuBar(_ seconds: TimeInterval) -> String {
        let total = max(0, Int(seconds.rounded(.up)))
        if total < 60 { return "\(total)秒" }
        return "\(Int((Double(total) / 60).rounded(.up)))分"
    }

    /// 设置界面用的时长说明，例如「45 分钟」「30 秒」「1.5 分钟」
    public static func minutes(_ value: Double) -> String {
        value.rounded() == value ? "\(Int(value)) 分钟" : String(format: "%.1f 分钟", value)
    }
}
