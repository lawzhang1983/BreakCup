using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BreakCup.Core;
using Microsoft.Win32;

namespace BreakCup.Platform
{
    /// <summary>
    /// 读系统隐私记录判断麦克风、摄像头是否正被占用（判开会）。
    /// 不打开设备、不申请任何权限，只看「设置 › 隐私和安全性」背后的使用时间戳。
    /// </summary>
    internal sealed class MediaUsageMonitor
    {
        private const string ConsentStorePath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool loggedFailure;

        public MediaState CurrentState()
        {
            try
            {
                return new MediaState(Users("microphone"), Users("webcam"));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    Log.Write($"读取麦克风/摄像头占用失败：{ex.Message}");
                }
                return new MediaState();
            }
        }

        private List<string> Users(string capability)
        {
            var users = new List<string>();
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var root = hive.OpenSubKey(ConsentStorePath + capability);
                if (root == null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(name);
                    if (key == null) continue;
                    if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var app in key.GetSubKeyNames())
                        {
                            using var appKey = key.OpenSubKey(app);
                            if (appKey != null && InUse(appKey)) users.Add(DisplayName(app, nonPackaged: true));
                        }
                    }
                    else if (InUse(key))
                    {
                        users.Add(DisplayName(name, nonPackaged: false));
                    }
                }
            }
            return users;
        }

        private static bool InUse(RegistryKey key) =>
            ConsentStore.IsInUse(ToLong(key.GetValue("LastUsedTimeStart")), ToLong(key.GetValue("LastUsedTimeStop")));

        private static long ToLong(object? value) => value switch
        {
            long l => l,
            int i => i,
            _ => 0
        };

        /// <summary>非打包应用优先用可执行文件的描述（如「腾讯会议」），取不到再退回文件名</summary>
        private string DisplayName(string keyName, bool nonPackaged)
        {
            if (names.TryGetValue(keyName, out var cached)) return cached;
            var name = ConsentStore.FallbackName(keyName, nonPackaged);
            if (nonPackaged)
            {
                try
                {
                    var description = FileVersionInfo.GetVersionInfo(ConsentStore.ExecutablePath(keyName)).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
                }
                catch (FileNotFoundException)
                {
                }
                catch (ArgumentException)
                {
                }
            }
            names[keyName] = name;
            return name;
        }
    }
}
