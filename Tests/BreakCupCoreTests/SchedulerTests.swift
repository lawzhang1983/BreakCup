import Foundation
import Testing
import BreakCupCore

private let t0 = Date(timeIntervalSince1970: 1_800_000_000)

private func settings(
    work: Double = 10,
    rest: Double = 5,
    warning: Double = 60,
    resetWhenAway: Bool = true,
    deferWhenMediaInUse: Bool = true
) -> BreakSettings {
    var s = BreakSettings.default
    s.workMinutes = work
    s.restMinutes = rest
    s.warningSeconds = warning
    s.resetWhenAway = resetWhenAway
    s.deferWhenMediaInUse = deferWhenMediaInUse
    return s
}

/// 以 1 秒步长从 `from` 跑到 `to`（含），环境由闭包按时间给出
private func run(
    _ scheduler: inout BreakScheduler,
    from: TimeInterval,
    to: TimeInterval,
    environment: (TimeInterval) -> ActivityEnvironment = { _ in ActivityEnvironment() }
) {
    var t = from
    while t <= to {
        scheduler.tick(now: t0.addingTimeInterval(t), environment: environment(t))
        t += 1
    }
}

struct SchedulerTests {
    @Test func normalCycleWarnsThenRestsThenResumes() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 539)
        #expect(s.phase == .working(since: t0))

        run(&s, from: 540, to: 540)
        #expect(s.phase == .warning(since: t0, restAt: t0.addingTimeInterval(600)))

        run(&s, from: 541, to: 600)
        #expect(s.phase == .resting(startedAt: t0.addingTimeInterval(600), until: t0.addingTimeInterval(900)))
        #expect(s.restRemaining(now: t0.addingTimeInterval(700)) == 200)

        run(&s, from: 601, to: 900)
        #expect(s.phase == .working(since: t0.addingTimeInterval(900)))
        #expect(s.lastResetReason == .restFinished)
    }

    @Test func idleDuringRestDoesNotEndRestEarly() {
        var s = BreakScheduler(settings: settings(warning: 0), now: t0)
        run(&s, from: 1, to: 600)
        #expect(s.isResting)
        run(&s, from: 601, to: 899) { _ in ActivityEnvironment(idleSeconds: 10_000) }
        #expect(s.isResting)
    }

    @Test func awayLongEnoughResetsWorkTimer() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 400) { t in ActivityEnvironment(idleSeconds: t >= 100 ? t - 100 : 0) }
        // 100 秒起不再碰键鼠，空闲满 300 秒（休息时长）时清零
        #expect(s.lastResetReason == .away)
        #expect(s.phase == .working(since: t0.addingTimeInterval(400)))
    }

    @Test func shortRestStillNeedsFiveMinutesAwayBeforeReset() {
        var s = BreakScheduler(settings: settings(work: 30, rest: 1), now: t0)
        run(&s, from: 1, to: 240) { _ in ActivityEnvironment(idleSeconds: 200) }
        #expect(s.phase == .working(since: t0))
        run(&s, from: 241, to: 241) { _ in ActivityEnvironment(idleSeconds: 300) }
        #expect(s.lastResetReason == .away)
    }

    @Test func awayResetCanBeTurnedOff() {
        var s = BreakScheduler(settings: settings(resetWhenAway: false), now: t0)
        run(&s, from: 1, to: 540) { _ in ActivityEnvironment(idleSeconds: 10_000) }
        #expect(s.phase == .warning(since: t0, restAt: t0.addingTimeInterval(600)))
    }

    @Test func meetingDefersAndResumesWithFullWarningAfterGrace() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 1200) { _ in ActivityEnvironment(mediaInUse: true) }
        #expect(s.phase == .deferred(since: t0, deferredAt: t0.addingTimeInterval(540)))
        #expect(s.secondsUntilRest(now: t0.addingTimeInterval(1200)) == nil)

        // 会议结束后需连续空闲 5 秒才开始预告
        run(&s, from: 1201, to: 1205)
        if case .deferred = s.phase {} else { Issue.record("宽限期内不该开始预告：\(s.phase)") }
        run(&s, from: 1206, to: 1206)
        #expect(s.phase == .warning(since: t0, restAt: t0.addingTimeInterval(1266)))
    }

    @Test func meetingFlickerRestartsGrace() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 600) { _ in ActivityEnvironment(mediaInUse: true) }
        run(&s, from: 601, to: 603)
        run(&s, from: 604, to: 604) { _ in ActivityEnvironment(mediaInUse: true) }
        run(&s, from: 605, to: 609)
        if case .deferred = s.phase {} else { Issue.record("短暂断流不该结束推迟：\(s.phase)") }
        run(&s, from: 610, to: 610)
        if case .warning = s.phase {} else { Issue.record("连续空闲 5 秒后应开始预告：\(s.phase)") }
    }

    @Test func meetingStartingDuringWarningCancelsWarning() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 560)
        if case .warning = s.phase {} else { Issue.record("应处于预告：\(s.phase)") }
        run(&s, from: 561, to: 561) { _ in ActivityEnvironment(mediaInUse: true) }
        #expect(s.phase == .deferred(since: t0, deferredAt: t0.addingTimeInterval(561)))
    }

    @Test func meetingDoesNotCountAsAway() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 539) { _ in ActivityEnvironment(idleSeconds: 10_000, mediaInUse: true) }
        #expect(s.phase == .working(since: t0))
    }

    @Test func meetingDeferralCanBeTurnedOff() {
        var s = BreakScheduler(settings: settings(deferWhenMediaInUse: false), now: t0)
        run(&s, from: 1, to: 600) { _ in ActivityEnvironment(mediaInUse: true) }
        #expect(s.isResting)
    }

    @Test func systemSleepLongerThanRestCountsAsRest() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 500)
        s.tick(now: t0.addingTimeInterval(820), environment: ActivityEnvironment())
        #expect(s.phase == .working(since: t0.addingTimeInterval(820)))
        #expect(s.lastResetReason == .systemSlept)
    }

    @Test func lockLongerThanRestResetsOnUnlock() {
        var s = BreakScheduler(settings: settings(resetWhenAway: false), now: t0)
        run(&s, from: 1, to: 200)
        run(&s, from: 201, to: 520) { _ in ActivityEnvironment(screenLocked: true) }
        run(&s, from: 521, to: 521)
        #expect(s.phase == .working(since: t0.addingTimeInterval(521)))
        #expect(s.lastResetReason == .screenLocked)
    }

    @Test func shortLockKeepsCountingAndPausesWarning() {
        var s = BreakScheduler(settings: settings(resetWhenAway: false), now: t0)
        run(&s, from: 1, to: 550)
        if case .warning = s.phase {} else { Issue.record("应处于预告：\(s.phase)") }
        run(&s, from: 551, to: 600) { _ in ActivityEnvironment(screenLocked: true) }
        #expect(s.phase == .working(since: t0))
        // 解锁后重新给满预告
        run(&s, from: 601, to: 601)
        #expect(s.phase == .warning(since: t0, restAt: t0.addingTimeInterval(661)))
    }

    @Test func shorteningWorkTimeStillGivesFullWarning() {
        var s = BreakScheduler(settings: settings(work: 45), now: t0)
        run(&s, from: 1, to: 1200)
        s.settings = settings(work: 10)
        run(&s, from: 1201, to: 1201)
        #expect(s.phase == .warning(since: t0, restAt: t0.addingTimeInterval(1261)))
    }

    @Test func zeroWarningGoesStraightToRest() {
        var s = BreakScheduler(settings: settings(warning: 0), now: t0)
        run(&s, from: 1, to: 599)
        #expect(s.phase == .working(since: t0))
        run(&s, from: 600, to: 600)
        #expect(s.isResting)
    }

    @Test func manualRestAndRestart() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 100)
        s.restartWork(now: t0.addingTimeInterval(100))
        #expect(s.phase == .working(since: t0.addingTimeInterval(100)))
        #expect(s.lastResetReason == .manual)

        s.startRestNow(now: t0.addingTimeInterval(101))
        #expect(s.phase == .resting(startedAt: t0.addingTimeInterval(101), until: t0.addingTimeInterval(401)))

        // 休息中「重新计时」无效
        s.restartWork(now: t0.addingTimeInterval(102))
        #expect(s.isResting)
    }

    @Test func secondsUntilRestAndWorked() {
        var s = BreakScheduler(settings: settings(), now: t0)
        run(&s, from: 1, to: 100)
        #expect(s.secondsUntilRest(now: t0.addingTimeInterval(100)) == 500)
        #expect(s.secondsWorked(now: t0.addingTimeInterval(100)) == 100)
    }
}

