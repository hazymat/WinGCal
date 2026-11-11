// What the shell sends a glass event window
export type EditorCalendar = { key: string; name: string; colour: string; account: string; readOnly: boolean; shown: boolean; inSwitcher: boolean } // inSwitcher: picked in Settings › Calendars
export type EditorEvent = { id: string; calendarKey: string; title: string; start: string; end: string; allDay: boolean; location: string; description: string; meetUrl: string; recurring: boolean; colour: string; link: string }
