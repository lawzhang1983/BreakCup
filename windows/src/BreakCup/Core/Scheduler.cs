using System;

namespace BreakCup.Core
{
    /// <summary>每次 tick 时从系统采集到的环境快照</summary>
    public readonly struct ActivityEnvironment
    {
        /// <summary>距离最后一次键盘或鼠标输入的秒数</summary>
        public double IdleSeconds { get; }
        /// <summary>麦克风或摄像头正在被使用</summary>
        public bool MediaInUse { get; }
        /// <summary>屏幕已锁定（或会话已切走）</summary>
        public bool ScreenLocked { get; }

        public ActivityEnvironment(double idleSeconds = 0, bool mediaInUse = false, bool screenLocked = false)
        {
            IdleSeconds = idleSeconds;
            MediaInUse = mediaInUse;
            ScreenLocked = screenLocked;
        }
    }

    public abstract record BreakPhase
    {
        /// <summary>从 Since 起连续使用中</summary>
        public sealed record Working(DateTime Since) : BreakPhase;
        /// <summary>预告中，RestAt 时进入休息</summary>
        public sealed record Warning(DateTime Since, DateTime RestAt) : BreakPhase;
        /// <summary>到点时麦克风或摄像头在用，等它们空闲后再预告</summary>
        public sealed record Deferred(DateTime Since, DateTime DeferredAt) : BreakPhase;
        /// <summary>强制休息中，Until 前不能结束</summary>
        public sealed record Resting(DateTime StartedAt, DateTime Until) : BreakPhase;
    }

    /// <summary>工作计时被清零的原因，供界面提示</summary>
    public enum WorkResetReason
    {
        Launched,
        RestFinished,
        Away,
        ScreenLocked,
        SystemSlept,
        Manual
    }

    /// <summary>久坐休息的状态机。纯逻辑、时间由调用方注入，便于单测；与 Mac 版 BreakScheduler 逐行对应。</summary>
    public sealed class BreakScheduler
    {
        public BreakPhase Phase { get; private set; }
        public WorkResetReason LastResetReason { get; private set; }
        public BreakSettings Settings { get; set; }

        private DateTime lastTick;
        private DateTime? lockedSince;
        private DateTime? mediaFreeSince;

        /// <summary>麦克风和摄像头需连续空闲这么久才算会议结束，避免通话中短暂断流就弹预告</summary>
        public const double MediaFreeGrace = 5;

        public BreakScheduler(BreakSettings settings, DateTime now)
        {
            Settings = settings;
            Phase = new BreakPhase.Working(now);
            LastResetReason = WorkResetReason.Launched;
            lastTick = now;
        }

        public bool IsResting => Phase is BreakPhase.Resting;

