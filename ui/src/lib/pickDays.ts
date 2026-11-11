// Picking days for a new event on a month grid (Month, Month and days): drag across days, or press and hold one (Settings › View › Hold to pick up), or right-click one; the bar then offers New event. Days carry data-key (yyyy-mm-dd); a press on an event is left to the event
import { ui } from '../state.svelte'
import { prefs, sameDay } from './dates'
const dayOf = (key: string | undefined) => { if (!key) return null; const [year, month, date] = key.split('-').map(Number); return new Date(year, month - 1, date) }
const dayUnder = (pointer: MouseEvent) => dayOf((document.elementFromPoint(pointer.clientX, pointer.clientY) as HTMLElement | null)?.closest<HTMLElement>('[data-key]')?.dataset.key)
// After a pick, the click that ends the press does nothing (it would open the day)
export const swallowClick = () => addEventListener('click', (click) => { click.stopPropagation(); click.preventDefault() }, { capture: true, once: true })
export function inPicked(day: Date) { const picked = ui.pickedDays; return !!picked && !picked.timed && day >= new Date(picked.from.getFullYear(), picked.from.getMonth(), picked.from.getDate()) && day <= picked.to }
export function dayPicker() {
  let start: Date | null = null, picking = false, holdTimer = 0
  const pick = (first: Date, second: Date) => { ui.picked = []; ui.pickedDays = first <= second ? { from: first, to: second } : { from: second, to: first } }
  return {
    down(pointer: PointerEvent) {
      if (pointer.button !== 0 || (pointer.target as HTMLElement).closest('[data-event], .more')) return
      start = dayUnder(pointer); picking = false; clearTimeout(holdTimer)
      const held = start; if (held) holdTimer = setTimeout(() => { if (start && sameDay(start, held)) { picking = true; pick(held, held) } }, prefs.holdMs)
    },
    move(pointer: PointerEvent) {
      if (!start) return
      const over = dayUnder(pointer); if (!over || (!picking && sameDay(over, start))) return
      clearTimeout(holdTimer)
      if (!picking) (pointer.currentTarget as HTMLElement).setPointerCapture(pointer.pointerId) // only once dragging: capturing on press would steal the plain click from the day
      picking = true; pick(start, over)
    },
    up() { clearTimeout(holdTimer); if (start && picking) swallowClick(); start = null; picking = false },
    menu(menuEvent: MouseEvent) { if ((menuEvent.target as HTMLElement).closest('[data-event]')) return; const day = dayUnder(menuEvent); if (!day) return; menuEvent.preventDefault(); pick(day, day) },
  }
}
