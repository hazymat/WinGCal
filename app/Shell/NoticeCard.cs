using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace WinGCal.Shell;

// WinGCal's own small card above the clock, never a Windows notification (SPEC.md › Window › Tray dots). The same card and colours as WinSwitch:
// dark glass, bottom right above the taskbar, a dot and edge in the colour of the tray dot that led here, one action on the right. Fades after 5 s unless the pointer is on it; never takes the keyboard
public sealed class NoticeCard : Window
{
    public const string MessageColour = "#E0912F", UpdateColour = "#4CC2FF"; // amber = something waiting (an event coming up), blue = an update: dot, menu row and card always match
    static NoticeCard? shownCard;
    public static bool IsShowing => shownCard != null;
    readonly DispatcherTimer fadeTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    // `asked` = shown by a click, so it appears even in Do not disturb or full screen: a click never does nothing
    public static void Show(string title, string text, string actionLabel, Action action, string accent, bool asked)
    {
        if (!asked && IsQuiet()) return;
        shownCard?.Close();
        var card = shownCard = new NoticeCard(title, text, actionLabel, action, accent);
        card.Closed += (sender, args) => { if (shownCard == card) shownCard = null; };
        card.ShowInCorner();
    }

    NoticeCard(string title, string text, string actionLabel, Action action, string accent)
    {
        var accentColour = (Color)ColorConverter.ConvertFromString(accent); var accentBrush = new SolidColorBrush(accentColour);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = 372; SizeToContent = SizeToContent.Height; FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var quietBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xC0, 0xCC));
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Children = { PulsingLamp(accentColour), new TextBlock { Text = "WinGCal", Foreground = quietBrush, FontSize = 11 } } };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF7)), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (text != "") body.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD0, 0xDA)), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var actionLink = new TextBlock { Text = actionLabel, Foreground = accentBrush, FontWeight = FontWeights.SemiBold, FontSize = 12.5, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        actionLink.MouseLeftButtonUp += (sender, args) => { Close(); action(); };
        var line = new DockPanel(); DockPanel.SetDock(actionLink, Dock.Right); line.Children.Add(actionLink); line.Children.Add(body);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x24, 0x2C)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, accentColour.R, accentColour.G, accentColour.B)), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(10), Child = new StackPanel { Children = { header, line } }, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.4 } };
        fadeTimer.Tick += (sender, args) => { if (IsMouseOver) return; fadeTimer.Stop(); var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)); fade.Completed += (fadeSender, fadeArgs) => Close(); BeginAnimation(OpacityProperty, fade); };
        SourceInitialized += (sender, args) => { var handle = new WindowInteropHelper(this).Handle; SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000080); }; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
    }

    // The card's dot is a small lamp that breathes like the blue update lamp in WinSqueeze's hi-fi mode (1.6 s a breath): a lit centre, the colour, a dark rim, and a glow that swells and fades
    static Border PulsingLamp(Color colour)
    {
        Color Shade(double towardsWhite, double towardsBlack) => Color.FromRgb((byte)((colour.R + (255 - colour.R) * towardsWhite) * (1 - towardsBlack)), (byte)((colour.G + (255 - colour.G) * towardsWhite) * (1 - towardsBlack)), (byte)((colour.B + (255 - colour.B) * towardsWhite) * (1 - towardsBlack)));
        var lampFill = new RadialGradientBrush { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.4, 0.35), RadiusX = 0.75, RadiusY = 0.75, GradientStops = { new GradientStop(Shade(0.75, 0), 0), new GradientStop(colour, 0.5), new GradientStop(Shade(0, 0.6), 1) } };
        var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = colour, ShadowDepth = 0, BlurRadius = 4, Opacity = 0.4 };
        var lamp = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = lampFill, Margin = new Thickness(1, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Effect = glow, Opacity = 0.7 };
        DoubleAnimation Breath(double low, double high) => new(low, high, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        lamp.Loaded += (sender, args) => { lamp.BeginAnimation(OpacityProperty, Breath(0.7, 1)); glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, Breath(4, 13)); glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, Breath(0.4, 0.95)); };
        return lamp;
    }

    // Bottom right of the main screen's work area. The window is moved onto that screen first so it takes that screen's scaling
    void ShowInCorner()
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, (int)SystemParameters.WorkArea.Width, (int)SystemParameters.WorkArea.Height);
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, 0, 0, 0x0015 /* NOSIZE NOZORDER NOACTIVATE */);
        Opacity = 0; Show();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX; var finalTop = area.Bottom / scale - ActualHeight - 6;
        Left = area.Right / scale - Width - 6; Top = finalTop + 18;
        BeginAnimation(TopProperty, new DoubleAnimation(finalTop, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        fadeTimer.Start();
    }

    // Full-screen apps, presentations and Do not disturb
    static bool IsQuiet() { try { return SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4 or 6; } catch { return false; } }
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
