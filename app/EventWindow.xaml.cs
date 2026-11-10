using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinGCal.Google;
namespace WinGCal;

// Editing an event in its own window (own taskbar button, movable anywhere), like Settings; Save sends only what changed (SPEC.md › Event card)
public partial class EventWindow : Window
{
    readonly UiEvent original;
    readonly string originalNotes;
    readonly DateTime originalStart, originalEnd; // all-day: first and last day; timed: local times
    bool adjusting;
    public string EventId => original.Id;

    public EventWindow(UiEvent editedEvent, string focus)
    {
        InitializeComponent();
        original = editedEvent;
        var calendar = App.Instance.Store.Calendars.FirstOrDefault(candidate => candidate.Key == editedEvent.CalendarKey);
        calendarName.Text = calendar == null ? "" : $"{calendar.Name} · {calendar.Account}";
        try { calendarSwatch.Background = (Brush)new BrushConverter().ConvertFromString(editedEvent.Colour is { Length: > 0 } eventColour ? eventColour : calendar == null ? "#4f8ef7" : App.Instance.Store.ColourOf(calendar))!; } catch { calendarSwatch.Background = Brushes.SteelBlue; }
        Title = editedEvent.Title;
        originalNotes = NotesText(editedEvent.Description);
        if (editedEvent.AllDay) { originalStart = DateTime.ParseExact(editedEvent.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture); originalEnd = DateTime.ParseExact(editedEvent.End, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-1); }
        else { originalStart = DateTimeOffset.Parse(editedEvent.Start, CultureInfo.InvariantCulture).LocalDateTime; originalEnd = DateTimeOffset.Parse(editedEvent.End, CultureInfo.InvariantCulture).LocalDateTime; }
        // Times in quarter hours to pick from, or type any time
        var quarterHours = Enumerable.Range(0, 96).Select(quarter => TimeSpan.FromMinutes(quarter * 15).ToString(@"hh\:mm")).ToList();
        startTimeBox.ItemsSource = quarterHours; endTimeBox.ItemsSource = quarterHours;
        titleBox.Text = editedEvent.Title; locationBox.Text = editedEvent.Location; notesBox.Text = originalNotes;
        allDayBox.IsChecked = editedEvent.AllDay;
        startDatePicker.SelectedDate = originalStart.Date; endDatePicker.SelectedDate = originalEnd.Date;
        startTimeBox.Text = editedEvent.AllDay ? "09:00" : originalStart.ToString("HH:mm"); endTimeBox.Text = editedEvent.AllDay ? "10:00" : originalEnd.ToString("HH:mm");
        repeatNote.Visibility = editedEvent.Recurring ? Visibility.Visible : Visibility.Collapsed;
        ShowTimes();
        // Moving the start keeps the length, as Google Calendar does
        var lastStart = CurrentStart();
        void StartMoved() { if (adjusting) return; var newStart = CurrentStart(); if (newStart != null && lastStart != null && CurrentEnd() is { } end) { adjusting = true; var shifted = end + (newStart.Value - lastStart.Value); endDatePicker.SelectedDate = shifted.Date; endTimeBox.Text = shifted.ToString("HH:mm"); adjusting = false; } lastStart = newStart; Check(); }
        startDatePicker.SelectedDateChanged += (sender, args) => StartMoved();
        startTimeBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((sender, args) => StartMoved()));
        startTimeBox.SelectionChanged += (sender, args) => Dispatcher.BeginInvoke(StartMoved);
        endDatePicker.SelectedDateChanged += (sender, args) => Check();
        endTimeBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((sender, args) => Check()));
        endTimeBox.SelectionChanged += (sender, args) => Dispatcher.BeginInvoke(Check);
        allDayBox.Click += (sender, args) => { ShowTimes(); lastStart = CurrentStart(); Check(); };
        titleBox.TextChanged += (sender, args) => Title = titleBox.Text.Length > 0 ? titleBox.Text : "Edit event";
        saveButton.Click += async (sender, args) => await SaveAsync();
        cancelButton.Click += (sender, args) => Close();
        moreLink.Click += (sender, args) => { if (original.Link.Length > 0) Process.Start(new ProcessStartInfo(original.Link) { UseShellExecute = true }); };
        PreviewKeyDown += async (sender, keyArgs) => { if (keyArgs.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control || keyArgs.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { keyArgs.Handled = true; await SaveAsync(); } };
        Loaded += (sender, args) => { Control focused = focus switch { "when" => startDatePicker, "description" => notesBox, "location" => locationBox, _ => titleBox }; focused.Focus(); if (focused is TextBox box) box.CaretIndex = box.Text.Length; };
    }

    // Google descriptions are HTML: edited as plain text with line breaks kept
    static string NotesText(string html) => string.IsNullOrEmpty(html) ? "" : WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<(br|/p|/li|/div)\s*/?>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", "")).Trim();

    bool AllDay => allDayBox.IsChecked == true;
    void ShowTimes() { var timeVisibility = AllDay ? Visibility.Collapsed : Visibility.Visible; startTimeBox.Visibility = timeVisibility; endTimeBox.Visibility = timeVisibility; }
    internal static TimeSpan? ParseTime(string text) => TimeSpan.TryParseExact(text.Trim().Replace('.', ':'), new[] { @"h\:mm", @"hh\:mm", "hhmm", "%h" }, CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1) ? time : null;
    DateTime? At(DatePicker picker, ComboBox timeBox) => picker.SelectedDate is not { } date ? null : AllDay ? date.Date : ParseTime(timeBox.Text) is { } time ? date.Date + time : null;
    DateTime? CurrentStart() => At(startDatePicker, startTimeBox);
    DateTime? CurrentEnd() => At(endDatePicker, endTimeBox);

    string Problem()
    {
        if (startDatePicker.SelectedDate == null || endDatePicker.SelectedDate == null) return "Pick the dates";
        if (!AllDay && (ParseTime(startTimeBox.Text) == null || ParseTime(endTimeBox.Text) == null)) return "Type a time like 09:30";
        return AllDay ? (CurrentEnd() < CurrentStart() ? "Ends before it starts" : "") : (CurrentEnd() <= CurrentStart() ? "Ends before it starts" : "");
    }
    void Check() { var problem = Problem(); problemText.Text = problem; problemText.Visibility = problem.Length > 0 ? Visibility.Visible : Visibility.Collapsed; saveButton.IsEnabled = problem.Length == 0; }

    // "2026-11-10T14:00:00+00:00": local time with its offset, as Google wants it
    internal static string WithOffset(DateTime local) => new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    internal static string DayText(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    async Task SaveAsync()
    {
        if (!saveButton.IsEnabled || Problem().Length > 0) return;
        var changes = new JsonObject();
        if (titleBox.Text != original.Title) changes["summary"] = titleBox.Text;
        if (locationBox.Text != original.Location) changes["location"] = locationBox.Text;
        if (notesBox.Text.Trim() != originalNotes) changes["description"] = notesBox.Text.Trim();
        var start = CurrentStart()!.Value; var end = CurrentEnd()!.Value;
        if (AllDay != original.AllDay || start != originalStart || end != originalEnd)
        {
            // All-day ends are exclusive in Google: a one-day event ends the next day
            changes["start"] = AllDay ? new JsonObject { ["date"] = DayText(start), ["dateTime"] = null, ["timeZone"] = null } : new JsonObject { ["dateTime"] = WithOffset(start), ["date"] = null };
            changes["end"] = AllDay ? new JsonObject { ["date"] = DayText(end.AddDays(1)), ["dateTime"] = null, ["timeZone"] = null } : new JsonObject { ["dateTime"] = WithOffset(end), ["date"] = null };
        }
        if (changes.Count == 0) { Close(); return; }
        saveButton.IsEnabled = false; saveButton.Content = "Saving…"; saveError.Visibility = Visibility.Collapsed;
        try { await App.Instance.Store.UpdateEventAsync(original.Id, changes); Close(); }
        catch (Exception saveFailure) { saveError.Text = "Couldn't save the change: " + saveFailure.Message; saveError.Visibility = Visibility.Visible; saveButton.Content = "Save"; Check(); }
    }
}
