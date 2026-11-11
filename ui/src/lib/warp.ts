// "Warp" scroll back to a place in a list: speeds up, streaks (a vertical motion blur that grows with speed), then eases in to land
const running = new WeakMap<HTMLElement, number>()
export const isWarping = (list: HTMLElement) => running.has(list)
export function warpScroll(list: HTMLElement, targetTop: number) {
  const startTop = list.scrollTop, distance = targetTop - startTop
  if (Math.abs(distance) < 2) return
  cancelAnimationFrame(running.get(list) ?? 0)
  const duration = Math.min(1100, 380 + Math.sqrt(Math.abs(distance)) * 9), startTime = performance.now()
  const ease = (progress: number) => progress < 0.5 ? 4 * progress ** 3 : 1 - (-2 * progress + 2) ** 3 / 2 // accelerate, then brake
  let lastTop = startTop
  const step = (now: number) => {
    const progress = Math.min(1, (now - startTime) / duration), top = startTop + distance * ease(progress)
    list.scrollTop = top
    const speed = Math.abs(top - lastTop); lastTop = top
    list.style.filter = progress < 1 && speed > 8 ? `blur(${Math.min(3, speed / 60).toFixed(2)}px)` : ''
    if (progress < 1) running.set(list, requestAnimationFrame(step)); else { list.style.filter = ''; running.delete(list) }
  }
  running.set(list, requestAnimationFrame(step))
}
