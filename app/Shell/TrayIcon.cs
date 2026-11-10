using System.Drawing;
using System.Windows.Forms;
namespace WinGCal.Shell;

// Tray icon and menu, as WinSqueeze (SPEC.md › Window › Tray). WinForms NotifyIcon runs fine on the WPF dispatcher
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon notifyIcon;
    readonly ToolStripMenuItem showItem = new("Show WinGCal"), pinItem = new("Pinned to the desktop"), skinMenu = new("Skin");
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
    }

    public void ShowTip(string title, string text) => notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    static Icon CreateIcon()
    {
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinGCal.ico")).Stream;
        return new Icon(iconStream, SystemInformation.SmallIconSize);
    }

    public void Dispose() { notifyIcon.Visible = false; notifyIcon.Dispose(); }
}
