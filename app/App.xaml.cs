using System.Windows;
using WinGCal.Google;
using WinGCal.Settings;
using WinGCal.Shell;
namespace WinGCal;

public partial class App : Application
{
    public static App Instance => (App)Current;
    public AppSettings Settings { get; private set; } = null!;
    public AppUpdater Updater { get; } = new();
    public CalendarStore Store { get; private set; } = null!;
    public bool IsQuitting { get; private set; }
    public bool Exporting { get; set; }
    public event Action? StatusChanged;
    public event Action? SettingsChanged;
    public event Action<string, string>? Tip;
    MainWindow mainWindow = null!;
    SettingsWindow? settingsWindow;
    Mutex? singleInstanceMutex;
    TrayIcon? trayIcon;

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        // One copy per data folder: a portable copy runs beside the installed one
        var instanceName = "WinGCal" + (AppPaths.IsPortable ? "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(AppPaths.DataFolder.ToLowerInvariant())))[..12] : "");
        singleInstanceMutex = new Mutex(true, instanceName + ".SingleInstance", out var isFirstInstance);
        var showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".ShowWindow");
        if (!isFirstInstance) { showWindowSignal.Set(); Shutdown(); return; } // a second launch brings the running one forward
        ThreadPool.RegisterWaitForSingleObject(showWindowSignal, (state, timedOut) => Dispatcher.BeginInvoke(ShowMainWindow), null, -1, false);
        if (AppPaths.IsPortable) AppUpdater.CleanUpAfterPortableUpdate();
        CrashReporter.Install(this);
        Settings = AppSettings.Load();
        try { StartupRegistration.Apply(Settings.General.StartWithWindows && !AppPaths.IsPortable); } catch { }
        Store = new CalendarStore(() => Settings);
        mainWindow = new MainWindow();
        ApplyTray();
        // Start hidden in the tray, or with no tray icon minimised to the taskbar (a pinned widget has no taskbar button, so it just shows)
        if (Settings.General.StartMinimised && Settings.General.ShowTrayIcon) mainWindow.EnsureHandle();
        else { if (Settings.General.StartMinimised && !Settings.Window.Pinned) mainWindow.WindowState = WindowState.Minimized; mainWindow.Show(); }
        Updater.Changed += () => Dispatcher.BeginInvoke(() => StatusChanged?.Invoke());
        Updater.Start();
        versionBeforeThisRun = Settings.General.LastRunVersion;
        if (Settings.General.LastRunVersion != Updater.CurrentVersion) { Settings.General.LastRunVersion = Updater.CurrentVersion; Settings.Save(); }
        Store.Start();
        if (CrashReporter.TakeUnseenReport()) ShowTip("WinGCal had a problem last time", "It crashed or stopped responding. Details are in Settings › About › Open logs (crash.log).");
    }

    // ---- Updates and versions ----
    string versionBeforeThisRun = "";
    bool whatsNewChecked;
    public async Task ShowWhatsNewAsync()
    {
        if (whatsNewChecked) return;
        whatsNewChecked = true;
        string previousVersion = versionBeforeThisRun, currentVersion = Updater.CurrentVersion;
        if (previousVersion == "" || !AppUpdater.IsNewer(currentVersion, previousVersion)) return;
        try
        {
            var releases = (await Updater.ListReleasesAsync(true)).Where(release => AppUpdater.IsNewer(release.Version, previousVersion) && !AppUpdater.IsNewer(release.Version, currentVersion)).OrderByDescending(release => Version.Parse(release.Version)).ToList();
            if (releases.Count > 0) mainWindow.PushWhatsNew(previousVersion, currentVersion, releases);
        }
        catch (Exception error) { CrashReporter.Write("What's new: release notes unavailable", error, false); }
    }

    public bool CanRestart()
    {
        if (Exporting) { MessageBox.Show("Wait for the export to finish, then restart to update.", "WinGCal", MessageBoxButton.OK, MessageBoxImage.Information); return false; }
        return true;
    }
    public Task<string[]> ResumeArgumentsAsync() => Task.FromResult(Array.Empty<string>()); // the window finds its own place from the monitor setup
    public void PrepareForRestart()
    {
        mainWindow.SavePlacement(); Settings.Save();
        IsQuitting = true;
        singleInstanceMutex?.ReleaseMutex(); singleInstanceMutex?.Dispose(); singleInstanceMutex = null;
    }

    public void ApplyWindowStyle() => mainWindow.ApplyWindowStyle();
    public void ApplySkin() => mainWindow.PushSkin();
    public void ApplyUiScale() => mainWindow.ApplyUiScale();
    public void ApplyPinned() => mainWindow.ApplyPinned();
    public void ApplyView() => mainWindow.PushPrefs();
    public void ApplyTasks() { mainWindow.PushTasks(); _ = Store.Tasks.SyncAsync(); }
    public double UiScale { get => mainWindow.UiScale; set => mainWindow.UiScale = value; }
    public Task AddAccountAsync() => mainWindow.AddAccountAsync();
    public void RefreshInactiveGlass() => mainWindow.RefreshInactiveGlass();
    public void ForgetSetup(string key) => mainWindow.ForgetSetup(key);
    public string CurrentSetupKey => mainWindow.CurrentSetupKey;
    public void NotifySettingsChanged() => SettingsChanged?.Invoke();
    public void ShowMainWindow() { if (!IsQuitting) mainWindow.ShowFromTray(); }

    // ---- Tray (SPEC.md › Window › Tray) ----
    public void ApplyTray()
    {
        if (Settings.General.ShowTrayIcon && trayIcon == null) trayIcon = new TrayIcon();
        if (!Settings.General.ShowTrayIcon && trayIcon != null) { trayIcon.Dispose(); trayIcon = null; if (!mainWindow.IsVisible) ShowMainWindow(); } // no icon: never leave it hidden
    }
    public bool MainWindowShowing => mainWindow.IsVisible;
    public void ToggleMainWindow() { if (mainWindow.IsVisible && mainWindow.IsActive || mainWindow.IsVisible && Settings.Window.Pinned) mainWindow.HideToTray(); else ShowMainWindow(); }
    public void SetPinned(bool pinned) { Settings.Window.Pinned = pinned; Settings.Save(); ShowMainWindow(); mainWindow.ApplyPinned(); NotifySettingsChanged(); }
    public void SetSkin(string skin) { Settings.Look.Skin = skin; Settings.Save(); ApplySkin(); ApplyWindowStyle(); NotifySettingsChanged(); }
    // Tips show in the window, or from the tray while the window is hidden
    public void ShowTip(string title, string text) { if (trayIcon != null && !mainWindow.IsVisible) trayIcon.ShowTip(title, text); else Tip?.Invoke(title, text); }

    public void OpenSettings(string tabName = "")
    {
        if (settingsWindow == null) { settingsWindow = new SettingsWindow(); settingsWindow.Closed += (sender, args) => settingsWindow = null; }
        settingsWindow.SelectTab(tabName);
        settingsWindow.Show(); settingsWindow.Activate();
    }

    public void Quit(bool windowClosing = false)
    {
        if (IsQuitting) return;
        if (Exporting && MessageBox.Show("An export is in progress. Quit anyway? The file won't be finished.", "WinGCal", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        IsQuitting = true;
        try { mainWindow.SavePlacement(); Settings.Save(); settingsWindow?.Close(); trayIcon?.Dispose(); trayIcon = null; if (!windowClosing) mainWindow.Close(); }
        catch (Exception quitError) { CrashReporter.Write("While quitting", quitError, false); }
        finally { Shutdown(); }
    }
}
