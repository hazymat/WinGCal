// Today's border chase plays once each time today comes (half) into view; it re-arms once today has fully left the view
export function chaseOnView(section: HTMLElement, isToday: boolean) {
  let armed = true
  const observer = new IntersectionObserver(([entry]) => {
    if (entry.intersectionRatio === 0) { armed = true; section.classList.remove('chasing'); return }
    if (armed && entry.intersectionRatio >= 0.5) { armed = false; section.classList.remove('chasing'); void section.offsetWidth; section.classList.add('chasing') } // reflow restarts the animation
  }, { threshold: [0, 0.5] })
  const watch = (today: boolean) => { observer.disconnect(); section.classList.remove('chasing'); armed = true; if (today) observer.observe(section) }
  watch(isToday)
  return { update: watch, destroy: () => observer.disconnect() }
}
