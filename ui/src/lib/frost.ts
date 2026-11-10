// Clear glass (SPEC.md › Window › Clear glass): the window itself is see-through and each [data-frost] element gets real Windows frost
// from a backing window the shell keeps exactly behind it. This reports where those elements are, whenever the layout changes
import { sendToShell } from '../bridge'
import { ui } from '../state.svelte'

let lastReport = '', scheduled = false
function report() {
  scheduled = false
  const scale = window.devicePixelRatio || 1
  const boxes = document.documentElement.classList.contains('clear') ? Array.from(document.querySelectorAll<HTMLElement>('[data-frost]')).map(element => element.getBoundingClientRect()).filter(box => box.width > 0 && box.height > 0) : []
  ui.frostRects = boxes.map(box => ({ left: box.left, top: box.top, width: box.width, height: box.height }))
  const panels = boxes.map(box => ({ x: Math.round(box.left * scale), y: Math.round(box.top * scale), width: Math.round(box.width * scale), height: Math.round(box.height * scale) }))
  const text = JSON.stringify(panels)
  if (text === lastReport) return
  lastReport = text
  sendToShell({ type: 'frostPanels', panels })
  if (window.parent !== window) window.parent.postMessage({ type: 'frostPanels', panels, scale }, '*') // mockups draw the frost themselves
}
export function scheduleFrostReport() { if (!scheduled) { scheduled = true; requestAnimationFrame(report) } }
export function watchFrostPanels() {
  new ResizeObserver(scheduleFrostReport).observe(document.body)
  new MutationObserver(scheduleFrostReport).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'style'] })
  addEventListener('resize', scheduleFrostReport)
  // Popovers slide in: report again once they have settled
  addEventListener('animationend', scheduleFrostReport, true); addEventListener('transitionend', scheduleFrostReport, true)
}
