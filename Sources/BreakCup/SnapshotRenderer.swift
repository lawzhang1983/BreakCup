import AppKit
import BreakCupCore
import SwiftUI

/// 界面验收用：离屏渲染各界面为 PNG，不需要录屏权限
@MainActor
enum SnapshotRenderer {
    static func run(outputDirectory: URL) {
        _ = NSApplication.shared
        NSApp.setActivationPolicy(.prohibited)
        try? FileManager.default.createDirectory(at: outputDirectory, withIntermediateDirectories: true)

        let screen = NSScreen.main
        let size = screen?.frame.size ?? CGSize(width: 1512, height: 982)
        let background = screen.flatMap(WallpaperLoader.background(for:))
        let now = Date()

        let tip = TipLibrary.all.first { $0.category == .eyes } ?? TipLibrary.all[0]
        let resting = RestSession(tip: tip, startedAt: now.addingTimeInterval(-28), until: now.addingTimeInterval(272))
        write(RestView(session: resting, size: size, staticBackground: .some(background)),
              size: size, to: outputDirectory.appendingPathComponent("rest.png"))

        let longTip = TipLibrary.all.max { $0.text.count < $1.text.count } ?? tip
        let longSession = RestSession(tip: longTip, startedAt: now.addingTimeInterval(-200), until: now.addingTimeInterval(100))
        let laptop = CGSize(width: 1512, height: 982)
        write(RestView(session: longSession, size: laptop, staticBackground: .some(background)),
              size: laptop, to: outputDirectory.appendingPathComponent("rest-laptop-longtip.png"))

        let finished = RestSession(tip: tip, startedAt: now.addingTimeInterval(-300), until: now)
        finished.finished = true
        write(RestView(session: finished, size: size, staticBackground: .some(background)),
              size: size, to: outputDirectory.appendingPathComponent("rest-finished.png"))

        write(WarningView(restAt: now.addingTimeInterval(42), shownAt: now.addingTimeInterval(-18), restMinutes: 5),
              size: CGSize(width: 470, height: 76), to: outputDirectory.appendingPathComponent("warning.png"))

        let model = AppModel(settings: .default)
        let start = now.addingTimeInterval(-12 * 60 - 20)
        var scheduler = BreakScheduler(settings: .default, now: start)
        for second in stride(from: 1.0, through: now.timeIntervalSince(start), by: 1) {
            scheduler.tick(now: start.addingTimeInterval(second), environment: ActivityEnvironment())
        }
        model.refresh(scheduler: scheduler, media: MediaState(screenCaptureAudio: true), now: now)
        model.todayRestCount = 3
        for (name, appearance) in [("settings-light.png", NSAppearance.Name.aqua), ("settings-dark.png", .darkAqua)] {
            // 离屏绘制不带弹窗底色，补一层窗口背景色
            writeHosted(SettingsView(model: model).background(Color(nsColor: .windowBackgroundColor)),
                        appearance: NSAppearance(named: appearance),
                        to: outputDirectory.appendingPathComponent(name))
        }
        for hint in hoverHints(in: NSHostingView(rootView: SettingsView(model: model))) {
            print("悬停说明：\(hint.replacingOccurrences(of: "\n", with: " / "))")
        }
        print("snapshots -> \(outputDirectory.path)")
    }

    /// 纯 SwiftUI 界面用 ImageRenderer
    private static func write<V: View>(_ view: V, size: CGSize, to url: URL) {
        let renderer = ImageRenderer(content: view.frame(width: size.width, height: size.height))
        renderer.scale = 1
        guard let image = renderer.cgImage else {
            print("render failed: \(url.lastPathComponent)")
            return
        }
        save(image, to: url)
    }

    /// 含系统控件（开关、步进器）的界面走 NSHostingView 离屏绘制
    private static func writeHosted<V: View>(_ view: V, appearance: NSAppearance?, to url: URL) {
        let hosting = NSHostingView(rootView: view)
        hosting.appearance = appearance
        let fitting = hosting.fittingSize
        hosting.frame = CGRect(origin: .zero, size: fitting)
        let window = NSWindow(contentRect: hosting.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.appearance = appearance
        window.backgroundColor = appearance?.name == .darkAqua ? NSColor(white: 0.16, alpha: 1) : NSColor(white: 0.96, alpha: 1)
        window.contentView = hosting
        hosting.layoutSubtreeIfNeeded()
        RunLoop.current.run(until: Date().addingTimeInterval(0.4))
        guard let rep = hosting.bitmapImageRepForCachingDisplay(in: hosting.bounds) else { return }
        hosting.cacheDisplay(in: hosting.bounds, to: rep)
        guard let image = rep.cgImage else { return }
        save(image, to: url)
    }

    /// 收集感叹号上挂的悬停说明和位置，核对文字是否挂到了图标上
    private static func hoverHints(in hosting: NSView) -> [String] {
        hosting.frame = CGRect(origin: .zero, size: hosting.fittingSize)
        let window = NSWindow(contentRect: hosting.frame, styleMask: [.borderless], backing: .buffered, defer: false)
        window.contentView = hosting
        hosting.layoutSubtreeIfNeeded()
        RunLoop.current.run(until: Date().addingTimeInterval(0.4))
        var hints: [String] = []
        func walk(_ view: NSView) {
            if let tip = view.toolTip, !tip.isEmpty {
                let frame = view.convert(view.bounds, to: hosting)
                hints.append("(\(Int(frame.minX)), \(Int(frame.minY)) \(Int(frame.width))×\(Int(frame.height))) \(tip)")
            }
            view.subviews.forEach(walk)
        }
        walk(hosting)
        return hints
    }

    private static func save(_ image: CGImage, to url: URL) {
        let rep = NSBitmapImageRep(cgImage: image)
        try? rep.representation(using: .png, properties: [:])?.write(to: url)
    }
}