        public void Tick(DateTime now, ActivityEnvironment env)
        {
            var gap = Seconds(now, lastTick);
            lastTick = now;

            if (Phase is BreakPhase.Resting resting)
            {
                if (now >= resting.Until)
                {
                    ResetWork(now, WorkResetReason.RestFinished);
                }
                return;
            }

            // 两次 tick 之间隔了一个休息时长以上，说明系统睡眠过：视为已休息
            if (gap >= Settings.RestSeconds)
            {
                ResetWork(now, WorkResetReason.SystemSlept);
                return;
            }

            if (env.ScreenLocked)
            {
                lockedSince ??= now;
                // 锁屏期间不预告、不进休息，退回计时状态并保留起点
                switch (Phase)
                {
                    case BreakPhase.Warning w:
                        Phase = new BreakPhase.Working(w.Since);
                        break;
                    case BreakPhase.Deferred d:
                        Phase = new BreakPhase.Working(d.Since);
                        break;
                }
                return;
            }
            if (lockedSince is DateTime locked)
            {
                lockedSince = null;
                if (Seconds(now, locked) >= Settings.RestSeconds)
                {
                    ResetWork(now, WorkResetReason.ScreenLocked);
                    return;
                }
            }

            // 长时间没碰键鼠视为已离开；开会时人还坐着，不算离开
            if (Settings.ResetWhenAway && !env.MediaInUse && env.IdleSeconds >= Settings.AwaySeconds)
            {
                ResetWork(now, WorkResetReason.Away);
                return;
            }

            var holdForMedia = Settings.DeferWhenMediaInUse && env.MediaInUse;

            switch (Phase)
            {
                case BreakPhase.Working working:
                    if (Seconds(now, working.Since) >= Settings.WorkSeconds - Settings.WarningSeconds)
                    {
                        if (holdForMedia)
                        {
                            DeferRest(working.Since, now);
                        }
                        else
                        {
                            BeginWarningOrRest(now, working.Since);
                        }
                    }
                    break;
                case BreakPhase.Warning warning:
                    if (holdForMedia)
                    {
                        DeferRest(warning.Since, now);
                    }
                    else if (now >= warning.RestAt)
                    {
                        BeginRest(now);
                    }
                    break;
                case BreakPhase.Deferred deferred:
                    if (holdForMedia)
                    {
                        mediaFreeSince = null;
                    }
                    else
                    {
                        var freeSince = mediaFreeSince ?? now;
                        mediaFreeSince = freeSince;
                        if (Seconds(now, freeSince) >= MediaFreeGrace)
                        {
                            BeginWarningOrRest(now, deferred.Since);
                        }
                    }
                    break;
            }
        }

        /// <summary>手动「立即休息」：不受会议推迟约束</summary>
        public void StartRestNow(DateTime now)
        {
            if (IsResting) return;
            lastTick = now;
            BeginRest(now);
        }

        /// <summary>手动「重新计时」</summary>
        public void RestartWork(DateTime now)
        {
            if (IsResting) return;
            lastTick = now;
            ResetWork(now, WorkResetReason.Manual);
        }

        /// <summary>距离进入休息还有多少秒；会议推迟和休息中返回 null</summary>
        public double? SecondsUntilRest(DateTime now) => Phase switch
        {
            BreakPhase.Working w => Math.Max(0, Seconds(w.Since.AddSeconds(Settings.WorkSeconds), now)),
            BreakPhase.Warning w => Math.Max(0, Seconds(w.RestAt, now)),
            _ => null
        };

        /// <summary>已连续使用的秒数；休息中返回 null</summary>
        public double? SecondsWorked(DateTime now) => Phase switch
        {
            BreakPhase.Working w => Math.Max(0, Seconds(now, w.Since)),
            BreakPhase.Warning w => Math.Max(0, Seconds(now, w.Since)),
            BreakPhase.Deferred d => Math.Max(0, Seconds(now, d.Since)),
            _ => null
        };

        public double? RestRemaining(DateTime now) =>
            Phase is BreakPhase.Resting r ? Math.Max(0, Seconds(r.Until, now)) : null;

        private void ResetWork(DateTime now, WorkResetReason reason)
        {
            Phase = new BreakPhase.Working(now);
            LastResetReason = reason;
            mediaFreeSince = null;
        }

        private void DeferRest(DateTime since, DateTime now)
        {
            if (Phase is BreakPhase.Deferred) return;
            Phase = new BreakPhase.Deferred(since, now);
            mediaFreeSince = null;
        }

        private void BeginWarningOrRest(DateTime now, DateTime since)
        {
            mediaFreeSince = null;
            if (Settings.WarningSeconds <= 0)
            {
                BeginRest(now);
            }
            else
            {
                // 正常路径下 now ≈ since + 工作时长 - 预告时长；
                // 会议推迟或把工作时长调短后，也保证给满一个预告时长
                Phase = new BreakPhase.Warning(since, now.AddSeconds(Settings.WarningSeconds));
            }
        }

        private void BeginRest(DateTime now)
        {
            Phase = new BreakPhase.Resting(now, now.AddSeconds(Settings.RestSeconds));
            mediaFreeSince = null;
        }

        private static double Seconds(DateTime later, DateTime earlier) => (later - earlier).TotalSeconds;
    }
}
