using System.Runtime.InteropServices;
namespace WinGCal.Shell;

// Clear glass draws its frost from the wallpaper, which is only right while the desktop itself is behind the window. This says whether any
// other app's window sits behind (below in the z-order and overlapping); then the shell uses Windows' own frost, which blurs whatever is really there
public static class WindowsBehind
{
    public static bool Any(IntPtr ownWindow)
    {
        if (ownWindow == IntPtr.Zero || DwmGetWindowAttribute(ownWindow, ExtendedFrameBounds, out Rect own, Marshal.SizeOf<Rect>()) != 0) return false;
        GetWindowThreadProcessId(ownWindow, out var ownProcess);
        for (var below = GetWindow(ownWindow, NextBelow); below != IntPtr.Zero; below = GetWindow(below, NextBelow))
        {
            var className = new System.Text.StringBuilder(64); GetClassName(below, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW") return false; // reached the desktop
            if (!IsWindowVisible(below) || IsIconic(below)) continue;
            GetWindowThreadProcessId(below, out var process); if (process == ownProcess) continue; // our own frost and event windows
            if (DwmGetWindowAttribute(below, Cloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue; // on another virtual desktop, or a hidden UWP shell
            if ((GetWindowLong(below, ExStyleIndex) & TransparentStyle) != 0) continue; // click-through overlays
            if (DwmGetWindowAttribute(below, ExtendedFrameBounds, out Rect other, Marshal.SizeOf<Rect>()) != 0) continue;
            if (other.Right - other.Left < 8 || other.Bottom - other.Top < 8) continue;
            if (other.Left < own.Right && other.Right > own.Left && other.Top < own.Bottom && other.Bottom > own.Top) return true;
        }
        return false;
    }
    const uint NextBelow = 2; const int ExtendedFrameBounds = 9, Cloaked = 14, ExStyleIndex = -20, TransparentStyle = 0x20;
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint relation);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int capacity);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
