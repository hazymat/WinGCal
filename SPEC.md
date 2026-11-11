# WinGCal: specification

Source of truth for WinGCal. Propose changes here rather than silently diverging.

## What it is

A Google Calendar widget for the Windows desktop. One small glass window shows your calendars like a paper calendar, sits on the desktop (still there after Win+D), and goes back to the right place, size and scale whenever a monitor is plugged in or unplugged. It reads and writes Google Calendar for one or more Google accounts.

Same family as WinSqueeze and WinTapeRecorder: C# .NET 8 WPF shell hosting WebView2, a Svelte front end, Velopack updates from GitHub releases, the same Settings window layout and the same Appearance options (skin, colour, scale, glass, window).

## Principles

1. **A widget first.** It lives on the desktop and stays out of the way. Everything you do daily is in the window; set-up lives in Settings.
2. **Never lose a place.** Each monitor setup has its own position, size and scale.
3. **Your data is yours.** Full JSON export of everything Google holds for each event. Nothing leaves the PC except calls to Google (and the update check to GitHub).
4. **Layout follows the window.** Any size, from a narrow strip to full screen; the calendar picks the layout that fits.

## Window

### Pin

A pin button near the top right of the title strip (the WinSqueeze pin glyph; upright and lit in the accent when on).

| | Pinned | Unpinned (first install) |
|---|---|---|
| Move and resize | Locked | Drag the title strip, resize from the edges |
| Win+D (Show desktop) | Stays showing, with the desktop icons | Minimises with everything else |
| Z-order | Sits on the desktop, under other windows | Normal window |
| Taskbar and Alt+Tab | Hidden | Shown |

To move a pinned widget: unpin, move or resize, pin again. Hovering the title strip of a pinned widget shows a quiet lock hint the first time someone tries to drag it ("Unpin to move").

Implementation: pinned sets the window's owner to the desktop (`Progman`), adds `WS_EX_TOOLWINDOW`, and keeps it at the bottom of the z-order (`HWND_BOTTOM` on activation). A fallback watches for Show desktop (foreground becomes `WorkerW`/`Progman`) and re-shows the window without activating it if Windows hid it anyway. The window stays top-level so the acrylic glass keeps working.

### Tray

As WinSqueeze. Settings › Window › Start and tray:

- **Show an icon in the system tray** (on by default). Left-click shows or hides the widget. The menu opens with a small shaded row showing just the version number, then has Show/Hide, Refresh from Google, Pinned to the desktop, Skin, Check for updates, Settings and Quit.
- **Start hidden in the tray** (off by default).
- **Close (X) keeps WinGCal running in the tray** (off by default, so Close and Alt+F4 quit). The first time, a tip says where it went.
- With the icon off (as WinSqueeze): no balloons, and the window is never hidden. The two options become "…to the taskbar" and minimise instead; a pinned widget has no taskbar button, so it starts showing and Close quits. Launching WinGCal again always brings it forward.

Hiding to the tray works when pinned too: the widget hides, and Show desktop doesn't bring it back until it's shown again.

### Monitor setups

A **monitor setup** is the set of monitors connected, identified the way Windows identifies them: each monitor's EDID manufacturer and product codes plus its device path (from `QueryDisplayConfig` / `DisplayConfigGetDeviceInfo`), sorted, so the same monitors give the same setup whichever order they come up in. The laptop screen alone is one setup; laptop plus the desk monitor is another.

