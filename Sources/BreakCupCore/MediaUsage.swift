import Foundation

/// 一个正在采集音频输入的进程
public struct AudioCapture: Equatable, Sendable {
    public var name: String
    public var bundleID: String?

    public init(name: String, bundleID: String? = nil) {
        self.name = name
        self.bundleID = bundleID
    }

    /// 系统录屏服务 replayd：ScreenCaptureKit 采集系统声音都经由它，
    /// UU 远程这类远程桌面和录屏软件会让它持续处于采集状态，但并没有在用麦克风，与开会无关。
    /// 真正开会时，会议软件自己的进程会采集麦克风，排除它不会漏判。
    public var isSystemScreenCapture: Bool {
        bundleID == "com.apple.replayd" || name == "replayd"
    }
}

public struct MediaState: Equatable, Sendable {
    /// 正在采集麦克风的进程名
    public var microphoneUsers: [String]
    /// 系统录屏服务在采集声音（远程桌面、录屏），不算开会
    public var screenCaptureAudio: Bool
    public var cameraInUse: Bool

    public init(microphoneUsers: [String] = [], screenCaptureAudio: Bool = false, cameraInUse: Bool = false) {
        self.microphoneUsers = microphoneUsers
        self.screenCaptureAudio = screenCaptureAudio
        self.cameraInUse = cameraInUse
    }

    /// 把正在采集音频的进程分成会议嫌疑与系统录屏两类
    public init(audioCaptures: [AudioCapture], cameraInUse: Bool) {
        var users: [String] = []
        for capture in audioCaptures where !capture.isSystemScreenCapture && !users.contains(capture.name) {
            users.append(capture.name)
        }
        self.init(
            microphoneUsers: users,
            screenCaptureAudio: audioCaptures.contains(where: \.isSystemScreenCapture),
            cameraInUse: cameraInUse
        )
    }

    public var inUse: Bool { !microphoneUsers.isEmpty || cameraInUse }

    public var microphoneStatus: String {
        if !microphoneUsers.isEmpty { return "\(microphoneUsers.joined(separator: "、")) 使用中" }
        return screenCaptureAudio ? "仅系统录屏，不算开会" : "空闲"
    }

    public var cameraStatus: String { cameraInUse ? "使用中" : "空闲" }

    public var summary: String { "麦克风：\(microphoneStatus) · 摄像头：\(cameraStatus)" }
}
