import Foundation

/// 系统设置「锁定屏幕」里，屏保开始或显示器关闭后多久要求密码。
/// 读自 `sysadminctl -screenLock status`，结果打在标准错误里，形如
/// `screenLock delay is immediate`、`screenLock delay is 900 seconds`、`screenLock is off`。
public enum ScreenLockDelay: Equatable, Sendable {
    case immediate
    case after(TimeInterval)
    case off
    case unknown

    public init(sysadminctlOutput output: String) {
        if output.contains("screenLock is off") {
            self = .off
            return
        }
        let words = output.split(whereSeparator: \.isWhitespace)
        guard let index = words.firstIndex(of: "delay"), index + 2 < words.count, words[index + 1] == "is" else {
            self = .unknown
            return
        }
        let value = words[index + 2]
        if value == "immediate" {
            self = .immediate
        } else if let seconds = Double(value) {
            self = seconds <= 0 ? .immediate : .after(seconds)
        } else {
            self = .unknown
        }
    }

    /// 面板悬停说明和日志里的一句话
    public var summary: String {
        switch self {
        case .immediate:
            return "屏保一开始就要求密码"
        case let .after(seconds):
            return "屏保开始 \(Self.duration(seconds))后才要求密码，在那之前动一下键鼠就能回到桌面"
        case .off:
            return "唤醒不要求密码"
        case .unknown:
            return "没读到密码设置"
        }
    }

    private static func duration(_ seconds: TimeInterval) -> String {
        if seconds < 60 { return "\(Int(seconds)) 秒" }
        if seconds >= 3600, seconds.truncatingRemainder(dividingBy: 3600) == 0 { return "\(Int(seconds / 3600)) 小时" }
        return "\(Int((seconds / 60).rounded())) 分钟"
    }
}
