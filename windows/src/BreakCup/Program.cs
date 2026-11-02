using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using BreakCup.Platform;
using BreakCup.UI;
using Microsoft.Win32;

namespace BreakCup
{
    internal static class Program
    {
        private const string MutexName = @"Local\BreakCup.SingleInstance";
        private const string ShowEventName = @"Local\BreakCup.ShowPanel";

        [STAThread]
        private static int Main(string[] args)
        {
            // 从命令行启动时把输出接回控制台，便于看日志和截图结果
            Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);

            // `--snapshot <目录>`：把休息屏、预告条、设置面板、托盘图标渲染成 PNG，用于界面验收，不启动托盘应用
            var snapshot = Array.IndexOf(args, "--snapshot");
            if (snapshot >= 0)
            {
                var folder = snapshot + 1 < args.Length ? args[snapshot + 1] : Environment.CurrentDirectory;
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SnapshotRenderer.Run(Path.GetFullPath(folder));
                app.Shutdown();
                return 0;
            }

            // `--make-icon <文件>`：生成应用图标 .ico（构建脚本用）
            var makeIcon = Array.IndexOf(args, "--make-icon");
            if (makeIcon >= 0 && makeIcon + 1 < args.Length)
            {
                var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 256 };
                File.WriteAllBytes(args[makeIcon + 1], IconArt.Ico(sizes.Select(IconArt.App)));
                Console.WriteLine($"icon -> {args[makeIcon + 1]}");
                return 0;
            }

            using var mutex = new Mutex(true, MutexName, out var isFirst);
            if (!isFirst)
            {
                // 已经在运行：让那个实例弹出设置面板，免得托盘图标被收起来时以为没反应
                Log.Write("已有实例在运行，本实例退出");
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
                return 0;
            }

            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(application.Resources, Theme.AppsUseDark);
            application.DispatcherUnhandledException += (_, e) =>
            {
                Log.Write($"未处理的异常：{e.Exception}");
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"未处理的异常：{e.ExceptionObject}");

            var controller = new AppController();
            using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var wait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) =>
                application.Dispatcher.BeginInvoke(new Action(controller.ShowPanel)), null, Timeout.Infinite, false);

            application.Startup += (_, _) => controller.Start(FirstRun());
            application.Run();
            wait.Unregister(null);
            GC.KeepAlive(mutex);
            return 0;
        }

        /// <summary>第一次运行时弹一条通知，告诉用户图标在托盘里</summary>
        private static bool FirstRun()
        {
            using var key = Registry.CurrentUser.CreateSubKey(Core.SettingsStore.DefaultKeyPath);
            if (key.GetValue("welcomed") != null) return false;
            key.SetValue("welcomed", 1, RegistryValueKind.DWord);
            return true;
        }
    }
}
