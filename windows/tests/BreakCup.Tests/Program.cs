using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using BreakCup.Core;
using Microsoft.Win32;

namespace BreakCup.Tests
{
    /// <summary>
    /// 核心逻辑单测，与 Mac 版 BreakCupCoreTests 逐条对应。
    /// 不依赖测试框架：用 <c>dotnet run</c> 直接执行，有失败时退出码为 1。
    /// </summary>
    internal static class Program
    {
        private static readonly DateTime T0 = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        private static int failures;
        private static string current = "";

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            var tests = new List<(string Suite, string Name, Action Body)>
            {
                ("SchedulerTests", "normalCycleWarnsThenRestsThenResumes", NormalCycleWarnsThenRestsThenResumes),
                ("SchedulerTests", "idleDuringRestDoesNotEndRestEarly", IdleDuringRestDoesNotEndRestEarly),
                ("SchedulerTests", "awayLongEnoughResetsWorkTimer", AwayLongEnoughResetsWorkTimer),
                ("SchedulerTests", "shortRestStillNeedsFiveMinutesAwayBeforeReset", ShortRestStillNeedsFiveMinutesAwayBeforeReset),
                ("SchedulerTests", "awayResetCanBeTurnedOff", AwayResetCanBeTurnedOff),
                ("SchedulerTests", "meetingDefersAndResumesWithFullWarningAfterGrace", MeetingDefersAndResumesWithFullWarningAfterGrace),
                ("SchedulerTests", "meetingFlickerRestartsGrace", MeetingFlickerRestartsGrace),
                ("SchedulerTests", "meetingStartingDuringWarningCancelsWarning", MeetingStartingDuringWarningCancelsWarning),
                ("SchedulerTests", "meetingDoesNotCountAsAway", MeetingDoesNotCountAsAway),
                ("SchedulerTests", "meetingDeferralCanBeTurnedOff", MeetingDeferralCanBeTurnedOff),
                ("SchedulerTests", "systemSleepLongerThanRestCountsAsRest", SystemSleepLongerThanRestCountsAsRest),
                ("SchedulerTests", "lockLongerThanRestResetsOnUnlock", LockLongerThanRestResetsOnUnlock),
                ("SchedulerTests", "leavingDuringScreenSaverAfterRestOnlyRestartsTimer", LeavingDuringScreenSaverAfterRestOnlyRestartsTimer),
                ("SchedulerTests", "shortLockKeepsCountingAndPausesWarning", ShortLockKeepsCountingAndPausesWarning),
                ("SchedulerTests", "shorteningWorkTimeStillGivesFullWarning", ShorteningWorkTimeStillGivesFullWarning),
                ("SchedulerTests", "zeroWarningGoesStraightToRest", ZeroWarningGoesStraightToRest),
                ("SchedulerTests", "manualRestAndRestart", ManualRestAndRestart),
                ("SchedulerTests", "secondsUntilRestAndWorked", SecondsUntilRestAndWorked),
                ("SettingsTests", "sanitizedClampsAndKeepsRestBounded", SanitizedClampsAndKeepsRestBounded),
                ("SettingsTests", "storeRoundTrip", StoreRoundTrip),
                ("SettingsTests", "screenSaverAfterRestIsOnByDefault", ScreenSaverAfterRestIsOnByDefault),
                ("ScreenSaverTests", "fallsBackToLockWithoutScreenSaver", FallsBackToLockWithoutScreenSaver),
                ("TipTests", "libraryCoversEveryCategoryWithUniqueIDs", LibraryCoversEveryCategoryWithUniqueIds),
                ("TipTests", "deckRotatesCategoriesWithoutRepeatsWithinRound", DeckRotatesCategoriesWithoutRepeatsWithinRound),
                ("TipTests", "deckFallsBackWhenNoCategoryUsable", DeckFallsBackWhenNoCategoryUsable),
                ("TipTests", "formatting", Formatting),
                ("MediaStateTests", "consentStoreTimestampsDecideInUse", ConsentStoreTimestampsDecideInUse),
                ("MediaStateTests", "meetingAppNamesAreDeduplicated", MeetingAppNamesAreDeduplicated),
                ("MediaStateTests", "cameraAloneCountsAsMeeting", CameraAloneCountsAsMeeting),
            };

