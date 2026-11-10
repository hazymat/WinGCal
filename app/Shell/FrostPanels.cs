using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace WinGCal.Shell;

// Clear glass (SPEC.md › Window › Clear glass). Windows frosts a whole window evenly, so the main window is left clear and each
// frosted panel the page reports gets its own borderless acrylic window, kept exactly behind it (just below the main window in the z-order).
// They take no clicks and no focus; the page draws the panels' tint and edges on top
public sealed class FrostPanels : IDisposable
{
    public record Panel(int X, int Y, int Width, int Height);
    readonly Window owner;
    readonly List<Window> backings = new();
    List<Panel> panels = new();
    bool enabled, darkTheme = true;

    public FrostPanels(Window mainWindow)
    {
        owner = mainWindow;
        HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle)?.AddHook(OwnerHook);
        owner.IsVisibleChanged += (sender, args) => Sync();
        owner.StateChanged += (sender, args) => Sync();
        owner.Closed += (sender, args) => Dispose();
    }

    public void SetEnabled(bool on, bool dark) { enabled = on; if (dark != darkTheme) { darkTheme = dark; foreach (var backing in backings) Style(backing); } Sync(); }
    public void Update(List<Panel> reported) { panels = reported; Sync(); }

    // Places the backing windows behind the panels, in screen pixels from the page's top-left corner
    public void Sync()
    {
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        var showing = enabled && owner.IsVisible && owner.WindowState != WindowState.Minimized && ownerHandle != IntPtr.Zero;
        var origin = showing && owner.Content is FrameworkElement content && PresentationSource.FromVisual(content) != null ? content.PointToScreen(new Point(0, 0)) : new Point();
        var wanted = showing ? panels : new List<Panel>();
        // Pinned, the widget sits at the bottom of the desktop: "just below the widget" can't go lower than the bottom, so the frost goes to the very bottom instead (still behind the widget)
        var insertAfter = GetWindow(ownerHandle, OwnerRelation) != IntPtr.Zero ? BottomWindow : ownerHandle;
        while (backings.Count < wanted.Count) backings.Add(CreateBacking());
        for (var index = 0; index < backings.Count; index++)
        {
            var handle = new WindowInteropHelper(backings[index]).Handle;
            if (index >= wanted.Count) { ShowWindow(handle, HideWindow); continue; }
            var panel = wanted[index];
            SetWindowPos(handle, insertAfter, (int)origin.X + panel.X, (int)origin.Y + panel.Y, panel.Width, panel.Height, NoActivate | ShowWindowFlag | NoOwnerZOrder);
        }
    }

    Window CreateBacking()
    {
        var backing = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Focusable = false, Background = Brushes.Transparent, Width = 1, Height = 1, Left = -32000, Top = -32000, Title = "WinGCal frost" };
        var handle = new WindowInteropHelper(backing).EnsureHandle();
        var extendedStyle = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64() | ToolWindowStyle | NoActivateStyle | TransparentStyle;
        SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(extendedStyle));
        // Owned by the desktop, like the pinned widget, so Show desktop leaves the frost where the widget is
        SetWindowLongPtr(handle, OwnerIndex, FindWindow("Progman", null));
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source?.AddHook(BackingHook);
        Style(backing);
        backing.Show();
        return backing;
    }

    void Style(Window backing)
    {
        var handle = new WindowInteropHelper(backing).Handle;
        var dark = darkTheme ? 1 : 0; DwmSetWindowAttribute(handle, DarkModeAttribute, ref dark, sizeof(int));
        var corners = RoundCorners; DwmSetWindowAttribute(handle, CornerAttribute, ref corners, sizeof(int));
        var noBorder = unchecked((int)0xFFFFFFFE); DwmSetWindowAttribute(handle, BorderColourAttribute, ref noBorder, sizeof(int));
        var backdrop = AcrylicBackdrop; DwmSetWindowAttribute(handle, BackdropAttribute, ref backdrop, sizeof(int));
        var margins = new FrameMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 }; DwmExtendFrameIntoClientArea(handle, ref margins);
        DefWindowProc(handle, NonClientActivate, (IntPtr)1, (IntPtr)(-1)); // acrylic only shows on an "active" frame
    }

    // The frost never looks inactive, and never takes the mouse
    IntPtr BackingHook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NonClientActivate && wParam == IntPtr.Zero) { handled = true; return DefWindowProc(handle, message, (IntPtr)1, (IntPtr)(-1)); }
        if (message == MouseActivate) { handled = true; return (IntPtr)NoActivateAnswer; }
        return IntPtr.Zero;
    }
    // Follow the main window as it moves, resizes or changes place in the z-order
    IntPtr OwnerHook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WindowPositionChanged && enabled && panels.Count > 0) Sync();
        return IntPtr.Zero;
    }

    public void Dispose() { foreach (var backing in backings) backing.Close(); backings.Clear(); }

    // ---- Win32 ----
    const int ExtendedStyleIndex = -20, OwnerIndex = -8, HideWindow = 0, NonClientActivate = 0x0086, MouseActivate = 0x0021, NoActivateAnswer = 3, WindowPositionChanged = 0x0047;
    const long ToolWindowStyle = 0x80, NoActivateStyle = 0x08000000, TransparentStyle = 0x20;
    const uint NoActivate = 0x10, ShowWindowFlag = 0x40, NoOwnerZOrder = 0x200;
    const int DarkModeAttribute = 20, CornerAttribute = 33, RoundCorners = 2, BorderColourAttribute = 34, BackdropAttribute = 38, AcrylicBackdrop = 3;
    struct FrameMargins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref FrameMargins margins);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr handle, uint relation);
    const uint OwnerRelation = 4; static readonly IntPtr BottomWindow = new(1);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
}
