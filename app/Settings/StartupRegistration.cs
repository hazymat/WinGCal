using Microsoft.Win32;
namespace WinGCal.Settings;

// Per-user "Start with Windows" via HKCU Run, no admin rights
public static class StartupRegistration
{
    const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string valueName = "WinGCal";
    public static void Apply(bool startWithWindows)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (startWithWindows) runKey.SetValue(valueName, $"\"{AppPaths.ExePath}\""); else runKey.DeleteValue(valueName, false);
    }
}
