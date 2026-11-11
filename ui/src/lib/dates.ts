// Calendar maths on local dates. Event times arrive as ISO strings; all-day events as yyyy-mm-dd (end exclusive, as Google sends them)
export const dayMs = 86_400_000
export function startOfDay(date: Date) { return new Date(date.getFullYear(), date.getMonth(), date.getDate()) }
export function addDays(date: Date, days: number) { return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days, date.getHours(), date.getMinutes()) }
export function addMonths(date: Date, months: number) { return new Date(date.getFullYear(), date.getMonth() + months, 1) }
export function sameDay(first: Date, second: Date) { return first.getFullYear() === second.getFullYear() && first.getMonth() === second.getMonth() && first.getDate() === second.getDate() }
export function startOfWeek(date: Date, weekStart: number) { const day = startOfDay(date); return addDays(day, -((day.getDay() - weekStart + 7) % 7)) }
export function dayKey(date: Date) { return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}` }
// "2026-11-10" is a local date, not UTC midnight
export function parseWhen(text: string) { return /^\d{4}-\d{2}-\d{2}$/.test(text) ? new Date(Number(text.slice(0, 4)), Number(text.slice(5, 7)) - 1, Number(text.slice(8, 10))) : new Date(text) }
export function minutesIntoDay(date: Date) { return date.getHours() * 60 + date.getMinutes() }

const locale = navigator.language || 'en-GB'
export const prefs = { hour12: new Intl.DateTimeFormat(locale, { hour: 'numeric' }).resolvedOptions().hour12 ?? false, weekStart: 1, holdMs: 350 }
export function formatTime(date: Date) {
  if (prefs.hour12) { const hours = date.getHours() % 12 || 12; const minutes = date.getMinutes(); return `${hours}${minutes ? ':' + String(minutes).padStart(2, '0') : ''}${date.getHours() < 12 ? 'am' : 'pm'}` }
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
}
export function formatHour(hour: number) { return prefs.hour12 ? `${hour % 12 || 12}${hour < 12 ? 'am' : 'pm'}` : `${String(hour).padStart(2, '0')}:00` }
export const monthName = (date: Date) => date.toLocaleDateString(locale, { month: 'long' })
export const weekdayShort = (date: Date) => date.toLocaleDateString(locale, { weekday: 'short' })
export const weekdayLong = (date: Date) => date.toLocaleDateString(locale, { weekday: 'long' })
export const longDate = (date: Date) => date.toLocaleDateString(locale, { weekday: 'long', day: 'numeric', month: 'long' })
export const shortDate = (date: Date) => date.toLocaleDateString(locale, { day: 'numeric', month: 'short' })
