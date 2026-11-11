// Titles for the small views (Month, Week's all-day row, Split): a birthday becomes a cake and the person's first name and initials ("🎂 Ingrid G")
import type { CalendarEvent } from '../state.svelte'
const birthdayWord = /\b(birthday|bday|b'day|b’day)\b/i
export function isBirthday(event: CalendarEvent) { return birthdayWord.test(event.title) || event.calendarKey.includes('#contacts@') }
export function shortTitle(event: CalendarEvent) {
  if (!isBirthday(event)) return event.title
  const nameWords = event.title.replace(/\b(happy|birthday|bday|b'day|b’day|party|celebration|drinks|meal|for|the|of|at|to)\b/gi, ' ').replace(/['’]s\b/g, ' ').replace(/[^\p{L}\p{M}\s'’-]/gu, ' ').split(/\s+/).filter(word => /\p{L}/u.test(word))
  return nameWords.length === 0 ? '🎂 Birthday' : '🎂 ' + [nameWords[0]].concat(nameWords.slice(1).map(word => word.match(/\p{L}/u)![0].toUpperCase())).join(' ')
}
