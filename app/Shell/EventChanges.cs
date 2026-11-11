using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinGCal.Google;
namespace WinGCal.Shell;

// Turning the glass editor's fields into Google's event JSON (SPEC.md › Event windows)
public static class EventChanges
{
    static string Text(JsonElement message, string name) => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    static JsonObject When(bool allDay, string text) => allDay ? new() { ["date"] = text, ["dateTime"] = null, ["timeZone"] = null } : new() { ["dateTime"] = text, ["date"] = null };

    // The editor sends only what changed: summary, location, description, and start/end (with allDay) when the times changed
    public static JsonObject FromMessage(JsonElement message)
    {
        var changes = new JsonObject();
        foreach (var field in new[] { "summary", "location", "description" }) if (message.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String) changes[field] = value.GetString();
        if (message.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.String)
        {
            var allDay = message.TryGetProperty("allDay", out var allDayValue) && allDayValue.GetBoolean();
            changes["start"] = When(allDay, start.GetString() ?? ""); changes["end"] = When(allDay, Text(message, "end"));
        }
        return changes;
    }

    // "2026-11-10T14:00:00+00:00": local time with its offset, as Google wants it
    static string WithOffset(DateTime local) => new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    static string DayText(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static (DateTime start, DateTime end) LocalTimes(UiEvent item) => item.AllDay
        ? (DateTime.ParseExact(item.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTime.ParseExact(item.End, "yyyy-MM-dd", CultureInfo.InvariantCulture))
        : (DateTimeOffset.Parse(item.Start, CultureInfo.InvariantCulture).LocalDateTime, DateTimeOffset.Parse(item.End, CultureInfo.InvariantCulture).LocalDateTime);

    // Carried by drag and drop: each event moves by the same days (and, dropped in Week's hours, minutes), keeping its length. All-day events move by whole days only
    public static async Task<List<string>> ShiftManyAsync(CalendarStore store, List<UiEvent> events, int days, int minutes)
    {
        var failed = new List<string>();
        foreach (var item in events)
        {
            try
            {
                var (start, end) = LocalTimes(item); var shift = TimeSpan.FromDays(days) + (item.AllDay ? TimeSpan.Zero : TimeSpan.FromMinutes(minutes));
                if (shift == TimeSpan.Zero) continue;
                start += shift; end += shift;
                await store.UpdateEventAsync(item.Id, new JsonObject { ["start"] = When(item.AllDay, item.AllDay ? DayText(start) : WithOffset(start)), ["end"] = When(item.AllDay, item.AllDay ? DayText(end) : WithOffset(end)) });
            }
            catch (Exception changeError) { failed.Add($"{item.Title}: {changeError.Message}"); }
        }
        return failed;
    }

    // Several picked events at once: only the given fields change; each event keeps its own time and length where that makes sense
    public static async Task<List<string>> ApplyToManyAsync(CalendarStore store, List<UiEvent> events, DateTime? newDate, TimeSpan? newStart, TimeSpan? newEnd, string? place, string? targetKey, IProgress<int>? progress = null)
    {
        var failed = new List<string>(); var done = 0;
        foreach (var item in events)
        {
            progress?.Report(++done);
            try
            {
                var changes = new JsonObject();
                if (place != null) changes["location"] = place;
                if (newDate != null || newStart != null || newEnd != null)
                {
                    var (start, end) = LocalTimes(item); var allDay = item.AllDay; var length = end - start;
                    if (newDate is { } date) { start = date.Date + start.TimeOfDay; end = start + length; }
                    if (newStart is { } startTime) { if (allDay) { allDay = false; length = TimeSpan.FromHours(1); } start = start.Date + startTime; end = start + length; }
                    if (newEnd is { } endTime && !allDay) { end = start.Date + endTime; if (end <= start) end = end.AddDays(1); }
                    changes["start"] = When(allDay, allDay ? DayText(start) : WithOffset(start)); changes["end"] = When(allDay, allDay ? DayText(end) : WithOffset(end));
                }
                if (changes.Count > 0) await store.UpdateEventAsync(item.Id, changes);
                if (targetKey != null && targetKey != item.CalendarKey)
                {
                    if (item.Recurring) throw new InvalidOperationException("a repeating event can't move to another calendar");
                    var moved = store.FindEvent(item.Id) ?? item; await store.MoveEventAsync(moved.Id, targetKey);
                }
            }
            catch (Exception changeError) { failed.Add($"{item.Title}: {changeError.Message}"); }
        }
        return failed;
    }
}
