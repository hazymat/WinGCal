using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinGCal.Google;
namespace WinGCal.Shell;

// WinGCal's own notifications (SPEC.md › Next event › Pop-out and sounds), no Windows toasts: as the next event from a Notify calendar nears, the countdown capsule pops out as a small window over everything else and a sound plays.
// Three moments: when the lead time is reached, five minutes before, and at start time. The capsule stays a while each time (longer at start; while the pointer is on it), then slides away
public sealed class NextAlert
{
    static readonly TimeSpan linger = TimeSpan.FromMinutes(10); // as the capsule in the calendar: until ten minutes after the start
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly HashSet<string> fired = new(), dismissed = new();
    AlertCapsule? capsule; DateTime showUntil; string shownKey = ""; bool askedFor; // askedFor: brought up from the tray, so shown even with the pop-out turned off
    App App => App.Instance;

    public void Start() { timer.Tick += (sender, args) => Tick(); timer.Start(); }
    // × on either capsule (this one or the calendar's) lets that event go everywhere
    public void Dismiss(string key, bool tellCalendar) { dismissed.Add(key); if (shownKey == key) Hide(); if (tellCalendar) App.MainShell.PushNextDismissed(key); }
    static string KeyOf(UiEvent item) => item.Id + item.Start;
    public static DateTime StartOf(UiEvent item) => DateTimeOffset.Parse(item.Start, CultureInfo.InvariantCulture).LocalDateTime;
    static DateTime EndOf(UiEvent item) => DateTimeOffset.Parse(item.End, CultureInfo.InvariantCulture).LocalDateTime;

    UiEvent? Next(DateTime now)
    {
        var store = App.Store; var lead = TimeSpan.FromMinutes(App.Settings.View.NextLead);
        return store.EventsByCalendar.Values.SelectMany(list => list)
            .Where(item => !item.AllDay && !item.Declined && store.IsNotified(item.CalendarKey) && store.IsShown(item.CalendarKey) && !dismissed.Contains(KeyOf(item)))
            .Select(item => (item, start: StartOf(item), end: EndOf(item)))
            .Where(found => found.start - now <= lead && now < (found.end < found.start + linger ? found.end : found.start + linger))
            .OrderBy(found => found.start).Select(found => found.item).FirstOrDefault();
    }

    // What the tray's amber dot stands for: the next event inside its lead time that hasn't started (the dot always goes at the start time) or been dismissed
    public UiEvent? Upcoming { get { try { var now = DateTime.Now; return Next(now) is { } next && StartOf(next) > now ? next : null; } catch { return null; } } }
    public bool Showing => capsule != null;
    // The tray icon or its menu row: bring the countdown capsule up now
    public void ShowNow() { if (Upcoming is not { } next) return; shownKey = KeyOf(next); showUntil = DateTime.Now.AddSeconds(20); askedFor = true; capsule ??= new AlertCapsule(this); Tick(); }

    void Tick()
    {
        try
        {
            var now = DateTime.Now; var view = App.Settings.View; var next = Next(now);
            if (next == null) { Hide(); return; }
            var key = KeyOf(next); var secondsLeft = (StartOf(next) - now).TotalSeconds;
            var moment = secondsLeft <= 0 ? "start" : secondsLeft <= 300 && view.NextLead > 5 ? "five" : "lead";
            if (fired.Add(key + moment) && secondsLeft > -60) // an event already under way when WinGCal starts isn't announced again
            {
                Chime.Play(view.NextSound, moment == "start", view.NextVolume);
                if (view.NextPopOut) { askedFor = false; shownKey = key; showUntil = now.AddSeconds(moment == "start" ? 60 : 20); capsule ??= new AlertCapsule(this); }
            }
            if (capsule == null) return;
            if (shownKey != key || !view.NextPopOut && !askedFor || (now > showUntil && !capsule.IsMouseOver)) { Hide(); return; }
            capsule.Show(next, key, secondsLeft, Math.Clamp(secondsLeft / (view.NextLead * 60.0), 0, 1), ColourFor(next), view.NextStrength);
        }
        catch (Exception alertError) { CrashReporter.Write("Next event pop-out", alertError, false); }
    }
    void Hide() { if (capsule == null) return; var leaving = capsule; capsule = null; shownKey = ""; askedFor = false; leaving.SlideAway(); }

