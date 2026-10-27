import AppKit

// 设置面板的感叹号说明靠系统悬停提示，默认要停一秒左右才出现，调快到 0.25 秒
UserDefaults.standard.register(defaults: ["NSInitialToolTipDelay": 250])

// `--snapshot <目录>`：把休息屏、预告条、设置面板渲染成 PNG，用于界面验收，不启动菜单栏应用
if let index = CommandLine.arguments.firstIndex(of: "--snapshot") {
    let path = CommandLine.arguments.indices.contains(index + 1)
        ? CommandLine.arguments[index + 1]
        : FileManager.default.currentDirectoryPath
    MainActor.assumeIsolated {
        SnapshotRenderer.run(outputDirectory: URL(fileURLWithPath: path))
    }
    exit(0)
}

let app = NSApplication.shared
let controller = MainActor.assumeIsolated { AppController() }
app.delegate = controller
app.setActivationPolicy(.accessory)
app.run()