struct SettingsTests {
    @Test func sanitizedClampsAndKeepsRestBounded() {
        var s = BreakSettings.default
        s.workMinutes = 0
        s.restMinutes = 999
        s.warningSeconds = 9999
        s.enabledCategories = []
        let clean = s.sanitized()
        #expect(clean.workMinutes == 0.5)
        #expect(clean.restMinutes == 60)
        #expect(clean.warningSeconds == 15)
        #expect(clean.enabledCategories == TipCategory.allCases)
    }

    @Test func storeRoundTrip() throws {
        let suite = "breakcup.tests.\(UUID().uuidString)"
        let defaults = try #require(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = SettingsStore(defaults: defaults)
        #expect(store.load() == BreakSettings.default)

        var s = BreakSettings.default
        s.workMinutes = 50
        s.restMinutes = 8
        s.enabledCategories = [.eyes, .hydration]
        s.showCountdownInMenuBar = false
        store.save(s)
        #expect(store.load() == s)
    }
}

struct TipTests {
    @Test func libraryCoversEveryCategoryWithUniqueIDs() {
        for category in TipCategory.allCases {
            #expect(TipLibrary.all.filter { $0.category == category }.count >= 5)
        }
        #expect(Set(TipLibrary.all.map(\.id)).count == TipLibrary.all.count)
    }

