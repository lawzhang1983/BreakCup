import AppKit
import BreakCupCore
import IOKit.pwr_mgt
import SwiftUI

/// 强制休息屏：每块屏幕一扇全屏窗口，kiosk 模式下吞掉所有键鼠输入，直到休息时间结束。
@MainActor
final class RestOverlayController {
    private(set) var isPresented = false
    private var finishing = false
    private var windows: [RestWindow] = []
    private var session: RestSession?
    private var deck = TipDeck(categories: TipCategory.allCases)
    private var tipTimer: Timer?
    private var inputMonitor: Any?
    private var observers: [NSObjectProtocol] = []
    private let power = PowerAssertions()
    private var previousApp: NSRunningApplication?

    private static let kioskOptions: NSApplication.PresentationOptions = [
        .hideDock,
        .hideMenuBar,
        .disableProcessSwitching,
        .disableForceQuit,
        .disableSessionTermination,
        .disableHideApplication,
        .disableAppleMenu
    ]

    /// 预告阶段先把壁纸模糊图算好，进休息时不卡顿
    func prepare() {
        for screen in NSScreen.screens {
            _ = WallpaperLoader.background(for: screen)
        }
    }

    /// 进入或维持休息屏；重复调用只做巡检
    func present(startedAt: Date, until: Date, settings: BreakSettings) {
        if isPresented {
            if !finishing { enforce() }
            return
        }
        isPresented = true
        finishing = false

        let front = NSWorkspace.shared.frontmostApplication
        previousApp = front == NSRunningApplication.current ? nil : front

        deck = TipDeck(categories: settings.enabledCategories)
        session = RestSession(tip: deck.next(), startedAt: startedAt, until: until)
        let tipTimer = Timer(timeInterval: settings.tipIntervalSeconds, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.advanceTip() }
        }
        RunLoop.main.add(tipTimer, forMode: .common)
        self.tipTimer = tipTimer

        buildWindows(fadeIn: true)
        lockDown()
        power.begin()

        let center = NotificationCenter.default
        observers.append(center.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
        ) { [weak self] _ in
            // 休息中插拔显示器：按当前屏幕重建（kiosk 隐藏菜单栏也会发这个通知，屏幕没变就不动）
            MainActor.assumeIsolated { self?.rebuildIfScreensChanged() }
        })
        observers.append(center.addObserver(
            forName: NSApplication.didResignActiveNotification, object: nil, queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.enforce() }
        })
        Log.write("休息屏已覆盖 \(windows.count) 块屏幕")
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in
            guard let self, self.isPresented else { return }
            let front = NSWorkspace.shared.frontmostApplication?.localizedName ?? "?"
            Log.write("巡检：本应用在前台=\(NSApp.isActive) 前台应用=\(front) 键盘窗口=\(self.windows.contains(where: \.isKeyWindow))")
        }
    }

    /// 休息时间到：先显示「欢迎回来」，再淡出解锁；重复调用无副作用
    func dismiss() {
        guard isPresented, !finishing else { return }
        finishing = true
        tipTimer?.invalidate()
        tipTimer = nil
        withAnimation(.easeInOut(duration: 0.5)) {
            session?.finished = true
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { [weak self] in
            self?.fadeOutAndTearDown()
        }
        // 兜底：淡出动画的回调万一没来，也要按时解锁
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.5) { [weak self] in
            guard let self, self.finishing else { return }
            Log.write("淡出回调超时，强制收尾")
            self.tearDown()
        }
    }

    /// 兜底巡检：不在休息时，任何残留的休息窗口一律关掉
    func sweepOrphans() {
        guard !isPresented else { return }
        for case let window as RestWindow in NSApp.windows where window.isVisible {
            Log.write("清理残留休息窗口")
            window.orderOut(nil)
            window.close()
        }
    }

    /// 每秒巡检：保持前台、窗口在最上层、屏幕数一致、不熄屏
    func enforce() {
        guard isPresented, !finishing else { return }
        rebuildIfScreensChanged()
        if !NSApp.isActive {
            NSApp.activate(ignoringOtherApps: true)
        }
        for window in windows {
            window.orderFrontRegardless()
        }
        if !windows.contains(where: \.isKeyWindow) {
            windows.first?.makeKey()
        }
        power.keepAwake()
    }

    private func advanceTip() {
        guard let session, !finishing else { return }
        let next = deck.next()
        withAnimation(.easeInOut(duration: 0.8)) {
            session.tip = next
        }
    }

    private func rebuildIfScreensChanged() {
        let screens = NSScreen.screens.map(\.frame)
        let covered = windows.map(\.frame)
        let same = screens.count == covered.count && screens.allSatisfy { covered.contains($0) }
        if !same {
            Log.write("屏幕布局变化，重建休息屏（\(screens.count) 块）")
            buildWindows(fadeIn: false)
        }
    }

    private func buildWindows(fadeIn: Bool) {
        // 收尾阶段绝不新建窗口，否则新窗口没人回收，屏幕会被一直盖住
        guard let session, isPresented, !finishing else { return }
        let old = windows
        let mouse = NSEvent.mouseLocation
        windows = NSScreen.screens.map { screen in
            RestWindow(
                screen: screen,
                background: WallpaperLoader.background(for: screen),
                rootView: RestView(session: session, size: screen.frame.size)
            )
        }
        for window in old {
            window.orderOut(nil)
            window.close()
        }
        for window in windows {
            window.alphaValue = fadeIn ? 0 : 1
            window.orderFrontRegardless()
        }
        let keyWindow = windows.first { NSMouseInRect(mouse, $0.frame, false) } ?? windows.first
        keyWindow?.makeKey()

        if fadeIn {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.6
                for window in windows { window.animator().alphaValue = 1 }
            }
        }
    }

    private func lockDown() {
        NSApp.activate(ignoringOtherApps: true)
        NSApp.presentationOptions = Self.kioskOptions
        // 应用内的键盘、滚轮、点击一律丢弃，⌘Q、⌘W 之类的快捷键也到不了菜单
        inputMonitor = NSEvent.addLocalMonitorForEvents(matching: [
            .keyDown, .keyUp, .flagsChanged, .scrollWheel,
            .leftMouseDown, .rightMouseDown, .otherMouseDown
        ]) { _ in nil }
        NSCursor.hide()
    }

    private func fadeOutAndTearDown() {
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.6
            for window in windows { window.animator().alphaValue = 0 }
        }, completionHandler: { [weak self] in
            MainActor.assumeIsolated { self?.tearDown() }
        })
    }

    private func tearDown() {
        guard isPresented else { return }
        // 先注销监听、清空状态，再恢复程序坞和菜单栏：
        // 恢复时系统会同步发出屏幕参数变化通知，监听还在的话会把休息窗口重建出来
        observers.forEach(NotificationCenter.default.removeObserver)
        observers = []
        let closing = windows
        windows = []
        session = nil
        isPresented = false
        finishing = false

        for window in closing {
            window.orderOut(nil)
            window.close()
        }
        if let inputMonitor { NSEvent.removeMonitor(inputMonitor) }
        inputMonitor = nil
        NSApp.presentationOptions = []
        NSCursor.unhide()
        power.end()
        previousApp?.activate()
        previousApp = nil
        sweepOrphans()
        Log.write("休息屏已解除")
    }
}

