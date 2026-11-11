using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WinGCal.Google;
using WinGCal.Shell;
namespace WinGCal;

// A glass window for one event (view and edit in one), a new event, or several picked events (SPEC.md › Event windows).
// The same page as the calendar, opened as ?window=…; as many as you like, grouped on one taskbar button (right-click › Close all windows)
public sealed class EventPopup : Window
{
    public enum Kind { Event, New, Batch }
    public readonly Kind Mode;
    public readonly string EventId; // Event: the event shown; Batch: "" (ids below)
    readonly List<string> batchIds;
    readonly string newDay, newTime, newEndDay, newEndTime; // New: the day (or first of the days picked) and the hour, if any; the last day and end time when hours were dragged in Week
    readonly WebView2 webView = new();
    bool ready;
    App App => App.Instance;
    static readonly JsonSerializerOptions camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EventPopup(Kind mode, string eventId = "", List<string>? ids = null, string day = "", string time = "", string endDay = "", string endTime = "")
    {
        Mode = mode; EventId = eventId; batchIds = ids ?? new(); newDay = day; newTime = time; newEndDay = endDay; newEndTime = endTime;
        Title = mode == Kind.New ? "New event" : mode == Kind.Batch ? "Change events" : App.Store.FindEvent(eventId)?.Title ?? "Event";
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/WinGCal.ico"));
        Width = 400; Height = mode == Kind.Batch ? 470 : 540; MinWidth = 320; MinHeight = 300;
        Background = Brushes.Transparent; Content = webView;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(8) });
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (sender, args) => { ApplyGlass(); PlaceNearPointer(); };
        Loaded += async (sender, args) => { PlaceNearPointer(); await StartAsync(); };
        App.Store.Changed += OnStoreChanged;
        Closed += (sender, args) => App.Store.Changed -= OnStoreChanged;
        LocationChanged += (sender, args) => { if (clearOn) PushWallpaper(); CheckBehind(); };
        behindTimer.Tick += (sender, args) => CheckBehind(); Loaded += (sender, args) => behindTimer.Start(); Closed += (sender, args) => behindTimer.Stop();
        SizeChanged += (sender, args) => { if (clearOn) PushWallpaper(); };
    }
    static int openCount;
    // Opens near the pointer on the pointer's own monitor, nudged so a new one doesn't sit exactly on the last. Done in screen pixels with that monitor's scale: WPF's Left/Top use the main screen's scale, which lands on the wrong monitor when monitors differ
    System.Drawing.Point? placedAt; int placedOffset;
    void PlaceNearPointer()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        if (placedAt == null) { placedAt = System.Windows.Forms.Control.MousePosition; placedOffset = (openCount++ % 6) * 24; }
        var pointer = placedAt.Value; var area = System.Windows.Forms.Screen.FromPoint(pointer).WorkingArea;
        var scale = GetDpiForMonitor(MonitorFromPoint(pointer, 2), 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1;
        int width = (int)(Width * scale), height = (int)(Height * scale), offset = (int)(placedOffset * scale);
        var left = Math.Clamp(pointer.X + (int)(16 * scale) + offset, area.Left, Math.Max(area.Left, area.Right - width)); var top = Math.Clamp(pointer.Y - (int)(40 * scale) + offset, area.Top, Math.Max(area.Top, area.Bottom - height));
        SetWindowPos(handle, IntPtr.Zero, left, top, width, height, 0x0014 /*NOZORDER|NOACTIVATE*/);
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(System.Drawing.Point point, int flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    async Task StartAsync()
    {
        await webView.EnsureCoreWebView2Async(await MainWindow.WebEnvironment());
        var core = webView.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(MainWindow.UiHost, Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
        webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        webView.ZoomFactor = Math.Clamp(App.UiScale, 0.75, 1.5);
        core.WebMessageReceived += async (sender, args) => await OnMessageAsync(args.WebMessageAsJson);
        core.NewWindowRequested += (sender, args) => { args.Handled = true; MainWindow.OpenLink(args.Uri); };
        core.NavigationStarting += (sender, args) => { if (!args.Uri.StartsWith($"https://{MainWindow.UiHost}/")) { args.Cancel = true; MainWindow.OpenLink(args.Uri); } };
        core.Navigate($"https://{MainWindow.UiHost}/index.html?window={Mode.ToString().ToLowerInvariant()}");
    }

    void Push(string json) { if (ready) Dispatcher.BeginInvoke(() => webView.CoreWebView2?.PostWebMessageAsJson(json)); }
    public void PushSkin() { ApplyGlass(); if (webView.CoreWebView2 != null) webView.ZoomFactor = Math.Clamp(App.UiScale, 0.75, 1.5); Push(App.MainShell.SkinMessage()); Push(JsonSerializer.Serialize(new { type = "window", glass = glassOn, clear = clearOn, frameless = true, maximised = false, pinned = false })); if (clearOn) PushWallpaper(true); }
    // Clear glass, as the calendar window: see-through, with each panel frosted from the wallpaper behind it (Shell/WallpaperFrost.cs)
    readonly WallpaperFeed wallpaperFeed = new();
    void PushWallpaper(bool recheck = false) { if (ready && wallpaperFeed.Message(webView, ActualWidth, ActualHeight, true, recheck) is { } message) Push(message); }
    // Windows sometimes drops the backdrop set before the window first shows (as with the calendar window), so it's set again once the page is up and on first activation
    async Task ReapplyGlassSoonAsync() { await Task.Delay(1200); PushSkin(); }
    bool activatedOnce;
    protected override void OnActivated(EventArgs args) { base.OnActivated(args); if (activatedOnce) return; activatedOnce = true; PushSkin(); }
    void OnStoreChanged() => Dispatcher.BeginInvoke(PushContent);

    static object UiEventJson(UiEvent item) => new { id = item.Id, calendarKey = item.CalendarKey, title = item.Title, start = item.Start, end = item.End, allDay = item.AllDay, location = item.Location, description = item.Description, meetUrl = item.MeetUrl, recurring = item.Recurring, colour = item.Colour, link = item.Link };
    void PushContent()
    {
        var store = App.Store;
        var calendars = store.Calendars.Select(calendar => new { key = calendar.Key, name = calendar.Name, colour = store.ColourOf(calendar), account = calendar.Account, readOnly = calendar.ReadOnly, shown = store.IsShown(calendar.Key), inSwitcher = store.IsInSwitcher(calendar.Key), isDefault = calendar.Key == App.Settings.Sync.DefaultCalendar });
        var shown = Mode == Kind.Event ? store.FindEvent(EventId) is { } found ? new[] { UiEventJson(found) } : Array.Empty<object>() : Mode == Kind.Batch ? batchIds.Select(id => store.FindEvent(id)).Where(found => found != null).Select(found => UiEventJson(found!)).ToArray() : Array.Empty<object>();
        Push(JsonSerializer.Serialize(new { type = "editor", mode = Mode.ToString().ToLowerInvariant(), events = shown, calendars, day = newDay, time = newTime, endDay = newEndDay, endTime = newEndTime }, camel));
        if (Mode == Kind.Event && shown.Length > 0 && store.FindEvent(EventId) is { } current) Title = current.Title;
    }

    static string Text(JsonElement message, string name) => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    async Task OnMessageAsync(string json)
    {
        using var document = JsonDocument.Parse(json); var message = document.RootElement;
        try
        {
            switch (Text(message, "type"))
            {
                case "ready": ready = true; wallpaperFeed.Forget(); Activate(); webView.Focus(); PushSkin(); _ = ReapplyGlassSoonAsync(); Push(App.MainShell.PrefsMessage()); PushContent(); break;
                case "dragWindow": ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, 0xA1, (IntPtr)2, IntPtr.Zero); break;
                case "windowControl": if (Text(message, "action") == "minimise") WindowState = WindowState.Minimized; else Close(); break;
                case "growWindow":
                    // Taller by what the page asks for, up to the screen; moves up if the bottom would run off
                    if (message.TryGetProperty("by", out var growBy) && growBy.TryGetDouble(out var extra)) { var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea; var scale = VisualTreeHelper.GetDpi(this).DpiScaleY; double bottom = screen.Bottom / scale, top = screen.Top / scale; Height = Math.Min(Height + extra + 4, bottom - top); if (Top + Height > bottom) Top = Math.Max(top, bottom - Height); }
                    break;
                case "openUrl": MainWindow.OpenLink(Text(message, "url")); break;
                case "saveEvent": using (ChangeHistory.Begin($"Edited “{App.Store.FindEvent(EventId)?.Title ?? "event"}”")) await App.Store.UpdateEventAsync(EventId, EventChanges.FromMessage(message)); Push("{\"type\":\"saved\"}"); break;
                case "createEvent":
                    var newEvent = EventChanges.FromMessage(message);
                    await App.Store.CreateEventAsync(Text(message, "calendarKey"), newEvent); Close(); break;
                case "deleteEvents":
                    var series = message.TryGetProperty("series", out var seriesValue) && seriesValue.GetBoolean();
                    var deleting = message.GetProperty("ids").EnumerateArray().Select(id => id.GetString() ?? "").ToList();
                    using (ChangeHistory.Begin(deleting.Count == 1 ? $"Deleted “{App.Store.FindEvent(deleting[0])?.Title ?? "event"}”" : $"Deleted {deleting.Count} events")) foreach (var id in deleting) await App.Store.DeleteEventAsync(id, series);
                    Close(); break;
                case "batchSave":
                    DateTime? date = Text(message, "date") is { Length: > 0 } dateText ? DateTime.ParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                    TimeSpan? start = Text(message, "startTime") is { Length: > 0 } startText ? TimeSpan.Parse(startText, CultureInfo.InvariantCulture) : null;
                    TimeSpan? end = Text(message, "endTime") is { Length: > 0 } endText ? TimeSpan.Parse(endText, CultureInfo.InvariantCulture) : null;
                    string? place = message.TryGetProperty("location", out var placeValue) && placeValue.ValueKind == JsonValueKind.String ? placeValue.GetString() : null;
                    string? target = Text(message, "calendarKey") is { Length: > 0 } key ? key : null;
                    var picked = batchIds.Select(id => App.Store.FindEvent(id)).Where(found => found != null).Select(found => found!).ToList();
                    var failed = await EventChanges.ApplyToManyAsync(App.Store, picked, date, start, end, place, target);
                    if (failed.Count == 0) Close(); else Push(JsonSerializer.Serialize(new { type = "error", text = (failed.Count == 1 ? "Couldn't change one event. " : $"Couldn't change {failed.Count} events. ") + string.Join(" ", failed) }));
                    break;
            }
        }
        catch (Exception actionError) { Push(JsonSerializer.Serialize(new { type = "error", text = actionError.Message })); }
    }

    // Glass skins: an acrylic window the page tints; Paper, Technical and Wall calendar: the page is solid
    bool glassOn, clearOn, overWindows;
    readonly System.Windows.Threading.DispatcherTimer behindTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    // Another app's window moved behind (or away): switch between clear glass and Windows' acrylic
    void CheckBehind() { if (glassOn && IsVisible && App.Settings.Look.ClearGlass && WindowsBehind.Any(new WindowInteropHelper(this).Handle) != overWindows) PushSkin(); }
    void ApplyGlass()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var look = App.Settings.Look;
        var dark = App.MainShell.DarkTheme ? 1 : 0; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var corners = 2; DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
        // No Windows caption: its buttons and accent-coloured title bar otherwise show through the glass
        var style = GetWindowLong(handle, -16); var wantedStyle = style & ~(0x00C00000 | 0x00080000);
        if (wantedStyle != style) { SetWindowLong(handle, -16, wantedStyle); SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027 /*NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED*/); }
        var captionColour = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(handle, 35, ref captionColour, sizeof(int));
        // Clear glass needs the wallpaper to frost from, and only the desktop behind; otherwise (or with Panel frost at none) the window keeps Windows' acrylic, which blurs what is really there
        overWindows = WindowsBehind.Any(handle);
        var clear = look.IsGlassSkin && look.Glass && look.ClearGlass && look.PanelFrost > 0 && !overWindows && wallpaperFeed.Message(webView, ActualWidth, ActualHeight, false, true) != null;
        var backdrop = look.IsGlassSkin && !clear ? 3 : 1;
        glassOn = look.IsGlassSkin && DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) == 0;
        clearOn = glassOn && clear;
        var margins = glassOn && !clearOn ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins(); DwmExtendFrameIntoClientArea(handle, ref margins);
        // Clear: keeps its alpha through blur-behind with an empty region (see-through, no blur, no accent colour)
        var emptyRegion = CreateRectRgn(0, 0, -1, -1); var blurBehind = new BlurBehind { Flags = 0x1 | 0x2, Enable = clearOn, Region = emptyRegion }; DwmEnableBlurBehindWindow(handle, ref blurBehind); DeleteObject(emptyRegion);
        // Glass stays when another window is in front (as the calendar, Settings › Appearance › Glass when inactive). Our hook goes in front of WindowChrome's
        if (HwndSource.FromHwnd(handle) is { } hwndSource) { hwndSource.RemoveHook(KeepGlassHook); hwndSource.AddHook(KeepGlassHook); }
        if (glassOn && !IsActive) DefWindowProc(handle, 0x0086, look.GlassWhenInactive ? (IntPtr)1 : IntPtr.Zero, (IntPtr)(-1));
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = glassOn ? Colors.Transparent : Color.FromRgb(16, 17, 20);
    }
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct BlurBehind { public int Flags; public bool Enable; public IntPtr Region; public bool TransitionOnMaximized; }
    [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr windowHandle, ref BlurBehind blurBehind);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr gdiObject);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr windowHandle, ref Margins margins);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam);
    IntPtr KeepGlassHook(IntPtr handle, int messageId, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (messageId == 0x0086 /*WM_NCACTIVATE*/ && wParam == IntPtr.Zero && glassOn && App.Settings.Look.GlassWhenInactive) { handled = true; return DefWindowProc(handle, messageId, (IntPtr)1, (IntPtr)(-1)); } // -1: no native caption painted over ours
        return IntPtr.Zero;
    }
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr windowHandle, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr windowHandle, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam);
}
