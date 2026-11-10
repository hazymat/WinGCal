using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace WinGCal.Shell;

// Pinned (SPEC.md › Window › Pin): owned by the desktop so Show desktop (Win+D) leaves it showing, no taskbar button, kept at the bottom
// of the z-order, and locked in place. The window stays top-level, so the acrylic glass keeps working
public sealed class DesktopPin : IDisposable
{
    readonly IntPtr windowHandle;
    readonly WinEventProc foregroundCallback;
    IntPtr foregroundHook;
    public bool Pinned { get; private set; }
    public bool AllowPlacement { get; set; } // true while WinGCal itself moves the window (monitor setups)

    public DesktopPin(IntPtr handle)
    {
        windowHandle = handle;
        foregroundCallback = OnForegroundChanged;
        HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
    }

    public void Apply(bool pinned)
    {
        Pinned = pinned; AllowPlacement = true;
        var extendedStyle = GetWindowLongPtr(windowHandle, ExtendedStyleIndex).ToInt64();
        extendedStyle = pinned ? (extendedStyle | ToolWindowStyle) & ~AppWindowStyle : (extendedStyle & ~ToolWindowStyle) | AppWindowStyle;
        // A taskbar button only comes and goes while the window is hidden
        var visible = IsWindowVisible(windowHandle);
        if (visible) ShowWindow(windowHandle, HideWindow);
        SetWindowLongPtr(windowHandle, ExtendedStyleIndex, new IntPtr(extendedStyle));
        SetWindowLongPtr(windowHandle, OwnerIndex, pinned ? DesktopWindow() : IntPtr.Zero);
        if (visible) ShowWindow(windowHandle, ShowNoActivate);
        AllowPlacement = false;
        if (pinned) SendToBottom();
        if (pinned && foregroundHook == IntPtr.Zero) foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, IntPtr.Zero, foregroundCallback, 0, 0, OutOfContext);
        if (!pinned && foregroundHook != IntPtr.Zero) { UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
    }

    public void SendToBottom() => SetWindowPos(windowHandle, BottomWindow, 0, 0, 0, 0, NoSize | NoMove | NoActivate);

    // Progman owns the desktop icons; its owned windows are shown with the desktop rather than minimised
    static IntPtr DesktopWindow() { var progman = FindWindow("Progman", null); return progman != IntPtr.Zero ? progman : GetShellWindow(); }

    // Show desktop on some Windows builds still hides owned windows: when the desktop comes to the front, show the widget again without taking focus
    void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr foreground, int objectId, int childId, uint thread, uint time)
    {
        if (!Pinned) return;
        var className = new System.Text.StringBuilder(64); GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is not ("WorkerW" or "Progman")) return;
        if (IsIconic(windowHandle) || !IsWindowVisible(windowHandle)) ShowWindow(windowHandle, RestoreNoActivate);
        SendToBottom();
    }

    // Locked while pinned: no move, size, minimise or maximise from Windows (drag, snap, Win+arrows, Win+D, Win+M)
    IntPtr WindowHook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!Pinned) return IntPtr.Zero;
        if (message == WindowPositionChanging && !AllowPlacement)
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            position.Flags |= NoMove | NoSize;
            position.InsertAfter = BottomWindow; position.Flags &= ~NoZOrder;
            if ((position.Flags & HideFlag) != 0) position.Flags &= ~HideFlag; // stays showing
            Marshal.StructureToPtr(position, lParam, false);
        }
        if (message == SystemCommand)
        {
            var command = wParam.ToInt64() & 0xFFF0;
            if (command is MoveCommand or SizeCommand or MinimiseCommand or MaximiseCommand) { handled = true; return IntPtr.Zero; }
        }
        return IntPtr.Zero;
    }

    public void Dispose() { if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook); HwndSource.FromHwnd(windowHandle)?.RemoveHook(WindowHook); }

    // ---- Win32 ----
    const int ExtendedStyleIndex = -20, OwnerIndex = -8, HideWindow = 0, ShowNoActivate = 8, RestoreNoActivate = 4;
    const long ToolWindowStyle = 0x80, AppWindowStyle = 0x40000;
    const uint NoSize = 0x1, NoMove = 0x2, NoZOrder = 0x4, NoActivate = 0x10, HideFlag = 0x80, ForegroundEvent = 0x0003, OutOfContext = 0;
    const int WindowPositionChanging = 0x0046, SystemCommand = 0x0112;
    const long MoveCommand = 0xF010, SizeCommand = 0xF000, MinimiseCommand = 0xF020, MaximiseCommand = 0xF030;
    static readonly IntPtr BottomWindow = new(1);
    [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public IntPtr Handle; public IntPtr InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr handle, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
}
