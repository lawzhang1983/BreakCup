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
            let button = self.item.button!
            let imageRect = button.cell?.imageRect(forBounds: button.bounds) ?? .zero
            Log.write("菜单栏图标已就位：可见=\(self.item.isVisible) 位置=\(NSStringFromRect(window.frame)) 屏幕=\(window.screen?.localizedName ?? "?") 图标区=\(NSStringFromRect(imageRect))")
        }
    }

    func refresh() {
        guard let button = item.button else { return }
        if model.menuBarSymbol != shownSymbol {
            shownSymbol = model.menuBarSymbol
            button.image = Self.menuBarImage(shownSymbol)
        }
        let title = model.menuBarTitle.isEmpty ? "" : " \(model.menuBarTitle)"
        if title != shownTitle {
            shownTitle = title
            button.title = title
        }
    }

    /// SF Symbol 的对齐框比字形矮（咖啡杯 10.5pt 对字形约 13pt），菜单栏落在 1x 屏上时 AppKit 按对齐框裁图，
    /// 杯口上沿会被切掉一截。转绘成普通图片后对齐框就是整张图，1x、2x 屏都完整，按钮也回到标准高度。
    private static func menuBarImage(_ name: String) -> NSImage? {
        let base = NSImage(systemSymbolName: name, accessibilityDescription: "歇一杯")
            ?? NSImage(systemSymbolName: "cup.and.saucer", accessibilityDescription: "歇一杯")
        guard let symbol = base?.withSymbolConfiguration(.init(pointSize: 15, weight: .regular)) else { return nil }
        let image = NSImage(size: symbol.size, flipped: false) { rect in
            symbol.draw(in: rect)
            return true
        }
        image.isTemplate = true
        image.accessibilityDescription = "歇一杯"
        return image
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
