import AppKit
import BreakCupCore
import SwiftUI

enum PhaseKind {
    case working, warning, deferred, resting

    var title: String {
        switch self {
        case .working: return "计时中"
        case .warning: return "即将休息"
        case .deferred: return "会议中"
        case .resting: return "休息中"
        }
    }
}

/// 设置面板的视图模型
@MainActor
final class AppModel: ObservableObject {
    @Published var settings: BreakSettings {
        didSet {
            if settings != oldValue { onSettingsChange?(settings) }
        }
    }

    @Published private(set) var phase: PhaseKind = .working
    @Published private(set) var countdown = "--:--"
    @Published private(set) var countdownCaption = "距离下次休息"
    @Published private(set) var progress: Double = 0
    @Published private(set) var detail = ""
    /// 「开会时推迟休息」的悬停说明，带上当前麦克风和摄像头状态
    @Published private(set) var meetingHint = ""
    @Published var todayRestCount = 0
    @Published private(set) var launchAtLogin = LoginItem.isEnabled
    @Published private(set) var loginItemNote: String? = LoginItem.note
    /// 「立即休息」需要点两次：开始后无法中途退出，防止误触
    @Published private(set) var confirmingRest = false

    private(set) var menuBarTitle = ""
    private(set) var menuBarSymbol = "cup.and.saucer"

    var onSettingsChange: ((BreakSettings) -> Void)?
    var onRestNow: (() -> Void)?
    var onRestartWork: (() -> Void)?

    init(settings: BreakSettings) {
        self.settings = settings
    }

    func refresh(scheduler: BreakScheduler, media: MediaState, now: Date) {
        let s = scheduler.settings
        var title = ""
        switch scheduler.phase {
        case .working:
            let left = scheduler.secondsUntilRest(now: now) ?? 0
            let worked = scheduler.secondsWorked(now: now) ?? 0
            phase = .working
            countdownCaption = "距离下次休息"
            countdown = BreakFormat.clock(left)
            progress = min(1, worked / max(1, s.workSeconds))
            detail = workingDetail(scheduler.lastResetReason, worked: worked)
            menuBarSymbol = "cup.and.saucer"
            title = BreakFormat.menuBar(left)
        case let .warning(_, restAt):
            let left = max(0, restAt.timeIntervalSince(now))
            phase = .warning
            countdownCaption = "休息即将开始"
            countdown = BreakFormat.clock(left)
            progress = 1
            detail = "先保存手头的工作，准备起身走一走"
            menuBarSymbol = "cup.and.heat.waves"
            title = BreakFormat.menuBar(left)
        case let .deferred(_, deferredAt):
            phase = .deferred
            countdownCaption = "会议中，已推迟休息"
            countdown = BreakFormat.clock(now.timeIntervalSince(deferredAt))
            progress = 1
            detail = s.warningSeconds > 0
                ? "麦克风和摄像头都停用后，先预告 \(Int(s.warningSeconds)) 秒再休息"
                : "麦克风和摄像头都停用后立即休息"
            menuBarSymbol = "cup.and.saucer"
            title = "会议中"
        case let .resting(startedAt, until):
            let left = max(0, until.timeIntervalSince(now))
            phase = .resting
            countdownCaption = "休息剩余"
            countdown = BreakFormat.clock(left)
            progress = min(1, now.timeIntervalSince(startedAt) / max(1, until.timeIntervalSince(startedAt)))
            detail = "休息结束后自动解锁"
            menuBarSymbol = "cup.and.heat.waves.fill"
            title = "休息中"
        }
        menuBarTitle = s.showCountdownInMenuBar ? title : ""
        meetingHint = "麦克风或摄像头在用时，到点先不强制休息\n麦克风：\(media.microphoneStatus)\n摄像头：\(media.cameraStatus)"
    }

    func requestRestNow() {
        guard confirmingRest else {
            confirmingRest = true
            DispatchQueue.main.asyncAfter(deadline: .now() + 3) { [weak self] in
                self?.confirmingRest = false
            }
            return
        }
        confirmingRest = false
        onRestNow?()
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            try LoginItem.set(enabled)
            loginItemNote = LoginItem.note
        } catch {
            loginItemNote = "设置失败：\(error.localizedDescription)"
        }
        launchAtLogin = LoginItem.isEnabled
    }

    func toggle(_ category: TipCategory) {
        var list = settings.enabledCategories
        if let index = list.firstIndex(of: category) {
            guard list.count > 1 else { return }
            list.remove(at: index)
        } else {
            list.append(category)
        }
        settings.enabledCategories = TipCategory.allCases.filter(list.contains)
    }

    private func workingDetail(_ reason: WorkResetReason, worked: TimeInterval) -> String {
        if worked < 120 {
            switch reason {
            case .away: return "检测到你离开过电脑，已重新计时"
            case .screenLocked: return "锁屏时间够长，算作休息过了"
            case .systemSlept: return "电脑睡眠过，算作休息过了"
            case .restFinished: return "刚休息完，继续加油"
            case .manual: return "已重新开始计时"
            case .launched: break
            }
        }
        return "已连续使用 \(Int(worked / 60)) 分钟"
    }
}

extension Color {
    /// 咖啡色主色，深色模式下换成拿铁色
    static let coffee = Color(nsColor: NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            ? NSColor(srgbRed: 0.84, green: 0.65, blue: 0.47, alpha: 1)
            : NSColor(srgbRed: 0.55, green: 0.35, blue: 0.22, alpha: 1)
    })

    /// 休息屏和预告条（固定深色底）上的强调色
    static let latte = Color(red: 0.87, green: 0.70, blue: 0.52)
}
