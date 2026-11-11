using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinGCal.Google;
using WinGCal.Settings;
namespace WinGCal.Shell;

// One change to one event, with what's needed to take it back and do it again (SPEC.md › Undo)
// Kind: "update" (Before/After: the changed fields as Google patches), "create", "delete" (taken back by restoring the same event; After/Before hold its fields in case Google won't), "move" (to ToCalendarKey)
public sealed class HistoryStep
{
    public string Kind { get; set; } = "";
    public string CalendarKey { get; set; } = "";
    public string EventId { get; set; } = "";
    public string ToCalendarKey { get; set; } = "";
    public JsonObject? Before { get; set; }
    public JsonObject? After { get; set; }
}

// One thing the person did (one drop of several events is one entry)
public sealed class HistoryEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string Summary { get; set; } = "";
    public List<HistoryStep> Steps { get; set; } = new();
    public bool Undone { get; set; }
    public DateTime? UndoneAt { get; set; }
}

// Every change WinGCal makes to an event is recorded here, to undo and redo from the bar, Ctrl+Z / Ctrl+Y or Settings › History. The last 1000 are kept on disk
public static class ChangeHistory
{
    const int Limit = 1000;
    static List<HistoryEntry>? entries;
    public static List<HistoryEntry> Entries => entries ??= Load();
    // The entry being built by the action in progress; AsyncLocal so two actions running at once never share one
    static readonly AsyncLocal<HistoryEntry?> open = new();
    public static event Action<HistoryEntry, string>? Changed; // the entry, and "did", "undid" or "redid"
    static string FilePath => Path.Combine(AppPaths.DataFolder, "history.json");
    static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Groups the steps of one action under a summary ("Moved 3 events to Thu 12 Nov"); a step outside any group is its own entry
    public static IDisposable Begin(string summary)
    {
        if (open.Value != null) return new Scope(null);
        var entry = new HistoryEntry { Summary = summary }; open.Value = entry; return new Scope(entry);
    }
    sealed class Scope(HistoryEntry? entry) : IDisposable
    {
        public void Dispose() { if (entry == null || open.Value != entry) return; open.Value = null; if (entry.Steps.Count > 0) Add(entry); }
    }
    public static void SetSummary(string summary) { if (open.Value is { } entry) entry.Summary = summary; }

    public static void Record(HistoryStep step, string summaryIfAlone) { if (open.Value is { } entry) entry.Steps.Add(step); else Add(new HistoryEntry { Summary = summaryIfAlone, Steps = { step } }); }

    static void Add(HistoryEntry entry)
    {
        entry.Id = (Entries.Count == 0 ? 0 : Entries[^1].Id) + 1; entry.At = DateTime.Now;
        Entries.Add(entry); if (Entries.Count > Limit) Entries.RemoveRange(0, Entries.Count - Limit);
        Save(); Changed?.Invoke(entry, "did");
    }

    // Without an id: the newest change still in effect (undo), or the change most recently undone (redo)
    public static async Task UndoAsync(CalendarStore store, long? id = null)
    {
        var entry = id is { } wanted ? Entries.FirstOrDefault(candidate => candidate.Id == wanted) : Entries.LastOrDefault(candidate => !candidate.Undone);
        if (entry == null || entry.Undone) return;
        for (var index = entry.Steps.Count - 1; index >= 0; index--) await store.ReplayAsync(entry.Steps[index], backwards: true);
        entry.Undone = true; entry.UndoneAt = DateTime.Now; Save(); Changed?.Invoke(entry, "undid");
    }
    public static async Task RedoAsync(CalendarStore store, long? id = null)
    {
        var entry = id is { } wanted ? Entries.FirstOrDefault(candidate => candidate.Id == wanted) : Entries.Where(candidate => candidate.Undone).OrderByDescending(candidate => candidate.UndoneAt).FirstOrDefault();
        if (entry == null || !entry.Undone) return;
        foreach (var step in entry.Steps) await store.ReplayAsync(step, backwards: false);
        entry.Undone = false; entry.UndoneAt = null; Save(); Changed?.Invoke(entry, "redid");
    }

    // An event that had to be added again comes back under a new ID: later entries follow it
    public static void Renamed(string calendarKey, string oldId, string newId) { foreach (var step in Entries.SelectMany(entry => entry.Steps)) { if (step.EventId == oldId && (step.CalendarKey == calendarKey || step.ToCalendarKey == calendarKey)) step.EventId = newId; } Save(); }

    static List<HistoryEntry> Load() { try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath), json) ?? new() : new(); } catch (Exception loadError) { CrashReporter.Write("Reading undo history", loadError, false); return new(); } }
    static void Save() { try { Directory.CreateDirectory(AppPaths.DataFolder); var temporary = FilePath + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(Entries, json)); File.Move(temporary, FilePath, true); } catch (Exception saveError) { CrashReporter.Write("Saving undo history", saveError, false); } }
}
