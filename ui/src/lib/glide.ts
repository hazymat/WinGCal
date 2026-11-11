import { prefs } from './dates'
import { isWarping } from './warp'
// Smooth wheel (Settings › View): each notch glides the list instead of jumping. Spin faster and it goes further; it stops when the wheel stops (no added momentum).
// Anything else that moves the list meanwhile (more days added above, a warp) is followed, so the glide never fights it
const glides = new WeakMap<HTMLElement, { target: number; last: number; frame: number }>()
export function glideWheel(list: HTMLElement, wheelEvent: WheelEvent) {
  if (!prefs.smoothWheel || wheelEvent.ctrlKey || Math.abs(wheelEvent.deltaX) > Math.abs(wheelEvent.deltaY) || isWarping(list)) return
  wheelEvent.preventDefault()
  const delta = wheelEvent.deltaMode === 1 ? wheelEvent.deltaY * 40 : wheelEvent.deltaMode === 2 ? wheelEvent.deltaY * list.clientHeight : wheelEvent.deltaY
  let glide = glides.get(list)
  if (!glide) { glide = { target: list.scrollTop, last: list.scrollTop, frame: 0 }; glides.set(list, glide) }
  glide.target = Math.max(0, Math.min(list.scrollHeight - list.clientHeight, glide.target + delta))
  if (!glide.frame) glide.frame = requestAnimationFrame(() => step(list))
}
function step(list: HTMLElement) {
  const glide = glides.get(list); if (!glide) return
  if (isWarping(list)) { glides.delete(list); return }
  const moved = list.scrollTop - glide.last
  if (Math.abs(moved) > 1.5) glide.target += moved // the list was moved by something else (days added above): keep the same glide relative to it
  const next = list.scrollTop + (glide.target - list.scrollTop) * 0.24
  if (Math.abs(glide.target - next) < 0.6) { list.scrollTop = glide.target; glides.delete(list); return }
  list.scrollTop = next; glide.last = list.scrollTop
  glide.frame = requestAnimationFrame(() => step(list))
}