    @Test func deckRotatesCategoriesWithoutRepeatsWithinRound() {
        var deck = TipDeck(categories: TipCategory.allCases, shuffle: { $0 })
        let first = (0..<5).map { _ in deck.next().category }
        #expect(first == TipCategory.allCases)

        var eyesDeck = TipDeck(categories: [.eyes], shuffle: { $0 })
        let eyes = TipLibrary.all.filter { $0.category == .eyes }
        let drawn = (0..<eyes.count).map { _ in eyesDeck.next() }
        #expect(drawn == eyes)
        #expect(eyesDeck.next() == eyes[0])
    }

    @Test func deckFallsBackWhenNoCategoryUsable() {
        var deck = TipDeck(categories: [], shuffle: { $0 })
        #expect(deck.next().category == .exercise)
    }

    @Test func formatting() {
        #expect(BreakFormat.clock(272) == "04:32")
        #expect(BreakFormat.clock(3872) == "1:04:32")
        #expect(BreakFormat.clock(0.2) == "00:01")
        #expect(BreakFormat.menuBar(45) == "45秒")
        #expect(BreakFormat.menuBar(61) == "2分")
        #expect(BreakFormat.minutes(45) == "45 分钟")
    }
}

struct MediaStateTests {
    @Test func systemScreenCaptureIsNotAMeeting() {
        // UU 远程实测：只有 replayd 在采集，到点被误判为开会
        let remote = MediaState(audioCaptures: [AudioCapture(name: "replayd", bundleID: "com.apple.replayd")], cameraInUse: false)
        #expect(!remote.inUse)
        #expect(remote.summary == "麦克风：仅系统录屏，不算开会 · 摄像头：空闲")

        // 拿不到 bundle ID 时按进程名识别
        #expect(!MediaState(audioCaptures: [AudioCapture(name: "replayd")], cameraInUse: false).inUse)
    }

    @Test func meetingDuringScreenCaptureStillDefers() {
        let state = MediaState(audioCaptures: [
            AudioCapture(name: "replayd", bundleID: "com.apple.replayd"),
            AudioCapture(name: "腾讯会议", bundleID: "com.tencent.meeting"),
            AudioCapture(name: "腾讯会议", bundleID: "com.tencent.meeting"),
        ], cameraInUse: false)
        #expect(state.inUse)
        #expect(state.microphoneUsers == ["腾讯会议"])
        #expect(state.summary == "麦克风：腾讯会议 使用中 · 摄像头：空闲")
    }

    @Test func cameraAloneCountsAsMeeting() {
        let state = MediaState(audioCaptures: [AudioCapture(name: "replayd")], cameraInUse: true)
        #expect(state.inUse)
        #expect(MediaState().summary == "麦克风：空闲 · 摄像头：空闲")
    }
}
