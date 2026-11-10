using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using WinGCal.Settings;
using WinGCal.Shell;
namespace WinGCal.Google;

public record TaskListInfo(string Key, string Account, string Id, string Name);
public record TaskItem(string Id, string TaskId, string ListKey, string Title, string Notes, string Due, bool Completed, string CompletedAt, string Position, string Parent);

// Google Tasks beside the calendar (SPEC.md › Tasks): every list of every account that allowed task access, refreshed with the calendars
public sealed class TaskStore(GoogleAuth auth, Func<AppSettings> settings)
{
    const string apiBase = "https://tasks.googleapis.com/tasks/v1";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    public List<TaskListInfo> Lists { get; private set; } = new();
    public List<TaskItem> Items { get; private set; } = new();
    public event Action? Changed;
    static string CacheFile => Path.Combine(AppPaths.CacheFolder, "tasks.json");
    static readonly JsonSerializerOptions cacheJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public bool IsShown(string key) => !settings().Tasks.HiddenLists.Contains(key);

    public async Task SyncAsync()
    {
        var tasksSettings = settings().Tasks;
        if (!tasksSettings.Enabled) return;
        var lists = new List<TaskListInfo>(); var items = new List<TaskItem>();
        foreach (var email in tasksSettings.Accounts.Where(account => settings().Sync.Accounts.Contains(account)).ToList())
        {
            try
            {
                foreach (var listElement in await PagedAsync(email, $"{apiBase}/users/@me/lists?maxResults=100"))
                {
                    var list = new TaskListInfo($"{email}|{Text(listElement, "id")}", email, Text(listElement, "id"), Text(listElement, "title"));
                    lists.Add(list);
                    // Completed tasks from the last 30 days only, so the list doesn't fill with history
                    var completedMin = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
                    foreach (var task in await PagedAsync(email, $"{apiBase}/lists/{Uri.EscapeDataString(list.Id)}/tasks?maxResults=100&showCompleted=true&showHidden=true&completedMin={completedMin}"))
                        items.Add(ToItem(list, task));
                    foreach (var task in await PagedAsync(email, $"{apiBase}/lists/{Uri.EscapeDataString(list.Id)}/tasks?maxResults=100&showCompleted=false"))
                        if (!items.Any(existing => existing.Id == $"{list.Key}|{Text(task, "id")}")) items.Add(ToItem(list, task));
                }
            }
            catch (Exception tasksError) when (tasksError is not OperationCanceledException)
            {
                lists.AddRange(Lists.Where(list => list.Account == email)); items.AddRange(Items.Where(item => item.ListKey.StartsWith(email + "|")));
                if (tasksError is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized }) { tasksSettings.Accounts.Remove(email); settings().Save(); } // access was taken back
                else if (tasksError is not HttpRequestException { StatusCode: null }) CrashReporter.Write("Tasks " + email, tasksError, false);
            }
        }
        Lists = lists; Items = items; SaveCache(); Changed?.Invoke();
    }

    public async Task SetCompletedAsync(string itemId, bool completed)
    {
        if (Find(itemId) is not { } found) return;
        var body = completed ? (object)new { status = "completed" } : new { status = "needsAction", completed = (string?)null };
        await SendAsync(found.list.Account, new HttpRequestMessage(HttpMethod.Patch, $"{apiBase}/lists/{Uri.EscapeDataString(found.list.Id)}/tasks/{Uri.EscapeDataString(found.item.TaskId)}") { Content = JsonContent.Create(body) });
        Items = Items.Select(item => item.Id == itemId ? item with { Completed = completed, CompletedAt = completed ? DateTime.UtcNow.ToString("o") : "" } : item).ToList();
        SaveCache(); Changed?.Invoke();
    }

    public async Task AddAsync(string listKey, string title)
    {
        var list = Lists.FirstOrDefault(candidate => candidate.Key == listKey);
        if (list == null || title.Trim() == "") return;
        using var response = await SendAsync(list.Account, new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/lists/{Uri.EscapeDataString(list.Id)}/tasks") { Content = JsonContent.Create(new { title = title.Trim() }) });
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Items = new[] { ToItem(list, created.RootElement) }.Concat(Items).ToList();
        SaveCache(); Changed?.Invoke();
    }

    public async Task DeleteAsync(string itemId)
    {
        if (Find(itemId) is not { } found) return;
        await SendAsync(found.list.Account, new HttpRequestMessage(HttpMethod.Delete, $"{apiBase}/lists/{Uri.EscapeDataString(found.list.Id)}/tasks/{Uri.EscapeDataString(found.item.TaskId)}"));
        Items = Items.Where(item => item.Id != itemId).ToList();
        SaveCache(); Changed?.Invoke();
    }

    public void SetShown(string key, bool shown)
    {
        var hidden = settings().Tasks.HiddenLists;
        if (shown) hidden.Remove(key); else if (!hidden.Contains(key)) hidden.Add(key);
        settings().Save(); Changed?.Invoke();
    }

    (TaskItem item, TaskListInfo list)? Find(string itemId)
    {
        var item = Items.FirstOrDefault(candidate => candidate.Id == itemId);
        var list = item == null ? null : Lists.FirstOrDefault(candidate => candidate.Key == item.ListKey);
        return item == null || list == null ? null : (item, list);
    }

    static TaskItem ToItem(TaskListInfo list, JsonElement task) => new($"{list.Key}|{Text(task, "id")}", Text(task, "id"), list.Key, Text(task, "title"), Text(task, "notes"), Text(task, "due") is { Length: >= 10 } due ? due[..10] : "", Text(task, "status") == "completed", Text(task, "completed"), Text(task, "position"), Text(task, "parent"));

    async Task<List<JsonElement>> PagedAsync(string email, string url)
    {
        var items = new List<JsonElement>(); var pageToken = "";
        do
        {
            using var response = await SendAsync(email, new HttpRequestMessage(HttpMethod.Get, url + (pageToken != "" ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "")));
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.TryGetProperty("items", out var page)) foreach (var item in page.EnumerateArray()) items.Add(item.Clone());
            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() ?? "" : "";
        } while (pageToken != "");
        return items;
    }

    async Task<HttpResponseMessage> SendAsync(string email, HttpRequestMessage request)
    {
        using (request)
        {
            request.Headers.Authorization = new("Bearer", await auth.AccessTokenAsync(email));
            var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) { var status = response.StatusCode; response.Dispose(); throw new HttpRequestException($"Google Tasks said {(int)status}", null, status); }
            return response;
        }
    }

    static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    record CacheFile_(List<TaskListInfo> Lists, List<TaskItem> Items);
    void SaveCache() { try { File.WriteAllText(CacheFile, JsonSerializer.Serialize(new CacheFile_(Lists, Items), cacheJson)); } catch { } }
    public void LoadCache() { try { if (File.Exists(CacheFile) && JsonSerializer.Deserialize<CacheFile_>(File.ReadAllText(CacheFile), cacheJson) is { } cached) { Lists = cached.Lists; Items = cached.Items; } } catch { } }
}