            var started = DateTime.Now;
            foreach (var test in tests)
            {
                current = $"{test.Suite}.{test.Name}";
                var before = failures;
                try
                {
                    test.Body();
                }
                catch (Exception ex)
                {
                    failures += 1;
                    Console.WriteLine($"  异常：{ex.GetType().Name}: {ex.Message}");
                }
                Console.WriteLine(failures == before ? $"✔ {current}" : $"✘ {current}");
            }
            var elapsed = (DateTime.Now - started).TotalSeconds;
            Console.WriteLine(failures == 0
                ? $"✔ {tests.Count} 个测试全部通过（{elapsed:0.000} 秒）"
                : $"✘ {tests.Count} 个测试中有 {failures} 处失败");
            return failures == 0 ? 0 : 1;
        }

        private static void Expect(bool condition, string message = "", [CallerLineNumber] int line = 0)
        {
            if (condition) return;
            failures += 1;
            Console.WriteLine($"  第 {line} 行未通过 {message}");
        }

        private static void Equal<T>(T actual, T expected, [CallerLineNumber] int line = 0)
        {
            if (Equals(actual, expected)) return;
            failures += 1;
            Console.WriteLine($"  第 {line} 行：期望 {expected}，实际 {actual}");
        }

        private static DateTime At(double seconds) => T0.AddSeconds(seconds);

        private static BreakSettings Settings(double work = 10, double rest = 5, double warning = 60,
            bool resetWhenAway = true, bool deferWhenMediaInUse = true)
        {
            var s = BreakSettings.Default;
            s.WorkMinutes = work;
            s.RestMinutes = rest;
            s.WarningSeconds = warning;
            s.ResetWhenAway = resetWhenAway;
            s.DeferWhenMediaInUse = deferWhenMediaInUse;
            return s;
        }

        /// <summary>以 1 秒步长从 from 跑到 to（含），环境由闭包按时间给出</summary>
        private static void Run(BreakScheduler scheduler, double from, double to, Func<double, ActivityEnvironment>? environment = null)
        {
            for (var t = from; t <= to; t += 1)
            {
                scheduler.Tick(At(t), environment?.Invoke(t) ?? new ActivityEnvironment());
            }
        }

        // MARK: - SchedulerTests

