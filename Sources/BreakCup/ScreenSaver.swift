import AppKit
import BreakCupCore

/// 系统屏幕保护：休息屏收起后接着启动，人走开了屏幕也不会一直亮着内容。
/// 唤醒时要不要输密码由「系统设置 › 锁定屏幕」决定，歇一杯只读不改。
enum ScreenSaver {
    private static let engine = URL(fileURLWithPath: "/System/Library/CoreServices/ScreenSaverEngine.app")

    @MainActor
    static func start() {
        NSWorkspace.shared.openApplication(at: engine, configuration: NSWorkspace.OpenConfiguration()) { _, error in
            if let error { Log.write("屏幕保护启动失败：\(error.localizedDescription)") }
        }
        DispatchQueue.global(qos: .utility).async {
            Log.write("已启动屏幕保护：\(lockDelay().summary)")
        }
    }

    /// 读当前的锁屏密码设置。会起一个 sysadminctl 进程，别放在主线程上调
    static func lockDelay() -> ScreenLockDelay {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/sbin/sysadminctl")
        process.arguments = ["-screenLock", "status"]
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        do {
            try process.run()
        } catch {
            return .unknown
        }
        // 平时几毫秒就返回；万一卡住也不能一直等
        let deadline = Date().addingTimeInterval(2)
        while process.isRunning && Date() < deadline {
            usleep(20_000)
        }
        if process.isRunning {
            process.terminate()
            return .unknown
        }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        return ScreenLockDelay(sysadminctlOutput: String(decoding: data, as: UTF8.self))
    }
}
