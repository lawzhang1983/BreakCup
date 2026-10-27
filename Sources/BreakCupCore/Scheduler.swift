import Foundation

/// 每次 tick 时从系统采集到的环境快照
public struct ActivityEnvironment: Equatable, Sendable {
    /// 距离最后一次键盘或鼠标输入的秒数
    public var idleSeconds: TimeInterval
    /// 麦克风或摄像头正在被使用
    public var mediaInUse: Bool
    /// 屏幕已锁定（或会话已切走）
    public var screenLocked: Bool

    public init(idleSeconds: TimeInterval = 0, mediaInUse: Bool = false, screenLocked: Bool = false) {
        self.idleSeconds = idleSeconds
        self.mediaInUse = mediaInUse
        self.screenLocked = screenLocked
    }
}

public enum BreakPhase: Equatable, Sendable {
    /// 从 `since` 起连续使用中
    case working(since: Date)
    /// 预告中，`restAt` 时进入休息
    case warning(since: Date, restAt: Date)
    /// 到点时麦克风或摄像头在用，等它们空闲后再预告
    case deferred(since: Date, deferredAt: Date)
    /// 强制休息中，`until` 前不能结束
    case resting(startedAt: Date, until: Date)
}

/// 工作计时被清零的原因，供界面提示
public enum WorkResetReason: Equatable, Sendable {
    case launched
    case restFinished
    case away
    case screenLocked
    case systemSlept
    case manual
}

/// 久坐休息的状态机。纯逻辑、时间由调用方注入，便于单测。
public struct BreakScheduler: Sendable {
    public private(set) var phase: BreakPhase
    public private(set) var lastResetReason: WorkResetReason
    public var settings: BreakSettings

    private var lastTick: Date
    private var lockedSince: Date?
    private var mediaFreeSince: Date?

    /// 麦克风和摄像头需连续空闲这么久才算会议结束，避免通话中短暂断流就弹预告
    public static let mediaFreeGrace: TimeInterval = 5

    public init(settings: BreakSettings, now: Date) {
        self.settings = settings
        self.phase = .working(since: now)
        self.lastResetReason = .launched
        self.lastTick = now
    }

    public var isResting: Bool {
        if case .resting = phase { return true }
        return false
    }

    public mutating func tick(now: Date, environment env: ActivityEnvironment) {
        let gap = now.timeIntervalSince(lastTick)
        lastTick = now

        if case let .resting(_, until) = phase {
            if now >= until {
                resetWork(now, reason: .restFinished)
            }
            return
        }

        // 两次 tick 之间隔了一个休息时长以上，说明系统睡眠过：视为已休息
        if gap >= settings.restSeconds {
            resetWork(now, reason: .systemSlept)
            return
        }

        if env.screenLocked {
            if lockedSince == nil { lockedSince = now }
            // 锁屏期间不预告、不进休息，退回计时状态并保留起点
            switch phase {
            case let .warning(since, _), let .deferred(since, _):
                phase = .working(since: since)
            default:
                break
            }
            return
        }
        if let locked = lockedSince {
            lockedSince = nil
            if now.timeIntervalSince(locked) >= settings.restSeconds {
                resetWork(now, reason: .screenLocked)
                return
            }
        }

        // 长时间没碰键鼠视为已离开；开会时人还坐着，不算离开
        if settings.resetWhenAway && !env.mediaInUse && env.idleSeconds >= settings.awaySeconds {
            resetWork(now, reason: .away)
            return
        }

        let holdForMedia = settings.deferWhenMediaInUse && env.mediaInUse

        switch phase {
        case let .working(since):
            if now.timeIntervalSince(since) >= settings.workSeconds - settings.warningSeconds {
                if holdForMedia {
                    deferRest(since: since, now: now)
                } else {
                    beginWarningOrRest(now: now, since: since)
                }
            }
        case let .warning(since, restAt):
            if holdForMedia {
                deferRest(since: since, now: now)
            } else if now >= restAt {
                beginRest(now)
            }
        case let .deferred(since, _):
            if holdForMedia {
                mediaFreeSince = nil
            } else {
                let freeSince = mediaFreeSince ?? now
                mediaFreeSince = freeSince
                if now.timeIntervalSince(freeSince) >= Self.mediaFreeGrace {
                    beginWarningOrRest(now: now, since: since)
                }
            }
        case .resting:
            break
        }
    }

    /// 手动「立即休息」：不受会议推迟约束
    public mutating func startRestNow(now: Date) {
        guard !isResting else { return }
        lastTick = now
        beginRest(now)
    }

    /// 手动「重新计时」
    public mutating func restartWork(now: Date) {
        guard !isResting else { return }
        lastTick = now
        resetWork(now, reason: .manual)
    }

    /// 距离进入休息还有多少秒；会议推迟和休息中返回 nil
    public func secondsUntilRest(now: Date) -> TimeInterval? {
        switch phase {
        case let .working(since):
            return max(0, since.addingTimeInterval(settings.workSeconds).timeIntervalSince(now))
        case let .warning(_, restAt):
            return max(0, restAt.timeIntervalSince(now))
        case .deferred, .resting:
            return nil
        }
    }

    /// 已连续使用的秒数；休息中返回 nil
    public func secondsWorked(now: Date) -> TimeInterval? {
        switch phase {
        case let .working(since), let .warning(since, _), let .deferred(since, _):
            return max(0, now.timeIntervalSince(since))
        case .resting:
            return nil
        }
    }

    public func restRemaining(now: Date) -> TimeInterval? {
        guard case let .resting(_, until) = phase else { return nil }
        return max(0, until.timeIntervalSince(now))
    }

    private mutating func resetWork(_ now: Date, reason: WorkResetReason) {
        phase = .working(since: now)
        lastResetReason = reason
        mediaFreeSince = nil
    }

    private mutating func deferRest(since: Date, now: Date) {
        if case .deferred = phase { return }
        phase = .deferred(since: since, deferredAt: now)
        mediaFreeSince = nil
    }

    private mutating func beginWarningOrRest(now: Date, since: Date) {
        mediaFreeSince = nil
        if settings.warningSeconds <= 0 {
            beginRest(now)
        } else {
            // 正常路径下 now ≈ since + 工作时长 - 预告时长；
            // 会议推迟或把工作时长调短后，也保证给满一个预告时长
            phase = .warning(since: since, restAt: now.addingTimeInterval(settings.warningSeconds))
        }
    }

    private mutating func beginRest(_ now: Date) {
        phase = .resting(startedAt: now, until: now.addingTimeInterval(settings.restSeconds))
        mediaFreeSince = nil
    }
}
