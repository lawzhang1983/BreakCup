import AppKit
import SwiftUI

/// 菜单栏咖啡杯图标：点击弹出设置面板
@MainActor
final class StatusBarController: NSObject {
    private let item: NSStatusItem
    private let popover = NSPopover()
    private let model: AppModel
    private var shownSymbol = ""
    private var shownTitle: String?

    init(model: AppModel) {
        self.model = model
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()

        let hosting = NSHostingController(rootView: SettingsView(model: model))
        hosting.sizingOptions = [.preferredContentSize]
        popover.contentViewController = hosting
        popover.behavior = .transient
        popover.animates = true

        if let button = item.button {
            button.target = self
            button.action = #selector(togglePopover(_:))
            button.imagePosition = .imageLeading
            button.font = NSFont.monospacedDigitSystemFont(ofSize: NSFont.systemFontSize, weight: .regular)
            button.toolTip = "歇一杯"
        }
        refresh()

        // 自检：菜单栏图标不以独立窗口出现在系统窗口列表里，只能由应用自己报告位置
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in
            guard let self, let window = self.item.button?.window else {
                Log.write("菜单栏图标未就位")
                return
            }
            Log.write("菜单栏图标已就位：可见=\(self.item.isVisible) 位置=\(NSStringFromRect(window.frame)) 屏幕=\(window.screen?.localizedName ?? "?")")
        }
    }

    func refresh() {
        guard let button = item.button else { return }
        if model.menuBarSymbol != shownSymbol {
            shownSymbol = model.menuBarSymbol
            let base = NSImage(systemSymbolName: shownSymbol, accessibilityDescription: "歇一杯")
                ?? NSImage(systemSymbolName: "cup.and.saucer", accessibilityDescription: "歇一杯")
            let image = base?.withSymbolConfiguration(.init(pointSize: 15, weight: .regular))
            image?.isTemplate = true
            button.image = image
        }
        let title = model.menuBarTitle.isEmpty ? "" : " \(model.menuBarTitle)"
        if title != shownTitle {
            shownTitle = title
            button.title = title
        }
    }

    func closePopover() {
        if popover.isShown { popover.performClose(nil) }
    }

    @objc private func togglePopover(_ sender: Any?) {
        if popover.isShown {
            popover.performClose(sender)
            return
        }
        guard let button = item.button else { return }
        NSApp.activate(ignoringOtherApps: true)
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        popover.contentViewController?.view.window?.makeKey()
    }
}
