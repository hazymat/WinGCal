using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinGCal.Settings;
namespace WinGCal.Google;

// Sign-in with Google for installed apps (SPEC.md › Google accounts and sync): the system browser, a loopback redirect and PKCE.
// Refresh tokens are kept per account, encrypted with DPAPI for this Windows user
public sealed class GoogleAuth
{
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar", TasksScope = "https://www.googleapis.com/auth/tasks";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    readonly Dictionary<string, (string token, DateTime expires)> accessTokens = new();
    readonly SemaphoreSlim refreshLock = new(1, 1);
    Dictionary<string, string> refreshTokens = LoadTokens();

    // Built in by CI from repository secrets; local builds read google-client.json from the data folder
    public static (string id, string secret) Client
    {
        get
        {
            var metadata = typeof(GoogleAuth).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? "");
            if (metadata.GetValueOrDefault("GoogleClientId", "") is { Length: > 0 } builtInId) return (builtInId, metadata.GetValueOrDefault("GoogleClientSecret", ""));
            try
            {
                using var local = JsonDocument.Parse(File.ReadAllText(AppPaths.LocalClientFile));
                return (local.RootElement.GetProperty("clientId").GetString() ?? "", local.RootElement.TryGetProperty("clientSecret", out var secret) ? secret.GetString() ?? "" : "");
            }
            catch { return ("", ""); }
        }
    }
    public static bool HasClient => Client.id != "";
    public IReadOnlyCollection<string> Accounts => refreshTokens.Keys;

    // Opens Google's sign-in page and waits (up to 5 minutes) for the browser to come back. Returns the account's e-mail address and the scopes granted.
    // Tasks access is asked for only when the task list is turned on (incremental: the account keeps its calendar access)
    public async Task<(string email, string scopes)> SignInAsync(CancellationToken cancel, bool withTasks = false, string loginHint = "")
    {
        var (clientId, clientSecret) = Client;
        if (clientId == "") throw new InvalidOperationException("This build has no Google sign-in client yet.");
        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        using var listener = new HttpListener(); listener.Prefixes.Add(redirect); listener.Start();
        var authUrl = "https://accounts.google.com/o/oauth2/v2/auth?" + Query(new() { ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = $"openid email {CalendarScope}" + (withTasks ? " " + TasksScope : ""), ["include_granted_scopes"] = "true", ["login_hint"] = loginHint, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["access_type"] = "offline", ["prompt"] = "consent select_account", ["state"] = state });
        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var contextTask = listener.GetContextAsync();
        if (await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeout.Token)) != contextTask) throw new TimeoutException("Sign-in wasn't finished in the browser.");
        var context = await contextTask;
        var code = context.Request.QueryString["code"] ?? ""; var error = context.Request.QueryString["error"] ?? "";
        var ok = code != "" && context.Request.QueryString["state"] == state;
        var page = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>WinGCal</title><body style=\"font:16px Segoe UI,sans-serif;background:#10131a;color:#e8ebf2;display:grid;place-items:center;height:90vh\"><div>{(ok ? "Signed in. You can close this tab and go back to WinGCal." : "Sign-in didn't finish" + (error != "" ? $" ({WebUtility.HtmlEncode(error)})" : "") + ". You can close this tab.")}</div>");
        context.Response.ContentType = "text/html; charset=utf-8"; await context.Response.OutputStream.WriteAsync(page, cancel); context.Response.Close();
        if (!ok) throw new InvalidOperationException(error == "access_denied" ? "Access wasn't allowed." : "Sign-in didn't finish.");
        using var tokenDocument = await PostTokenAsync(new() { ["client_id"] = clientId, ["client_secret"] = clientSecret, ["code"] = code, ["code_verifier"] = verifier, ["grant_type"] = "authorization_code", ["redirect_uri"] = redirect });
        var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString() ?? "";
        var refreshToken = tokenDocument.RootElement.TryGetProperty("refresh_token", out var refreshValue) ? refreshValue.GetString() ?? "" : "";
        var granted = tokenDocument.RootElement.TryGetProperty("scope", out var scopes) ? scopes.GetString() ?? "" : "";
        if (!granted.Contains(CalendarScope)) throw new InvalidOperationException("Calendar access wasn't ticked on Google's page. Sign in again and tick it.");
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo"); userRequest.Headers.Authorization = new("Bearer", accessToken);
        using var userResponse = await http.SendAsync(userRequest, cancel); userResponse.EnsureSuccessStatusCode();
        using var user = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(cancel));
        var email = user.RootElement.GetProperty("email").GetString() ?? "";
        if (refreshToken != "") refreshTokens[email] = refreshToken;
        accessTokens[email] = (accessToken, DateTime.UtcNow.AddSeconds(tokenDocument.RootElement.GetProperty("expires_in").GetInt32() - 60));
        SaveTokens();
        return (email, granted);
    }

    // A current access token, refreshed when it's within a minute of expiring
    public async Task<string> AccessTokenAsync(string email)
    {
        await refreshLock.WaitAsync();
        try
        {
            if (accessTokens.TryGetValue(email, out var cached) && cached.expires > DateTime.UtcNow) return cached.token;
            if (!refreshTokens.TryGetValue(email, out var refreshToken)) throw new SignInNeededException(email);
            var (clientId, clientSecret) = Client;
            JsonDocument tokenDocument;
            try { tokenDocument = await PostTokenAsync(new() { ["client_id"] = clientId, ["client_secret"] = clientSecret, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token" }); }
            catch (GoogleTokenException tokenError) when (tokenError.Error == "invalid_grant") { throw new SignInNeededException(email); } // revoked, or expired (consent screen left in Testing)
            using (tokenDocument)
            {
                var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString() ?? "";
                accessTokens[email] = (accessToken, DateTime.UtcNow.AddSeconds(tokenDocument.RootElement.GetProperty("expires_in").GetInt32() - 60));
                return accessToken;
            }
        }
        finally { refreshLock.Release(); }
    }

    public async Task SignOutAsync(string email)
    {
        if (refreshTokens.TryGetValue(email, out var refreshToken)) try { await http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken })); } catch { }
        refreshTokens.Remove(email); accessTokens.Remove(email); SaveTokens();
    }

    static async Task<JsonDocument> PostTokenAsync(Dictionary<string, string> form)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) { string error = ""; try { using var errorDocument = JsonDocument.Parse(body); error = errorDocument.RootElement.GetProperty("error").GetString() ?? ""; } catch { } throw new GoogleTokenException(error, body); }
        return JsonDocument.Parse(body);
    }

    static Dictionary<string, string> LoadTokens()
    {
        try { return File.Exists(AppPaths.TokensFile) ? JsonSerializer.Deserialize<Dictionary<string, string>>(ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.TokensFile), null, DataProtectionScope.CurrentUser)) ?? new() : new(); }
        catch { return new(); }
    }
    void SaveTokens() { Directory.CreateDirectory(AppPaths.DataFolder); File.WriteAllBytes(AppPaths.TokensFile, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(refreshTokens), null, DataProtectionScope.CurrentUser)); }
    static int FreePort() { var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); return port; }
    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string Query(Dictionary<string, string> values) => string.Join("&", values.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
}

public class SignInNeededException(string email) : Exception($"{email} needs signing in again.") { public string Email { get; } = email; }
public class GoogleTokenException(string error, string body) : Exception($"Google refused the sign-in ({error}).") { public string Error { get; } = error; public string Body { get; } = body; }
