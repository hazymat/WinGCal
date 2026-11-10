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
    readonly string newDay, newTime;
    readonly WebView2 webView = new();
    bool ready;
    App App => App.Instance;
    static readonly JsonSerializerOptions camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EventPopup(Kind mode, string eventId = "", List<string>? ids = null, string day = "", string time = "")
    {
        Mode = mode; EventId = eventId; batchIds = ids ?? new(); newDay = day; newTime = time;
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
    public void PushSkin() { ApplyGlass(); Push(App.MainShell.SkinMessage()); Push(JsonSerializer.Serialize(new { type = "window", glass = glassOn, clear = false, frameless = true, maximised = false, pinned = false })); }
    void OnStoreChanged() => Dispatcher.BeginInvoke(PushContent);

    static object UiEventJson(UiEvent item) => new { id = item.Id, calendarKey = item.CalendarKey, title = item.Title, start = item.Start, end = item.End, allDay = item.AllDay, location = item.Location, description = item.Description, meetUrl = item.MeetUrl, recurring = item.Recurring, colour = item.Colour, link = item.Link };
    void PushContent()
    {
        var store = App.Store;
        var calendars = store.Calendars.Select(calendar => new { key = calendar.Key, name = calendar.Name, colour = store.ColourOf(calendar), account = calendar.Account, readOnly = calendar.ReadOnly, shown = store.IsShown(calendar.Key) });
        var shown = Mode == Kind.Event ? store.FindEvent(EventId) is { } found ? new[] { UiEventJson(found) } : Array.Empty<object>() : Mode == Kind.Batch ? batchIds.Select(id => store.FindEvent(id)).Where(found => found != null).Select(found => UiEventJson(found!)).ToArray() : Array.Empty<object>();
        Push(JsonSerializer.Serialize(new { type = "editor", mode = Mode.ToString().ToLowerInvariant(), events = shown, calendars, day = newDay, time = newTime }, camel));
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
                case "ready": ready = true; PushSkin(); Push(App.MainShell.PrefsMessage()); PushContent(); break;
                case "dragWindow": ReleaseCapture(); SendMessage(new WindowInteropHelper(this).Handle, 0xA1, (IntPtr)2, IntPtr.Zero); break;
                case "windowControl": if (Text(message, "action") == "minimise") WindowState = WindowState.Minimized; else Close(); break;
                case "openUrl": MainWindow.OpenLink(Text(message, "url")); break;
                case "saveEvent": await App.Store.UpdateEventAsync(EventId, EventChanges.FromMessage(message)); Push("{\"type\":\"saved\"}"); break;
                case "createEvent":
                    var newEvent = EventChanges.FromMessage(message);
                    await App.Store.CreateEventAsync(Text(message, "calendarKey"), newEvent); Close(); break;
                case "deleteEvents":
                    var series = message.TryGetProperty("series", out var seriesValue) && seriesValue.GetBoolean();
                    foreach (var id in message.GetProperty("ids").EnumerateArray().Select(id => id.GetString() ?? "")) await App.Store.DeleteEventAsync(id, series);
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
    bool glassOn;
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
        var backdrop = look.IsGlassSkin ? 3 : 1;
        glassOn = look.IsGlassSkin && DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) == 0;
        var margins = glassOn ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins(); DwmExtendFrameIntoClientArea(handle, ref margins);
        // Glass stays when another window is in front (as the calendar, Settings › Appearance › Glass when inactive). Our hook goes in front of WindowChrome's
        if (HwndSource.FromHwnd(handle) is { } hwndSource) { hwndSource.RemoveHook(KeepGlassHook); hwndSource.AddHook(KeepGlassHook); }
        if (glassOn && !IsActive) DefWindowProc(handle, 0x0086, look.GlassWhenInactive ? (IntPtr)1 : IntPtr.Zero, (IntPtr)(-1));
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = glassOn ? Colors.Transparent : Color.FromRgb(16, 17, 20);
    }
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
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
