using System;
using System.IO;
using BreakCup.Platform;
using Forms = System.Windows.Forms;

namespace BreakCup.UI
{
    /// <summary>托盘图标：左键、右键都弹出设置面板，和 Mac 版点菜单栏咖啡杯一样</summary>
    internal sealed class TrayController : IDisposable
    {
        private readonly Forms.NotifyIcon icon = new Forms.NotifyIcon();
        private readonly AppModel model;
        private readonly SettingsWindow panel;
        private TrayGlyph? shownGlyph;
        private int shownSize;
        private bool shownDark;
        private System.Drawing.Icon? current;

        public TrayController(AppModel model)
        {
            this.model = model;
            panel = new SettingsWindow(model);
            icon.Text = "歇一杯";
            icon.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left || e.Button == Forms.MouseButtons.Right) panel.Toggle();
            };
            Refresh();
            icon.Visible = true;
        }

        public SettingsWindow Panel => panel;

        public void Refresh(bool force = false)
        {
            var glyph = model.TrayGlyph;
            var size = Forms.SystemInformation.SmallIconSize.Width;
            var dark = Theme.TaskbarIsDark;
            if (force || !shownGlyph.HasValue || !shownGlyph.Value.Equals(glyph) || size != shownSize || dark != shownDark)
            {
                shownGlyph = glyph;
                shownSize = size;
                shownDark = dark;
                var next = MakeIcon(IconArt.Tray(glyph, size, dark), size);
                icon.Icon = next;
                current?.Dispose();
                current = next;
            }
            var tip = model.TrayTooltip;
            // 托盘提示最多 63 个字符
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (icon.Text != tip) icon.Text = tip;
        }

        public void ShowWelcome()
        {
            icon.ShowBalloonTip(8000, "歇一杯已在托盘里运行", "点托盘里的咖啡杯打开设置。图标收在「^」里的话，可以把它拖到任务栏上常驻。", Forms.ToolTipIcon.None);
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            current?.Dispose();
            panel.Close();
        }

        private static System.Drawing.Icon MakeIcon(System.Windows.Media.Imaging.BitmapSource image, int size)
        {
            using var stream = new MemoryStream(IconArt.Ico(new[] { image }));
            return new System.Drawing.Icon(stream, size, size);
        }
    }
}
