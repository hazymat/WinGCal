using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WinGCal.Google;
using WinGCal.Settings;
using WinGCal.Shell;
namespace WinGCal;

public partial class MainWindow : Window
{
    const string uiHost = "app.wingcal"; // virtual host mapped to wwwroot
    bool webViewReady;
    App App => App.Instance;
    static readonly JsonSerializerOptions camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    DesktopPin? pin;
    FrostPanels? frost;
    readonly DispatcherTimer placementSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer displaysSettleTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    CancellationTokenSource? signInCancel;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (sender, args) => await InitWebViewAsync();
        SourceInitialized += (sender, args) => { pin = new DesktopPin(new WindowInteropHelper(this).Handle); frost = new FrostPanels(this); RestorePlacement(); ApplyWindowStyle(); ApplyPinned(); };
        Activated += (sender, args) => { OnFirstActivation(); if (App.Settings.Window.Pinned) pin?.SendToBottom(); };
        StateChanged += (sender, args) => { UpdateFrameMargin(); PushWindowState(); placementSaveTimer.Stop(); placementSaveTimer.Start(); };
        LocationChanged += (sender, args) => { if (!placing) { placementSaveTimer.Stop(); placementSaveTimer.Start(); } };
        SizeChanged += (sender, args) => { if (!placing) { placementSaveTimer.Stop(); placementSaveTimer.Start(); } };
        placementSaveTimer.Tick += (sender, args) => { placementSaveTimer.Stop(); SavePlacement(); };
        // Plug, unplug, dock, resolution or scaling change: wait for Windows to settle, then go where this setup had it (SPEC.md › Monitor setups)
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (sender, args) => Dispatcher.BeginInvoke(() => { placementSaveTimer.Stop(); displaysSettleTimer.Stop(); displaysSettleTimer.Start(); });
        displaysSettleTimer.Tick += (sender, args) => { displaysSettleTimer.Stop(); RestorePlacement(); };
        App.StatusChanged += () => Push(JsonSerializer.Serialize(new { type = "update", version = App.Updater.ReadyVersion }));
        App.Tip += (title, text) => Push(JsonSerializer.Serialize(new { type = "tip", title, text }));
        App.Store.Changed += () => Dispatcher.BeginInvoke(PushCalendars);
        App.Store.Tasks.Changed += () => Dispatcher.BeginInvoke(PushTasks);
        // WebView2 forwards accelerator keys to WPF as KeyDown
        PreviewKeyDown += (sender, keyArgs) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            switch (keyArgs.Key)
            {
                case Key.Q: keyArgs.Handled = true; App.Quit(); break;
                case Key.OemComma: keyArgs.Handled = true; App.OpenSettings(); break;
                case Key.R: keyArgs.Handled = true; _ = App.Store.SyncAsync(); break;
            }
        };
    }

    async Task InitWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataFolder, "WebView2"));
        await webView.EnsureCoreWebView2Async(environment);
        webView.DefaultBackgroundColor = glassActive || App.Settings.Look.Frameless ? System.Drawing.Color.Transparent : System.Drawing.Color.FromArgb(16, 17, 20);
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(uiHost, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        webView.CoreWebView2.WebMessageReceived += async (sender, args) => await OnUiMessageAsync(args.WebMessageAsJson);
        // Links (maps, video calls) open in the default browser, never inside the widget
        webView.CoreWebView2.NewWindowRequested += (sender, args) => { args.Handled = true; OpenUrl(args.Uri); };
        webView.CoreWebView2.NavigationStarting += (sender, args) => { if (!args.Uri.StartsWith($"https://{uiHost}/")) { args.Cancel = true; OpenUrl(args.Uri); } };
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        webView.CoreWebView2.ContextMenuRequested += (sender, args) => { if (!args.ContextMenuTarget.IsEditable) args.Handled = true; };
        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        ApplyUiScale();
        webView.CoreWebView2.NavigationCompleted += (sender, args) => webViewReady = true;
        webView.CoreWebView2.ProcessFailed += (sender, args) => { CrashReporter.Write($"WebView2 process failed: {args.ProcessFailedKind} ({args.Reason})", null); if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) webView.CoreWebView2.Reload(); };
        webView.CoreWebView2.Navigate($"https://{uiHost}/index.html");
    }

    void Push(string messageJson) { if (webViewReady) Dispatcher.BeginInvoke(() => webView.CoreWebView2?.PostWebMessageAsJson(messageJson)); }
    void Reply(JsonElement message, object? data) { if (message.TryGetProperty("requestId", out var requestId)) Push($"{{\"type\":\"result\",\"id\":{requestId.GetInt64()},\"data\":{JsonSerializer.Serialize(data, camel)}}}"); }
    static string Text(JsonElement message, string name) => message.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText() : "";
    static void OpenUrl(string url) { if (url.StartsWith("https://") || url.StartsWith("http://") || url.StartsWith("mailto:")) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }

    async Task OnUiMessageAsync(string messageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var message = document.RootElement;
            switch (Text(message, "type"))
            {
                case "ready": webViewReady = true; _ = ReapplyLookSoonAsync(); PushSkin(); PushWindowState(); PushPrefs(); PushCalendars(); PushTasks(); _ = App.ShowWhatsNewAsync(); break;
                case "openSettings": App.OpenSettings(Text(message, "tab")); break;
                case "refresh": await App.Store.SyncAsync(); break;
                case "addAccount": await AddAccountAsync(); break;
                case "showCalendar": App.Store.SetShown(Text(message, "key"), message.GetProperty("shown").GetBoolean()); App.NotifySettingsChanged(); break;
                case "setPinned": App.Settings.Window.Pinned = message.GetProperty("on").GetBoolean(); App.Settings.Save(); ApplyPinned(); App.NotifySettingsChanged(); break;
                case "editEvent": if (App.Store.FindEvent(Text(message, "id")) is { Link.Length: > 0 } toEdit) OpenUrl(toEdit.Link); break; // editing in the widget comes in phase 3; Google Calendar opens on the event meanwhile
                case "deleteEvent": await DeleteEventAsync(Text(message, "id")); break;
                case "taskDone": await TaskActionAsync(() => App.Store.Tasks.SetCompletedAsync(Text(message, "id"), message.GetProperty("done").GetBoolean())); break;
                case "taskAdd": await TaskActionAsync(() => App.Store.Tasks.AddAsync(Text(message, "listKey"), Text(message, "title"))); break;
                case "taskDelete": await TaskActionAsync(() => App.Store.Tasks.DeleteAsync(Text(message, "id"))); break;
                case "showTaskList": App.Store.Tasks.SetShown(Text(message, "key"), message.GetProperty("shown").GetBoolean()); App.NotifySettingsChanged(); break;
                case "applyUpdate": await App.Updater.RestartToUpdateAsync(); break;
                case "frostPanels": frost?.Update(message.GetProperty("panels").EnumerateArray().Select(panel => new FrostPanels.Panel(panel.GetProperty("x").GetInt32(), panel.GetProperty("y").GetInt32(), panel.GetProperty("width").GetInt32(), panel.GetProperty("height").GetInt32())).ToList()); break;
                case "edgeColour": SetEdgeColour(Text(message, "colour")); break;
                case "resizeWindow": if (!App.Settings.Window.Pinned && resizeHitCodes.TryGetValue(Text(message, "edge"), out var hitCode)) { ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)hitCode, IntPtr.Zero); } break;
                case "dragWindow": if (!App.Settings.Window.Pinned) { ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero); webView.Focus(); } break;
                case "windowControl":
                    if (App.Settings.Window.Pinned) break;
                    switch (Text(message, "action")) { case "minimise": WindowState = WindowState.Minimized; break; case "maximise": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break; case "close": Close(); break; }
                    break;
                case "theme": darkTheme = message.TryGetProperty("dark", out var darkValue) && darkValue.GetBoolean(); ApplyWindowStyle(); break;
            }
        }
        catch (Exception messageError) { CrashReporter.Write("UI message", messageError, false); }
    }

    public async Task AddAccountAsync()
    {
        if (!GoogleAuth.HasClient) { App.ShowTip("Google sign-in isn't set up yet", "This build doesn't have WinGCal's Google sign-in key yet. It comes with a later update."); return; }
        signInCancel?.Cancel(); signInCancel = new CancellationTokenSource();
        try { var email = await App.Store.AddAccountAsync(signInCancel.Token); App.NotifySettingsChanged(); var syncError = App.Store.AccountStates.TryGetValue(email, out var state) ? state.Error : ""; if (syncError.Length == 0 && App.Store.Calendars.Any(calendar => calendar.Account == email)) App.ShowTip("Signed in", $"{email} is added. Choose which of its calendars show with the calendar button at the top."); }
        catch (OperationCanceledException) { }
        catch (Exception signInError) { App.ShowTip("Sign-in didn't work", signInError.Message); }
    }

    async Task DeleteEventAsync(string uiEventId)
    {
        if (App.Store.FindEvent(uiEventId) is not { } toDelete) return;
        if (MessageBox.Show(this, $"Delete “{toDelete.Title}”{(toDelete.Recurring ? " (this one only)" : "")}?", "WinGCal", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try { await App.Store.DeleteEventAsync(uiEventId); }
        catch (Exception deleteError) { App.ShowTip("Couldn't delete it", deleteError.Message); }
    }

    async Task TaskActionAsync(Func<Task> action) { try { await action(); } catch (Exception taskError) { App.ShowTip("Google Tasks didn't take that", taskError.Message); PushTasks(); } }

    // ---- To the page ----
    public void PushTasks()
    {
        var tasks = App.Store.Tasks;
        Push(JsonSerializer.Serialize(new { type = "tasks", enabled = App.Settings.Tasks.Enabled, lists = tasks.Lists.Select(list => new { key = list.Key, account = list.Account, name = list.Name, shown = tasks.IsShown(list.Key) }), items = tasks.Items.Select(item => new { id = item.Id, listKey = item.ListKey, title = item.Title, notes = item.Notes, due = item.Due, completed = item.Completed, completedAt = item.CompletedAt, position = item.Position, parent = item.Parent }) }, camel));
    }

    void PushCalendars()
    {
        var store = App.Store;
        var accounts = App.Settings.Sync.Accounts.Select(email => store.AccountStates.TryGetValue(email, out var state) ? new { email, lastSync = state.LastSync == DateTime.MinValue ? "" : state.LastSync.ToString("o"), error = state.Error } : new { email, lastSync = "", error = "" });
        var calendars = store.Calendars.Select(calendar => new { key = calendar.Key, account = calendar.Account, name = calendar.Name, colour = store.ColourOf(calendar), shown = store.IsShown(calendar.Key), primary = calendar.Primary, readOnly = calendar.ReadOnly });
        Push(JsonSerializer.Serialize(new { type = "calendars", accounts, calendars }, camel));
        var showDeclined = App.Settings.View.ShowDeclined;
        Push(JsonSerializer.Serialize(new { type = "events", events = store.EventsByCalendar.Values.SelectMany(list => list).Select(item => new { id = item.Id, calendarKey = item.CalendarKey, title = item.Title, start = item.Start, end = item.End, allDay = item.AllDay, location = item.Location, description = item.Description, meetUrl = item.MeetUrl, recurring = item.Recurring, colour = item.Colour, declined = item.Declined && !showDeclined }) }, camel));
        Push(JsonSerializer.Serialize(new { type = "sync", syncing = store.Syncing, offline = store.Offline }));
    }

    public void PushPrefs()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
        var view = App.Settings.View;
        Push(JsonSerializer.Serialize(new { type = "prefs", weekStart = view.WeekStart >= 0 ? view.WeekStart : (int)culture.FirstDayOfWeek, hour12 = view.Clock == "auto" ? culture.ShortTimePattern.Contains('h') : view.Clock == "12" }));
        PushCalendars();
    }

    public void PushWhatsNew(string fromVersion, string toVersion, List<ReleaseEntry> releases) => Push(JsonSerializer.Serialize(new { type = "whatsNew", fromVersion, toVersion, releases = releases.Select(release => new { version = release.Version, date = release.Published.ToString("d MMM"), notes = release.Notes, url = release.Url }) }));
    public void PushSkin() => Push(JsonSerializer.Serialize(new { type = "skin", skin = App.Settings.Look.Skin, accent = App.Settings.Look.AccentColour, glassTransparency = App.Settings.Look.GlassTransparency, glassTintHue = App.Settings.Look.GlassTintHue, windowTint = App.Settings.Look.WindowTint, panelTint = App.Settings.Look.PanelTint }));
    bool clearActive;
    void PushWindowState() => Push(JsonSerializer.Serialize(new { type = "window", glass = glassActive, clear = clearActive, frameless = App.Settings.Look.Frameless, maximised = WindowState == WindowState.Maximized, pinned = App.Settings.Window.Pinned }));

    // ---- Pin (SPEC.md › Window › Pin) ----
    public void ApplyPinned()
    {
        var pinned = App.Settings.Window.Pinned;
        if (pinned && WindowState != WindowState.Normal) { placing = true; WindowState = WindowState.Normal; placing = false; }
        pin?.Apply(pinned);
        ApplyWindowStyle(); // no resize border while pinned
        PushWindowState();
    }

    // ---- Monitor setups (SPEC.md › Monitor setups): place, size and scale per set of connected monitors, in physical pixels ----
    bool placing;
    List<Shell.Monitor> monitors = new();
    public string CurrentSetupKey => MonitorSetup.KeyOf(monitors.Count > 0 ? monitors : MonitorSetup.Current());
    SetupPlacement? CurrentPlacement => App.Settings.Window.Setups.TryGetValue(CurrentSetupKey, out var placement) ? placement : null;
    public double UiScale
    {
        get => CurrentPlacement?.UiScale ?? NearestPlacement()?.UiScale ?? 1;
        set { SavePlacement(); if (CurrentPlacement is { } placement) { placement.UiScale = value; App.Settings.Save(); } ApplyUiScale(); }
    }

    public void SavePlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || placing || WindowState == WindowState.Minimized) return;
        if (monitors.Count == 0) monitors = MonitorSetup.Current();
        GetWindowRect(handle, out var rect);
        var maximised = WindowState == WindowState.Maximized;
        if (maximised) { var normal = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() }; GetWindowPlacement(handle, ref normal); rect = normal.NormalPosition; }
        if (rect.Right - rect.Left < 100 || rect.Bottom - rect.Top < 80) return;
        // The monitor holding the window's centre (or the nearest one)
        var centreX = (rect.Left + rect.Right) / 2; var centreY = (rect.Top + rect.Bottom) / 2;
        var monitor = monitors.FirstOrDefault(candidate => candidate.Bounds.Contains(centreX, centreY)) ?? monitors.OrderBy(candidate => Math.Abs(candidate.Bounds.X + candidate.Bounds.Width / 2 - centreX) + Math.Abs(candidate.Bounds.Y + candidate.Bounds.Height / 2 - centreY)).FirstOrDefault();
        if (monitor == null) return;
        var key = MonitorSetup.KeyOf(monitors);
        var scale = App.Settings.Window.Setups.TryGetValue(key, out var existing) ? existing.UiScale : NearestPlacement()?.UiScale ?? 1;
        App.Settings.Window.Setups[key] = new SetupPlacement { Name = MonitorSetup.NameOf(monitors), MonitorId = monitor.Id, X = rect.Left - monitor.Bounds.X, Y = rect.Top - monitor.Bounds.Y, Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top, Maximised = maximised, UiScale = scale, LastUsed = DateTime.Now };
        App.Settings.Save(); App.NotifySettingsChanged();
    }

    // A setup seen before goes back exactly; a new one starts from the most recent setup that shares a monitor (else the most recent), kept on screen
    public void RestorePlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        monitors = MonitorSetup.Current();
        var placement = CurrentPlacement ?? NearestPlacement();
        var monitor = placement == null ? null : monitors.FirstOrDefault(candidate => candidate.Id == placement.MonitorId);
        monitor ??= monitors.FirstOrDefault(candidate => candidate.Bounds.X == 0 && candidate.Bounds.Y == 0) ?? monitors.First();
        int width, height, x, y;
        if (placement != null) { width = placement.Width; height = placement.Height; x = monitor.Bounds.X + placement.X; y = monitor.Bounds.Y + placement.Y; }
        else
        {
            // First run: top right of the main screen's desk space, about a third of its width
            var dpiScale = GetDpiForWindow(handle) / 96.0;
            width = (int)Math.Min(monitor.WorkArea.Width * 0.45, 900 * dpiScale); height = (int)Math.Min(monitor.WorkArea.Height * 0.7, 660 * dpiScale);
            x = monitor.WorkArea.Right - width - (int)(24 * dpiScale); y = monitor.WorkArea.Top + (int)(24 * dpiScale);
        }
        // Kept wholly inside the monitor's desk space
        width = Math.Min(width, monitor.WorkArea.Width); height = Math.Min(height, monitor.WorkArea.Height);
        x = Math.Clamp(x, monitor.WorkArea.Left, monitor.WorkArea.Right - width); y = Math.Clamp(y, monitor.WorkArea.Top, monitor.WorkArea.Bottom - height);
        placing = true; if (pin != null) pin.AllowPlacement = true;
        try
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            // Twice: the first move can land on a monitor with another scaling, which makes Windows resize the window for it
            SetWindowPos(handle, IntPtr.Zero, x, y, width, height, NoZOrder | NoActivate);
            SetWindowPos(handle, IntPtr.Zero, x, y, width, height, NoZOrder | NoActivate);
            if (placement?.Maximised == true && !App.Settings.Window.Pinned) WindowState = WindowState.Maximized;
        }
        finally { placing = false; if (pin != null) pin.AllowPlacement = false; }
        if (CurrentPlacement == null) Dispatcher.BeginInvoke(SavePlacement, DispatcherPriority.ApplicationIdle);
        ApplyUiScale();
        if (App.Settings.Window.Pinned) pin?.SendToBottom();
    }

    SetupPlacement? NearestPlacement()
    {
        var ids = (monitors.Count > 0 ? monitors : MonitorSetup.Current()).Select(monitor => monitor.Id).ToHashSet();
        var saved = App.Settings.Window.Setups.Values;
        return saved.Where(placement => ids.Contains(placement.MonitorId)).OrderByDescending(placement => placement.LastUsed).FirstOrDefault() ?? saved.OrderByDescending(placement => placement.LastUsed).FirstOrDefault();
    }

    public void ForgetSetup(string key) { App.Settings.Window.Setups.Remove(key); App.Settings.Save(); if (key == CurrentSetupKey) SavePlacement(); App.NotifySettingsChanged(); }
    public void ApplyUiScale() { if (webView.CoreWebView2 != null) webView.ZoomFactor = Math.Clamp(UiScale, 0.75, 1.5); }

    // ---- Glass and frameless (from WinSqueeze) ----
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr windowHandle, ref FrameMargins margins);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr windowHandle, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr windowHandle, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr windowHandle, ref WindowPlacement placement);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr windowHandle);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct WindowPlacement { public int Length, Flags, ShowCommand; public NativePoint MinPosition, MaxPosition; public NativeRect NormalPosition; }
    const uint NoZOrder = 0x4, NoActivate = 0x10;
    const int StyleIndex = -16, CaptionStyle = 0x00C00000, SystemMenuStyle = 0x00080000, CaptionColourAttribute = 35, CornerAttribute = 33, RoundCorners = 2;
    const int DarkModeAttribute = 20, BorderColourAttribute = 34, BackdropAttribute = 38, AcrylicBackdrop = 3, NoBackdrop = 1;
    struct FrameMargins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct BlurBehind { public int Flags; public bool Enable; public IntPtr Region; public bool TransitionOnMaximized; }
    [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr windowHandle, ref BlurBehind blurBehind);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr gdiObject);
    bool glassActive, darkTheme = true;

    public void ApplyWindowStyle()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero) return;
        var look = App.Settings.Look;
        var resizable = !App.Settings.Window.Pinned;
        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        WindowChrome.SetWindowChrome(this, look.Frameless ? new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(resizable ? 6 : 0), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(8) } : null);
        var darkValue = darkTheme ? 1 : 0;
        DwmSetWindowAttribute(windowHandle, DarkModeAttribute, ref darkValue, sizeof(int));
        var style = GetWindowLong(windowHandle, StyleIndex);
        var wantedStyle = look.Frameless ? style & ~(CaptionStyle | SystemMenuStyle) : style | CaptionStyle | SystemMenuStyle;
        if (wantedStyle != style) { SetWindowLong(windowHandle, StyleIndex, wantedStyle); SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, 0x0027 /*NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED*/); }
        var captionColour = look.Frameless ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
        DwmSetWindowAttribute(windowHandle, CaptionColourAttribute, ref captionColour, sizeof(int));
        var corners = RoundCorners;
        DwmSetWindowAttribute(windowHandle, CornerAttribute, ref corners, sizeof(int));
        var borderColour = look.Frameless ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
        DwmSetWindowAttribute(windowHandle, BorderColourAttribute, ref borderColour, sizeof(int));
        // Acrylic needs Windows 11 22H2+; elsewhere the call fails and the UI stays opaque
        var wantGlass = look.Glass && look.IsGlassSkin;
        // Clear glass: no frost on the window itself (it stays see-through); FrostPanels frosts behind each panel instead
        var clear = wantGlass && look.ClearGlass;
        var backdrop = wantGlass && !clear ? AcrylicBackdrop : NoBackdrop;
        glassActive = wantGlass && DwmSetWindowAttribute(windowHandle, BackdropAttribute, ref backdrop, sizeof(int)) == 0;
        clearActive = glassActive && clear;
        frost?.SetEnabled(clearActive, darkTheme);
        // Clear: an extended frame with no backdrop is painted in the Windows accent colour, so instead the window keeps its alpha through blur-behind with an empty region (see-through, no blur, no accent)
        var margins = glassActive && !clearActive ? new FrameMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new FrameMargins();
        DwmExtendFrameIntoClientArea(windowHandle, ref margins);
        var emptyRegion = CreateRectRgn(0, 0, -1, -1);
        var blurBehind = new BlurBehind { Flags = 0x1 | 0x2 /*ENABLE|BLURREGION*/, Enable = clearActive, Region = emptyRegion };
        DwmEnableBlurBehindWindow(windowHandle, ref blurBehind);
        DeleteObject(emptyRegion);
        var hwndSource = HwndSource.FromHwnd(windowHandle);
        if (hwndSource?.CompositionTarget != null) hwndSource.CompositionTarget.BackgroundColor = glassActive ? Colors.Transparent : Color.FromRgb(16, 17, 20);
        Background = look.Frameless && edgeColour != null ? new SolidColorBrush(edgeColour.Value) : glassActive ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(16, 17, 20));
        if (webView.CoreWebView2 != null) webView.DefaultBackgroundColor = glassActive || look.Frameless ? System.Drawing.Color.Transparent : System.Drawing.Color.FromArgb(16, 17, 20);
        UpdateFrameMargin();
        if (hwndSource != null) { hwndSource.RemoveHook(ThemeMessageHook); hwndSource.AddHook(ThemeMessageHook); }
        PushWindowState();
    }

    bool restyleQueued, firstActivationSeen; DateTime lastRestyle;
    async Task ReapplyLookSoonAsync() { await Task.Delay(1500); ApplyWindowStyle(); PushSkin(); }
    void OnFirstActivation() { if (firstActivationSeen) return; firstActivationSeen = true; ApplyWindowStyle(); PushSkin(); }
    public void RefreshInactiveGlass() { var windowHandle = new WindowInteropHelper(this).Handle; if (windowHandle != IntPtr.Zero && !IsActive) DefWindowProc(windowHandle, 0x0086, App.Settings.Look.GlassWhenInactive && glassActive ? (IntPtr)1 : IntPtr.Zero, (IntPtr)(-1)); }
    IntPtr ThemeMessageHook(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Windows draws the backdrop solid while the frame looks inactive; telling it the frame is still active keeps the glass
        const int WM_NCACTIVATE = 0x0086;
        if (messageId == WM_NCACTIVATE && wParam == IntPtr.Zero && glassActive && App.Settings.Look.GlassWhenInactive) { handled = true; return DefWindowProc(windowHandle, messageId, (IntPtr)1, (IntPtr)(-1)); }
        const int WM_SETTINGCHANGE = 0x001A, WM_THEMECHANGED = 0x031A, WM_DWMCOMPOSITIONCHANGED = 0x031E, WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        if (messageId is WM_SETTINGCHANGE or WM_THEMECHANGED or WM_DWMCOMPOSITIONCHANGED or WM_DWMCOLORIZATIONCOLORCHANGED && !restyleQueued && DateTime.UtcNow - lastRestyle > TimeSpan.FromSeconds(2))
        {
            restyleQueued = true;
            Dispatcher.BeginInvoke(async () => { await Task.Delay(400); restyleQueued = false; lastRestyle = DateTime.UtcNow; ApplyWindowStyle(); PushSkin(); PushPrefs(); }, DispatcherPriority.ApplicationIdle);
        }
        return IntPtr.Zero;
    }

    static readonly Dictionary<string, int> resizeHitCodes = new() { ["left"] = 10, ["right"] = 11, ["top"] = 12, ["topleft"] = 13, ["topright"] = 14, ["bottom"] = 15, ["bottomleft"] = 16, ["bottomright"] = 17 };
    Color? edgeColour;
    void SetEdgeColour(string cssColour)
    {
        var parts = System.Text.RegularExpressions.Regex.Matches(cssColour, @"[\d.]+").Select(match => double.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        if (parts.Count < 3) return;
        edgeColour = Color.FromArgb((byte)Math.Round((parts.Count > 3 ? parts[3] : 1) * 255), (byte)parts[0], (byte)parts[1], (byte)parts[2]);
        if (App.Settings.Look.Frameless) Background = new SolidColorBrush(edgeColour.Value);
    }
    void UpdateFrameMargin() => webView.Margin = App.Settings.Look.Frameless && WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

    // Close (Alt+F4, or the close button when unpinned) quits, or hides to the tray when Settings says so
    protected override void OnClosing(CancelEventArgs closingArgs)
    {
        if (App.IsQuitting) return;
        closingArgs.Cancel = true;
        if (!App.Settings.General.CloseToTray || (!App.Settings.General.ShowTrayIcon && App.Settings.Window.Pinned)) { Dispatcher.BeginInvoke(() => App.Quit()); return; } // after this Closing: Close() inside it throws
        if (!App.Settings.General.ShowTrayIcon) { WindowState = WindowState.Minimized; return; } // no tray icon: keeps running on the taskbar
        SavePlacement(); App.Settings.Save(); HideToTray();
        if (!App.Settings.General.CloseToTrayTipShown) { App.ShowTip("WinGCal is still running", "Closing the window hides it to the tray. Quit from the tray menu."); App.Settings.General.CloseToTrayTipShown = true; App.Settings.Save(); }
    }
    // Hidden to the tray: the pin lets this hide through, and the desktop doesn't bring it back
    public void HideToTray() { if (pin != null) { pin.HiddenToTray = true; pin.AllowPlacement = true; } Hide(); if (pin != null) pin.AllowPlacement = false; }
    public void ShowFromTray() { if (pin != null) pin.HiddenToTray = false; Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; if (App.Settings.Window.Pinned) pin?.SendToBottom(); else Activate(); }
    public void EnsureHandle() => new WindowInteropHelper(this).EnsureHandle();
}
