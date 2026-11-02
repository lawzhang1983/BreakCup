using System;
using System.IO;

namespace BreakCup.Core
{
    /// <summary>休息结束后，怎么让屏幕不再一直亮着内容</summary>
    public enum AfterRestAction
    {
        /// <summary>启动系统屏幕保护，唤醒要不要输密码按系统设置</summary>
        ScreenSaver,
        /// <summary>没设屏保时直接锁屏：人可能走开了，隐私优先</summary>
        Lock
    }

    /// <summary>
    /// 系统屏保设置，读自 HKCU\Control Panel\Desktop（组策略里的同名值优先）。
    /// 在「屏幕保护程序设置」里选「无」只会删掉 SCRNSAVE.EXE，ScreenSaveActive 往往仍是 1，
    /// 这时发 SC_SCREENSAVE 系统什么也不做，所以要看屏保程序是否真的存在。
    /// </summary>
    public sealed class ScreenSaverConfig
    {
        public ScreenSaverConfig(string? program, bool active, bool secure, bool programExists)
        {
            Program = string.IsNullOrWhiteSpace(program) ? null : program!.Trim();
            Active = active;
            Secure = secure;
            ProgramExists = programExists;
        }

        /// <summary>按注册表里的原始字符串解读：ScreenSaveActive 缺省算开，ScreenSaverIsSecure 只有 1 算要密码</summary>
        public static ScreenSaverConfig FromRegistry(string? program, string? active, string? secure, Func<string, bool> exists)
        {
            var hasProgram = !string.IsNullOrWhiteSpace(program);
            return new ScreenSaverConfig(
                program,
                active: active?.Trim() != "0",
                secure: secure?.Trim() == "1",
                programExists: hasProgram && exists(program!.Trim()));
        }

        /// <summary>SCRNSAVE.EXE 的原值，没设屏保时为 null</summary>
        public string? Program { get; }
        public bool Active { get; }
        /// <summary>「在恢复时显示登录屏幕」</summary>
        public bool Secure { get; }
        public bool ProgramExists { get; }

        public AfterRestAction Action => Program != null && Active && ProgramExists ? AfterRestAction.ScreenSaver : AfterRestAction.Lock;

        /// <summary>面板悬停说明和日志里的一句话</summary>
        public string Summary
        {
            get
            {
                if (Action == AfterRestAction.Lock) return "没设屏保，休息结束后直接锁屏，回来要输密码解锁";
                var name = Path.GetFileNameWithoutExtension(Program);
                return Secure
                    ? $"屏保 {name}，唤醒要输密码"
                    : $"屏保 {name}，唤醒不要求密码，动一下键鼠就能回到桌面";
            }
        }
    }
}
