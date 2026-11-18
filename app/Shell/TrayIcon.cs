using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace WinGCal.Shell;

// Tray icon and menu, as WinSqueeze (SPEC.md › Window › Tray). WinForms NotifyIcon runs fine on the WPF dispatcher
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon notifyIcon;
    readonly ToolStripMenuItem showItem = new("Show WinGCal"), pinItem = new("Pinned to the desktop"), skinMenu = new("Skin");
    readonly System.Windows.Forms.Timer dayTimer = new() { Interval = 30000 };
    DateTime drawnDay = DateTime.Today;
    App App => App.Instance;

    public TrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
        showItem.Click += (sender, args) => App.ToggleMainWindow();
        // The version, just the number, in a small shaded row at the top
        trayMenu.Items.Add(new ToolStripMenuItem(App.Updater.CurrentVersion) { Enabled = false, BackColor = Color.FromArgb(236, 236, 236), Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, System.Drawing.FontStyle.Regular) });
        trayMenu.Items.Add(showItem);
        trayMenu.Items.Add("Refresh from Google", null, async (sender, args) => await App.Store.SyncAsync());
        trayMenu.Items.Add(new ToolStripSeparator());
        pinItem.Click += (sender, args) => App.SetPinned(!App.Settings.Window.Pinned);
        trayMenu.Items.Add(pinItem);
        trayMenu.Items.Add(skinMenu);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Check for updates", null, async (sender, args) => { await App.Updater.CheckNowAsync(); if (App.Updater.ReadyVersion == "") ShowTip("WinGCal", App.Updater.StatusText); });
        trayMenu.Items.Add("Settings", null, (sender, args) => App.OpenSettings());
        trayMenu.Items.Add("Quit", null, (sender, args) => App.Quit());
        trayMenu.Opening += (sender, args) =>
        {
            showItem.Text = App.MainWindowShowing ? "Hide WinGCal" : "Show WinGCal";
            pinItem.Checked = App.Settings.Window.Pinned;
            skinMenu.DropDownItems.Clear();
            foreach (var (skin, label) in new[] { ("auto", "Glass (follow Windows)"), ("dark", "Glass dark"), ("light", "Glass light"), ("paper", "Paper"), ("technical", "Technical"), ("wall", "Wall calendar") })
                skinMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (sender, args) => App.SetSkin(skin)) { Checked = App.Settings.Look.Skin == skin });
        };
        notifyIcon = new NotifyIcon { Icon = CreateIcon(), Text = "WinGCal", ContextMenuStrip = trayMenu, Visible = true };
        notifyIcon.MouseClick += (sender, args) => { if (args.Button == MouseButtons.Left) App.ToggleMainWindow(); };
        // Redrawn when the taskbar changes colour or the screen scale changes, and at midnight for the new date
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += ThemeChanged;
        dayTimer.Tick += (sender, args) => { if (drawnDay != DateTime.Today) Redraw(); };
        dayTimer.Start();
    }

    public void ShowTip(string title, string text) => notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    // Line art drawn at the tray's own pixel size, in the taskbar's ink: a calendar leaf holding today's date
    static Icon CreateIcon()
    {
        int pixelSize = SystemInformation.SmallIconSize.Width; float unit = pixelSize / 16f;
        var inkColour = TaskbarIsLight() ? Color.FromArgb(28, 28, 28) : Color.White;
        using var bitmap = new Bitmap(pixelSize, pixelSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var outlinePen = new Pen(inkColour, 1.5f * unit) { LineJoin = LineJoin.Round })
        using (var ringPen = new Pen(inkColour, 1.7f * unit) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var inkBrush = new SolidBrush(inkColour))
        using (var leafPath = new GraphicsPath())
        using (var datePath = new GraphicsPath())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float left = 1.5f * unit, top = 3f * unit, width = 13f * unit, height = 11.75f * unit, corner = 5.2f * unit;
            leafPath.AddArc(left, top, corner, corner, 180, 90); leafPath.AddArc(left + width - corner, top, corner, corner, 270, 90);
            leafPath.AddArc(left + width - corner, top + height - corner, corner, corner, 0, 90); leafPath.AddArc(left, top + height - corner, corner, corner, 90, 90);
            leafPath.CloseFigure();
            graphics.DrawPath(outlinePen, leafPath);
            foreach (float ringX in new[] { 5f, 11f }) graphics.DrawLine(ringPen, ringX * unit, 1.2f * unit, ringX * unit, 4.6f * unit);
            // The date is scaled to a fixed box, so every number sits dead centre whatever the font's own spacing
            using var dateFamily = new FontFamily("Segoe UI");
            datePath.AddString(DateTime.Today.Day.ToString(), dateFamily, (int)System.Drawing.FontStyle.Bold, 20f, new PointF(0, 0), StringFormat.GenericTypographic);
            var drawnBox = datePath.GetBounds();
            float fitScale = Math.Min(5.6f * unit / drawnBox.Height, 8.6f * unit / drawnBox.Width);
            using var fitMatrix = new Matrix();
            fitMatrix.Translate(8f * unit - (drawnBox.X + drawnBox.Width / 2) * fitScale, 9.55f * unit - (drawnBox.Y + drawnBox.Height / 2) * fitScale); fitMatrix.Scale(fitScale, fitScale);
            datePath.Transform(fitMatrix);
            graphics.FillPath(inkBrush, datePath);
        }
        IntPtr iconHandle = bitmap.GetHicon();
        try { using var borrowedIcon = Icon.FromHandle(iconHandle); return (Icon)borrowedIcon.Clone(); }
        finally { DestroyIcon(iconHandle); }
    }

    // The taskbar has its own light or dark setting, apart from the one apps follow
    static bool TaskbarIsLight() => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is int lightFlag && lightFlag == 1;

    void Redraw() { var staleIcon = notifyIcon.Icon; drawnDay = DateTime.Today; notifyIcon.Icon = CreateIcon(); staleIcon?.Dispose(); }
    void ThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs args) => Redraw();

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr iconHandle);

    public void Dispose() { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= ThemeChanged; dayTimer.Dispose(); notifyIcon.Visible = false; notifyIcon.Dispose(); }
}
