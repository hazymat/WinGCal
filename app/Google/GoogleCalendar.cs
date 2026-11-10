using System.Net;
using System.Net.Http;
using System.Text.Json;
namespace WinGCal.Google;

// Google Calendar API v3 over plain HTTPS (no client library). Resources come back as Google's own JSON, untouched
public sealed class GoogleCalendar(GoogleAuth auth)
{
    const string apiBase = "https://www.googleapis.com/calendar/v3";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    // Every event type, so a backup has working-location, out-of-office, focus time and birthdays too
    static readonly string[] allEventTypes = { "default", "birthday", "focusTime", "fromGmail", "outOfOffice", "workingLocation" };

    public Task<List<JsonElement>> CalendarListAsync(string email, CancellationToken cancel) => PagedAsync(email, $"{apiBase}/users/me/calendarList?showHidden=true&maxResults=250", cancel);

    // Display: repeating events expanded into their instances, within the sync range
    public Task<List<JsonElement>> EventsAsync(string email, string calendarId, DateTime from, DateTime to, CancellationToken cancel) =>
        PagedAsync(email, $"{apiBase}/calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&maxResults=2500&timeMin={Uri.EscapeDataString(Rfc3339(from))}&timeMax={Uri.EscapeDataString(Rfc3339(to))}", cancel);

    // Export: everything, repeating events as their rule plus changed and cancelled instances
    public async Task<List<JsonElement>> AllEventsAsync(string email, string calendarId, CancellationToken cancel)
    {
        var types = string.Join("", allEventTypes.Select(type => "&eventTypes=" + type));
        var everything = await PagedAsync(email, $"{apiBase}/calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=false&showDeleted=true&maxResults=2500&maxAttendees=1000{types}", cancel);
        // showDeleted brings cancelled instances of repeating events (needed to rebuild them) but also deleted events: keep only the former
        return everything.Where(item => !(item.TryGetProperty("status", out var status) && status.GetString() == "cancelled" && !item.TryGetProperty("recurringEventId", out _))).ToList();
    }

    // The calendar's first ever event: one request, earliest instance from 1900
    public async Task<JsonElement?> FirstEventAsync(string email, string calendarId, CancellationToken cancel)
    {
        using var document = await GetAsync(email, $"{apiBase}/calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&orderBy=startTime&maxResults=1&timeMin={Uri.EscapeDataString("1900-01-01T00:00:00Z")}", cancel);
        return document.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0 ? items[0].Clone() : null;
    }

    public async Task DeleteEventAsync(string email, string calendarId, string eventId, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{apiBase}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}");
        using var response = await SendAsync(email, request, cancel);
        if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.Gone)) throw new HttpRequestException(await Describe(response), null, response.StatusCode);
    }

    async Task<List<JsonElement>> PagedAsync(string email, string url, CancellationToken cancel)
    {
        var items = new List<JsonElement>();
        var pageToken = "";
        do
        {
            using var document = await GetAsync(email, url + (pageToken != "" ? "&pageToken=" + Uri.EscapeDataString(pageToken) : ""), cancel);
            if (document.RootElement.TryGetProperty("items", out var page)) foreach (var item in page.EnumerateArray()) items.Add(item.Clone());
            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() ?? "" : "";
        } while (pageToken != "");
        return items;
    }

    async Task<JsonDocument> GetAsync(string email, string url, CancellationToken cancel)
    {
        // Rate limits and Google's brief 5xx: back off and retry a few times
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendAsync(email, request, cancel);
            if (response.IsSuccessStatusCode) return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            var retry = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout || (response.StatusCode == HttpStatusCode.Forbidden && (await response.Content.ReadAsStringAsync(cancel)).Contains("ateLimitExceeded"));
            if (!retry || attempt >= 5) throw new HttpRequestException(await Describe(response), null, response.StatusCode);
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancel);
        }
    }

    async Task<HttpResponseMessage> SendAsync(string email, HttpRequestMessage request, CancellationToken cancel)
    {
        request.Headers.Authorization = new("Bearer", await auth.AccessTokenAsync(email));
        return await http.SendAsync(request, cancel);
    }

    static async Task<string> Describe(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        try { using var document = JsonDocument.Parse(body); return $"Google said {(int)response.StatusCode}: {document.RootElement.GetProperty("error").GetProperty("message").GetString()}"; }
        catch { return $"Google said {(int)response.StatusCode}"; }
    }

    static string Rfc3339(DateTime time) => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
