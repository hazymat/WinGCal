using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace WinGCal.Settings;

// Where the widget sits on one monitor setup (SPEC.md › Monitor setups): physical pixels from the top-left of the monitor it's on
public class SetupPlacement
{
    public string Name { get; set; } = ""; // the monitors' names, for Settings › Window
    public string MonitorId { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximised { get; set; }
    public double UiScale { get; set; } = 1;
    public DateTime LastUsed { get; set; } = DateTime.Now;
}

public class GeneralSettings
{
    public bool AutoUpdate { get; set; } = true;
    public string UpdateChannel { get; set; } = "stable"; // stable | dev
    public bool StartWithWindows { get; set; } = true;
    public string LastRunVersion { get; set; } = "";
}

// Appearance, as WinSqueeze and WinTapeRecorder (SPEC.md › Window › Frameless and glass)
public class LookSettings
{
    public string Skin { get; set; } = "auto"; // auto | dark | light (glass) | paper | technical | wall (solid)
    [System.Text.Json.Serialization.JsonIgnore] public bool IsGlassSkin => Skin is "auto" or "dark" or "light";
    public string AccentColour { get; set; } = "";
    public bool Glass { get; set; } = true;
    public int GlassTransparency { get; set; } = -1; // 0..100, -1 = default
    public int GlassTintHue { get; set; } = 0; // 0 = neutral
    public bool GlassWhenInactive { get; set; } = true;
    public bool ShowTitleBar { get; set; } // frameless by default
    [JsonIgnore] public bool Frameless { get => !ShowTitleBar; set => ShowTitleBar = !value; }
}

public class WindowSettings
{
    public bool Pinned { get; set; } = true;
    public Dictionary<string, SetupPlacement> Setups { get; set; } = new(); // by monitor setup key
}

// What the calendar shows (SPEC.md › Calendar views)
public class ViewSettings
{
    public int WeekStart { get; set; } = -1; // 0 = Sunday, 1 = Monday; -1 = Windows' setting
    public string Clock { get; set; } = "auto"; // auto | 12 | 24
    public bool ShowDeclined { get; set; }
}

// SPEC.md › Google accounts and sync
public class SyncSettings
{
    public string BackMode { get; set; } = "months"; // months | date | all
    public int BackMonths { get; set; } = 2;
    public DateTime BackDate { get; set; } = new(2020, 1, 1);
    public int AheadYears { get; set; } = 1;
    public int RefreshMinutes { get; set; } = 5;
    public List<string> HiddenCalendars { get; set; } = new(); // "account|calendarId"
    public List<string> KnownCalendars { get; set; } = new(); // seen before: a new one starts shown or hidden as it is in Google Calendar
    public Dictionary<string, string> ColourOverrides { get; set; } = new();
    public List<string> Accounts { get; set; } = new(); // e-mail addresses, in the order added; tokens live in accounts.bin
}

// Google Tasks beside the calendar (SPEC.md › Tasks); off by default
public class TasksSettings
{
    public bool Enabled { get; set; }
    public List<string> Accounts { get; set; } = new(); // accounts that have allowed task access
    public List<string> HiddenLists { get; set; } = new(); // "account|listId"
}

public class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public LookSettings Look { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public ViewSettings View { get; set; } = new();
    public SyncSettings Sync { get; set; } = new();
    public TasksSettings Tasks { get; set; } = new();
    public List<string> DismissedHints { get; set; } = new();

    static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static AppSettings Load()
    {
        try { return File.Exists(AppPaths.SettingsFile) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), jsonOptions) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataFolder);
        var tempFile = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(tempFile, JsonSerializer.Serialize(this, jsonOptions));
        File.Move(tempFile, AppPaths.SettingsFile, true);
    }

    // The sync window: from (start of the range back) to (the limit ahead)
    public (DateTime from, DateTime to) SyncRange()
    {
        var today = DateTime.Today;
        var from = Sync.BackMode switch { "date" => Sync.BackDate, "all" => new DateTime(1900, 1, 1), _ => new DateTime(today.Year, today.Month, 1).AddMonths(-Math.Max(0, Sync.BackMonths)) };
        return (from, today.AddYears(Math.Clamp(Sync.AheadYears, 1, 10)));
    }
}
