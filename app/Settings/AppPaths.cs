using System.IO;
namespace WinGCal.Settings;

public static class AppPaths
{
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WinGCal.exe");
    public static string ExeFolder => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;
#if PORTABLE
    public const bool IsPortable = true;
#else
    // portable.txt next to the exe also forces portable data, for a hand-copied folder
    public static readonly bool IsPortable = File.Exists(Path.Combine(ExeFolder, "portable.txt"));
#endif
    // Portable: data next to the exe. Installed: outside Velopack's own folder, so an update or reinstall never touches it
    public static readonly string DataFolder = IsPortable ? Path.Combine(ExeFolder, "WinGCal-data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGCal-data");
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static string TokensFile => Path.Combine(DataFolder, "accounts.bin"); // refresh tokens, DPAPI-encrypted for this Windows user
    public static string CacheFolder => Directory.CreateDirectory(Path.Combine(DataFolder, "cache")).FullName;
    public static string LogsFolder => Directory.CreateDirectory(Path.Combine(DataFolder, "logs")).FullName;
    public static string LocalClientFile => Path.Combine(DataFolder, "google-client.json"); // local builds: {"clientId": "...", "clientSecret": "..."}
}