        private static void NormalCycleWarnsThenRestsThenResumes()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 539);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(T0));

            Run(s, 540, 540);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Warning(T0, At(600)));

            Run(s, 541, 600);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Resting(At(600), At(900)));
            Equal(s.RestRemaining(At(700)), 200.0);

            Run(s, 601, 900);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(At(900)));
            Equal(s.LastResetReason, WorkResetReason.RestFinished);
        }

        private static void IdleDuringRestDoesNotEndRestEarly()
        {
            var s = new BreakScheduler(Settings(warning: 0), T0);
            Run(s, 1, 600);
            Expect(s.IsResting);
            Run(s, 601, 899, _ => new ActivityEnvironment(idleSeconds: 10_000));
            Expect(s.IsResting);
        }

        private static void AwayLongEnoughResetsWorkTimer()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 400, t => new ActivityEnvironment(idleSeconds: t >= 100 ? t - 100 : 0));
            // 100 秒起不再碰键鼠，空闲满 300 秒（休息时长）时清零
            Equal(s.LastResetReason, WorkResetReason.Away);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(At(400)));
        }

        private static void ShortRestStillNeedsFiveMinutesAwayBeforeReset()
        {
            var s = new BreakScheduler(Settings(work: 30, rest: 1), T0);
            Run(s, 1, 240, _ => new ActivityEnvironment(idleSeconds: 200));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(T0));
            Run(s, 241, 241, _ => new ActivityEnvironment(idleSeconds: 300));
            Equal(s.LastResetReason, WorkResetReason.Away);
        }

        private static void AwayResetCanBeTurnedOff()
        {
            var s = new BreakScheduler(Settings(resetWhenAway: false), T0);
            Run(s, 1, 540, _ => new ActivityEnvironment(idleSeconds: 10_000));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Warning(T0, At(600)));
        }

        private static void MeetingDefersAndResumesWithFullWarningAfterGrace()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 1200, _ => new ActivityEnvironment(mediaInUse: true));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Deferred(T0, At(540)));
            Equal(s.SecondsUntilRest(At(1200)), null);

            // 会议结束后需连续空闲 5 秒才开始预告
            Run(s, 1201, 1205);
            Expect(s.Phase is BreakPhase.Deferred, $"宽限期内不该开始预告：{s.Phase}");
            Run(s, 1206, 1206);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Warning(T0, At(1266)));
        }

        private static void MeetingFlickerRestartsGrace()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 600, _ => new ActivityEnvironment(mediaInUse: true));
            Run(s, 601, 603);
            Run(s, 604, 604, _ => new ActivityEnvironment(mediaInUse: true));
            Run(s, 605, 609);
            Expect(s.Phase is BreakPhase.Deferred, $"短暂断流不该结束推迟：{s.Phase}");
            Run(s, 610, 610);
            Expect(s.Phase is BreakPhase.Warning, $"连续空闲 5 秒后应开始预告：{s.Phase}");
        }

        private static void MeetingStartingDuringWarningCancelsWarning()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 560);
            Expect(s.Phase is BreakPhase.Warning, $"应处于预告：{s.Phase}");
            Run(s, 561, 561, _ => new ActivityEnvironment(mediaInUse: true));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Deferred(T0, At(561)));
        }

        private static void MeetingDoesNotCountAsAway()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 539, _ => new ActivityEnvironment(idleSeconds: 10_000, mediaInUse: true));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(T0));
        }

        private static void MeetingDeferralCanBeTurnedOff()
        {
            var s = new BreakScheduler(Settings(deferWhenMediaInUse: false), T0);
            Run(s, 1, 600, _ => new ActivityEnvironment(mediaInUse: true));
            Expect(s.IsResting);
        }

        private static void SystemSleepLongerThanRestCountsAsRest()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 500);
            s.Tick(At(820), new ActivityEnvironment());
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(At(820)));
            Equal(s.LastResetReason, WorkResetReason.SystemSlept);
        }

        private static void LockLongerThanRestResetsOnUnlock()
        {
            var s = new BreakScheduler(Settings(resetWhenAway: false), T0);
            Run(s, 1, 200);
            Run(s, 201, 520, _ => new ActivityEnvironment(screenLocked: true));
            Run(s, 521, 521);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(At(521)));
            Equal(s.LastResetReason, WorkResetReason.ScreenLocked);
        }

        private static void LeavingDuringScreenSaverAfterRestOnlyRestartsTimer()
        {
            // 休息 600–900 秒；休息中每 30 秒刷新一次空闲计时，所以收屏时空闲不到 30 秒
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 900);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(At(900)));
            // 屏保期间人不在：空闲满 5 分钟按「离开过」重新计时，不会再进一次休息
            Run(s, 901, 1180, t => new ActivityEnvironment(idleSeconds: t - 880));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(At(1180)));
            Equal(s.LastResetReason, WorkResetReason.Away);
            Equal(s.SecondsUntilRest(At(1180)), 600.0);

            // 屏保要密码或直接锁屏：锁着的时间够一个休息时长，解锁时重新计时
            var locked = new BreakScheduler(Settings(), T0);
            Run(locked, 1, 900);
            Run(locked, 901, 1300, t => new ActivityEnvironment(idleSeconds: t - 880, screenLocked: true));
            Equal<BreakPhase>(locked.Phase, new BreakPhase.Working(At(900)));
            Run(locked, 1301, 1301);
            Equal<BreakPhase>(locked.Phase, new BreakPhase.Working(At(1301)));
            Equal(locked.LastResetReason, WorkResetReason.ScreenLocked);
        }

        private static void ShortLockKeepsCountingAndPausesWarning()
        {
            var s = new BreakScheduler(Settings(resetWhenAway: false), T0);
            Run(s, 1, 550);
            Expect(s.Phase is BreakPhase.Warning, $"应处于预告：{s.Phase}");
            Run(s, 551, 600, _ => new ActivityEnvironment(screenLocked: true));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(T0));
            // 解锁后重新给满预告
            Run(s, 601, 601);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Warning(T0, At(661)));
        }

        private static void ShorteningWorkTimeStillGivesFullWarning()
        {
            var s = new BreakScheduler(Settings(work: 45), T0);
            Run(s, 1, 1200);
            s.Settings = Settings(work: 10);
            Run(s, 1201, 1201);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Warning(T0, At(1261)));
        }

        private static void ZeroWarningGoesStraightToRest()
        {
            var s = new BreakScheduler(Settings(warning: 0), T0);
            Run(s, 1, 599);
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(T0));
            Run(s, 600, 600);
            Expect(s.IsResting);
        }

        private static void ManualRestAndRestart()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 100);
            s.RestartWork(At(100));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Working(At(100)));
            Equal(s.LastResetReason, WorkResetReason.Manual);

            s.StartRestNow(At(101));
            Equal<BreakPhase>(s.Phase, new BreakPhase.Resting(At(101), At(401)));

            // 休息中「重新计时」无效
            s.RestartWork(At(102));
            Expect(s.IsResting);
        }

        private static void SecondsUntilRestAndWorked()
        {
            var s = new BreakScheduler(Settings(), T0);
            Run(s, 1, 100);
            Equal(s.SecondsUntilRest(At(100)), 500.0);
            Equal(s.SecondsWorked(At(100)), 100.0);
        }

        // MARK: - SettingsTests

        private static void SanitizedClampsAndKeepsRestBounded()
        {
            var s = BreakSettings.Default;
            s.WorkMinutes = 0;
            s.RestMinutes = 999;
            s.WarningSeconds = 9999;
            s.EnabledCategories = new List<TipCategory>();
            var clean = s.Sanitized();
            Equal(clean.WorkMinutes, 0.5);
            Equal(clean.RestMinutes, 60.0);
            Equal(clean.WarningSeconds, 15.0);
            Expect(clean.EnabledCategories.SequenceEqual(TipCategories.All));
        }

        private static void StoreRoundTrip()
        {
            var path = $@"Software\BreakCupTests\{Guid.NewGuid():N}";
            try
            {
                var store = new SettingsStore(path);
                Equal(store.Load(), BreakSettings.Default);

                var s = BreakSettings.Default;
                s.WorkMinutes = 50;
                s.RestMinutes = 8;
                s.WarningSeconds = 22.5;
                s.EnabledCategories = new List<TipCategory> { TipCategory.Eyes, TipCategory.Hydration };
                s.ShowCountdownInTray = false;
                s.ScreenSaverAfterRest = false;
                store.Save(s);
                Equal(store.Load(), s);
                using var key = Registry.CurrentUser.OpenSubKey(path);
                Equal(key?.GetValue("screenSaverAfterRest"), (object)0);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\BreakCupTests", throwOnMissingSubKey: false);
            }
        }

        private static void ScreenSaverAfterRestIsOnByDefault()
        {
            var path = $@"Software\BreakCupTests\{Guid.NewGuid():N}";
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(path))
                {
                    key.SetValue("workMinutes", "30");
                }
                // 旧版本存下的设置里没有这个值，升级后也要按默认打开
                Expect(new SettingsStore(path).Load().ScreenSaverAfterRest);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\BreakCupTests", throwOnMissingSubKey: false);
            }
        }

        // MARK: - ScreenSaverTests

        private static void FallsBackToLockWithoutScreenSaver()
        {
            const string mystify = @"C:\Windows\system32\Mystify.scr";
            Func<string, bool> exists = path => path == mystify;

            // 设置里选「无」：SCRNSAVE.EXE 被删掉，ScreenSaveActive 仍是 1（没配过屏保的机器通常就是这样）
            var none = ScreenSaverConfig.FromRegistry("", "1", null, exists);
            Equal(none.Action, AfterRestAction.Lock);
            Equal(none.Summary, "没设屏保，休息结束后直接锁屏，回来要输密码解锁");

            var secure = ScreenSaverConfig.FromRegistry(mystify, "1", "1", exists);
            Equal(secure.Action, AfterRestAction.ScreenSaver);
            Equal(secure.Summary, "屏保 Mystify，唤醒要输密码");

            // ScreenSaveActive 缺省算开；不要密码时提示动一下键鼠就能回桌面
            var open = ScreenSaverConfig.FromRegistry(mystify, null, "0", exists);
            Equal(open.Action, AfterRestAction.ScreenSaver);
            Expect(open.Summary.EndsWith("唤醒不要求密码，动一下键鼠就能回到桌面"));

            // 屏保被关掉，或程序文件已不在：发 SC_SCREENSAVE 不会有反应，改为锁屏
            Equal(ScreenSaverConfig.FromRegistry(mystify, "0", "1", exists).Action, AfterRestAction.Lock);
            Equal(ScreenSaverConfig.FromRegistry(@"C:\Windows\system32\Gone.scr", "1", "1", exists).Action, AfterRestAction.Lock);
        }

        // MARK: - TipTests

        private static void LibraryCoversEveryCategoryWithUniqueIds()
        {
            foreach (var category in TipCategories.All)
            {
                Expect(TipLibrary.All.Count(t => t.Category == category) >= 5, category.ToString());
            }
            Equal(TipLibrary.All.Select(t => t.Id).Distinct().Count(), TipLibrary.All.Count);
        }

        private static void DeckRotatesCategoriesWithoutRepeatsWithinRound()
        {
            var deck = new TipDeck(TipCategories.All, shuffle: list => list);
            var first = Enumerable.Range(0, 5).Select(_ => deck.Next().Category).ToList();
            Expect(first.SequenceEqual(TipCategories.All));

            var eyesDeck = new TipDeck(new[] { TipCategory.Eyes }, shuffle: list => list);
            var eyes = TipLibrary.All.Where(t => t.Category == TipCategory.Eyes).ToList();
            var drawn = Enumerable.Range(0, eyes.Count).Select(_ => eyesDeck.Next()).ToList();
            Expect(drawn.SequenceEqual(eyes));
            Equal(eyesDeck.Next(), eyes[0]);
        }

        private static void DeckFallsBackWhenNoCategoryUsable()
        {
            var deck = new TipDeck(Array.Empty<TipCategory>(), shuffle: list => list);
            Equal(deck.Next().Category, TipCategory.Exercise);
        }

        private static void Formatting()
        {
            Equal(BreakFormat.Clock(272), "04:32");
            Equal(BreakFormat.Clock(3872), "1:04:32");
            Equal(BreakFormat.Clock(0.2), "00:01");
            Equal(BreakFormat.Compact(45), "45秒");
            Equal(BreakFormat.Compact(61), "2分");
            Equal(BreakFormat.Minutes(45), "45 分钟");
            Equal(BreakFormat.Minutes(1.5), "1.5 分钟");
            Equal(BreakFormat.TrayNumber(61), ("2", false));
            Equal(BreakFormat.TrayNumber(42.3), ("43", true));
        }

        // MARK: - MediaStateTests

        private static void ConsentStoreTimestampsDecideInUse()
        {
            // 正在用：结束时间为 0；用过又停了：结束时间晚于开始时间；从没用过：两个都是 0
            Expect(ConsentStore.IsInUse(134_000_000_000_000_000, 0));
            Expect(!ConsentStore.IsInUse(134_000_000_000_000_000, 134_000_000_100_000_000));
            Expect(!ConsentStore.IsInUse(0, 0));

            Equal(ConsentStore.FallbackName(@"C:#Program Files#Tencent#WeMeet#wemeetapp.exe", nonPackaged: true), "wemeetapp");
            Equal(ConsentStore.ExecutablePath(@"C:#Program Files#Tencent#WeMeet#wemeetapp.exe"), @"C:\Program Files\Tencent\WeMeet\wemeetapp.exe");
            Equal(ConsentStore.FallbackName("Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", nonPackaged: false), "WindowsSoundRecorder");
        }

        private static void MeetingAppNamesAreDeduplicated()
        {
            var state = new MediaState(microphoneUsers: new[] { "腾讯会议", "腾讯会议", "" });
            Expect(state.InUse);
            Expect(state.MicrophoneUsers.SequenceEqual(new[] { "腾讯会议" }));
            Equal(state.Summary, "麦克风：腾讯会议 使用中 · 摄像头：空闲");
        }

        private static void CameraAloneCountsAsMeeting()
        {
            var state = new MediaState(cameraUsers: new[] { "Teams" });
            Expect(state.InUse);
            Equal(state.Summary, "麦克风：空闲 · 摄像头：Teams 使用中");
            Equal(new MediaState().Summary, "麦克风：空闲 · 摄像头：空闲");
            Expect(!new MediaState().InUse);
        }
    }
}
