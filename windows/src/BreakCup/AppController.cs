using System;
using System.Windows;
using System.Windows.Threading;
using BreakCup.Core;
using BreakCup.Platform;
using BreakCup.UI;
using Microsoft.Win32;

namespace BreakCup
{
    /// <summary>应用总控：每秒采集一次系统状态，推进调度状态机，再把界面对齐到当前阶段。与 Mac 版 AppController 对应。</summary>
    internal sealed class AppController
    {
        private readonly SettingsStore store = new SettingsStore();
        private readonly BreakScheduler scheduler;
        private readonly AppModel model;
        private readonly MediaUsageMonitor media = new MediaUsageMonitor();
        private readonly SystemActivity activity;
        private readonly RestOverlay restOverlay = new RestOverlay();
        private readonly WarningToast warningToast = new WarningToast();
        private readonly DispatcherTimer timer;
        private TrayController? tray;
        private bool overlayShownForRest;
        private MediaState? lastMediaState;

        public AppController()
        {
            var settings = store.Load();
            scheduler = new BreakScheduler(settings, DateTime.Now);
            model = new AppModel(settings);
            activity = new SystemActivity(Application.Current.Dispatcher);
            timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => Tick();
        }

        public void Start(bool firstRun)
        {
            model.TodayRestCount = RestLog.TodayCount();
            model.OnSettingsChange = Apply;
            model.OnRestNow = RestNow;
            model.OnRestartWork = RestartWork;
            model.OnQuit = Quit;
            tray = new TrayController(model);

            activity.Start(Tick);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            // 休息中拒绝注销和关机（系统会提示「歇一杯正在阻止关机」，用户仍可强制）
            Application.Current.SessionEnding += (_, e) =>
            {
                if (scheduler.IsResting || restOverlay.IsPresented)
                {
                    Log.Write("休息中，拒绝注销或关机");
                    e.Cancel = true;
                }
            };
            Application.Current.Exit += (_, _) => tray?.Dispose();

            timer.Start();
            Tick();
            Log.Write($"歇一杯已启动：可使用 {BreakFormat.Minutes(scheduler.Settings.WorkMinutes)}，休息 {BreakFormat.Minutes(scheduler.Settings.RestMinutes)}");
            if (firstRun) tray.ShowWelcome();
        }

        /// <summary>再次启动时由已在运行的实例弹出设置面板</summary>
        public void ShowPanel()
        {
            if (!scheduler.IsResting) tray?.Panel.ShowPanel();
        }

        private void Tick()
        {
            var now = DateTime.Now;
            var mediaState = media.CurrentState();
            if (!mediaState.Equals(lastMediaState))
            {
                Log.Write($"设备占用：{mediaState.Summary}");
                lastMediaState = mediaState;
            }
            var environment = new ActivityEnvironment(
                idleSeconds: activity.IdleSeconds(),
                mediaInUse: mediaState.InUse,
                screenLocked: activity.IsScreenLocked);
            scheduler.Tick(now, environment);
            Reconcile();
            model.Refresh(scheduler, mediaState, now);
            tray?.Refresh();
        }

        private void Reconcile()
        {
            switch (scheduler.Phase)
            {
                case BreakPhase.Warning warning:
                    if (!warningToast.IsShowing)
                    {
                        Log.Write($"预告开始，{Math.Round((warning.RestAt - DateTime.Now).TotalSeconds)} 秒后休息");
                    }
                    restOverlay.Prepare();
                    warningToast.Show(warning.RestAt, scheduler.Settings.RestMinutes);
                    break;
                case BreakPhase.Resting resting:
                    warningToast.Hide(immediate: true);
                    if (!overlayShownForRest)
                    {
                        overlayShownForRest = true;
                        tray?.Panel.HidePanel();
                        RestLog.Record();
                        model.TodayRestCount = RestLog.TodayCount();
                        Log.Write($"休息开始，至 {resting.Until:HH:mm:ss}");
                    }
                    restOverlay.Present(resting.StartedAt, resting.Until, scheduler.Settings);
                    break;
                default:
                    warningToast.Hide();
                    if (overlayShownForRest)
                    {
                        overlayShownForRest = false;
                        Log.Write("休息结束");
                    }
                    restOverlay.Dismiss();
                    break;
            }
        }

        private void Apply(BreakSettings settings)
        {
            var clean = settings.Sanitized();
            store.Save(clean);
            scheduler.Settings = clean;
            model.ReplaceSettings(clean);
            Tick();
        }

        private void RestNow()
        {
            scheduler.StartRestNow(DateTime.Now);
            Tick();
        }

        private void RestartWork()
        {
            scheduler.RestartWork(DateTime.Now);
            Tick();
        }

        private void Quit()
        {
            if (scheduler.IsResting || restOverlay.IsPresented) return;
            Log.Write("退出歇一杯");
            tray?.Dispose();
            tray = null;
            Application.Current.Shutdown();
        }

        /// <summary>系统切换深浅色、任务栏颜色或缩放比例后，面板配色和托盘图标跟着刷新</summary>
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dark = Theme.AppsUseDark;
                if (dark != Theme.IsDark) Theme.Apply(Application.Current.Resources, dark);
                tray?.Refresh(force: true);
            }));
        }
    }
}