Per setup WinGCal remembers: which monitor the widget is on (by that monitor's id), its position relative to that monitor's top-left corner, its size, maximised or not, and the app's **Scale**. All in physical pixels, so a different Windows scaling on another monitor doesn't move it.

- When the displays change (plug, unplug, dock, resolution or Windows scaling change), WinGCal waits for Windows to settle (about 1.5 s), identifies the setup, and moves, resizes and rescales the widget to what that setup had last time.
- A setup it hasn't seen starts from the nearest one it has (same monitor if present), kept fully on screen.
- Moving, resizing or changing Scale saves to the current setup straight away.
- Settings › Window lists remembered setups by their monitors' names ("Built-in display + DELL U2720Q"), the current one marked, each with Forget.

### Frameless and glass

As WinSqueeze: frameless by default (drag from the title strip, resize from the edges when unpinned), Windows 11 acrylic glass behind a tinted page, transparency and tint sliders, glass kept when another app is in front.

### Clear glass

On by default for the glass skins (Settings › Appearance › Glass › Clear window). The window itself is see-through, with only a faint tint (Window tint slider, default 6%) and a thin glint around its edge. The title strip, the calendar and the tasks column each sit on their own frosted panel (Panel frost slider, default 42%), so they read as separate things on the desktop. Windows blurs a whole window or nothing, so each panel has a hidden backing window behind it carrying the acrylic blur; the backings follow the window as it moves, resizes, hides and shows. Popovers (event card, quick switcher, tasks overlay, hints) sit over the calendar itself, where blur can't reach, so they are near-solid. Turning Clear window off returns to one acrylic sheet with the transparency slider.

Clear glass sliders: **Window tint** (the see-through window), **Panel tint** (how dark the panels are), **Panel frost** (how strong the blur behind the panels is, 0 to 100%; 0 leaves the panels clear), **Frost grain** (extra noise over the panels, none by default), **Frost colour** (how much colour the frost keeps from the desktop behind, 40% by default, 0 = grey; the wallpaper frost only) and **Glass thickness** (40% by default: the desktop behind a panel is magnified a little, and each edge catches a thin bright line of the colours just beyond it, 1 to 2.5 px). **Tint colour** colours both the window tint and the panel tint (no colour = neutral grey). Windows' own frost has one fixed strength and ignores window transparency, so the page draws the frost itself: the shell sends the wallpaper of the window's monitor (as Windows places it: fill, fit, stretch, tile, centre, span; half size, sent again only when the wallpaper or monitor changes) and where the window sits, and the page blurs it behind each panel by the Panel frost amount. When another app's window is behind (an unpinned calendar moved over it), the panels switch to Windows' own frost instead, which blurs what is really there (fixed strength, no thickness edge), and back to the wallpaper frost once only the desktop is behind; checked as the window moves and every 0.7 s. Event windows do the same, switching to plain acrylic. If the wallpaper can't be read, the backing windows' Windows frost is used instead (on or off only). The backing frost always shares the widget's owner and follows it in the z-order, so other windows or a click on the desktop never bring it in front of the calendar. **Event tint** (Appearance › Colour, none by default, up to 60%) tints each event's panel in Agenda and Split with its calendar's colour.

### Title strip

Left: month and year of what's shown (click: back to today; left out rather than cut to a stub when the strip is too crowded), previous and next arrows, and an accent "Today" pill while you're away from today (the mini month has the same pill). Right: view switcher (Month, Week, Agenda, Month and days; by default the window size picks the view, which shows lit with a small accent dot; clicking a view keeps it, clicking the kept view again goes back to following the size), skin picker (a dropdown of the skins, each name shown in its own skin's colours and lettering), calendar switcher, refresh, Settings, pin, and (unpinned only) minimise and close. The strip hides its quieter buttons as the window narrows; the pin and the calendar switcher always stay.

Nothing in the window selects as text or can be dragged: a desktop widget must not change by an accidental drag. Data only changes through a second deliberate step (open the card, then edit).

## Calendar views

The layout follows the window's size (CSS breakpoints in device-independent pixels, after Scale):

- **Agenda** (narrow, under ~420 px wide): a list of days, today first, each event a row with its calendar's colour bar, time and title. It never runs out: scrolling near the end adds more weeks (as far as events are synced, Settings › Sync), and the mini month above turns its page to follow the day at the top of the list.
- **Split** (short and wide, about 1.6 to 3.2 times as wide as tall, under ~560 px high): a month on the left where each day shows its shape: a faint 7am to 11pm line with each timed event drawn at its time and length in its colour, and all-day events as ribbons along the bottom that run on across a multi-day event's days, and a scrolling list of days on the right (Today, Tomorrow, then dates). Clicking a day scrolls the list to it; scrolling the list moves the selected day with it; the mouse wheel over the month turns its pages. The list is compact (one line per event, text close to the month numbers in size; quiet days left out except today and the selected day) and a day with several events lays them out in two columns once the list is wide enough. Each event sits on its own glass panel. As in Agenda, the selected day sits a quarter of the way down with the days before it faded above, and days drift in from below. A handle between the halves resizes them (remembered; double-click returns to automatic). A resize never changes the selected day.
- **Week** (medium, or very wide and short): seven day columns with all-day events on top and a time grid below, scrolled to the working day, a red now-line on today.
- **Month** (large): a traditional six-week grid; each day shows as many events as fit, then "+3 more". Today has an accent ring. Weekends shaded slightly.
- **Mini month** (a corner of Week and Agenda when there's room): click a day to go there.

### Event windows

Clicking an event opens it in its own glass window straight away (no hover card): the details are the form, ready to edit (title, calendar, all day, start and end date and time, place with a Map link, video call link, notes). Save (Enter or Ctrl+S) sends only what changed and closes the window; Cancel (Esc) closes it, straight away when nothing changed, otherwise asking "Save your changes?" with Save as the default (Enter saves and closes, Esc goes back). Each bottom button shows its key (Del, Esc, Enter); Del outside a text box asks to delete, with Delete focused. With Clear glass the whole window is one frosted panel over the wallpaper, with the same tint, grain and Glass thickness as the calendar; no heading, just minimise and close. Esc closes Settings. The window opens next to the click on the same monitor, keeps its glass when in the background, and long titles wrap; it follows changes made elsewhere unless it has unsaved edits. Delete asks inside the window, with "This event" or "All events in the series" for repeats. "Open in Google" (under the notes) for guests, reminders and the rest. Read-only calendars show the same window without Save or Delete. Double-click an empty spot of a day for a New event window (all day on that day; in Week, an hour from the clicked time) with a calendar picker and Add event.

The windows are glass like the calendar (acrylic for the glass skins, solid for Paper, Technical and Wall calendar), frameless with their own minimise and close, movable anywhere and resizable. Open as many as you like (one per event; opening it again brings it forward). They share one taskbar button, so right-click › Close all windows closes them together; the calendar itself keeps no taskbar button. Windows message boxes are never used outside Settings: the selection bar's Delete asks in a glass dialog in the calendar (with the series choice), and its Edit… opens a glass "Change N events" window.

Press and hold an event (or Ctrl+click or Shift+click) to start picking several; while some are picked, a click adds or removes one. A floating rounded bar near the bottom (over the calendar, never pushing it up) shows "N events selected" with Edit… (a glass "Change N events" window: tick Date, Starts at, Ends at, Place or Calendar; each event keeps its own time and length where it makes sense; only ticked fields change; moving calendars stays within the account, never for repeats), Copy (titles and times as text), Delete (a glass dialog listing them; for repeats, only these occurrences or the whole series) and ×. Esc or × ends picking.

**Strip light** (Settings › View, on by default): in Agenda a cool fluorescent band, fixed in space, sweeps across each of today's and upcoming events as it scrolls past; today's events catch a brighter, glossier reflection. Past days get the tungsten warm-up instead: once a past day reaches the reading line (today can still be in view lower down) they flicker and glow warm after 0.7 s.

**Moving events (drag and drop).** Press and hold an event (Settings › View › Hold to pick up, 350 ms by default) and it lifts with a little wiggle, together with any other picked events, as a stack that follows the pointer. The day under the pointer is outlined; in Week's hours a dashed box shows the new time (quarter hours). In Agenda every empty day opens up as a dashed box to drop into (sliding the others aside) and the day under the pointer gets a "Move here" slot; holding near the top or bottom scrolls. Dropping moves each carried event by the same days (and minutes in Week), keeping its length; dropping where it started leaves them picked. Esc cancels.

Week starts on the Windows setting (Monday in the UK). Times follow Windows' 12/24-hour setting.

### Calendar switcher (quick)

A button in the title strip showing coloured dots for the calendars on view. It opens a glass popover: calendars grouped by account, a tick and colour dot each, click to show or hide. Changes apply immediately and are the same choice as Settings › Calendars.

Settings › Calendars lists every calendar with two ticks: Show (the same choice as the switcher) and In switcher. The switcher lists only the calendars ticked In switcher (all of them by default), so rarely changed calendars stay out of it; a calendar left out keeps whatever Show it has. Up and down arrows on each row set the calendars' order within their account: the order of the switcher and the dots at the top, and which events come first on a day (all-day events, and events starting at the same time).

## Google accounts and sync

- **Accounts:** sign in with Google in the browser (OAuth 2.0 for installed apps: system browser, loopback redirect, PKCE). Scope `https://www.googleapis.com/auth/calendar` (read and write). Any number of accounts. Refresh tokens are stored encrypted with Windows DPAPI (current user) in the data folder.
- **Calendars:** each account's calendar list (own, shared, subscribed, holidays, birthdays). Settings › Calendars ticks which show; colours follow Google's, with an optional override per calendar.
- **Sync range:** by default from 2 months ago to 12 months ahead. Back: a number of months, or from a date, or everything. Ahead: 1 to 10 years (a limit, so endless repeating events stop somewhere). Recurring events are fetched expanded (`singleEvents=true`) for display.
- **First event:** Settings › Sync shows each calendar's first ever event (date, time and title) so you can choose how far back to go. Found with one request per calendar (`orderBy=startTime`, `singleEvents=true`, from 1900, one result).
- **Refresh:** every 5 minutes, on resume from sleep, when the network returns, and with the refresh button. Cached on disk so the widget opens instantly offline; an offline note shows in the title strip.
- **Writing:** create, edit (title, time, all-day, calendar, location, description, reminders, repeat) and delete. Editing a repeating event asks: this event, this and following, or all.

## Export (Settings › Export)

Exports every event of every calendar of every account (or chosen ones) to one JSON file, for backup. Not limited to the sync range: the export fetches everything (`singleEvents=false`, so repeating events keep their rules, plus their changed and cancelled instances; all event types).

The file keeps Google's own event resource for each event untouched (so every field Google has is there: reminders and their methods, recurrence rules, attendees, conference data, attachments, colours, extended properties, HTML descriptions, time zones, created and updated times, iCal UID…) and wraps it with where it came from:

```json
{
  "format": "wingcal-export", "version": 1, "exportedAt": "2026-11-10T19:30:00Z", "app": "WinGCal 0.1.12",
  "accounts": [{
    "email": "someone@gmail.com",
    "calendars": [{
      "id": "…", "summary": "…", "timeZone": "Europe/London", "accessRole": "owner", "primary": true,
      "backgroundColor": "#9fe1e7", "defaultReminders": [{ "method": "popup", "minutes": 10 }], "notificationSettings": { … },
      "calendarListEntry": { …Google's whole calendarList resource… },
      "events": [{ …Google's whole event resource… }]
    }]
  }]
}
```

## Settings window

Same layout as WinSqueeze (left sidebar of pages grouped under small headings, white cards, tinted callouts, expanders):

- **Calendar:** Accounts (sign in, sign out, last sync); Calendars (tick, colour); Sync (range back and ahead, first event per calendar, refresh interval); Tasks (turn on, allow per account, lists to show); Export.
- **Look:** Appearance (Skin, Colour, Size › Scale, Glass, Window); Calendar view (week start, 12/24 h, which views Auto may use, show declined events, show weekends, time grid hours).
- **App:** Window (pin, start with Windows, monitor setups); General; About (version, updates channel, versions list, privacy, about the developer).

## Skins

- **Glass** (Auto, Dark, Light): frosted, the same tokens as WinSqueeze. The only skins the Glass setting applies to.
- **Paper:** a lined writing pad: very faint blue rules that events sit on, a faint red line under the title strip and each week, headings, weekday names and day numbers handwritten (Caveat), today (and the selected day in Split) circled in pen; event text in Inter. Circles are drawn by hand in one stroke: off-centre, never round, the end overshooting the start, and seeded by the date so every day's circle is a little different. All-day and multi-day events are their name written in the calendar's colour with a wavering pen line under it: a one-day event has just the line; a multi-day event's line runs across its days with an arrowhead at each end (pointing back on its first day, forward on its last).
- **Technical:** graphite with a faint blueprint grid, JetBrains Mono, amber highlight, square corners and dots.
- **Wall calendar:** textured parchment with a spiral binding along the top, Fraunces for headings and day numbers, Newsreader for text, Sundays in red, today circled.

Paper, Technical and Wall calendar are solid. Fonts are bundled (fontsource), so nothing is fetched at run time. Each skin is tokens plus a few shape touches in `ui/src/skins.css`.

## Tasks

Google Tasks beside the calendar, off by default (Settings › Tasks). Turning it on asks each account for task access separately (incremental consent), so calendar-only users never see a tasks permission.

- Every list of every allowed account; Settings › Tasks picks which show. Lists group under their account when there's more than one.
- A column on the right when the window is at least 760 px wide (the title strip's Tasks button shows or hides it); narrower, the button opens it as a panel over the calendar.
- Each list: tasks in Google's order with subtasks indented, a round tick box, a due chip (red when late), delete on hover, "Add a task", and a collapsed "Completed (n)" (the last 30 days).
- Synced with the calendars (same refresh). If Google takes access back, the account drops out of Tasks.

## Updates, crash log, data

As WinTapeRecorder: Velopack, every push to main is a dev pre-release, stable promoted by hand (`stable_version`). Data in `%LOCALAPPDATA%\WinGCal-data` (settings.json, tokens, cache, logs\crash.log); `portable.txt` beside the exe keeps it next to the exe.

## Google Cloud client

The OAuth client (Desktop app) is created in the developer's Google Cloud project. Its id and secret are not in the repo: CI writes them in at build time from the repository secrets `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`; local builds read `google-client.json` from the data folder. Google treats a desktop client's secret as not secret, but keeping it out of the repo stops copies of the app using our quota.

The consent screen must be **In production** (an unverified app shows Google's warning but works; in Testing, sign-ins expire after 7 days). Verification for the calendar scope comes later.

## Build phases

1. **Shell and glass calendar** with sample events: window, frameless, glass, pin, monitor setups, Settings with Appearance and Window, Month/Week/Agenda layouts, calendar switcher. Updates and CI.
2. **Google:** accounts, calendar list, sync and cache, Settings › Calendars and Sync, first event.
3. **Editing:** event card, create, edit, delete, repeating events.
4. **Export.**
5. **More skins** and per-skin options.
6. **Tasks.**
