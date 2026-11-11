import { prefs } from './dates'
import { isWarping } from './warp'
// Smooth wheel (Settings › View): each notch glides the list instead of jumping. Spin faster and it goes further; it stops when the wheel stops (no added momentum).
// A long fast spin streaks like the Today warp (motion blur growing with speed), coming back into focus as it slows.
// Anything else that moves the list meanwhile (more days added above, a warp) is followed, so the glide never fights it
const glides = new WeakMap<HTMLElement, { target: number; last: number; frame: number; blur: number; spins: { at: number; distance: number }[] }>()
export function glideWheel(list: HTMLElement, wheelEvent: WheelEvent) {
  if (!prefs.smoothWheel || wheelEvent.ctrlKey || Math.abs(wheelEvent.deltaX) > Math.abs(wheelEvent.deltaY) || isWarping(list)) return
  wheelEvent.preventDefault()
  const delta = wheelEvent.deltaMode === 1 ? wheelEvent.deltaY * 40 : wheelEvent.deltaMode === 2 ? wheelEvent.deltaY * list.clientHeight : wheelEvent.deltaY
  let glide = glides.get(list)
  if (!glide) { glide = { target: list.scrollTop, last: list.scrollTop, frame: 0, blur: 0, spins: [] }; glides.set(list, glide) }
  glide.spins.push({ at: performance.now(), distance: Math.abs(delta) })
  glide.target = Math.max(0, Math.min(list.scrollHeight - list.clientHeight, glide.target + delta))
  if (!glide.frame) glide.frame = requestAnimationFrame(() => step(list))
}
function step(list: HTMLElement) {
  const glide = glides.get(list); if (!glide) return
  if (isWarping(list)) { glides.delete(list); return } // the warp draws its own blur
  const moved = list.scrollTop - glide.last
  if (Math.abs(moved) > 1.5) glide.target += moved // the list was moved by something else (days added above): keep the same glide relative to it
  const next = list.scrollTop + (glide.target - list.scrollTop) * 0.24
  const arrived = Math.abs(glide.target - next) < 0.6
  if (arrived && glide.blur <= 0.15) { list.scrollTop = glide.target; list.style.filter = ''; glides.delete(list); return } // landed and back in focus
  // How hard the wheel is being spun: distance asked for over the last 300 ms. Past about 2500 px a second it streaks, eased so it swells and fades rather than pulsing
  const now = performance.now(); glide.spins = glide.spins.filter(spin => now - spin.at < 300)
  const rate = glide.spins.reduce((sum, spin) => sum + spin.distance, 0) / 0.3
  glide.blur += (Math.min(3, Math.max(0, (rate - 2500) / 2000)) - glide.blur) * 0.12
  list.style.filter = glide.blur > 0.15 ? `blur(${glide.blur.toFixed(2)}px)` : ''
  list.scrollTop = arrived ? glide.target : next; glide.last = list.scrollTop
  glide.frame = requestAnimationFrame(() => step(list))
}
