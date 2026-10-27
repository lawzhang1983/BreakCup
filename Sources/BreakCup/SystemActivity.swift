import AppKit
import CoreGraphics
import ServiceManagement

/// 键鼠空闲时长与锁屏状态
@MainActor
final class SystemActivity {
    private var tokens: [(NotificationCenter, NSObjectProtocol)] = []
    private var sessionInactive = false

    /// 锁屏或会话被切走（快速用户切换）都算锁定
    var isScreenLocked: Bool {
        if sessionInactive { return true }
        let session = CGSessionCopyCurrentDictionary() as? [String: Any]
        return session?["CGSSessionScreenIsLocked"] as? Bool ?? false
    }

    /// 状态变化（锁屏、解锁、唤醒）时立刻回调一次，不必等下一秒
    func start(onChange: @escaping @MainActor () -> Void) {
        let distributed = DistributedNotificationCenter.default()
        for name in ["com.apple.screenIsLocked", "com.apple.screenIsUnlocked"] {
            observe(distributed, NSNotification.Name(name), onChange)
        }
        let workspace = NSWorkspace.shared.notificationCenter
        observe(workspace, NSWorkspace.sessionDidResignActiveNotification) { [weak self] in
            self?.sessionInactive = true
            onChange()
        }
        observe(workspace, NSWorkspace.sessionDidBecomeActiveNotification) { [weak self] in
            self?.sessionInactive = false
            onChange()
        }
        observe(workspace, NSWorkspace.didWakeNotification, onChange)
    }

    func idleSeconds() -> TimeInterval {
        // ~0 即 kCGAnyInputEventType：任意键盘、鼠标、触控板输入。
        // 远程桌面注入的键鼠可能只记在会话状态里，取两者中较近的一次，远程操作也算在用电脑
        let anyInput = CGEventType(rawValue: ~0)!
        return min(
            CGEventSource.secondsSinceLastEventType(.hidSystemState, eventType: anyInput),
            CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: anyInput)
        )
    }

    private func observe(_ center: NotificationCenter, _ name: NSNotification.Name, _ action: @escaping @MainActor () -> Void) {
        let token = center.addObserver(forName: name, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { action() }
        }
        tokens.append((center, token))
    }
}

/// 登录时自动启动（系统设置 › 通用 › 登录项）
enum LoginItem {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static var note: String? {
        switch SMAppService.mainApp.status {
        case .requiresApproval:
            return "需要在「系统设置 › 通用 › 登录项」里允许歇一杯"
        case .notFound:
            return "请先把歇一杯放进「应用程序」文件夹再开启"
        default:
            return nil
        }
    }

    static func set(_ enabled: Bool) throws {
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }
}
