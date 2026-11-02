using System;
using System.Collections.Generic;
using System.Linq;

namespace BreakCup.Core
{
    /// <summary>麦克风、摄像头当前被哪些应用占用</summary>
    public sealed class MediaState : IEquatable<MediaState>
    {
        public IReadOnlyList<string> MicrophoneUsers { get; }
        public IReadOnlyList<string> CameraUsers { get; }

        public MediaState(IEnumerable<string>? microphoneUsers = null, IEnumerable<string>? cameraUsers = null)
        {
            MicrophoneUsers = Distinct(microphoneUsers);
            CameraUsers = Distinct(cameraUsers);
        }

        public bool InUse => MicrophoneUsers.Count > 0 || CameraUsers.Count > 0;

        public string MicrophoneStatus => Status(MicrophoneUsers);
        public string CameraStatus => Status(CameraUsers);
        public string Summary => $"麦克风：{MicrophoneStatus} · 摄像头：{CameraStatus}";

        private static string Status(IReadOnlyList<string> users) =>
            users.Count > 0 ? $"{string.Join("、", users)} 使用中" : "空闲";

        private static IReadOnlyList<string> Distinct(IEnumerable<string>? names)
        {
            var result = new List<string>();
            foreach (var name in names ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name)) result.Add(name);
            }
            return result;
        }

        public bool Equals(MediaState? other) =>
            other != null && MicrophoneUsers.SequenceEqual(other.MicrophoneUsers) && CameraUsers.SequenceEqual(other.CameraUsers);

        public override bool Equals(object? obj) => Equals(obj as MediaState);

        public override int GetHashCode() => MicrophoneUsers.Count * 31 + CameraUsers.Count;
    }

    /// <summary>
    /// 解读系统「隐私 › 麦克风/摄像头」的使用记录（CapabilityAccessManager\ConsentStore）。
    /// 每个用过设备的应用一个子键，记着最近一次开始和结束使用的时间；正在用时结束时间为 0。
    /// 这和「设置」里麦克风图标、隐私指示灯用的是同一份数据，读它不需要任何权限，也不会真去打开设备。
    /// </summary>
    public static class ConsentStore
    {
        /// <summary>开始时间晚于结束时间（正在使用时结束时间为 0）就是正在使用</summary>
        public static bool IsInUse(long lastUsedStart, long lastUsedStop) =>
            lastUsedStart > 0 && lastUsedStart > lastUsedStop;

        /// <summary>非打包应用的子键名是把完整路径里的 \ 换成 # 的结果</summary>
        public static string ExecutablePath(string keyName) => keyName.Replace('#', '\\');

        /// <summary>
        /// 拿不到可执行文件描述时的兜底名称：
        /// 非打包应用取文件名，如 C:#Program Files#Tencent#WeMeet#wemeetapp.exe → wemeetapp；
        /// 打包应用取包族名里的应用名，如 Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe → WindowsSoundRecorder。
        /// </summary>
        public static string FallbackName(string keyName, bool nonPackaged)
        {
            if (nonPackaged)
            {
                var path = ExecutablePath(keyName);
                var file = path.Substring(path.LastIndexOf('\\') + 1);
                return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 4) : file;
            }
            var family = keyName.Split('_')[0];
            var dot = family.LastIndexOf('.');
            return dot >= 0 && dot < family.Length - 1 ? family.Substring(dot + 1) : family;
        }
    }
}
