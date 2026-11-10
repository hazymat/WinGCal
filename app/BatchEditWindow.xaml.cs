using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinGCal.Google;
namespace WinGCal;

// Changing several picked events at once (SPEC.md › Multi-select): only the ticked fields change; times are worked out per event
public partial class BatchEditWindow : Window
{
    readonly List<UiEvent> events;
    App App => App.Instance;

    public BatchEditWindow(List<UiEvent> picked)
    {
        InitializeComponent();
        events = picked;
        heading.Text = events.Count == 1 ? "Change 1 event" : $"Change {events.Count} events"; Title = heading.Text;
        titlesText.Text = string.Join(" · ", events.Take(6).Select(item => item.Title)) + (events.Count > 6 ? $" and {events.Count - 6} more" : "");
        applyButton.Content = heading.Text;
        repeatNote.Visibility = events.Any(item => item.Recurring) ? Visibility.Visible : Visibility.Collapsed;
        var quarterHours = Enumerable.Range(0, 96).Select(quarter => TimeSpan.FromMinutes(quarter * 15).ToString(@"hh\:mm")).ToList();
        startTimeBox.ItemsSource = quarterHours; endTimeBox.ItemsSource = quarterHours;
        startTimeBox.Text = "09:00"; endTimeBox.Text = "10:00";
        datePicker.SelectedDate = DateTime.Today;
        // Another calendar of the same account (Google moves events only within an account)
        var accounts = events.Select(item => App.Store.Calendars.FirstOrDefault(calendar => calendar.Key == item.CalendarKey)?.Account).Distinct().ToList();
        var targets = accounts.Count == 1 ? App.Store.Calendars.Where(calendar => calendar.Account == accounts[0] && !calendar.ReadOnly).ToList() : new();
        calendarRow.Visibility = targets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        calendarBox.ItemsSource = targets; calendarBox.SelectedItem = targets.FirstOrDefault(calendar => calendar.Key == events[0].CalendarKey);
        // Changing a field ticks it
        datePicker.SelectedDateChanged += (sender, args) => dateTick.IsChecked = true;
        startTimeBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((sender, args) => { if (IsLoaded) startTick.IsChecked = true; }));
        startTimeBox.SelectionChanged += (sender, args) => startTick.IsChecked = true;
        endTimeBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((sender, args) => { if (IsLoaded) endTick.IsChecked = true; }));
        endTimeBox.SelectionChanged += (sender, args) => endTick.IsChecked = true;
        placeBox.TextChanged += (sender, args) => placeTick.IsChecked = true;
        calendarBox.SelectionChanged += (sender, args) => { if (IsLoaded) calendarTick.IsChecked = true; };
        cancelButton.Click += (sender, args) => Close();
        applyButton.Click += async (sender, args) => await ApplyAsync();
    }

    static (DateTime start, DateTime end) LocalTimes(UiEvent item) => item.AllDay
        ? (DateTime.ParseExact(item.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTime.ParseExact(item.End, "yyyy-MM-dd", CultureInfo.InvariantCulture))
        : (DateTimeOffset.Parse(item.Start, CultureInfo.InvariantCulture).LocalDateTime, DateTimeOffset.Parse(item.End, CultureInfo.InvariantCulture).LocalDateTime);

    void Status(string text, bool problem) { statusText.Text = text; statusText.Foreground = problem ? Brushes.Firebrick : Brushes.DimGray; statusText.Visibility = Visibility.Visible; }

    async Task ApplyAsync()
    {
        var newDate = dateTick.IsChecked == true ? datePicker.SelectedDate : null;
        var newStart = startTick.IsChecked == true ? EventWindow.ParseTime(startTimeBox.Text) : null;
        var newEnd = endTick.IsChecked == true ? EventWindow.ParseTime(endTimeBox.Text) : null;
        if (dateTick.IsChecked == true && newDate == null) { Status("Pick the date.", true); return; }
        if (startTick.IsChecked == true && newStart == null || endTick.IsChecked == true && newEnd == null) { Status("Type a time like 09:30.", true); return; }
        var target = calendarTick.IsChecked == true ? calendarBox.SelectedItem as CalendarInfo : null;
        if (dateTick.IsChecked != true && startTick.IsChecked != true && endTick.IsChecked != true && placeTick.IsChecked != true && target == null) { Close(); return; }
        applyButton.IsEnabled = false;
        var failed = new List<string>(); var done = 0;
        foreach (var item in events)
        {
            Status($"Changing {++done} of {events.Count}…", false);
            try
            {
                var changes = new JsonObject();
                if (placeTick.IsChecked == true) changes["location"] = placeBox.Text;
                if (newDate != null || newStart != null || newEnd != null)
                {
                    var (start, end) = LocalTimes(item); var allDay = item.AllDay;
                    var length = end - start;
                    if (newDate is { } date) { start = date.Date + start.TimeOfDay; end = start + length; }
                    if (newStart is { } startTime) { if (allDay) { allDay = false; length = TimeSpan.FromHours(1); } start = start.Date + startTime; end = start + length; }
                    if (newEnd is { } endTime && !allDay) { end = start.Date + endTime; if (end <= start) end = end.AddDays(1); }
                    changes["start"] = allDay ? new JsonObject { ["date"] = EventWindow.DayText(start), ["dateTime"] = null, ["timeZone"] = null } : new JsonObject { ["dateTime"] = EventWindow.WithOffset(start), ["date"] = null };
                    changes["end"] = allDay ? new JsonObject { ["date"] = EventWindow.DayText(end), ["dateTime"] = null, ["timeZone"] = null } : new JsonObject { ["dateTime"] = EventWindow.WithOffset(end), ["date"] = null };
                }
                if (changes.Count > 0) await App.Store.UpdateEventAsync(item.Id, changes);
                if (target != null && target.Key != item.CalendarKey)
                {
                    if (item.Recurring) throw new InvalidOperationException("a repeating event can't move to another calendar");
                    await App.Store.MoveEventAsync(item.Id, target.Key);
                }
            }
            catch (Exception changeError) { failed.Add($"{item.Title}: {changeError.Message}"); }
        }
        if (failed.Count == 0) { Close(); return; }
        Status((failed.Count == 1 ? "Couldn't change one event. " : $"Couldn't change {failed.Count} events. ") + string.Join(" ", failed), true);
        applyButton.IsEnabled = true;
    }
}
