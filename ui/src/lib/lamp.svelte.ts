// The day lists' lighting (SPEC.md › Strip light), shared by Agenda and Month and days. Tungsten: reading past days, after a moment they warm back up like a bulb coming on, with a warm pool of light over the past days only (it stops where today begins)
export function createLamp() {
  const lamp = $state({ warmed: false, height: 9999 }); let warmTimer = 0
  // readingSection: the day on the reading line. The bulb comes on once the past fills most of the view: a past day there and today pushed into the bottom quarter (or off it), after 0.7 s
  function update(list: HTMLElement, readingSection: HTMLElement | undefined) {
    const todaySection = list.querySelector<HTMLElement>('[data-day].today')
    lamp.height = todaySection ? Math.max(0, todaySection.offsetTop - list.scrollTop) : 9999
    const pastFills = !!readingSection?.classList.contains('gone') && (!todaySection || todaySection.offsetTop - list.scrollTop > list.clientHeight * 0.75)
    if (!pastFills) { clearTimeout(warmTimer); warmTimer = 0; lamp.warmed = false } else if (!lamp.warmed && !warmTimer) warmTimer = setTimeout(() => { lamp.warmed = true; warmTimer = 0 }, 700)
  }
  return { lamp, update }
}
// Strip light: each event reflects it as if set at its own slight tilt, like hand-set panes in an old sash window, so the reflection jumps and slides differently from one to the next. Fixed per event and day
export function paneTilt(seed: string) {
  let hash = 2166136261; for (let index = 0; index < seed.length; index++) hash = Math.imul(hash ^ seed.charCodeAt(index), 16777619)
  const part = (shift: number) => ((hash >>> shift) & 255) / 255 // 0..1
  return `--pane-angle: ${(part(0) * 26 - 13).toFixed(1)}deg; --pane-phase: ${(part(8) * 36 - 18).toFixed(1)}%; --pane-size: ${(250 + part(16) * 110).toFixed(0)}%; --pane-gain: ${(0.75 + part(24) * 0.5).toFixed(2)}`
}
