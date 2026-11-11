// Invented calendars and events around today, for vite dev and mockups (no Google account involved)
import type { Calendar, CalendarEvent, TaskItem } from '../state.svelte'
import { addDays, dayKey, startOfDay, startOfWeek } from './dates'

export const sampleAccounts = [{ email: 'home@example.com', lastSync: new Date().toISOString() }, { email: 'work@example.com', lastSync: new Date().toISOString() }]
export const sampleCalendars: Calendar[] = [
  { key: 'home@example.com|primary', account: 'home@example.com', name: 'Personal', colour: '#4f8ef7', shown: true, primary: true },
  { key: 'home@example.com|family', account: 'home@example.com', name: 'Family', colour: '#3fbf7f', shown: true },
  { key: 'home@example.com|holidays', account: 'home@example.com', name: 'UK holidays', colour: '#2bb3b1', shown: true, readOnly: true },
  { key: 'home@example.com|birthdays', account: 'home@example.com', name: 'Birthdays', colour: '#a77bf3', shown: true, readOnly: true },
  { key: 'work@example.com|primary', account: 'work@example.com', name: 'Work', colour: '#f29a3f', shown: true, primary: true },
  { key: 'work@example.com|rota', account: 'work@example.com', name: 'Team rota', colour: '#e5677a', shown: false },
]

export function sampleEvents(): CalendarEvent[] {
  const monday = startOfWeek(new Date(), 1)
  const events: CalendarEvent[] = []
  let counter = 0
  const at = (dayOffset: number, hour: number, minute = 0) => { const day = addDays(monday, dayOffset); return new Date(day.getFullYear(), day.getMonth(), day.getDate(), hour, minute).toISOString() }
  const timed = (calendar: string, title: string, dayOffset: number, hour: number, minute: number, lengthMinutes: number, extra: Partial<CalendarEvent> = {}) => {
    const start = at(dayOffset, hour, minute)
    events.push(Object.assign({ id: 'sample' + counter++, calendarKey: calendar, title, start, end: new Date(new Date(start).getTime() + lengthMinutes * 60000).toISOString(), allDay: false }, extra))
  }
  const allDay = (calendar: string, title: string, dayOffset: number, days = 1, extra: Partial<CalendarEvent> = {}) => events.push(Object.assign({ id: 'sample' + counter++, calendarKey: calendar, title, start: dayKey(addDays(monday, dayOffset)), end: dayKey(addDays(monday, dayOffset + days)), allDay: true }, extra))
  const personal = 'home@example.com|primary', family = 'home@example.com|family', work = 'work@example.com|primary'
  for (let week = -4; week <= 5; week++) {
    const base = week * 7
    timed(work, 'Team stand-up', base + 0, 9, 30, 15, { recurring: true, meetUrl: 'https://meet.google.com/abc-defg-hij' })
    timed(work, 'Team stand-up', base + 2, 9, 30, 15, { recurring: true, meetUrl: 'https://meet.google.com/abc-defg-hij' })
    timed(work, 'Team stand-up', base + 4, 9, 30, 15, { recurring: true, meetUrl: 'https://meet.google.com/abc-defg-hij' })
    timed(personal, 'Piano practice', base + 1, 19, 0, 60, { recurring: true })
    timed(family, 'Swimming', base + 5, 10, 0, 90, { recurring: true, location: 'Leisure centre' })
    timed(personal, 'Bins out', base + 3, 20, 0, 15, { recurring: true })
  }
  timed(work, 'Quarterly review', 1, 14, 0, 90, { location: 'Room 4', description: '<p>Agenda:</p><ul><li>Numbers</li><li>Plans for next quarter</li></ul>' })
  timed(work, '1:1 with Sam', 1, 11, 0, 30)
  timed(work, 'Design crit', 2, 15, 0, 60, { meetUrl: 'https://meet.google.com/xyz-abcd-efg' })
  timed(work, 'Lunch with Priya', 3, 12, 30, 60, { location: 'Dishoom' })
  timed(personal, 'Dentist', 3, 16, 15, 45, { location: 'High Street Dental' })
  timed(family, 'Parents evening', 2, 18, 0, 60)
  timed(personal, 'Concert: Brahms 2', 4, 19, 30, 150, { location: 'Wigmore Hall' })
  timed(work, 'Deploy window', 4, 13, 0, 120)
  timed(family, 'Dinner at Gran’s', 6, 13, 0, 180)
  timed(personal, 'Car service', 8, 8, 30, 60, { location: 'Garage' })
  timed(work, 'Offsite planning', 9, 10, 0, 240)
  timed(personal, 'Haircut', -3, 17, 0, 30)
  timed(family, 'Film night', -2, 20, 0, 150)
  timed(work, 'Interview: frontend', 10, 14, 0, 60)
  timed(personal, 'Book club', 11, 19, 0, 90)
  timed(work, 'Retro', 11, 16, 0, 60)
  timed(family, 'Ella’s birthday meal', 12, 15, 0, 180)
  timed(personal, 'Run club', 16, 7, 0, 60)
  timed(work, 'Board pack due', 17, 17, 0, 30)
  allDay(family, 'Half term', 14, 5)
  allDay(personal, 'Lisbon trip', 19, 4, { location: 'Lisbon' })
  allDay('home@example.com|birthdays', 'Mum’s birthday', 2)
  allDay('home@example.com|birthdays', 'Ingrid Gustafsson’s birthday', 13)
  allDay('home@example.com|holidays', 'St Andrew’s Day', 20)
  allDay(work, 'Annual leave', -6, 2)
  allDay('work@example.com|rota', 'On call', 0, 7)
  return events
}

// Invented task lists for mockups
export function sampleTasks() {
  const due = (days: number) => dayKey(addDays(startOfDay(new Date()), days))
  const lists = [{ key: 'home@example.com|l1', account: 'home@example.com', name: 'Home', shown: true }, { key: 'home@example.com|l2', account: 'home@example.com', name: 'Shopping', shown: true }, { key: 'work@example.com|l3', account: 'work@example.com', name: 'Work', shown: true }]
  const task = (listKey: string, id: string, title: string, extra: Partial<TaskItem> = {}): TaskItem => Object.assign({ id: `${listKey}|${id}`, listKey, title, notes: '', due: '', completed: false, completedAt: '', position: id, parent: '' }, extra)
  const items = [task(lists[0].key, '01', 'Book boiler service', { due: due(-1) }), task(lists[0].key, '02', 'Renew car insurance', { due: due(3), notes: 'Compare quotes first' }), task(lists[0].key, '03', 'Fix the garden gate'), task(lists[0].key, '04', 'Hang the picture in the hall', { completed: true, completedAt: new Date().toISOString() }),
    task(lists[1].key, '11', 'Coffee beans'), task(lists[1].key, '12', 'Birthday card for Mum', { due: due(0) }), task(lists[1].key, '13', 'Batteries (AA)'),
    task(lists[2].key, '21', 'Send the board pack', { due: due(7) }), task(lists[2].key, '22', 'Draft agenda', { parent: '21' }), task(lists[2].key, '23', 'Check figures with Priya', { parent: '21' }), task(lists[2].key, '24', 'Reply to the recruiter', { due: due(1) }), task(lists[2].key, '25', 'Expenses for October', { completed: true, completedAt: new Date().toISOString() })]
  return { enabled: true, lists, items }
}
