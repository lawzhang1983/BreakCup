import Foundation

/// 休息屏提示的类别；`allCases` 的顺序就是休息屏轮播的类别顺序。
public enum TipCategory: String, CaseIterable, Codable, Sendable {
    case exercise
    case eyes
    case hydration
    case sedentary
    case breathing

    public var title: String {
        switch self {
        case .exercise: return "简单运动"
        case .eyes: return "远眺护眼"
        case .hydration: return "喝水"
        case .sedentary: return "久坐危害"
        case .breathing: return "呼吸放松"
        }
    }

    /// SF Symbols 名称
    public var symbol: String {
        switch self {
        case .exercise: return "figure.walk"
        case .eyes: return "eye"
        case .hydration: return "drop"
        case .sedentary: return "heart.text.square"
        case .breathing: return "wind"
        }
    }
}

public struct BreakSettings: Equatable, Sendable {
    /// 连续可使用时间（分钟）
    public var workMinutes: Double
    /// 强制休息时间（分钟）
    public var restMinutes: Double
    /// 到点前预告（秒），0 表示不预告
    public var warningSeconds: Double
    /// 离开电脑足够久时视为已经休息过
    public var resetWhenAway: Bool
    /// 麦克风或摄像头使用中（开会）时推迟休息
    public var deferWhenMediaInUse: Bool
    /// 休息屏提示轮换间隔（秒）
    public var tipIntervalSeconds: Double
    /// 休息屏展示哪些类别的提示
    public var enabledCategories: [TipCategory]
    /// 菜单栏图标旁显示剩余时间
    public var showCountdownInMenuBar: Bool

    public init(
        workMinutes: Double,
        restMinutes: Double,
        warningSeconds: Double,
        resetWhenAway: Bool,
        deferWhenMediaInUse: Bool,
        tipIntervalSeconds: Double,
        enabledCategories: [TipCategory],
        showCountdownInMenuBar: Bool
    ) {
        self.workMinutes = workMinutes
        self.restMinutes = restMinutes
        self.warningSeconds = warningSeconds
        self.resetWhenAway = resetWhenAway
        self.deferWhenMediaInUse = deferWhenMediaInUse
        self.tipIntervalSeconds = tipIntervalSeconds
        self.enabledCategories = enabledCategories
        self.showCountdownInMenuBar = showCountdownInMenuBar
    }

    public static let `default` = BreakSettings(
        workMinutes: 45,
        restMinutes: 5,
        warningSeconds: 60,
        resetWhenAway: true,
        deferWhenMediaInUse: true,
        tipIntervalSeconds: 20,
        enabledCategories: TipCategory.allCases,
        showCountdownInMenuBar: true
    )

    /// 设置界面上步进器的取值范围
    public static let workRange: ClosedRange<Double> = 5...180
    public static let restRange: ClosedRange<Double> = 1...30
    public static let warningRange: ClosedRange<Double> = 0...180
    public static let tipIntervalRange: ClosedRange<Double> = 10...120

    /// 只因键鼠空闲判定「已离开」时的最短空闲时长：
    /// 看文档、想问题时几分钟不碰键鼠很常见，休息时长设得很短也不能据此清零计时。
    public static let minimumAwaySeconds: TimeInterval = 5 * 60

    public var workSeconds: TimeInterval { workMinutes * 60 }
    public var restSeconds: TimeInterval { restMinutes * 60 }
    public var awaySeconds: TimeInterval { max(restSeconds, Self.minimumAwaySeconds) }

    /// 兜底校正。下限比界面范围宽，方便用 `defaults write` 设小数分钟做实测；
    /// 休息上限 60 分钟，保证强制休息一定有尽头。
    public func sanitized() -> BreakSettings {
        var s = self
        s.workMinutes = Self.clamp(workMinutes, 0.5...240, fallback: Self.default.workMinutes)
        s.restMinutes = Self.clamp(restMinutes, 0.25...60, fallback: Self.default.restMinutes)
        s.warningSeconds = min(Self.clamp(warningSeconds, 0...300, fallback: 60), s.workSeconds / 2)
        s.tipIntervalSeconds = Self.clamp(tipIntervalSeconds, 5...300, fallback: 20)
        var seen = Set<TipCategory>()
        s.enabledCategories = TipCategory.allCases.filter { enabledCategories.contains($0) && seen.insert($0).inserted }
        if s.enabledCategories.isEmpty {
            s.enabledCategories = TipCategory.allCases
        }
        return s
    }

    private static func clamp(_ value: Double, _ range: ClosedRange<Double>, fallback: Double) -> Double {
        guard value.isFinite else { return fallback }
        return min(max(value, range.lowerBound), range.upperBound)
    }
}

/// 设置按字段存进 UserDefaults，便于 `defaults read/write com.lawzhang.breakcup` 直接查看和调整。
public final class SettingsStore {
    private let defaults: UserDefaults

    private enum Key {
        static let workMinutes = "workMinutes"
        static let restMinutes = "restMinutes"
        static let warningSeconds = "warningSeconds"
        static let resetWhenAway = "resetWhenAway"
        static let deferWhenMediaInUse = "deferWhenMediaInUse"
        static let tipIntervalSeconds = "tipIntervalSeconds"
        static let enabledCategories = "enabledCategories"
        static let showCountdownInMenuBar = "showCountdownInMenuBar"
    }

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    public func load() -> BreakSettings {
        let base = BreakSettings.default
        let categories = (defaults.array(forKey: Key.enabledCategories) as? [String])?
            .compactMap(TipCategory.init(rawValue:)) ?? base.enabledCategories
        return BreakSettings(
            workMinutes: double(Key.workMinutes) ?? base.workMinutes,
            restMinutes: double(Key.restMinutes) ?? base.restMinutes,
            warningSeconds: double(Key.warningSeconds) ?? base.warningSeconds,
            resetWhenAway: bool(Key.resetWhenAway) ?? base.resetWhenAway,
            deferWhenMediaInUse: bool(Key.deferWhenMediaInUse) ?? base.deferWhenMediaInUse,
            tipIntervalSeconds: double(Key.tipIntervalSeconds) ?? base.tipIntervalSeconds,
            enabledCategories: categories,
            showCountdownInMenuBar: bool(Key.showCountdownInMenuBar) ?? base.showCountdownInMenuBar
        ).sanitized()
    }

    public func save(_ settings: BreakSettings) {
        defaults.set(settings.workMinutes, forKey: Key.workMinutes)
        defaults.set(settings.restMinutes, forKey: Key.restMinutes)
        defaults.set(settings.warningSeconds, forKey: Key.warningSeconds)
        defaults.set(settings.resetWhenAway, forKey: Key.resetWhenAway)
        defaults.set(settings.deferWhenMediaInUse, forKey: Key.deferWhenMediaInUse)
        defaults.set(settings.tipIntervalSeconds, forKey: Key.tipIntervalSeconds)
        defaults.set(settings.enabledCategories.map(\.rawValue), forKey: Key.enabledCategories)
        defaults.set(settings.showCountdownInMenuBar, forKey: Key.showCountdownInMenuBar)
    }

    private func double(_ key: String) -> Double? {
        defaults.object(forKey: key) == nil ? nil : defaults.double(forKey: key)
    }

    private func bool(_ key: String) -> Bool? {
        defaults.object(forKey: key) == nil ? nil : defaults.bool(forKey: key)
    }
}
