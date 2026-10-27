import AppKit
import BreakCupCore

/// 应用总控：每秒采集一次系统状态，推进调度状态机，再把界面对齐到当前阶段。
@MainActor
final class AppController: NSObject, NSApplicationDelegate {
    private let store = SettingsStore()
    private var scheduler: BreakScheduler
    private let model: AppModel
    private let media = MediaUsageMonitor()
    private let activity = SystemActivity()
    private let restOverlay = RestOverlayController()
    private let warningToast = WarningToastController()
    private let restLog = RestLog()
    private var statusBar: StatusBarController?
    private var timer: Timer?
    private var napGuard: NSObjectProtocol?
    private var overlayShownForRest = false
    private var lastMediaState: MediaState?

    override init() {
        let settings = store.load()
        scheduler = BreakScheduler(settings: settings, now: Date())
        model = AppModel(settings: settings)
        super.init()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if terminateIfAlreadyRunning() { return }

        // 菜单栏应用在后台容易被 App Nap 降频，计时会被拖慢
        napGuard = ProcessInfo.processInfo.beginActivity(
            options: [.userInitiatedAllowingIdleSystemSleep],
            reason: "歇一杯需要准时计时"
        )

        model.todayRestCount = restLog.todayCount()
        model.onSettingsChange = { [weak self] in self?.apply(settings: $0) }
        model.onRestNow = { [weak self] in self?.restNow() }
        model.onRestartWork = { [weak self] in self?.restartWork() }
        statusBar = StatusBarController(model: model)

        activity.start { [weak self] in self?.tick() }

        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        timer.tolerance = 0.2
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
        tick()
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        // 休息中拒绝退出（包括 ⌘Q、AppleScript 退出和注销时的询问）
        scheduler.isResting || restOverlay.isPresented ? .terminateCancel : .terminateNow
    }

    // MARK: - 调度

    private func tick() {
        let now = Date()
        let mediaState = media.currentState()
        if mediaState != lastMediaState {
            Log.write("设备占用：\(mediaState.summary)")
            lastMediaState = mediaState
        }
        let environment = ActivityEnvironment(
            idleSeconds: activity.idleSeconds(),
            mediaInUse: mediaState.inUse,
            screenLocked: activity.isScreenLocked
        )
        scheduler.tick(now: now, environment: environment)
        reconcile()
        model.refresh(scheduler: scheduler, media: mediaState, now: now)
        statusBar?.refresh()
    }

    private func reconcile() {
        switch scheduler.phase {
        case let .warning(_, restAt):
            if !warningToast.isShowing {
                Log.write("预告开始，\(Int(restAt.timeIntervalSinceNow.rounded())) 秒后休息")
            }
            restOverlay.prepare()
            warningToast.show(restAt: restAt, restMinutes: scheduler.settings.restMinutes)
        case let .resting(startedAt, until):
            warningToast.hide()
            if !overlayShownForRest {
                overlayShownForRest = true
                statusBar?.closePopover()
                restLog.record()
                model.todayRestCount = restLog.todayCount()
                Log.write("休息开始，至 \(until)")
            }
            restOverlay.present(startedAt: startedAt, until: until, settings: scheduler.settings)
        case .working, .deferred:
            warningToast.hide()
            if overlayShownForRest {
                overlayShownForRest = false
                Log.write("休息结束")
            }
            restOverlay.dismiss()
            restOverlay.sweepOrphans()
        }
    }

    private func apply(settings: BreakSettings) {
        let clean = settings.sanitized()
        store.save(clean)
        scheduler.settings = clean
        if model.settings != clean {
            model.settings = clean
        }
        tick()
    }

    private func restNow() {
        scheduler.startRestNow(now: Date())
        tick()
    }

    private func restartWork() {
        scheduler.restartWork(now: Date())
        tick()
    }

    private func terminateIfAlreadyRunning() -> Bool {
        guard let bundleID = Bundle.main.bundleIdentifier else { return false }
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID)
            .filter { $0 != NSRunningApplication.current }
        guard !others.isEmpty else { return false }
        Log.write("已有实例在运行，本实例退出")
        NSApp.terminate(nil)
        return true
    }
}

/// 今日休息次数，只保留当天
struct RestLog {
    private let defaults = UserDefaults.standard

    func todayCount() -> Int {
        defaults.string(forKey: "restLogDay") == today ? defaults.integer(forKey: "restLogCount") : 0
    }

    func record() {
        let count = todayCount() + 1
        defaults.set(today, forKey: "restLogDay")
        defaults.set(count, forKey: "restLogCount")
    }

    private var today: String {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyy-MM-dd"
        return formatter.string(from: Date())
    }
}

/// 标准输出日志：从终端直接运行可执行文件时便于观察
enum Log {
    static func write(_ message: String) {
        let stamp = ISO8601DateFormatter().string(from: Date())
        print("[歇一杯 \(stamp)] \(message)")
        fflush(stdout)
    }
}
