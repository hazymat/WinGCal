using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinGCal.Google;
using WinGCal.Settings;
using WinGCal.Shell;
namespace WinGCal;

// Changes apply immediately (no OK/Apply), as WinSqueeze (SPEC.md › Settings window)
public partial class SettingsWindow : Window
{
    App App => App.Instance;
    AppSettings Settings => App.Settings;
    CancellationTokenSource? exportCancel;

    // Dragging a look slider fires on every step: the calendar is redrawn at most every 50 ms, and saved once the drag rests
    System.Windows.Threading.DispatcherTimer? lookApplyTimer, lookSaveTimer;
    bool restyleWindow;
    void LookChangedSoon(bool restyle = false)
    {
        restyleWindow |= restyle;
        lookApplyTimer ??= NewTimer(50, () => { if (restyleWindow) App.ApplyWindowStyle(); else App.ApplySkin(); restyleWindow = false; }); lookSaveTimer ??= NewTimer(400, () => Settings.Save());
        if (!lookApplyTimer.IsEnabled) lookApplyTimer.Start();
        lookSaveTimer.Stop(); lookSaveTimer.Start();
    }
    System.Windows.Threading.DispatcherTimer NewTimer(int milliseconds, Action action) { var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) }; timer.Tick += (sender, args) => { timer.Stop(); action(); }; return timer; }
    public SettingsWindow()
    {
        InitializeComponent(); settingsTabs.SelectedItem = App.Settings.Sync.Accounts.Count == 0 ? accountsTab : appearanceTab;
        defaultCalendarBox.SelectionChanged += (sender, args) => { if (fillingDefault || defaultCalendarBox.SelectedValue is not string key) return; Settings.Sync.DefaultCalendar = key; Settings.Save(); };
        WireAccountsTab(); WireSyncTab(); WireTasksTab(); WireExportTab(); WireAppearanceTab(); WireViewTab(); WireWindowTab(); WireAboutTab();
        FillLists();
        // Esc closes (settings save as they change); an open drop-down takes the Esc first and marks it handled
        KeyDown += (sender, keyArgs) => { if (keyArgs.Key == System.Windows.Input.Key.Escape && !keyArgs.Handled) { keyArgs.Handled = true; Close(); } };
        App.Store.Changed += OnStoreChanged; App.Store.Tasks.Changed += OnStoreChanged; App.SettingsChanged += OnStoreChanged; ChangeHistory.Changed += OnHistoryChanged;
        Closed += (sender, args) => { App.Store.Changed -= OnStoreChanged; App.Store.Tasks.Changed -= OnStoreChanged; App.SettingsChanged -= OnStoreChanged; ChangeHistory.Changed -= OnHistoryChanged; };
        FillHistory();
    }
    void OnStoreChanged() => Dispatcher.BeginInvoke(() => { FillLists(); if (Math.Abs(uiScaleSlider.Value - Math.Round(App.UiScale * 100)) > 0.5) uiScaleSlider.Value = Math.Round(App.UiScale * 100); });

    // ---- History (SPEC.md › Undo) ----
    record HistoryRow(long Id, string When, string Summary, string Action, double Fade);
    void OnHistoryChanged(HistoryEntry entry, string action) => Dispatcher.BeginInvoke(FillHistory);
    void FillHistory()
    {
        var rows = Enumerable.Reverse(ChangeHistory.Entries).Select(entry => new HistoryRow(entry.Id, entry.At.Date == DateTime.Today ? entry.At.ToString("t") : entry.At.ToString("d MMM, t"), entry.Undone ? $"Undone: {entry.Summary}" : entry.Summary, entry.Undone ? "Redo" : "Undo", entry.Undone ? 0.55 : 1)).ToList();
        historyList.ItemsSource = rows; noHistoryText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    async void HistoryAction_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: long id } button) return;
        var entry = ChangeHistory.Entries.FirstOrDefault(candidate => candidate.Id == id); if (entry == null) return;
        button.IsEnabled = false;
        try { if (entry.Undone) await ChangeHistory.RedoAsync(App.Store, id); else await ChangeHistory.UndoAsync(App.Store, id); }
        catch (Exception historyError) { MessageBox.Show(this, historyError.Message, entry.Undone ? "Couldn't redo that" : "Couldn't undo that", MessageBoxButton.OK, MessageBoxImage.Warning); FillHistory(); }
    }

    public void SelectTab(string tabName)
    {
        var tabByName = new Dictionary<string, TabItem> { ["Accounts"] = accountsTab, ["Calendars"] = calendarsTab, ["Sync"] = syncTab, ["Tasks"] = tasksTab, ["Export"] = exportTab, ["History"] = historyTab, ["Appearance"] = appearanceTab, ["View"] = viewTab, ["Window"] = windowTab, ["About"] = aboutTab };
        if (tabByName.TryGetValue(tabName, out var selectedTab)) settingsTabs.SelectedItem = selectedTab;
    }

    static Brush BrushOf(string colour) { try { return (Brush)new BrushConverter().ConvertFromString(colour)!; } catch { return Brushes.SteelBlue; } }

    // ---- Lists that follow the store: accounts, calendars, task lists, monitor setups ----
    record AccountRow(string Email, string Status, Brush StatusBrush);
    record CalendarRow(string Key, string Name, bool Shown, Brush ColourBrush, string AccountHeading, Visibility HeadingVisibility, bool InSwitcher = true);
    record SetupRow(string Key, string Name, string CurrentLabel, string Detail);
    record ExportRow(string Email) { public bool Chosen { get; set; } = true; }
    record DefaultChoice(string Key, string Label);
    bool fillingDefault;
    void FillDefaultCalendar()
    {
        var store = App.Store; var many = store.Calendars.Select(calendar => calendar.Account).Distinct().Count() > 1;
        var choices = new List<DefaultChoice> { new("", "The first ticked calendar") }.Concat(store.Calendars.Where(calendar => !calendar.ReadOnly).Select(calendar => new DefaultChoice(calendar.Key, many ? $"{calendar.Name} · {calendar.Account}" : calendar.Name))).ToList();
        fillingDefault = true; defaultCalendarBox.ItemsSource = choices; defaultCalendarBox.SelectedValue = choices.Any(choice => choice.Key == Settings.Sync.DefaultCalendar) ? Settings.Sync.DefaultCalendar : ""; fillingDefault = false;
    }
    void FillLists()
    {
        var store = App.Store;
        FillDefaultCalendar();
        accountsList.ItemsSource = Settings.Sync.Accounts.Select(email =>
        {
            var state = store.AccountStates.GetValueOrDefault(email);
            var synced = state == null || state.LastSync == DateTime.MinValue ? "Not synced yet" : $"Synced {state.LastSync:HH:mm}, {store.Calendars.Count(calendar => calendar.Account == email)} calendars";
            // Partial failure names the calendar ("Couldn't read Family: …"); a whole-account failure says the account couldn't sync
            var status = state is not { Error.Length: > 0 } ? synced : store.Calendars.Any(calendar => calendar.Account == email) && state.LastSync != DateTime.MinValue ? $"{synced}. Couldn't read {state.Error}" : $"Couldn't sync: {state.Error}";
            return new AccountRow(email, status, state?.Error is { Length: > 0 } ? Brushes.Firebrick : Brushes.Gray);
        }).ToList();
        noAccountsText.Visibility = Settings.Sync.Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        noClientCallout.Visibility = GoogleAuth.HasClient ? Visibility.Collapsed : Visibility.Visible;
        addAccountButton.IsEnabled = GoogleAuth.HasClient;
        string previousAccount = "";
        calendarsList.ItemsSource = store.Calendars.Select(calendar => { var first = calendar.Account != previousAccount; previousAccount = calendar.Account; return new CalendarRow(calendar.Key, calendar.Name, store.IsShown(calendar.Key), BrushOf(store.ColourOf(calendar)), calendar.Account, first ? Visibility.Visible : Visibility.Collapsed, store.IsInSwitcher(calendar.Key)); }).ToList();
        calendarsHeader.Visibility = store.Calendars.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        noCalendarsText.Visibility = store.Calendars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        noCalendarsText.Text = NoCalendarsReason();
        previousAccount = "";
        taskListsList.ItemsSource = Settings.Tasks.Enabled ? store.Tasks.Lists.Select(list => { var first = list.Account != previousAccount; previousAccount = list.Account; return new CalendarRow(list.Key, list.Name, store.Tasks.IsShown(list.Key), Brushes.Transparent, list.Account, first ? Visibility.Visible : Visibility.Collapsed); }).ToList() : null;
        var needAccess = Settings.Tasks.Enabled ? Settings.Sync.Accounts.Where(email => !Settings.Tasks.Accounts.Contains(email)).ToList() : new();
        tasksAccessPanel.Visibility = needAccess.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        tasksAccessText.Text = needAccess.Count > 0 ? "  for " + string.Join(", ", needAccess) : "";
        var currentKey = App.CurrentSetupKey;
        setupsList.ItemsSource = Settings.Window.Setups.OrderByDescending(pair => pair.Key == currentKey).ThenByDescending(pair => pair.Value.LastUsed)
            .Select(pair => new SetupRow(pair.Key, pair.Value.Name, pair.Key == currentKey ? "  ✔ now" : "", $"{pair.Value.Width}×{pair.Value.Height} at {pair.Value.X}, {pair.Value.Y} · scale {pair.Value.UiScale * 100:0}%{(pair.Key == currentKey ? "" : $" · last used {pair.Value.LastUsed:d MMM}")}")).ToList();
        if (exportAccountsList.ItemsSource is not List<ExportRow> rows || !rows.Select(row => row.Email).SequenceEqual(Settings.Sync.Accounts)) exportAccountsList.ItemsSource = Settings.Sync.Accounts.Select(email => new ExportRow(email)).ToList();
        pinnedBox.IsChecked = Settings.Window.Pinned;
        uiScaleSetupText.Text = Settings.Window.Setups.TryGetValue(currentKey, out var placement) ? $"For this monitor setup ({placement.Name})" : "For this monitor setup";
    }

    // ---- Accounts and calendars ----
    void WireAccountsTab()
    {
        addAccountButton.Click += async (sender, args) => { addAccountButton.IsEnabled = false; try { await App.AddAccountAsync(); } finally { addAccountButton.IsEnabled = GoogleAuth.HasClient; } };
        syncNowButton.Click += async (sender, args) => await App.Store.SyncAsync();
    }
    async void SignOut_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string email }) return;
        if (MessageBox.Show(this, $"Sign out {email}? Its calendars leave WinGCal; nothing changes in Google.", "WinGCal", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await App.Store.RemoveAccountAsync(email);
    }
    void CalendarShown_Click(object sender, RoutedEventArgs args) { if (sender is CheckBox { Tag: string key } box) App.Store.SetShown(key, box.IsChecked == true); }
    // Calendar order: press and hold the grip, the row lifts with a wiggle, then drag it into place among its account's calendars
    string? dragKey; FrameworkElement? dragRow, dragGrip; List<FrameworkElement> dragSiblings = new(); double dragStartY; int dragFrom, dragTo; bool dragLifted;
    readonly System.Windows.Threading.DispatcherTimer liftTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    IEnumerable<(FrameworkElement Row, CalendarRow Item)> CalendarRows() => calendarsList.Items.Cast<CalendarRow>().Select((item, index) => (calendarsList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container && VisualTreeHelper.GetChildrenCount(container) > 0 && VisualTreeHelper.GetChild(container, 0) is StackPanel stack ? (FrameworkElement)stack.Children[1] : null, item)).Where(pair => pair.Item1 != null).Select(pair => (pair.Item1!, pair.item));
    void CalendarGrip_Down(object sender, System.Windows.Input.MouseButtonEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string key } grip || grip.Parent is not FrameworkElement row) return;
        var account = calendarsList.Items.Cast<CalendarRow>().First(item => item.Key == key).AccountHeading;
        dragKey = key; dragGrip = grip; dragRow = row; dragLifted = false; dragStartY = args.GetPosition(calendarsList).Y;
        dragSiblings = CalendarRows().Where(pair => pair.Item.AccountHeading == account).Select(pair => pair.Row).ToList(); dragFrom = dragTo = dragSiblings.IndexOf(row);
        grip.CaptureMouse(); args.Handled = true;
        liftTimer.Tick -= Lift; liftTimer.Tick += Lift; liftTimer.Start();
    }
    void Lift(object? sender, EventArgs args)
    {
        liftTimer.Stop(); if (dragRow == null) return;
        dragLifted = true; Panel.SetZIndex(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(dragRow)) as UIElement ?? dragRow, 10);
        var wiggle = new RotateTransform(); var group = new TransformGroup(); group.Children.Add(new ScaleTransform(1.03, 1.03)); group.Children.Add(wiggle); group.Children.Add(new TranslateTransform());
        dragRow.RenderTransformOrigin = new Point(0.5, 0.5); dragRow.RenderTransform = group; dragRow.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 };
        // A quick wiggle as it comes up, like picking up a card
        var swing = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
        foreach (var (angle, at) in new[] { (0.0, 0), (-1.4, 60), (1.2, 130), (-0.8, 200), (0.4, 260), (0.0, 320) }) swing.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(angle, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))));
        wiggle.BeginAnimation(RotateTransform.AngleProperty, swing);
    }
    void CalendarGrip_Move(object sender, System.Windows.Input.MouseEventArgs args)
    {
        if (!dragLifted || dragRow?.RenderTransform is not TransformGroup group) return;
        var offset = args.GetPosition(calendarsList).Y - dragStartY; ((TranslateTransform)group.Children[2]).Y = offset;
        // Where it would land: past the middles of the rows it has crossed. The rows in between slide aside to make room
        var pitch = dragRow.ActualHeight + dragRow.Margin.Top + dragRow.Margin.Bottom;
        dragTo = Math.Clamp(dragFrom + (int)Math.Round(offset / pitch), 0, dragSiblings.Count - 1);
        for (var index = 0; index < dragSiblings.Count; index++)
        {
            if (index == dragFrom) continue;
            var shift = index > dragFrom && index <= dragTo ? -pitch : index < dragFrom && index >= dragTo ? pitch : 0;
            if (dragSiblings[index].RenderTransform is not TranslateTransform slide) dragSiblings[index].RenderTransform = slide = new TranslateTransform();
            if (dragSiblings[index].Tag as double? != shift) { dragSiblings[index].Tag = shift; slide.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(shift, TimeSpan.FromMilliseconds(120)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase() }); }
        }
    }
    void CalendarGrip_Up(object sender, System.Windows.Input.MouseButtonEventArgs args) { var move = dragLifted && dragTo != dragFrom ? (dragKey, dragTo) : (null, 0); EndDrag(); if (move.dragKey != null) App.Store.MoveCalendarTo(move.dragKey, move.dragTo); }
    void CalendarGrip_Lost(object sender, System.Windows.Input.MouseEventArgs args) => EndDrag();
    void EndDrag()
    {
        liftTimer.Stop(); var grip = dragGrip; dragGrip = null;
        foreach (var row in dragSiblings) { row.RenderTransform = Transform.Identity; row.Effect = null; row.Tag = null; }
        if (dragRow != null) Panel.SetZIndex(VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(dragRow)) as UIElement ?? dragRow, 0);
        dragRow = null; dragKey = null; dragLifted = false; dragSiblings = new();
        if (grip?.IsMouseCaptured == true) grip.ReleaseMouseCapture();
    }
    void CalendarInSwitcher_Click(object sender, RoutedEventArgs args) { if (sender is CheckBox { Tag: string key } box) App.Store.SetInSwitcher(key, box.IsChecked == true); }
    void CalendarColour_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string key }) return;
        using var colourDialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (colourDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        Settings.Sync.ColourOverrides[key] = $"#{colourDialog.Color.R:x2}{colourDialog.Color.G:x2}{colourDialog.Color.B:x2}"; Settings.Save();
        App.Store.SetShown(key, App.Store.IsShown(key)); // redraws with the new colour
    }

    // ---- Sync range and first events ----
    void WireSyncTab()
    {
        var sync = Settings.Sync;
        backMonthsBox.Text = sync.BackMonths.ToString(); backDatePicker.SelectedDate = sync.BackDate;
        (sync.BackMode switch { "date" => backDateRadio, "all" => backAllRadio, _ => backMonthsRadio }).IsChecked = true;
        void SaveRange() { Settings.Save(); _ = App.Store.SyncAsync(); }
        backMonthsRadio.Checked += (sender, args) => { sync.BackMode = "months"; SaveRange(); };
        backDateRadio.Checked += (sender, args) => { sync.BackMode = "date"; SaveRange(); };
        backAllRadio.Checked += (sender, args) => { sync.BackMode = "all"; SaveRange(); };
        backMonthsBox.LostFocus += (sender, args) => { if (int.TryParse(backMonthsBox.Text, out var months) && months >= 0 && months <= 1200) { sync.BackMonths = months; backMonthsRadio.IsChecked = true; sync.BackMode = "months"; SaveRange(); } backMonthsBox.Text = sync.BackMonths.ToString(); };
        backDatePicker.SelectedDateChanged += (sender, args) => { if (backDatePicker.SelectedDate is { } date) { sync.BackDate = date; backDateRadio.IsChecked = true; sync.BackMode = "date"; SaveRange(); } };
        aheadSlider.Value = sync.AheadYears; aheadText.Text = sync.AheadYears == 1 ? "1 year" : $"{sync.AheadYears} years";
        aheadSlider.ValueChanged += (sender, args) => { sync.AheadYears = (int)aheadSlider.Value; aheadText.Text = sync.AheadYears == 1 ? "1 year" : $"{sync.AheadYears} years"; Settings.Save(); };
        aheadSlider.LostMouseCapture += (sender, args) => _ = App.Store.SyncAsync();
        refreshBox.SelectedItem = refreshBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == sync.RefreshMinutes.ToString()) ?? refreshBox.Items[1];
        refreshBox.SelectionChanged += (sender, args) => { if (refreshBox.SelectedItem is ComboBoxItem { Tag: string minutes }) { sync.RefreshMinutes = int.Parse(minutes); Settings.Save(); App.Store.Start(); } };
        firstEventsButton.Click += async (sender, args) => await FindFirstEventsAsync();
    }

    record FirstEventRow(string Calendar, string When, string Title);
    async Task FindFirstEventsAsync()
    {
        firstEventsButton.IsEnabled = false; firstEventsStatus.Text = "Asking Google…";
        var rows = new List<FirstEventRow>();
        foreach (var calendar in App.Store.Calendars)
        {
            try
            {
                var first = await App.Store.Api.FirstEventAsync(calendar.Account, calendar.Id, CancellationToken.None);
                if (first is not { } found) { rows.Add(new FirstEventRow(calendar.Name, "No events", "")); continue; }
                var start = found.GetProperty("start");
                var when = start.TryGetProperty("dateTime", out var dateTime) ? DateTimeOffset.Parse(dateTime.GetString()!).LocalDateTime.ToString("d MMM yyyy HH:mm") : DateTime.Parse(start.GetProperty("date").GetString()!).ToString("d MMM yyyy");
                rows.Add(new FirstEventRow(calendar.Name, when, found.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "(No title)"));
            }
            catch (Exception lookupError) { rows.Add(new FirstEventRow(calendar.Name, "Couldn't check", lookupError.Message)); }
            firstEventsTable.ItemsSource = rows.ToList(); firstEventsTable.Visibility = Visibility.Visible;
        }
        firstEventsStatus.Text = rows.Count == 0 ? NoCalendarsReason() : "";
        firstEventsButton.IsEnabled = true;
    }

    // ---- Tasks ----
    void WireTasksTab()
    {
        tasksBox.IsChecked = Settings.Tasks.Enabled;
        tasksBox.Click += (sender, args) => { Settings.Tasks.Enabled = tasksBox.IsChecked == true; Settings.Save(); App.ApplyTasks(); FillLists(); };
        tasksAccessButton.Click += async (sender, args) =>
        {
            tasksAccessButton.IsEnabled = false;
            try { foreach (var email in Settings.Sync.Accounts.Where(account => !Settings.Tasks.Accounts.Contains(account)).ToList()) await App.Store.AllowTasksAsync(email, CancellationToken.None); }
            catch (Exception accessError) { MessageBox.Show(this, accessError.Message, "WinGCal", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { tasksAccessButton.IsEnabled = true; FillLists(); }
        };
    }
    void TaskListShown_Click(object sender, RoutedEventArgs args) { if (sender is CheckBox { Tag: string key } box) App.Store.Tasks.SetShown(key, box.IsChecked == true); }

    // ---- Export ----
    void WireExportTab()
    {
        exportButton.Click += async (sender, args) =>
        {
            var chosen = (exportAccountsList.ItemsSource as List<ExportRow> ?? new()).Where(row => row.Chosen).Select(row => row.Email).ToList();
            if (chosen.Count == 0) { exportStatus.Text = "Tick at least one account."; return; }
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = $"WinGCal backup {DateTime.Now:yyyy-MM-dd}.json", Filter = "JSON|*.json", DefaultExt = ".json" };
            if (dialog.ShowDialog(this) != true) return;
            exportCancel = new CancellationTokenSource(); App.Exporting = true;
            exportButton.IsEnabled = false; exportCancelButton.Visibility = Visibility.Visible; exportProgress.Visibility = Visibility.Visible; exportProgress.Value = 0;
            var progress = new Progress<CalendarExport.Progress>(step => { exportProgress.Maximum = Math.Max(1, step.CalendarsTotal); exportProgress.Value = step.CalendarsDone; exportStatus.Text = step.Calendar == "" ? "" : $"{step.Calendar} ({step.CalendarsDone + 1} of {step.CalendarsTotal}) · {step.Events:N0} events so far"; });
            try { var count = await CalendarExport.ExportAsync(App.Store, dialog.FileName, chosen, App.Updater.CurrentVersion, progress, exportCancel.Token); exportStatus.Text = $"Saved {count:N0} events to {dialog.FileName}"; }
            catch (OperationCanceledException) { exportStatus.Text = "Stopped. Nothing was saved."; }
            catch (Exception exportError) { exportStatus.Text = "The export stopped: " + exportError.Message; }
            finally { App.Exporting = false; exportButton.IsEnabled = true; exportCancelButton.Visibility = Visibility.Collapsed; exportProgress.Visibility = Visibility.Collapsed; try { System.IO.File.Delete(dialog.FileName + ".part"); } catch { } }
        };
        exportCancelButton.Click += (sender, args) => exportCancel?.Cancel();
    }

    // ---- Appearance (as WinSqueeze) ----
    void WireAppearanceTab()
    {
        var look = Settings.Look;
        var skinNames = new[] { ("auto", "Glass, following Windows (light or dark)"), ("dark", "Glass dark"), ("light", "Glass light"), ("paper", "Paper"), ("technical", "Technical"), ("wall", "Wall calendar") };
        skinBox.ItemsSource = skinNames.Select(skin => new ComboBoxItem { Content = skin.Item2, Tag = skin.Item1 }).ToList();
        skinBox.SelectedItem = ((List<ComboBoxItem>)skinBox.ItemsSource).FirstOrDefault(item => (string)item.Tag == look.Skin) ?? ((List<ComboBoxItem>)skinBox.ItemsSource)[0];
        skinBox.SelectionChanged += (sender, args) => { if (skinBox.SelectedItem is ComboBoxItem { Tag: string skin }) { look.Skin = skin; Settings.Save(); App.ApplySkin(); App.ApplyWindowStyle(); glassCard.IsEnabled = look.IsGlassSkin; } };
        glassCard.IsEnabled = look.IsGlassSkin;
        void ShowAccent() => accentSwatch.Background = look.AccentColour == "" ? new SolidColorBrush(Color.FromRgb(0x5c, 0xcf, 0xff)) : BrushOf(look.AccentColour);
        ShowAccent();
        accentChooseButton.Click += (sender, args) =>
        {
            using var colourDialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
            if (look.AccentColour != "") colourDialog.Color = System.Drawing.ColorTranslator.FromHtml(look.AccentColour);
            if (colourDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            look.AccentColour = $"#{colourDialog.Color.R:x2}{colourDialog.Color.G:x2}{colourDialog.Color.B:x2}"; Settings.Save(); App.ApplySkin(); ShowAccent();
        };
        eventTintSlider.Value = look.EventTint; eventTintText.Text = look.EventTint == 0 ? "None" : $"{look.EventTint}%";
        eventTintSlider.ValueChanged += (sender, args) => { look.EventTint = (int)eventTintSlider.Value; eventTintText.Text = look.EventTint == 0 ? "None" : $"{look.EventTint}%"; LookChangedSoon(); };
        accentResetButton.Click += (sender, args) => { look.AccentColour = ""; Settings.Save(); App.ApplySkin(); ShowAccent(); };
        // Scale belongs to the monitor setup (SPEC.md › Monitor setups)
        uiScaleSlider.Value = Math.Round(App.UiScale * 100); uiScaleText.Text = $"{uiScaleSlider.Value}%";
        uiScaleSlider.ValueChanged += (sender, args) => { App.UiScale = uiScaleSlider.Value / 100; uiScaleText.Text = $"{uiScaleSlider.Value}%"; };
        uiScaleResetButton.Click += (sender, args) => uiScaleSlider.Value = 100;
        void ShowGlass()
        {
            glassTransparencyText.Text = look.GlassTransparency < 0 ? $"{(int)glassTransparencySlider.Value}%  default" : $"{look.GlassTransparency}%";
            glassTintText.Text = look.GlassTintHue == 0 ? "Neutral" : $"{look.GlassTintHue}°";
            glassTintSwatch.Background = new SolidColorBrush(look.GlassTintHue == 0 ? Color.FromRgb(30, 32, 38) : HueColour(look.GlassTintHue));
        }
        var fillingGlass = true;
        glassTransparencySlider.Value = look.GlassTransparency < 0 ? 62 : look.GlassTransparency; glassTintSlider.Value = look.GlassTintHue; fillingGlass = false; ShowGlass();
        glassTransparencySlider.ValueChanged += (sender, args) => { if (fillingGlass) return; look.GlassTransparency = (int)glassTransparencySlider.Value; LookChangedSoon(); ShowGlass(); };
        glassTintSlider.ValueChanged += (sender, args) => { if (fillingGlass) return; look.GlassTintHue = (int)glassTintSlider.Value; LookChangedSoon(); ShowGlass(); };
        glassResetButton.Click += (sender, args) => { look.GlassTransparency = -1; look.GlassTintHue = 0; fillingGlass = true; glassTransparencySlider.Value = 62; glassTintSlider.Value = 0; fillingGlass = false; Settings.Save(); App.ApplySkin(); ShowGlass(); };
        glassInactiveBox.IsChecked = look.GlassWhenInactive;
        glassInactiveBox.Click += (sender, args) => { look.GlassWhenInactive = glassInactiveBox.IsChecked == true; Settings.Save(); App.RefreshInactiveGlass(); };
        glassBox.IsChecked = look.Glass; framelessBox.IsChecked = look.Frameless;
        // Clear glass: tint of the see-through window and of the frosted panels
        void ShowClear() { clearGlassBox.IsChecked = look.ClearGlass; clearControls.Visibility = look.ClearGlass ? Visibility.Visible : Visibility.Collapsed; transparencyRow.Visibility = look.ClearGlass ? Visibility.Collapsed : Visibility.Visible; windowTintText.Text = $"{look.WindowTint}%"; panelTintText.Text = $"{look.PanelTint}%"; panelFrostText.Text = look.PanelFrost == 0 ? "None" : $"{look.PanelFrost}%"; panelGrainText.Text = look.PanelGrain == 0 ? "None" : $"{look.PanelGrain}%"; glassThicknessText.Text = look.GlassThickness == 0 ? "None" : $"{look.GlassThickness}%"; frostColourText.Text = look.FrostColour == 0 ? "Grey" : $"{look.FrostColour}%"; }
        windowTintSlider.Value = look.WindowTint; panelTintSlider.Value = look.PanelTint; panelFrostSlider.Value = look.PanelFrost; panelGrainSlider.Value = look.PanelGrain; glassThicknessSlider.Value = look.GlassThickness; frostColourSlider.Value = look.FrostColour; ShowClear();
        panelFrostSlider.ValueChanged += (sender, args) => { look.PanelFrost = (int)panelFrostSlider.Value; LookChangedSoon(restyle: true); ShowClear(); };
        panelGrainSlider.ValueChanged += (sender, args) => { look.PanelGrain = (int)panelGrainSlider.Value; LookChangedSoon(); ShowClear(); };
        frostColourSlider.ValueChanged += (sender, args) => { look.FrostColour = (int)frostColourSlider.Value; LookChangedSoon(); ShowClear(); };
        glassThicknessSlider.ValueChanged += (sender, args) => { look.GlassThickness = (int)glassThicknessSlider.Value; LookChangedSoon(); ShowClear(); };
        clearGlassBox.Click += (sender, args) => { look.ClearGlass = clearGlassBox.IsChecked == true; Settings.Save(); App.ApplyWindowStyle(); App.ApplySkin(); ShowClear(); };
        windowTintSlider.ValueChanged += (sender, args) => { look.WindowTint = (int)windowTintSlider.Value; LookChangedSoon(); ShowClear(); };
        panelTintSlider.ValueChanged += (sender, args) => { look.PanelTint = (int)panelTintSlider.Value; LookChangedSoon(); ShowClear(); };
        glassControls.IsEnabled = look.Glass;
        glassBox.Click += (sender, args) => { look.Glass = glassBox.IsChecked == true; Settings.Save(); App.ApplyWindowStyle(); glassControls.IsEnabled = look.Glass; };
        framelessBox.Click += (sender, args) => { look.Frameless = framelessBox.IsChecked == true; Settings.Save(); App.ApplyWindowStyle(); };
    }

    static Color HueColour(int hue)
    {
        double hueSixth = hue / 60.0, chroma = 0.55, x = chroma * (1 - Math.Abs(hueSixth % 2 - 1)), lightness = 0.25;
        var (red, green, blue) = hueSixth switch { < 1 => (chroma, x, 0.0), < 2 => (x, chroma, 0.0), < 3 => (0.0, chroma, x), < 4 => (0.0, x, chroma), < 5 => (x, 0.0, chroma), _ => (chroma, 0.0, x) };
        byte Channel(double value) => (byte)Math.Round(Math.Clamp(value + lightness - chroma / 2, 0, 1) * 255);
        return Color.FromRgb(Channel(red), Channel(green), Channel(blue));
    }

    // ---- Calendar view ----
    void WireViewTab()
    {
        var view = Settings.View;
        weekStartBox.SelectedItem = weekStartBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == view.WeekStart.ToString()) ?? weekStartBox.Items[0];
        weekStartBox.SelectionChanged += (sender, args) => { if (weekStartBox.SelectedItem is ComboBoxItem { Tag: string start }) { view.WeekStart = int.Parse(start); Settings.Save(); App.ApplyView(); } };
        clockBox.SelectedItem = clockBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == view.Clock) ?? clockBox.Items[0];
        holdSlider.Value = view.HoldMs; holdText.Text = $"{view.HoldMs} ms";
        holdSlider.ValueChanged += (sender, args) => { view.HoldMs = (int)holdSlider.Value; holdText.Text = $"{view.HoldMs} ms"; Settings.Save(); App.ApplyView(); };
        clockBox.SelectionChanged += (sender, args) => { if (clockBox.SelectedItem is ComboBoxItem { Tag: string clock }) { view.Clock = clock; Settings.Save(); App.ApplyView(); } };
        smoothWheelBox.IsChecked = view.SmoothWheel; stripLightSlider.Value = view.StripLight; stripLightText.Text = view.StripLight == 0 ? "Off" : $"{view.StripLight}%";
        stripLightSlider.ValueChanged += (sender, args) => { view.StripLight = (int)stripLightSlider.Value; stripLightText.Text = view.StripLight == 0 ? "Off" : $"{view.StripLight}%"; Settings.Save(); App.ApplyView(); };
        smoothWheelBox.Click += (sender, args) => { view.SmoothWheel = smoothWheelBox.IsChecked == true; Settings.Save(); App.ApplyView(); };
        declinedBox.IsChecked = view.ShowDeclined;
        declinedBox.Click += (sender, args) => { view.ShowDeclined = declinedBox.IsChecked == true; Settings.Save(); App.ApplyView(); };
    }

    // ---- Window ----
    void WireWindowTab()
    {
        pinTable.ItemsSource = new List<KeyValuePair<string, (string, string)>> { new("Move and resize", ("Locked", "Title strip and edges")), new("Win+D", ("Stays showing", "Minimises")), new("Other windows", ("Always on top of it", "Normal")), new("Taskbar, Alt+Tab", ("Hidden", "Shown")) };
        pinnedBox.Click += (sender, args) => { Settings.Window.Pinned = pinnedBox.IsChecked == true; Settings.Save(); App.ApplyPinned(); };
        startWithWindowsBox.IsChecked = Settings.General.StartWithWindows; startWithWindowsBox.IsEnabled = !AppPaths.IsPortable;
        startWithWindowsBox.Click += (sender, args) => { Settings.General.StartWithWindows = startWithWindowsBox.IsChecked == true; Settings.Save(); try { StartupRegistration.Apply(Settings.General.StartWithWindows); } catch { } };
        var general = Settings.General;
        void ShowTrayChoices() { trayIconBox.IsChecked = general.ShowTrayIcon; startMinimisedBox.IsChecked = general.StartMinimised; closeToTrayBox.IsChecked = general.CloseToTray; var place = general.ShowTrayIcon ? "tray" : "taskbar"; startMinimisedBox.Content = general.ShowTrayIcon ? "Start hidden in the tray" : "Start minimised to the taskbar"; closeToTrayBox.Content = $"Close (X) keeps WinGCal running in the {place} (otherwise it quits)"; }
        ShowTrayChoices();
        trayIconBox.Click += (sender, args) => { general.ShowTrayIcon = trayIconBox.IsChecked == true; Settings.Save(); App.ApplyTray(); ShowTrayChoices(); };
        startMinimisedBox.Click += (sender, args) => { general.StartMinimised = startMinimisedBox.IsChecked == true; Settings.Save(); };
        closeToTrayBox.Click += (sender, args) => { general.CloseToTray = closeToTrayBox.IsChecked == true; Settings.Save(); };
        quitButton.Click += (sender, args) => App.Quit();
    }
    void ForgetSetup_Click(object sender, RoutedEventArgs args) { if (sender is Button { Tag: string key }) App.ForgetSetup(key); }

    // ---- About ----
    void WireAboutTab()
    {
        versionText.Text = $"WinGCal {App.Updater.CurrentVersion}";
        dataFolderText.Text = (AppPaths.IsPortable ? "Portable mode. " : "") + $"Settings, cache and logs: {AppPaths.DataFolder}";
        openLogsButton.Click += (sender, args) => Process.Start(new ProcessStartInfo(AppPaths.LogsFolder) { UseShellExecute = true });
        copyDiagnosticsButton.Click += (sender, args) => { try { Clipboard.SetText(DiagnosticsReport()); copyDiagnosticsDone.Text = "Copied"; } catch (Exception error) { copyDiagnosticsDone.Text = "Couldn't copy: " + error.Message; } };
        issuesLink.Click += (sender, args) => OpenUrl(AppUpdater.RepoUrl + "/issues");
        channelCombo.SelectedItem = channelCombo.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == Settings.General.UpdateChannel);
        channelCombo.SelectionChanged += async (sender, args) =>
        {
            if (channelCombo.SelectedItem is not ComboBoxItem chosen) return;
            Settings.General.UpdateChannel = (string)chosen.Tag; Settings.Save();
            await LoadVersionsAsync(); await App.Updater.CheckNowAsync();
        };
        autoUpdateBox.IsChecked = Settings.General.AutoUpdate;
        autoUpdateBox.Click += (sender, args) => { Settings.General.AutoUpdate = autoUpdateBox.IsChecked == true; Settings.Save(); };
        checkNowButton.Click += async (sender, args) => await App.Updater.CheckNowAsync();
        restartToUpdateButton.Click += async (sender, args) => await App.Updater.RestartToUpdateAsync();
        refreshVersionsButton.Click += async (sender, args) => await LoadVersionsAsync();
        versionsList.SelectionChanged += (sender, args) => ShowSelectedVersion();
        switchVersionButton.Click += async (sender, args) => { if (versionsList.SelectedItem is VersionItem item) await App.Updater.SwitchToAsync(item.Release); };
        App.Updater.Changed += ShowUpdateStatus;
        Closed += (sender, args) => App.Updater.Changed -= ShowUpdateStatus;
        ShowUpdateStatus();
        _ = LoadVersionsAsync();
    }

    record VersionItem(ReleaseEntry Release, bool IsCurrent)
    {
        public string Label => $"{Release.Version}{(Release.IsDev ? "  dev" : "")}  ·  {Release.Published:d MMM yyyy HH:mm}";
        public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
        public override string ToString() => Label + (IsCurrent ? "  ·  running now" : "");
    }

    async Task LoadVersionsAsync()
    {
        try
        {
            var releases = await App.Updater.ListReleasesAsync(Settings.General.UpdateChannel == "dev");
            versionsList.ItemsSource = releases.Select(release => new VersionItem(release, release.Version == App.Updater.CurrentVersion)).ToList();
            if (releases.Count == 0) versionNotesText.Text = "No versions published yet.";
        }
        catch (Exception listError) { versionNotesText.Text = $"Could not load versions: {listError.Message}"; }
    }

    void ShowSelectedVersion()
    {
        var selected = versionsList.SelectedItem as VersionItem;
        switchVersionButton.IsEnabled = selected != null && !selected.IsCurrent && App.Updater.IsInstalled;
        versionNotesText.Text = selected == null ? "" : (selected.Release.Notes.Length > 600 ? selected.Release.Notes[..600] + "…" : selected.Release.Notes);
    }

    void ShowUpdateStatus()
    {
        Dispatcher.BeginInvoke(() =>
        {
            updateStatusText.Text = App.Updater.StatusText;
            restartToUpdateButton.Visibility = App.Updater.ReadyVersion == "" ? Visibility.Collapsed : Visibility.Visible;
            restartToUpdateButton.Content = $"Restart to update to {App.Updater.ReadyVersion}";
        });
    }

    // Version, Windows, monitors and accounts (no e-mail addresses), plus the end of the crash log
    string DiagnosticsReport()
    {
        var lines = new List<string> { $"WinGCal {App.Updater.CurrentVersion} ({(AppPaths.IsPortable ? "portable" : "installed")}, {Settings.General.UpdateChannel})", $"Windows {Environment.OSVersion.Version}", $"Pinned: {Settings.Window.Pinned}, glass: {Settings.Look.Glass}, frameless: {Settings.Look.Frameless}, skin: {Settings.Look.Skin}" };
        foreach (var monitor in MonitorSetup.Current()) lines.Add($"Monitor: {monitor.Name} {monitor.Bounds.Width}x{monitor.Bounds.Height} at {monitor.Bounds.X},{monitor.Bounds.Y}{(monitor.Internal ? " (built-in)" : "")}");
        lines.Add($"Monitor setups remembered: {Settings.Window.Setups.Count}");
        lines.Add($"Accounts: {Settings.Sync.Accounts.Count}, calendars: {App.Store.Calendars.Count}, events: {App.Store.EventsByCalendar.Values.Sum(list => list.Count)}, tasks on: {Settings.Tasks.Enabled}");
        try { var crashLog = System.IO.File.ReadAllText(CrashReporter.LogPath); lines.Add("--- crash.log (end) ---"); lines.Add(crashLog.Length > 4000 ? crashLog[^4000..] : crashLog); } catch { }
        return string.Join(Environment.NewLine, lines);
    }

    // No calendars: not signed in, or signed in but the sync failed (say so rather than "sign in")
    string NoCalendarsReason() => App.Settings.Sync.Accounts.Count == 0 ? "Sign in on Accounts first." : App.Store.Syncing ? "Getting your calendars from Google…" : App.Store.AccountStates.Values.FirstOrDefault(state => state.Error != "") is { } failed ? $"No calendars yet: {failed.Email} couldn't sync. {failed.Error}" : "No calendars yet. They appear after the next sync.";
    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