    // As the capsule in the calendar: its own amber, the calendar's colour, the accent, or one chosen (Settings › Calendars › Next event)
    Color ColourFor(UiEvent item)
    {
        var choice = App.Settings.View.NextColour; var store = App.Store;
        var hex = choice == "accent" ? (App.Settings.Look.AccentColour is { Length: > 0 } accent ? accent : "#5ccfff") : choice == "calendar" ? (item.Colour is { Length: > 0 } own ? own : store.Calendars.FirstOrDefault(calendar => calendar.Key == item.CalendarKey) is { } calendar ? store.ColourOf(calendar) : "#ffb224") : choice.StartsWith('#') ? choice : "#ffb224";
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return Color.FromRgb(0xff, 0xb2, 0x24); }
    }
}

// The capsule itself, drawn by WPF so its round edge and glow are smooth over whatever is behind: a dark dial with the time to go and a ring running down, the event's name and start time, a round button to join its video call, and ×
sealed class AlertCapsule : Window
{
    static readonly Color ink = Color.FromRgb(0x1a, 0x12, 0x06);
    readonly NextAlert owner;
    readonly Border pill = new() { Height = 54, CornerRadius = new CornerRadius(27), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24, 14, 24, 30), Cursor = Cursors.Hand, RenderTransform = new TranslateTransform(0, -70) };
    readonly Path ring = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    readonly TextBlock number = new() { FontSize = 16, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center }, unit = new() { FontSize = 8, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromArgb(0xc0, 0xff, 0xff, 0xff)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
    readonly TextBlock title = new() { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(ink), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 }, when = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink), Opacity = 0.72 };
    readonly Border join;
    string eventId = "", key = "", meetUrl = ""; bool placed, leaving;

    public AlertCapsule(NextAlert owner)
    {
        this.owner = owner;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Title = "Next event"; UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = -10000; Top = -10000; // off-screen until it is measured and placed, so it never flashes somewhere else first
        var dial = new Grid { Width = 44, Height = 44, Margin = new Thickness(5, 0, 10, 0) };
        dial.Children.Add(new Ellipse { Fill = new SolidColorBrush(ink) });
        dial.Children.Add(new Ellipse { Margin = new Thickness(3), Stroke = new SolidColorBrush(Color.FromArgb(0x29, 0xff, 0xff, 0xff)), StrokeThickness = 3 });
        dial.Children.Add(ring);
        var figures = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; figures.Children.Add(number); figures.Children.Add(unit); dial.Children.Add(figures);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }; words.Children.Add(title); words.Children.Add(when);
        join = RoundButton("", 30, 13, new SolidColorBrush(ink), Brushes.White, "Join the video call"); join.Margin = new Thickness(0, 0, 4, 0);
        join.MouseLeftButtonUp += (sender, args) => { args.Handled = true; if (meetUrl != "") MainWindow.OpenLink(meetUrl); };
        var close = RoundButton("", 26, 10, Brushes.Transparent, new SolidColorBrush(ink), "Dismiss"); close.Margin = new Thickness(0, 0, 8, 0); close.Opacity = 0.65;
        close.MouseLeftButtonUp += (sender, args) => { args.Handled = true; owner.Dismiss(key, true); };
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(dial); row.Children.Add(words); row.Children.Add(join); row.Children.Add(close);
        pill.Child = row; pill.ToolTip = "Open this event";
        pill.MouseLeftButtonUp += (sender, args) => { if (eventId != "") App.Instance.OpenEventEditor(eventId); };
        Content = pill;
        SourceInitialized += (sender, args) => { var handle = new WindowInteropHelper(this).Handle; SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000080); }; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: never takes the keyboard, not in Alt+Tab
    }
    static Border RoundButton(string glyph, double size, double glyphSize, Brush fill, Brush glyphBrush, string tip) => new() { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip, Cursor = Cursors.Hand, Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = glyphSize, Foreground = glyphBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };

    public void Show(UiEvent item, string eventKey, double secondsLeft, double share, Color colour, int strength)
    {
        eventId = item.Id; key = eventKey; meetUrl = item.MeetUrl; join.Visibility = meetUrl != "" ? Visibility.Visible : Visibility.Collapsed;
        var left = (int)Math.Round(secondsLeft); var start = DateTimeOffset.Parse(item.Start, CultureInfo.InvariantCulture).LocalDateTime;
        (number.Text, unit.Text) = left <= 0 ? ("GO", "NOW") : left < 100 ? (left.ToString(), "SEC") : left < 5970 ? (Math.Ceiling(left / 60.0).ToString(), "MIN") : ((left / 3600.0).ToString(left < 36000 ? "0.#" : "0", CultureInfo.InvariantCulture), "HRS");
        title.Text = item.Title; when.Text = left > 0 ? "starts " + start.ToShortTimeString() : left > -60 ? "Now" : $"Started {-left / 60} min ago";
        // Boldness: from the colour mixed into a dark panel to the full colour, running towards a warmer pink on the right
        var mix = 0.45 + strength * 0.0055; var panel = Color.FromRgb(0x23, 0x26, 0x2e);
        pill.Background = new LinearGradientBrush(Mix(panel, colour, mix), Mix(panel, Mix(Color.FromRgb(0xff, 0x3d, 0x6e), colour, 0.78), mix), 35);
        pill.Effect = new DropShadowEffect { Color = colour, BlurRadius = left < 300 ? 30 : 22, ShadowDepth = 4, Direction = 270, Opacity = left < 300 ? 0.75 : 0.45 };
        ring.Stroke = new SolidColorBrush(colour); ring.Data = Arc(share);
        if (!IsVisible) { Show(); SlideIn(); }
        if (!placed) Place();
    }
    static Color Mix(Color from, Color to, double share) => Color.FromRgb((byte)(from.R + (to.R - from.R) * share), (byte)(from.G + (to.G - from.G) * share), (byte)(from.B + (to.B - from.B) * share));
    // What's left of the lead time, clockwise from the top, inside the 44 px dial
    static Geometry Arc(double share)
    {
        const double centre = 22, radius = 17.5;
        if (share >= 0.999) return new EllipseGeometry(new Point(centre, centre), radius, radius);
        if (share <= 0.001) return Geometry.Empty;
        var angle = share * 2 * Math.PI; var end = new Point(centre + radius * Math.Sin(angle), centre - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(centre, centre - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, share > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    // Top centre of the calendar's monitor, in screen pixels (WPF's own Left/Top use the main screen's scale, wrong where monitors differ)
    void Place()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero || ActualWidth <= 0) return;
        var calendar = new WindowInteropHelper(App.Instance.MainShell).Handle;
        var area = (calendar != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(calendar) : System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0]).WorkingArea;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        SetWindowPos(handle, IntPtr.Zero, area.Left + (area.Width - (int)(ActualWidth * scale)) / 2, area.Top + (int)(4 * scale), 0, 0, 0x0015 /*NOSIZE|NOZORDER|NOACTIVATE*/);
        placed = true;
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); placed = false; Place(); } // a longer name: stay centred

    void SlideIn() { var drop = new DoubleAnimation(-70, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } }; ((TranslateTransform)pill.RenderTransform).BeginAnimation(TranslateTransform.YProperty, drop); }
    public void SlideAway()
    {
        if (leaving) return; leaving = true;
        var lift = new DoubleAnimation(-70, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        lift.Completed += (sender, args) => Close();
        ((TranslateTransform)pill.RenderTransform).BeginAnimation(TranslateTransform.YProperty, lift);
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
