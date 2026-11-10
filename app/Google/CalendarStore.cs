using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using WinGCal.Settings;
using WinGCal.Shell;
namespace WinGCal.Google;

public record CalendarInfo(string Key, string Account, string Id, string Name, string Colour, bool Primary, bool ReadOnly, string TimeZone, bool SelectedInGoogle);
// One event as the window draws it (SPEC.md › Calendar views); the full Google resource is only kept for export
public record UiEvent(string Id, string EventId, string CalendarKey, string Title, string Start, string End, bool AllDay, string Location, string Description, string MeetUrl, bool Recurring, string Colour, bool Declined, string Link, string RecurringId = "");
public record AccountState(string Email, DateTime LastSync, string Error);

// The accounts' calendars and events within the sync range, refreshed in the background and cached on disk so the widget opens instantly offline
public sealed class CalendarStore
{
    public GoogleAuth Auth { get; } = new();
    public GoogleCalendar Api { get; }
    public TaskStore Tasks { get; }
    // In the order chosen in Settings › Calendars (within each account; accounts stay in sign-in order)
    List<CalendarInfo> calendarsAsSynced = new();
    public List<CalendarInfo> Calendars
    {
        get { var order = settings().Sync.CalendarOrder; var accounts = calendarsAsSynced.Select(calendar => calendar.Account).Distinct().ToList(); return calendarsAsSynced.Select((calendar, index) => (calendar, index)).OrderBy(pair => accounts.IndexOf(pair.calendar.Account)).ThenBy(pair => order.IndexOf(pair.calendar.Key) is var position and >= 0 ? position : int.MaxValue).ThenBy(pair => pair.index).Select(pair => pair.calendar).ToList(); }
        private set => calendarsAsSynced = value;
    }
    // Moves a calendar up (-1) or down (+1) among its account's calendars
    public void MoveCalendar(string key, int direction)
    {
        var ordered = Calendars; var moving = ordered.FirstOrDefault(calendar => calendar.Key == key); if (moving == null) return;
        var siblings = ordered.Where(calendar => calendar.Account == moving.Account).ToList(); var index = siblings.IndexOf(moving); var target = index + direction;
        if (target < 0 || target >= siblings.Count) return;
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        var keys = ordered.Where(calendar => calendar.Account != moving.Account).Select(calendar => calendar.Key).Concat(siblings.Select(calendar => calendar.Key)).ToList();
        settings().Sync.CalendarOrder = keys; settings().Save(); Changed?.Invoke();
    }
    public Dictionary<string, List<UiEvent>> EventsByCalendar { get; private set; } = new();
    public Dictionary<string, AccountState> AccountStates { get; } = new();
    public bool Syncing { get; private set; }
    public bool Offline { get; private set; }
    public event Action? Changed;
    readonly DispatcherTimer refreshTimer = new();
    readonly SemaphoreSlim syncLock = new(1, 1);
    Func<AppSettings> settings;
    static string CacheFile => Path.Combine(AppPaths.CacheFolder, "calendars.json");
    static readonly JsonSerializerOptions cacheJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    // Google's fixed event colours (colorId 1-11)
    static readonly string[] eventColours = { "", "#7986cb", "#33b679", "#8e24aa", "#e67c73", "#f6bf26", "#f4511e", "#039be5", "#616161", "#3f51b5", "#0b8043", "#d50000" };

