using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace WinGCal.Shell;

// Tray icon and menu, as WinSqueeze (SPEC.md › Window › Tray), with WinSwitch's dots (SPEC.md › Window › Tray dots). WinForms NotifyIcon runs fine on the WPF dispatcher
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon notifyIcon;
    readonly ContextMenuStrip trayMenu = new();
    readonly ToolStripMenuItem showItem = new("Show WinGCal"), pinItem = new("Pinned to the desktop"), skinMenu = new("Skin");
    readonly List<ToolStripMenuItem> dotRows = new(); // Update and next event rows, rebuilt each time the menu opens
    ToolStripMenuItem? updateRow, eventRow;
    readonly Image blueDotImage = DotImage(NoticeCard.UpdateColour), amberDotImage = DotImage(NoticeCard.MessageColour), noDotImage = new Bitmap(64, 64);
    readonly Dictionary<string, Icon> drawnIcons = new(); // one per look (date, size, ink, dot), so a flash only swaps icons
    readonly System.Windows.Forms.Timer stateTimer = new() { Interval = 1000 }, flashTimer = new() { Interval = 900 };
    bool flashOn = true, menuOpen;
    string seenUpdate = "", shownIconKey = ""; // the ready version already explained: it flashes until then, then stays solid blue
    App App => App.Instance;
    bool UpdateDot => App.Settings.General.TrayUpdateDot && App.Updater.ReadyVersion != "";
    bool UpdateUnseen => UpdateDot && seenUpdate != App.Updater.ReadyVersion;
    Google.UiEvent? ComingEvent => App.Settings.General.TrayEventDot ? App.Alerts.Upcoming : null;

    public TrayIcon()
    {
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
            // The same dots as the icon, straight under the version, so a dot always has its row
            foreach (var staleRow in dotRows) { trayMenu.Items.Remove(staleRow); staleRow.Dispose(); }
            dotRows.Clear(); updateRow = eventRow = null;
            if (App.Updater.ReadyVersion != "") dotRows.Add(updateRow = new ToolStripMenuItem($"Update WinGCal to {App.Updater.ReadyVersion}", blueDotImage, async (sender, args) => await App.Updater.RestartToUpdateAsync()));
            if (ComingEvent is { } coming) dotRows.Add(eventRow = new ToolStripMenuItem(Shorten(ComingLine(coming), 60), amberDotImage, (sender, args) => App.Alerts.ShowNow()));
            for (var rowIndex = 0; rowIndex < dotRows.Count; rowIndex++) trayMenu.Items.Insert(1 + rowIndex, dotRows[rowIndex]);
        };
        // Bold rows get their font once the menu is on its screen, from the menu's own font: fonts set earlier keep the main screen's scaling
        trayMenu.Opened += (sender, args) => { menuOpen = true; var boldFont = new Font(trayMenu.Font, System.Drawing.FontStyle.Bold); showItem.Font = boldFont; foreach (var dotRow in dotRows) dotRow.Font = boldFont; Refresh(); };
        trayMenu.Closed += (sender, args) => { menuOpen = false; if (updateRow != null) seenUpdate = App.Updater.ReadyVersion; Refresh(); }; // seen in the menu: the blue dot stops flashing
        notifyIcon = new NotifyIcon { Text = "WinGCal", ContextMenuStrip = trayMenu, Visible = true };
        // Left-click explains a dot first: amber brings the countdown capsule, blue the update card. With no dot, or its explanation already up, it shows or hides the calendar as ever
        notifyIcon.MouseClick += (sender, args) =>
        {
            if (args.Button != MouseButtons.Left) return;
            if (ComingEvent != null && !App.Alerts.Showing) { App.Alerts.ShowNow(); return; }
            if (UpdateDot && !NoticeCard.IsShowing) { var firstLook = UpdateUnseen; ShowUpdateCard(true); if (firstLook) return; } // a solid blue dot still explains itself, and the calendar toggles too
            App.ToggleMainWindow();
        };
        App.Updater.Changed += Refresh;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += ThemeChanged; // light or dark taskbar
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += ScreenChanged; // taskbar scaling
        stateTimer.Tick += (sender, args) => Refresh(); stateTimer.Start(); // midnight's new date, an event coming into its lead time or starting
        flashTimer.Tick += (sender, args) =>
        {
            flashOn = !flashOn; flashTimer.Interval = flashOn ? 900 : 500;
            if (menuOpen) { if (updateRow != null) { updateRow.Image = flashOn ? blueDotImage : noDotImage; updateRow.Invalidate(); } if (eventRow != null) { eventRow.Image = flashOn ? amberDotImage : noDotImage; eventRow.Invalidate(); } }
            Refresh();
        };
        Refresh();
    }

    public void ShowTip(string title, string text) => notifyIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    // "Update WinGCal now", never "Restart" (which reads as restarting Windows)
    public void ShowUpdateCard(bool asked)
    {
        if (asked) seenUpdate = App.Updater.ReadyVersion;
        NoticeCard.Show("Update ready", $"WinGCal {App.Updater.ReadyVersion} is downloaded." + (App.Settings.General.TrayUpdateDot ? " The blue dot stays until you update." : ""), "Update WinGCal now", () => _ = App.Updater.RestartToUpdateAsync(), NoticeCard.UpdateColour, asked);
        Refresh();
    }

    static string ComingLine(Google.UiEvent coming)
    {
        var minutesLeft = Math.Max(1, (int)Math.Ceiling((NextAlert.StartOf(coming) - DateTime.Now).TotalMinutes));
        return (minutesLeft < 90 ? $"In {minutesLeft} min" : $"In {minutesLeft / 60.0:0.#} hr") + ": " + coming.Title;
    }
    static string Shorten(string text, int length) => text.Length > length ? text[..(length - 1)] + "…" : text;

    // Hover text and the icon. One dot: amber while an event is coming (it always flashes, and goes at the start time), else blue for an update (flashing until seen, then solid)
    void Refresh()
    {
        if (!App.Dispatcher.CheckAccess()) { App.Dispatcher.BeginInvoke(Refresh); return; }
        var coming = ComingEvent; var updateDot = UpdateDot;
        // Whatever a dot means comes first in the hover text, with its dot; Windows caps the text at 127 characters
        var hoverText = "WinGCal" + (coming != null ? "\n🟠 " + Shorten(ComingLine(coming), 60) : "") + (App.Updater.ReadyVersion != "" ? $"\n🔵 Update ready: {App.Updater.ReadyVersion}" : "");
        if (notifyIcon.Text != hoverText) notifyIcon.Text = Shorten(hoverText, 127);
        var flashing = coming != null || UpdateUnseen || menuOpen && dotRows.Count > 0;
        if (flashing != flashTimer.Enabled) { flashOn = true; flashTimer.Interval = 900; if (flashing) flashTimer.Start(); else flashTimer.Stop(); }
        var dotColour = coming != null ? (flashOn ? NoticeCard.MessageColour : "") : updateDot ? (!UpdateUnseen || flashOn ? NoticeCard.UpdateColour : "") : "";
        int pixelSize = TrayIconSize(); bool lightTaskbar = TaskbarIsLight();
        var lookKey = $"{DateTime.Today.Day}|{pixelSize}|{lightTaskbar}|"; var iconKey = lookKey + dotColour;
        if (iconKey == shownIconKey) return;
        if (!shownIconKey.StartsWith(lookKey) && drawnIcons.Count > 0) { notifyIcon.Icon = null; foreach (var staleIcon in drawnIcons.Values) staleIcon.Dispose(); drawnIcons.Clear(); } // a new day, scale or taskbar colour: the old drawings are no use
        if (!drawnIcons.TryGetValue(iconKey, out var icon)) drawnIcons[iconKey] = icon = DrawIcon(pixelSize, lightTaskbar, dotColour);
        shownIconKey = iconKey; notifyIcon.Icon = icon;
    }
    void ThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs args) => Refresh();
    void ScreenChanged(object? sender, EventArgs args) => Refresh();

    // Drawn at the tray's own pixel size, in the taskbar's ink, on whole pixels so it is as crisp as Windows' own: a calendar leaf that is the whole square slot, edge to edge, in an outline as thin as Windows' battery icon, with a thin rule under the top margin and today's date as big as fits under it; the dot takes the top right corner
    static Icon DrawIcon(int pixelSize, bool lightTaskbar, string dotColour)
    {
        float unit = pixelSize / 16f;
        var inkColour = lightTaskbar ? Color.FromArgb(28, 28, 28) : Color.White;
        using var bitmap = new Bitmap(pixelSize, pixelSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var inkBrush = new SolidBrush(inkColour))
        using (var leafPath = new GraphicsPath())
        using (var insidePath = new GraphicsPath())
        using (var datePath = new GraphicsPath())
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int edgePixels = Math.Max(1, (int)Math.Round(unit)), rulePixels = Math.Max(edgePixels + 1, (int)Math.Round(4f * unit) - 1), bandPixels = rulePixels + edgePixels; // the rule sits rulePixels down; the date's room starts under it
            float outerCorner = 6.4f * unit, innerCorner = Math.Max(1f, outerCorner - edgePixels * 2);
            AddRoundBox(leafPath, 0, 0, pixelSize, pixelSize, outerCorner);
            graphics.FillPath(inkBrush, leafPath);
            // The inside is cleared again, leaving just the thin edge, then the rule is laid across
            AddRoundBox(insidePath, edgePixels, edgePixels, pixelSize - edgePixels * 2, pixelSize - edgePixels * 2, innerCorner);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            using (var clearBrush = new SolidBrush(Color.Transparent)) graphics.FillPath(clearBrush, insidePath);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.SmoothingMode = SmoothingMode.None; graphics.FillRectangle(inkBrush, edgePixels, rulePixels, pixelSize - edgePixels * 2, edgePixels); graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // The date is scaled to a fixed box, so every number sits dead centre whatever the font's own spacing
            using var dateFamily = DateFamily();
            datePath.AddString(DateTime.Today.Day.ToString(), dateFamily, (int)System.Drawing.FontStyle.Regular, 20f, new PointF(0, 0), StringFormat.GenericTypographic);
            var drawnBox = datePath.GetBounds();
            float roomTop = bandPixels + 1.3f * unit, roomBottom = pixelSize - edgePixels - 1.5f * unit;
            float fitScale = Math.Min((roomBottom - roomTop) / drawnBox.Height, (pixelSize - edgePixels * 2 - 3f * unit) / drawnBox.Width);
            using var fitMatrix = new Matrix();
            fitMatrix.Translate(pixelSize / 2f - (drawnBox.X + drawnBox.Width / 2) * fitScale, (roomTop + roomBottom) / 2 - (drawnBox.Y + drawnBox.Height / 2) * fitScale); fitMatrix.Scale(fitScale, fitScale);
            datePath.Transform(fitMatrix);
            graphics.FillPath(inkBrush, datePath);
            if (dotColour != "")
            {
                // A clear ring is cut around the dot so it reads on the line art
                float dotX = 13.3f * unit, dotY = 2.7f * unit, dotRadius = 2.7f * unit, gapRadius = 3.4f * unit;
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using (var clearBrush = new SolidBrush(Color.Transparent)) graphics.FillEllipse(clearBrush, dotX - gapRadius, dotY - gapRadius, gapRadius * 2, gapRadius * 2);
                graphics.CompositingMode = CompositingMode.SourceOver;
                using var dotBrush = new SolidBrush(ColorTranslator.FromHtml(dotColour));
                graphics.FillEllipse(dotBrush, dotX - dotRadius, dotY - dotRadius, dotRadius * 2, dotRadius * 2);
            }
        }
        IntPtr iconHandle = bitmap.GetHicon();
        try { using var borrowedIcon = Icon.FromHandle(iconHandle); return (Icon)borrowedIcon.Clone(); }
        finally { DestroyIcon(iconHandle); }
    }
    // Semibold reads at tray size without the weight of bold
    static FontFamily DateFamily() { try { return new FontFamily("Segoe UI Semibold"); } catch { return new FontFamily("Segoe UI"); } }
    static void AddRoundBox(GraphicsPath path, float left, float top, float width, float height, float corner)
    {
        path.AddArc(left, top, corner, corner, 180, 90); path.AddArc(left + width - corner, top, corner, corner, 270, 90);
        path.AddArc(left + width - corner, top + height - corner, corner, corner, 0, 90); path.AddArc(left, top + height - corner, corner, corner, 90, 90);
        path.CloseFigure();
    }

    static Image DotImage(string colour)
    {
        var dotBitmap = new Bitmap(64, 64); // large and scaled down by the menu, so it stays sharp at any scaling
        using var graphics = Graphics.FromImage(dotBitmap); graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var dotBrush = new SolidBrush(ColorTranslator.FromHtml(colour)); graphics.FillEllipse(dotBrush, 8, 8, 48, 48);
        return dotBitmap;
    }

    // The size the taskbar shows tray icons at, from the taskbar's own scaling. SystemInformation.SmallIconSize is fixed at the scaling Windows had at sign-in, so after a dock change the icon would be drawn too small and blown up (fuzzy)
    static int TrayIconSize()
    {
        try { var taskbar = FindWindow("Shell_TrayWnd", null); var taskbarDpi = taskbar == IntPtr.Zero ? 0u : GetDpiForWindow(taskbar); if (taskbarDpi > 0) return GetSystemMetricsForDpi(49 /* SM_CXSMICON */, taskbarDpi); } catch { }
        return SystemInformation.SmallIconSize.Width;
    }
    // The taskbar has its own light or dark setting, apart from the one apps follow
    static bool TaskbarIsLight() { try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is int lightFlag && lightFlag == 1; } catch { return false; } }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr iconHandle);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr FindWindow(string className, string? windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);

    public void Dispose()
    {
        App.Updater.Changed -= Refresh; Microsoft.Win32.SystemEvents.UserPreferenceChanged -= ThemeChanged; Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= ScreenChanged;
        stateTimer.Dispose(); flashTimer.Dispose(); notifyIcon.Visible = false; notifyIcon.Dispose();
        foreach (var drawnIcon in drawnIcons.Values) drawnIcon.Dispose();
    }
}
