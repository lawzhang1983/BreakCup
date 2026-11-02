using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using BreakCup.Core;
using BreakCup.Platform;

namespace BreakCup.UI
{
    internal enum PhaseKind
    {
        Working,
        Warning,
        Deferred,
        Resting
    }

    internal static class PhaseKindExtensions
    {
        public static string Title(this PhaseKind phase) => phase switch
        {
            PhaseKind.Working => "计时中",
            PhaseKind.Warning => "即将休息",
            PhaseKind.Deferred => "会议中",
            _ => "休息中"
        };
    }

    /// <summary>托盘图标要画什么：剩余数字，或咖啡杯（会议中、休息中、关掉剩余时间显示时）</summary>
    internal readonly struct TrayGlyph : IEquatable<TrayGlyph>
    {
        public string? Number { get; }
        public bool IsSeconds { get; }
        public bool Steaming { get; }

        public TrayGlyph(string? number, bool isSeconds, bool steaming)
        {
            Number = number;
            IsSeconds = isSeconds;
            Steaming = steaming;
        }

        public bool Equals(TrayGlyph other) => Number == other.Number && IsSeconds == other.IsSeconds && Steaming == other.Steaming;
        public override bool Equals(object? obj) => obj is TrayGlyph other && Equals(other);
        public override int GetHashCode() => (Number?.GetHashCode() ?? 0) ^ (IsSeconds ? 1 : 0) ^ (Steaming ? 2 : 0);
    }

    /// <summary>设置面板的视图模型，与 Mac 版 AppModel 对应</summary>
    internal sealed class AppModel : INotifyPropertyChanged
    {
        private BreakSettings settings;
        private PhaseKind phase = PhaseKind.Working;
        private string countdown = "--:--";
        private string countdownCaption = "距离下次休息";
        private double progress;
        private string detail = "";
        private string meetingHint = "";
        private string screenSaverHint = "";
        private int todayRestCount;
        private bool launchAtLogin;
        private string? loginItemNote;
        private bool confirmingRest;
        private readonly DispatcherTimer confirmTimer;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Action<BreakSettings>? OnSettingsChange { get; set; }
        public Action? OnRestNow { get; set; }
        public Action? OnRestartWork { get; set; }
        public Action? OnQuit { get; set; }

        public AppModel(BreakSettings settings)
        {
            this.settings = settings.Clone();
            launchAtLogin = LoginItem.IsEnabled;
            loginItemNote = LoginItem.Note;
            RefreshScreenSaverHint();
            confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            confirmTimer.Tick += (_, _) =>
            {
                confirmTimer.Stop();
                ConfirmingRest = false;
            };
            StepCommand = new RelayCommand(p => Step((string)p!), p => CanStep((string)p!));
            RestNowCommand = new RelayCommand(_ => RequestRestNow(), _ => Phase != PhaseKind.Resting);
            RestartWorkCommand = new RelayCommand(_ => OnRestartWork?.Invoke(), _ => Phase != PhaseKind.Resting);
            QuitCommand = new RelayCommand(_ => OnQuit?.Invoke(), _ => Phase != PhaseKind.Resting);
        }

        /// <summary>步进器：参数形如 "work+"、"rest-"</summary>
        public RelayCommand StepCommand { get; }
        public RelayCommand RestNowCommand { get; }
        public RelayCommand RestartWorkCommand { get; }
        public RelayCommand QuitCommand { get; }

        public BreakSettings Settings => settings;

        public PhaseKind Phase { get => phase; private set => Set(ref phase, value); }
        public string Countdown { get => countdown; private set => Set(ref countdown, value); }
        public string CountdownCaption { get => countdownCaption; private set => Set(ref countdownCaption, value); }
        public double Progress { get => progress; private set => Set(ref progress, value); }
        public string Detail { get => detail; private set => Set(ref detail, value); }
        /// <summary>「开会时推迟休息」的悬停说明，带上当前麦克风和摄像头状态</summary>
        public string MeetingHint { get => meetingHint; private set => Set(ref meetingHint, value); }
        /// <summary>「休息结束后启动屏幕保护」的悬停说明，带上当前的屏保和密码设置</summary>
        public string ScreenSaverHint { get => screenSaverHint; private set => Set(ref screenSaverHint, value); }
        public int TodayRestCount { get => todayRestCount; set => Set(ref todayRestCount, value); }
        public bool LaunchAtLogin { get => launchAtLogin; private set => Set(ref launchAtLogin, value); }
        public string? LoginItemNote { get => loginItemNote; private set => Set(ref loginItemNote, value); }
        /// <summary>「立即休息」需要点两次：开始后无法中途退出，防止误触</summary>
        public bool ConfirmingRest { get => confirmingRest; private set => Set(ref confirmingRest, value); }