    public CalendarStore(Func<AppSettings> settingsSource)
    {
        settings = settingsSource;
        Api = new GoogleCalendar(Auth);
        Tasks = new TaskStore(Auth, settingsSource); Tasks.LoadCache();
        LoadCache();
        refreshTimer.Tick += async (sender, args) => await SyncAsync();
        Microsoft.Win32.SystemEvents.PowerModeChanged += (sender, args) => { if (args.Mode == Microsoft.Win32.PowerModes.Resume) _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () => { await Task.Delay(5000); await SyncAsync(); }); };
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (sender, args) => { if (args.IsAvailable && Offline) _ = SyncAsync(); };
    }

    public void Start() { refreshTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(settings().Sync.RefreshMinutes, 1, 120)); refreshTimer.Start(); _ = SyncAsync(); }
    public bool IsShown(string key) => !settings().Sync.HiddenCalendars.Contains(key);
    public string ColourOf(CalendarInfo calendar) => settings().Sync.ColourOverrides.TryGetValue(calendar.Key, out var colour) ? colour : calendar.Colour;

    UiEvent? SafeUiEvent(CalendarInfo calendar, JsonElement item) { try { return ToUiEvent(calendar, item); } catch (Exception eventError) { CrashReporter.Write("Event in " + calendar.Name, eventError, false); return null; } }

    // wait: a new account's first sync queues behind one already running instead of being skipped
    public async Task SyncAsync(bool wait = false)
    {
        if (wait) await syncLock.WaitAsync(); else if (!await syncLock.WaitAsync(0)) return;
        Syncing = true; Changed?.Invoke();
        try
        {
            var appSettings = settings();
            var (from, to) = appSettings.SyncRange();
            var calendars = new List<CalendarInfo>(); var events = new Dictionary<string, List<UiEvent>>();
            var anyReached = false; var anyOffline = false;
            foreach (var email in appSettings.Sync.Accounts.ToList())
            {
                try
                {
                    var accountCalendars = (await Api.CalendarListAsync(email, CancellationToken.None)).Select(entry => ToCalendar(email, entry)).ToList();
                    var calendarErrors = new List<string>();
                    foreach (var calendar in accountCalendars)
                    {
                        // New calendars start shown or hidden as they are in Google Calendar
                        if (!appSettings.Sync.KnownCalendars.Contains(calendar.Key)) { appSettings.Sync.KnownCalendars.Add(calendar.Key); if (!calendar.SelectedInGoogle && !appSettings.Sync.HiddenCalendars.Contains(calendar.Key)) appSettings.Sync.HiddenCalendars.Add(calendar.Key); }
                        // One calendar or one odd event failing mustn't empty the whole account
                        try { events[calendar.Key] = (await Api.EventsAsync(email, calendar.Id, from, to, CancellationToken.None)).Select(item => SafeUiEvent(calendar, item)).Where(item => item != null).Select(item => item!).ToList(); }
                        catch (Exception calendarError) when (calendarError is not SignInNeededException && !(calendarError is HttpRequestException { StatusCode: null })) { calendarErrors.Add($"{calendar.Name}: {calendarError.Message}"); if (EventsByCalendar.TryGetValue(calendar.Key, out var cached)) events[calendar.Key] = cached; CrashReporter.Write("Sync " + calendar.Name, calendarError, false); }
                    }
                    calendars.AddRange(accountCalendars);
                    AccountStates[email] = new AccountState(email, DateTime.Now, string.Join(" ", calendarErrors)); anyReached = true;
                }
                catch (SignInNeededException) { KeepCached(email, calendars, events); AccountStates[email] = new AccountState(email, AccountStates.TryGetValue(email, out var before) ? before.LastSync : DateTime.MinValue, "Sign in again"); }
                catch (HttpRequestException requestError) when (requestError.StatusCode == null) { KeepCached(email, calendars, events); anyOffline = true; }
                catch (Exception syncError) { KeepCached(email, calendars, events); AccountStates[email] = new AccountState(email, AccountStates.TryGetValue(email, out var before) ? before.LastSync : DateTime.MinValue, syncError.Message); CrashReporter.Write("Sync " + email, syncError, false); }
            }
            Calendars = calendars; EventsByCalendar = events;
            Offline = anyOffline && !anyReached;
            appSettings.Save(); SaveCache();
            Changed?.Invoke();
            await Tasks.SyncAsync();
        }
        finally { Syncing = false; syncLock.Release(); Changed?.Invoke(); }
    }

    // An account that couldn't be reached keeps what was cached for it
    void KeepCached(string email, List<CalendarInfo> calendars, Dictionary<string, List<UiEvent>> events)
    {
        foreach (var calendar in Calendars.Where(calendar => calendar.Account == email)) { calendars.Add(calendar); if (EventsByCalendar.TryGetValue(calendar.Key, out var cached)) events[calendar.Key] = cached; }
    }

    public async Task<string> AddAccountAsync(CancellationToken cancel)
    {
        var (email, scopes) = await Auth.SignInAsync(cancel, settings().Tasks.Enabled);
        if (scopes.Contains(GoogleAuth.TasksScope) && !settings().Tasks.Accounts.Contains(email)) settings().Tasks.Accounts.Add(email);
        var appSettings = settings();
        if (!appSettings.Sync.Accounts.Contains(email)) appSettings.Sync.Accounts.Add(email);
        appSettings.Save();
        await SyncAsync(true);
        return email;
    }

    public async Task RemoveAccountAsync(string email)
    {
        await Auth.SignOutAsync(email);
        var appSettings = settings();
        appSettings.Sync.Accounts.Remove(email); appSettings.Tasks.Accounts.Remove(email);
        appSettings.Sync.HiddenCalendars.RemoveAll(key => key.StartsWith(email + "|")); appSettings.Sync.KnownCalendars.RemoveAll(key => key.StartsWith(email + "|"));
        appSettings.Save();
        Calendars = Calendars.Where(calendar => calendar.Account != email).ToList();
        foreach (var key in EventsByCalendar.Keys.Where(key => key.StartsWith(email + "|")).ToList()) EventsByCalendar.Remove(key);
        AccountStates.Remove(email); SaveCache(); Changed?.Invoke();
    }

    // Task access for an account already signed in: Google adds it to what the account has allowed
    public async Task AllowTasksAsync(string email, CancellationToken cancel)
    {
        var (signedIn, scopes) = await Auth.SignInAsync(cancel, true, email);
        if (!scopes.Contains(GoogleAuth.TasksScope)) throw new InvalidOperationException("Task access wasn't ticked on Google's page.");
        if (!settings().Tasks.Accounts.Contains(signedIn)) settings().Tasks.Accounts.Add(signedIn);
        if (!settings().Sync.Accounts.Contains(signedIn)) settings().Sync.Accounts.Add(signedIn);
        settings().Save();
        await Tasks.SyncAsync();
    }

    public bool IsInSwitcher(string key) => !settings().Sync.OutOfSwitcher.Contains(key);
    public void SetInSwitcher(string key, bool inSwitcher)
    {
        var left = settings().Sync.OutOfSwitcher;
        if (inSwitcher) left.Remove(key); else if (!left.Contains(key)) left.Add(key);
        settings().Save(); Changed?.Invoke();
    }

    public void SetShown(string key, bool shown)
    {
        var hidden = settings().Sync.HiddenCalendars;
        if (shown) hidden.Remove(key); else if (!hidden.Contains(key)) hidden.Add(key);
        settings().Save(); Changed?.Invoke();
    }

    // wholeSeries: a repeating event's whole series goes (Google deletes the series' main event), else only this occurrence
    public async Task DeleteEventAsync(string uiEventId, bool wholeSeries = false)
    {
        var found = EventsByCalendar.SelectMany(pair => pair.Value).FirstOrDefault(item => item.Id == uiEventId);
        var calendar = found == null ? null : Calendars.FirstOrDefault(candidate => candidate.Key == found.CalendarKey);
        if (found == null || calendar == null) return;
        var series = wholeSeries && found.RecurringId.Length > 0 ? found.RecurringId : "";
        await Api.DeleteEventAsync(calendar.Account, calendar.Id, series.Length > 0 ? series : found.EventId, CancellationToken.None);
        EventsByCalendar[calendar.Key] = EventsByCalendar[calendar.Key].Where(item => item.Id != uiEventId && (series.Length == 0 || item.RecurringId != series)).ToList();
        SaveCache(); Changed?.Invoke();
    }

    public async Task UpdateEventAsync(string uiEventId, System.Text.Json.Nodes.JsonObject changes)
    {
        var found = FindEvent(uiEventId);
        var calendar = found == null ? null : Calendars.FirstOrDefault(candidate => candidate.Key == found.CalendarKey);
        if (found == null || calendar == null) return;
        var saved = await Api.PatchEventAsync(calendar.Account, calendar.Id, found.EventId, changes, CancellationToken.None);
        var updated = ToUiEvent(calendar, saved);
        EventsByCalendar[calendar.Key] = EventsByCalendar[calendar.Key].Select(item => item.Id == uiEventId ? updated : item).Where(item => item != null).Select(item => item!).ToList();
        SaveCache(); Changed?.Invoke();
    }

    public async Task<UiEvent?> CreateEventAsync(string calendarKey, System.Text.Json.Nodes.JsonObject newEvent)
    {
        var calendar = Calendars.FirstOrDefault(candidate => candidate.Key == calendarKey) ?? throw new InvalidOperationException("That calendar isn't available");
        var saved = await Api.InsertEventAsync(calendar.Account, calendar.Id, newEvent, CancellationToken.None);
        var created = ToUiEvent(calendar, saved);
        if (created != null) EventsByCalendar[calendar.Key] = EventsByCalendar.GetValueOrDefault(calendar.Key, new()).Append(created).ToList();
        SaveCache(); Changed?.Invoke();
        return created;
    }

    public async Task MoveEventAsync(string uiEventId, string destinationKey)
    {
        var found = FindEvent(uiEventId);
        var calendar = found == null ? null : Calendars.FirstOrDefault(candidate => candidate.Key == found.CalendarKey);
        var destination = Calendars.FirstOrDefault(candidate => candidate.Key == destinationKey);
        if (found == null || calendar == null || destination == null || destination.Key == calendar.Key) return;
        var moved = await Api.MoveEventAsync(calendar.Account, calendar.Id, found.EventId, destination.Id, CancellationToken.None);
        EventsByCalendar[calendar.Key] = EventsByCalendar[calendar.Key].Where(item => item.Id != uiEventId).ToList();
        if (ToUiEvent(destination, moved) is { } arrived) EventsByCalendar[destination.Key] = EventsByCalendar.GetValueOrDefault(destination.Key, new()).Append(arrived).ToList();
        SaveCache(); Changed?.Invoke();
    }

    public UiEvent? FindEvent(string uiEventId) => EventsByCalendar.SelectMany(pair => pair.Value).FirstOrDefault(item => item.Id == uiEventId);

    static CalendarInfo ToCalendar(string email, JsonElement entry)
    {
        var id = Text(entry, "id");
        var role = Text(entry, "accessRole");
        var name = Text(entry, "summaryOverride") is { Length: > 0 } renamed ? renamed : Text(entry, "summary");
        return new CalendarInfo($"{email}|{id}", email, id, name, Text(entry, "backgroundColor") is { Length: > 0 } colour ? colour : "#4f8ef7", entry.TryGetProperty("primary", out var primary) && primary.GetBoolean(), role is not ("owner" or "writer"), Text(entry, "timeZone"), entry.TryGetProperty("selected", out var selected) && selected.GetBoolean());
    }

    static UiEvent? ToUiEvent(CalendarInfo calendar, JsonElement item)
    {
        if (Text(item, "status") == "cancelled" || !item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end)) return null;
        var allDay = start.TryGetProperty("date", out _);
        var startText = allDay ? Text(start, "date") : Text(start, "dateTime");
        var endText = allDay ? Text(end, "date") : Text(end, "dateTime");
        var meetUrl = Text(item, "hangoutLink");
        if (meetUrl == "" && item.TryGetProperty("conferenceData", out var conference) && conference.TryGetProperty("entryPoints", out var entryPoints))
            meetUrl = entryPoints.EnumerateArray().Where(point => Text(point, "entryPointType") == "video").Select(point => Text(point, "uri")).FirstOrDefault() ?? "";
        var declined = item.TryGetProperty("attendees", out var attendees) && attendees.EnumerateArray().Any(attendee => attendee.TryGetProperty("self", out var self) && self.GetBoolean() && Text(attendee, "responseStatus") == "declined");
        var colourId = int.TryParse(Text(item, "colorId"), out var colourNumber) && colourNumber > 0 && colourNumber < eventColours.Length ? eventColours[colourNumber] : "";
        var eventId = Text(item, "id");
        return new UiEvent($"{calendar.Key}|{eventId}", eventId, calendar.Key, Text(item, "summary") is { Length: > 0 } title ? title : "(No title)", startText, endText, allDay, Text(item, "location"), Text(item, "description"), meetUrl, item.TryGetProperty("recurringEventId", out _), colourId, declined, Text(item, "htmlLink"), Text(item, "recurringEventId"));
    }

    static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    record CacheFile_(List<CalendarInfo> Calendars, Dictionary<string, List<UiEvent>> Events, Dictionary<string, AccountState> Accounts);
    void SaveCache() { try { var temp = CacheFile + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(new CacheFile_(Calendars, EventsByCalendar, AccountStates), cacheJson)); File.Move(temp, CacheFile, true); } catch { } }
    void LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return;
            var cached = JsonSerializer.Deserialize<CacheFile_>(File.ReadAllText(CacheFile), cacheJson);
            if (cached == null) return;
            Calendars = cached.Calendars; EventsByCalendar = cached.Events;
            foreach (var pair in cached.Accounts) AccountStates[pair.Key] = pair.Value;
        }
        catch { }
    }
}
