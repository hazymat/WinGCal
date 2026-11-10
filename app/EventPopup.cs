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
        // Opens near the pointer, nudged so a new one doesn't sit exactly on the last
        var pointer = System.Windows.Forms.Control.MousePosition; var area = System.Windows.Forms.Screen.FromPoint(pointer).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? this).DpiScaleX;
        var offset = (openCount++ % 6) * 24;
        Left = Math.Clamp(pointer.X / dpi + 16 + offset, area.Left / dpi, area.Right / dpi - Width); Top = Math.Clamp(pointer.Y / dpi - 40 + offset, area.Top / dpi, area.Bottom / dpi - Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        SourceInitialized += (sender, args) => ApplyGlass();
        Loaded += async (sender, args) => await StartAsync();
        App.Store.Changed += OnStoreChanged;
        Closed += (sender, args) => App.Store.Changed -= OnStoreChanged;
    }
    static int openCount;

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
        var backdrop = look.IsGlassSkin ? 3 : 1;
        glassOn = look.IsGlassSkin && DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) == 0;
        var margins = glassOn ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins(); DwmExtendFrameIntoClientArea(handle, ref margins);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target) target.BackgroundColor = glassOn ? Colors.Transparent : Color.FromRgb(16, 17, 20);
    }
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr windowHandle, ref Margins margins);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr windowHandle, int messageId, IntPtr wParam, IntPtr lParam);
}
