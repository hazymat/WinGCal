import { sendToShell } from './bridge'
import { parseWhen, startOfDay } from './lib/dates'

export type Calendar = { key: string; account: string; name: string; colour: string; shown: boolean; primary?: boolean; readOnly?: boolean }
export type CalendarEvent = { id: string; calendarKey: string; title: string; start: string; end: string; allDay: boolean; location?: string; description?: string; meetUrl?: string; recurring?: boolean; colour?: string; declined?: boolean }
export type Placed = CalendarEvent & { startAt: Date; endAt: Date; colourShown: string }
export type TaskList = { key: string; account: string; name: string; shown: boolean }
export type TaskItem = { id: string; listKey: string; title: string; notes: string; due: string; completed: boolean; completedAt: string; position: string; parent: string }
export type ViewMode = 'auto' | 'month' | 'week' | 'agenda' | 'split'

// Small per-PC choices the page remembers itself; everything else comes from the shell
export function readSetting(name: string, fallback: string) { try { return localStorage.getItem('wingcal.' + name) ?? fallback } catch { return fallback } }
export function writeSetting(name: string, value: string) { try { localStorage.setItem('wingcal.' + name, value) } catch { } }

export const app = $state({
  window: { glass: false, frameless: true, maximised: false, pinned: false },
  accounts: [] as { email: string; lastSync: string; error?: string }[],
  calendars: [] as Calendar[],
  events: [] as CalendarEvent[],
  syncing: false, offline: false, sampleData: false,
  updateReady: '',
  tasks: { enabled: false, lists: [] as TaskList[], items: [] as TaskItem[] },
})
export const ui = $state({
  skin: readSetting('skin', 'auto'), accent: readSetting('accent', ''), glassTransparency: Number(readSetting('glassTransparency', '-1')), glassTintHue: Number(readSetting('glassTintHue', '0')), lookVersion: 0,
  view: readSetting('view', 'auto') as ViewMode,
  focus: startOfDay(new Date()), // the day the view is built around
  selectedEvent: null as null | { event: Placed; left: number; top: number },
  switcherOpen: false,
  hint: '' as string,
  tasksOpen: readSetting('tasksOpen', '1') === '1', // the side column, when the window is wide enough
  tip: null as null | { title: string; text: string },
})

export const calendarByKey = (key: string) => app.calendars.find(calendar => calendar.key === key)

// Events of shown calendars as dates, in start order (all-day first on a day)
export function placedEvents(): Placed[] {
  const shown = new Map(app.calendars.filter(calendar => calendar.shown).map(calendar => [calendar.key, calendar]))
  return app.events.filter(event => shown.has(event.calendarKey) && !event.declined)
    .map(event => Object.assign({}, event, { startAt: parseWhen(event.start), endAt: parseWhen(event.end), colourShown: event.colour || shown.get(event.calendarKey)!.colour }))
    .sort((first, second) => first.startAt.getTime() - second.startAt.getTime() || Number(second.allDay) - Number(first.allDay) || (second.endAt.getTime() - second.startAt.getTime()) - (first.endAt.getTime() - first.startAt.getTime()))
}

// Events touching one day (all-day ends are exclusive)
export function eventsOn(events: Placed[], day: Date) {
  const dayStart = startOfDay(day).getTime(), dayEnd = dayStart + 86_400_000
  return events.filter(event => event.startAt.getTime() < dayEnd && (event.endAt.getTime() > dayStart || (event.endAt.getTime() === event.startAt.getTime() && event.startAt.getTime() >= dayStart)))
}

export function toggleCalendar(key: string) {
  const calendar = calendarByKey(key); if (!calendar) return
  calendar.shown = !calendar.shown
  sendToShell({ type: 'showCalendar', key, shown: calendar.shown })
}

export function hint(text: string) { ui.hint = text; const shownText = text; setTimeout(() => { if (ui.hint === shownText) ui.hint = '' }, 2600) }
