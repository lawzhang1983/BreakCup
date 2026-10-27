import AppKit
import BreakCupCore
import SwiftUI

/// 到点前的预告条：每块屏幕顶部一条，不抢焦点、不挡点击
@MainActor
final class WarningToastController {
    private var panels: [NSPanel] = []
    private var restAt: Date?
    private var shownAt = Date()
    private var restMinutes: Double = 5
    private var screenObserver: NSObjectProtocol?

    var isShowing: Bool { restAt != nil }

    func show(restAt: Date, restMinutes: Double) {
        if self.restAt == restAt, panels.count == NSScreen.screens.count { return }
        if self.restAt != restAt { shownAt = Date() }
        self.restAt = restAt
        self.restMinutes = restMinutes
        rebuild(fadeIn: panels.isEmpty)
        if screenObserver == nil {
            screenObserver = NotificationCenter.default.addObserver(
                forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
            ) { [weak self] _ in
                MainActor.assumeIsolated { self?.rebuild(fadeIn: false) }
            }
        }
    }

    func hide() {
        guard restAt != nil else { return }
        restAt = nil
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
        screenObserver = nil
        let closing = panels
        panels = []
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = 0.3
            for panel in closing { panel.animator().alphaValue = 0 }
        }, completionHandler: {
            for panel in closing { panel.orderOut(nil) }
        })
    }

    private func rebuild(fadeIn: Bool) {
        guard let restAt else { return }
        panels.forEach { $0.orderOut(nil) }
        panels = NSScreen.screens.map { screen in
            Self.makePanel(on: screen, view: WarningView(restAt: restAt, shownAt: shownAt, restMinutes: restMinutes))
        }
        for panel in panels {
            panel.alphaValue = fadeIn ? 0 : 1
            panel.orderFrontRegardless()
        }
        if fadeIn {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.3
                for panel in panels { panel.animator().alphaValue = 1 }
            }
        }
    }

    private static func makePanel(on screen: NSScreen, view: WarningView) -> NSPanel {
        let size = NSSize(width: 470, height: 76)
        let visible = screen.visibleFrame
        let frame = NSRect(
            x: visible.midX - size.width / 2,
            y: visible.maxY - size.height - 14,
            width: size.width,
            height: size.height
        )
        let panel = NSPanel(contentRect: frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        // 不设 isFloatingPanel：它会把层级改回浮动层，排到程序坞和菜单栏下面
        panel.level = .popUpMenu
        panel.hidesOnDeactivate = false
        panel.ignoresMouseEvents = true
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        panel.contentView = NSHostingView(rootView: view.frame(width: size.width, height: size.height))
        return panel
    }
}

struct WarningView: View {
    let restAt: Date
    let shownAt: Date
    let restMinutes: Double

    var body: some View {
        TimelineView(.periodic(from: .now, by: 0.25)) { context in
            let total = max(1, restAt.timeIntervalSince(shownAt))
            let remaining = max(0, restAt.timeIntervalSince(context.date))
            let seconds = Int(remaining.rounded(.up))
            HStack(spacing: 14) {
                ZStack {
                    Circle().stroke(.white.opacity(0.15), lineWidth: 4)
                    Circle()
                        .trim(from: 0, to: remaining / total)
                        .stroke(Color.latte, style: StrokeStyle(lineWidth: 4, lineCap: .round))
                        .rotationEffect(.degrees(-90))
                    Text("\(seconds)")
                        .font(.system(size: 15, weight: .semibold, design: .rounded))
                        .monospacedDigit()
                        .foregroundStyle(.white)
                }
                .frame(width: 44, height: 44)

                VStack(alignment: .leading, spacing: 3) {
                    Text("\(seconds) 秒后开始休息 \(BreakFormat.minutes(restMinutes))")
                        .font(.system(size: 15, weight: .semibold))
                        .monospacedDigit()
                        .foregroundStyle(.white)
                    Text("先保存手头的工作，准备起身走一走")
                        .font(.system(size: 12))
                        .foregroundStyle(.white.opacity(0.65))
                }
                Spacer(minLength: 0)
                Image(systemName: "cup.and.heat.waves.fill")
                    .font(.system(size: 22))
                    .foregroundStyle(Color.latte)
            }
            .padding(.horizontal, 16)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .fill(Color(white: 0.11).opacity(0.94))
                    .overlay(
                        RoundedRectangle(cornerRadius: 18, style: .continuous)
                            .strokeBorder(.white.opacity(0.10))
                    )
            )
        }
        .environment(\.colorScheme, .dark)
    }
}