/// 休息屏窗口：盖住整块屏幕（含菜单栏区域），自己吞掉键盘事件
final class RestWindow: NSWindow {
    init<Content: View>(screen: NSScreen, background: NSImage?, rootView: Content) {
        super.init(contentRect: screen.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        setFrame(screen.frame, display: false)
        level = NSWindow.Level(rawValue: Int(CGShieldingWindowLevel()))
        collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        isOpaque = true
        backgroundColor = .black
        hasShadow = false
        isMovable = false
        isReleasedWhenClosed = false
        animationBehavior = .none

        let bounds = NSRect(origin: .zero, size: screen.frame.size)
        let container = NSView(frame: bounds)
        let backgroundView = DriftingBackgroundView(frame: bounds, image: background)
        backgroundView.autoresizingMask = [.width, .height]
        let hosting = NSHostingView(rootView: rootView)
        hosting.frame = bounds
        hosting.autoresizingMask = [.width, .height]
        container.addSubview(backgroundView)
        container.addSubview(hosting)
        contentView = container
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
    override func keyDown(with event: NSEvent) {}
    override func keyUp(with event: NSEvent) {}
    override func flagsChanged(with event: NSEvent) {}
    override func performKeyEquivalent(with event: NSEvent) -> Bool { true }
    override func cancelOperation(_ sender: Any?) {}
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
}

/// 休息期间保持亮屏，并定期声明用户活动，避免系统屏保或锁屏盖住休息屏
final class PowerAssertions {
    private var displayAssertion: IOPMAssertionID = 0
    private var activityAssertion: IOPMAssertionID = 0
    private var lastDeclared = Date.distantPast

    func begin() {
        if displayAssertion == 0 {
            IOPMAssertionCreateWithName(
                kIOPMAssertPreventUserIdleDisplaySleep as CFString,
                IOPMAssertionLevel(kIOPMAssertionLevelOn),
                "歇一杯：休息屏需要保持亮屏" as CFString,
                &displayAssertion
            )
        }
        keepAwake(force: true)
    }

    func keepAwake(force: Bool = false) {
        guard force || Date().timeIntervalSince(lastDeclared) >= 30 else { return }
        lastDeclared = Date()
        IOPMAssertionDeclareUserActivity("歇一杯：休息中" as CFString, kIOPMUserActiveLocal, &activityAssertion)
    }

    func end() {
        if displayAssertion != 0 {
            IOPMAssertionRelease(displayAssertion)
            displayAssertion = 0
        }
        if activityAssertion != 0 {
            IOPMAssertionRelease(activityAssertion)
            activityAssertion = 0
        }
        lastDeclared = .distantPast
    }
}