        public TrayGlyph TrayGlyph { get; private set; }
        public string TrayTooltip { get; private set; } = "歇一杯";

        public string AwayHint => $"键鼠闲置满 {(int)(settings.AwaySeconds / 60)} 分钟就重新计时，开会时不算";

        public void Refresh(BreakScheduler scheduler, MediaState media, DateTime now)
        {
            var s = scheduler.Settings;
            string? number = null;
            var isSeconds = false;
            var steaming = false;
            string tooltip;
            switch (scheduler.Phase)
            {
                case BreakPhase.Working:
                {
                    var left = scheduler.SecondsUntilRest(now) ?? 0;
                    var worked = scheduler.SecondsWorked(now) ?? 0;
                    Phase = PhaseKind.Working;
                    CountdownCaption = "距离下次休息";
                    Countdown = BreakFormat.Clock(left);
                    Progress = Math.Min(1, worked / Math.Max(1, s.WorkSeconds));
                    Detail = WorkingDetail(scheduler.LastResetReason, worked);
                    (number, isSeconds) = BreakFormat.TrayNumber(left);
                    tooltip = $"歇一杯 · 距离休息 {BreakFormat.Compact(left)}";
                    break;
                }
                case BreakPhase.Warning warning:
                {
                    var left = Math.Max(0, (warning.RestAt - now).TotalSeconds);
                    Phase = PhaseKind.Warning;
                    CountdownCaption = "休息即将开始";
                    Countdown = BreakFormat.Clock(left);
                    Progress = 1;
                    Detail = "先保存手头的工作，准备起身走一走";
                    (number, isSeconds) = BreakFormat.TrayNumber(left);
                    steaming = true;
                    tooltip = $"歇一杯 · {BreakFormat.Compact(left)}后开始休息";
                    break;
                }
                case BreakPhase.Deferred deferred:
                    Phase = PhaseKind.Deferred;
                    CountdownCaption = "会议中，已推迟休息";
                    Countdown = BreakFormat.Clock((now - deferred.DeferredAt).TotalSeconds);
                    Progress = 1;
                    Detail = s.WarningSeconds > 0
                        ? $"麦克风和摄像头都停用后，先预告 {(int)s.WarningSeconds} 秒再休息"
                        : "麦克风和摄像头都停用后立即休息";
                    tooltip = "歇一杯 · 会议中，已推迟休息";
                    break;
                case BreakPhase.Resting resting:
                {
                    var left = Math.Max(0, (resting.Until - now).TotalSeconds);
                    Phase = PhaseKind.Resting;
                    CountdownCaption = "休息剩余";
                    Countdown = BreakFormat.Clock(left);
                    Progress = Math.Min(1, (now - resting.StartedAt).TotalSeconds / Math.Max(1, (resting.Until - resting.StartedAt).TotalSeconds));
                    Detail = "休息结束后自动解锁";
                    steaming = true;
                    tooltip = "歇一杯 · 休息中";
                    break;
                }
                default:
                    tooltip = "歇一杯";
                    break;
            }
            TrayGlyph = s.ShowCountdownInTray ? new TrayGlyph(number, isSeconds, steaming) : new TrayGlyph(null, false, steaming);
            TrayTooltip = tooltip;
            MeetingHint = $"麦克风或摄像头在用时，到点先不强制休息\n麦克风：{media.MicrophoneStatus}\n摄像头：{media.CameraStatus}";
            RelayCommand.Refresh();
        }

        /// <summary>调度器校正过设置后同步回来（例如预告时长被压到工作时长一半以内）</summary>
        public void ReplaceSettings(BreakSettings clean)
        {
            if (settings.Equals(clean)) return;
            settings = clean.Clone();
            NotifySettings();
        }

        public void SetShowCountdown(bool value) => Mutate(s => s.ShowCountdownInTray = value);
        public void SetScreenSaverAfterRest(bool value) => Mutate(s => s.ScreenSaverAfterRest = value);

        /// <summary>重新读一次屏保设置，面板打开时调用</summary>
        public void RefreshScreenSaverHint()
        {
            ScreenSaverHint = "休息屏收起后接着启动系统屏幕保护，人走开了屏幕也不会一直亮着内容\n"
                + "唤醒时要不要输密码，看屏幕保护程序设置里的「在恢复时显示登录屏幕」\n"
                + $"当前：{ScreenSaver.ReadConfig().Summary}";
        }
        public void SetResetWhenAway(bool value) => Mutate(s => s.ResetWhenAway = value);
        public void SetDeferWhenMediaInUse(bool value) => Mutate(s => s.DeferWhenMediaInUse = value);

