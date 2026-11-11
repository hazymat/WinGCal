// Birthdays in every view: "Ingrid Guerrero's birthday" becomes "🎂 Ingrid G"; any other title with birthday in it ("Birthday meal") keeps its words and gets the cake in front
import type { CalendarEvent } from '../state.svelte'
const birthdayWord = /\b(birthday|bday|b'day|b’day)\b/i, justName = /^\s*(.+?)['’]s\s+(birthday|bday|b'day|b’day)\s*!?\s*$/i
export function isBirthday(event: CalendarEvent) { return birthdayWord.test(event.title) || event.calendarKey.includes('#contacts@') }
const shortName = (name: string) => { const words = name.trim().split(/\s+/); return [words[0]].concat(words.slice(1).map(word => (word.match(/\p{L}/u) ?? [''])[0].toUpperCase())).join(' ').trim() }
export function shortTitle(event: CalendarEvent) {
  if (!isBirthday(event)) return event.title
  const named = event.title.match(justName)
  if (named) return '🎂 ' + shortName(named[1])
  return '🎂 ' + (birthdayWord.test(event.title) ? event.title : shortName(event.title)) // a contacts birthday titled with just the name
}
