using System;
using System.IO;
using System.Text;

namespace BreakCup.Platform
{
    /// <summary>
    /// 运行日志写到 %LOCALAPPDATA%\BreakCup\breakcup.log，超过 512 KB 轮换一次。
    /// 从命令行启动时同时打到控制台，便于观察阶段切换和设备占用变化。
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreakCup");

        public static string FilePath => Path.Combine(Folder, "breakcup.log");

        public static void Write(string message)
        {
            var line = $"[歇一杯 {DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    var file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > 512 * 1024)
                    {
                        var old = FilePath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                try
                {
                    Console.WriteLine(line);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
