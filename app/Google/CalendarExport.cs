using System.IO;
using System.Text.Json;
namespace WinGCal.Google;

// Full backup (SPEC.md › Export): every event of every chosen calendar, Google's own resources untouched, wrapped with where each came from
public static class CalendarExport
{
    public record Progress(string Calendar, int CalendarsDone, int CalendarsTotal, int Events);

    public static async Task<int> ExportAsync(CalendarStore store, string path, IReadOnlyCollection<string> accounts, string appVersion, IProgress<Progress> progress, CancellationToken cancel)
    {
        var lists = new List<(string email, List<JsonElement> calendars)>();
        foreach (var email in accounts) lists.Add((email, await store.Api.CalendarListAsync(email, cancel)));
        var total = lists.Sum(list => list.calendars.Count); int done = 0, eventCount = 0;
        var tempPath = path + ".part";
        await using (var file = File.Create(tempPath))
        await using (var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "wingcal-export"); writer.WriteNumber("version", 1); writer.WriteString("exportedAt", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")); writer.WriteString("app", "WinGCal " + appVersion);
            writer.WriteStartArray("accounts");
            foreach (var (email, calendars) in lists)
            {
                writer.WriteStartObject(); writer.WriteString("email", email); writer.WriteStartArray("calendars");
                foreach (var calendar in calendars)
                {
                    cancel.ThrowIfCancellationRequested();
                    var calendarId = calendar.GetProperty("id").GetString() ?? "";
                    var name = calendar.TryGetProperty("summary", out var summary) ? summary.GetString() ?? calendarId : calendarId;
                    progress.Report(new Progress(name, done, total, eventCount));
                    var events = await store.Api.AllEventsAsync(email, calendarId, cancel);
                    writer.WriteStartObject();
                    // The fields people look for first, then Google's whole calendar list entry and every event
                    foreach (var field in new[] { "id", "summary", "description", "timeZone", "accessRole", "primary", "backgroundColor", "foregroundColor", "defaultReminders", "notificationSettings" })
                        if (calendar.TryGetProperty(field, out var value)) { writer.WritePropertyName(field); value.WriteTo(writer); }
                    writer.WritePropertyName("calendarListEntry"); calendar.WriteTo(writer);
                    writer.WriteNumber("eventCount", events.Count);
                    writer.WriteStartArray("events"); foreach (var item in events) item.WriteTo(writer); writer.WriteEndArray();
                    writer.WriteEndObject();
                    eventCount += events.Count; done++;
                    await writer.FlushAsync(cancel);
                }
                writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteNumber("eventCount", eventCount); writer.WriteEndObject();
        }
        File.Move(tempPath, path, true);
        progress.Report(new Progress("", total, total, eventCount));
        return eventCount;
    }
}