        public void SetLaunchAtLogin(bool enabled)
        {
            try
            {
                LoginItem.Set(enabled);
                LoginItemNote = LoginItem.Note;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException)
            {
                LoginItemNote = $"设置失败：{ex.Message}";
            }
            LaunchAtLogin = LoginItem.IsEnabled;
        }

        public void Toggle(TipCategory category)
        {
            var list = settings.EnabledCategories.ToList();
            if (list.Contains(category))
            {
                if (list.Count <= 1) return;
                list.Remove(category);
            }
            else
            {
                list.Add(category);
            }
            Mutate(s => s.EnabledCategories = TipCategories.All.Where(list.Contains).ToList());
        }

        private void RequestRestNow()
        {
            if (!ConfirmingRest)
            {
                ConfirmingRest = true;
                confirmTimer.Stop();
                confirmTimer.Start();
                return;
            }
            confirmTimer.Stop();
            ConfirmingRest = false;
            OnRestNow?.Invoke();
        }

        private static (double Min, double Max, double Step) Spec(string field) => field switch
        {
            "work" => (BreakSettings.WorkRange.Min, BreakSettings.WorkRange.Max, 5),
            "rest" => (BreakSettings.RestRange.Min, BreakSettings.RestRange.Max, 1),
            "warning" => (BreakSettings.WarningRange.Min, BreakSettings.WarningRange.Max, 15),
            _ => (BreakSettings.TipIntervalRange.Min, BreakSettings.TipIntervalRange.Max, 5)
        };

        private double Value(string field) => field switch
        {
            "work" => settings.WorkMinutes,
            "rest" => settings.RestMinutes,
            "warning" => settings.WarningSeconds,
            _ => settings.TipIntervalSeconds
        };

        private bool CanStep(string parameter)
        {
            var field = parameter.Substring(0, parameter.Length - 1);
            var (min, max, _) = Spec(field);
            var value = Value(field);
            return parameter.EndsWith("+") ? value < max : value > min;
        }

        /// <summary>与 SwiftUI Stepper 一致：按步长加减并夹在范围内（实测时填过的小数也能一步回到范围里）</summary>
        private void Step(string parameter)
        {
            var field = parameter.Substring(0, parameter.Length - 1);
            var (min, max, step) = Spec(field);
            var next = Math.Min(max, Math.Max(min, Value(field) + (parameter.EndsWith("+") ? step : -step)));
            Mutate(s =>
            {
                switch (field)
                {
                    case "work": s.WorkMinutes = next; break;
                    case "rest": s.RestMinutes = next; break;
                    case "warning": s.WarningSeconds = next; break;
                    default: s.TipIntervalSeconds = next; break;
                }
            });
        }

        private void Mutate(Action<BreakSettings> change)
        {
            var next = settings.Clone();
            change(next);
            if (next.Equals(settings)) return;
            settings = next;
            NotifySettings();
            OnSettingsChange?.Invoke(settings.Clone());
        }

        private void NotifySettings()
        {
            Raise(nameof(Settings));
            Raise(nameof(AwayHint));
            RelayCommand.Refresh();
        }

        private static string WorkingDetail(WorkResetReason reason, double worked)
        {
            if (worked < 120)
            {
                switch (reason)
                {
                    case WorkResetReason.Away: return "检测到你离开过电脑，已重新计时";
                    case WorkResetReason.ScreenLocked: return "锁屏时间够长，算作休息过了";
                    case WorkResetReason.SystemSlept: return "电脑睡眠过，算作休息过了";
                    case WorkResetReason.RestFinished: return "刚休息完，继续加油";
                    case WorkResetReason.Manual: return "已重新开始计时";
                }
            }
            return $"已连续使用 {(int)(worked / 60)} 分钟";
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            Raise(name!);
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    internal sealed class RelayCommand : ICommand
    {
        private static event EventHandler? RefreshRequested;
        private readonly Action<object?> execute;
        private readonly Func<object?, bool> canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool> canExecute)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => RefreshRequested += value;
            remove => RefreshRequested -= value;
        }

        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);

        /// <summary>每秒刷新一次就够：可用状态只随阶段和设置变化</summary>
        public static void Refresh() => RefreshRequested?.Invoke(null, EventArgs.Empty);
    }
}
