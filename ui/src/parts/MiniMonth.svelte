<script lang="ts">
  import { untrack } from 'svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, addMonths, sameDay, startOfDay, startOfWeek, prefs, monthName } from '../lib/dates'
  import Icon from './Icon.svelte'
  let { events, follow, onday }: { events: Placed[]; follow?: Date; onday?: (day: Date) => void } = $props() // onday: the owner handles a click on a day (Agenda glides there) instead of it becoming the focus
  let shownMonth = $state(new Date(ui.focus.getFullYear(), ui.focus.getMonth(), 1))
  // Follows the day list's scrolling (agenda), turning the page when the list reaches another month
  $effect(() => { const day = follow; if (day) untrack(() => { if (day.getMonth() !== shownMonth.getMonth() || day.getFullYear() !== shownMonth.getFullYear()) turnTo(new Date(day.getFullYear(), day.getMonth(), 1)) }) })
  const days = $derived(Array.from({ length: 42 }, (unused, index) => addDays(startOfWeek(shownMonth, prefs.weekStart), index)))
  const today = $derived(ui.today)
  // Up to three dots under a day, one per calendar with something on
  // The highlight is one elastic blob: moving on, it pinches in, stretches across to the next day, then settles there
  let cells: HTMLDivElement | undefined = $state(), blob: HTMLSpanElement | undefined = $state(), lastRect: { left: number; top: number; width: number; height: number } | null = null, cellsWidth = $state(0)
  const marked = $derived(follow ?? ui.focus)
  $effect(() => {
    marked; shownMonth; days; cellsWidth
    requestAnimationFrame(() => {
      const cell = cells?.querySelector<HTMLElement>('.cell.focus')
      if (!blob) return
      if (!cell) { blob.style.opacity = '0'; lastRect = null; return }
      const rect = { left: cell.offsetLeft, top: cell.offsetTop, width: cell.offsetWidth, height: cell.offsetHeight }, frame = (box: typeof rect) => ({ left: `${box.left}px`, top: `${box.top}px`, width: `${box.width}px`, height: `${box.height}px` })
      Object.assign(blob.style, frame(rect), { opacity: '1' })
      const from = lastRect; lastRect = rect
      if (!from || (from.left === rect.left && from.top === rect.top)) return
      const pinch = { left: from.left + from.width * 0.18, top: from.top + from.height * 0.12, width: from.width * 0.64, height: from.height * 0.76 }
      const spanLeft = Math.min(from.left, rect.left) + 4, spanTop = Math.min(from.top, rect.top) + rect.height * 0.15
      const stretch = { left: spanLeft, top: spanTop, width: Math.max(from.left, rect.left) + rect.width - 4 - spanLeft, height: Math.max(from.top, rect.top) + rect.height * 0.85 - spanTop }
      blob.animate([frame(from), { offset: 0.25, ...frame(pinch) }, { offset: 0.55, ...frame(stretch) }, { offset: 0.8, ...frame({ left: rect.left - 2, top: rect.top - 1, width: rect.width + 4, height: rect.height + 2 }) }, frame(rect)], { duration: 420, easing: 'cubic-bezier(0.3, 0, 0.2, 1)' })
    })
  })
  // Changing month, the sheet scrolls the whole way like one continuous run of weeks: a snapshot of the old month scrolls off as the new one scrolls in, the weeks they share lined up
  // It has weight: it glides, overshoots a touch and settles back; another turn mid-glide carries on from wherever the sheet has got to
  let previousMonth = untrack(() => shownMonth.getTime()), sheet: HTMLDivElement | undefined = $state(), outgoing: HTMLElement | null = null, carried = 0
  const sheetOffset = (element: HTMLElement) => new DOMMatrix(getComputedStyle(element).transform).m42
  // Every page turn goes through here, so the old month is snapshotted before it's redrawn
  function turnTo(month: Date) { if (month.getTime() === shownMonth.getTime()) return; if (cells) { carried = sheetOffset(cells); sheet?.querySelectorAll('.leaving').forEach(old => old.remove()); outgoing = cells.cloneNode(true) as HTMLElement } shownMonth = month }
  $effect(() => { const month = shownMonth.getTime(); untrack(() => {
    if (month === previousMonth || !cells || !sheet) return
    const firstCell = cells.querySelector<HTMLElement>('.cell'), rowHeight = (firstCell?.offsetHeight ?? 28) + 1, weeks = Math.round((startOfWeek(new Date(month), prefs.weekStart).getTime() - startOfWeek(new Date(previousMonth), prefs.weekStart).getTime()) / (7 * 86_400_000)), shift = weeks * rowHeight
    previousMonth = month
    const timing = { duration: 680, easing: 'cubic-bezier(0.22, 1.3, 0.36, 1)' } // overshoots about 6%, then settles
    if (outgoing) { const leaving = outgoing; outgoing = null; leaving.classList.add('leaving'); sheet.appendChild(leaving); leaving.animate([{ transform: `translateY(${carried}px)` }, { transform: `translateY(${carried - shift}px)` }], timing).onfinish = () => leaving.remove() }
    cells.getAnimations().forEach(running => running.cancel())
    cells.animate([{ transform: `translateY(${carried + shift}px)` }, { transform: 'none' }], timing)
  }) })
  const dots = (day: Date) => Array.from(new Set(eventsOn(events, day).map(event => event.colourShown))).slice(0, 3)
</script>

<div class="mini-month">
  {#if !follow}<div class="mini-head"><span>{monthName(shownMonth)} {shownMonth.getFullYear()}</span>{#if !follow && (shownMonth.getMonth() !== today.getMonth() || shownMonth.getFullYear() !== today.getFullYear())}<button class="back-today" onclick={() => { ui.focus = today; turnTo(new Date(today.getFullYear(), today.getMonth(), 1)) }} title="Back to today (T)">Today</button>{/if}<button onclick={() => turnTo(addMonths(shownMonth, -1))} aria-label="Previous month"><Icon name="previous" size={14} /></button><button onclick={() => turnTo(addMonths(shownMonth, 1))} aria-label="Next month"><Icon name="next" size={14} /></button></div>{/if}
  <div class="weekdays">{#each days.slice(0, 7) as day}<span class="weekday">{day.toLocaleDateString(undefined, { weekday: 'narrow' })}</span>{/each}</div>
  <div class="sheet" bind:this={sheet}><div class="cells" bind:this={cells} bind:clientWidth={cellsWidth}>
    <span class="blob" bind:this={blob} aria-hidden="true"></span>
    {#each days as day (day.getTime())}
      <button class="cell" class:other={day.getMonth() !== shownMonth.getMonth()} class:today={sameDay(day, today)} class:focus={sameDay(day, follow ?? ui.focus)} onclick={() => onday ? onday(day) : ui.focus = day}>
        {day.getDate()}<span class="dots">{#each dots(day) as colour}<i style="background: {colour}"></i>{/each}</span>
      </button>
    {/each}
  </div></div>
</div>

<style>
  .mini-month { overflow: hidden; }
  .mini-head { display: flex; align-items: center; gap: 2px; font-weight: 600; font-size: 13px; padding: 2px 2px 4px; }
  .mini-head span { flex: 1; }
  .mini-head button { display: grid; place-items: center; width: 26px; height: 24px; border: 0; border-radius: 6px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .mini-head button:hover { background: var(--hover); color: var(--fg); }
  /* Back to today: only while away from it, in the accent so it's hard to miss */
  .mini-head .back-today { width: auto; height: 22px; padding: 0 10px; margin-right: 6px; border-radius: 11px; background: var(--accent-soft); box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--accent) 45%, transparent); color: var(--accent); font: inherit; font-size: 11.5px; font-weight: 700; }
  .mini-head .back-today:hover { background: var(--accent); color: var(--on-accent); }
  .weekdays, .cells { display: grid; grid-template-columns: repeat(7, 1fr); }
  .cells { position: relative; row-gap: 1px; }
  .sheet { position: relative; overflow: clip; }
  .sheet :global(.leaving) { position: absolute; inset: 0 0 auto 0; pointer-events: none; } /* the old month's snapshot, scrolling off */
  .weekday { text-align: center; font-size: 10.5px; font-weight: 600; color: var(--fg-quiet); padding-bottom: 2px; }
  .cell { position: relative; height: 28px; border: 0; border-radius: 8px; background: none; color: var(--fg); font: inherit; font-size: 12px; font-variant-numeric: tabular-nums; cursor: pointer; padding: 0 0 6px; }
  .cell:hover { background: var(--hover); }
  .cell.other { color: var(--fg-quiet); opacity: 0.6; }
  .blob { position: absolute; z-index: 0; border-radius: 8px; background: var(--accent-soft); pointer-events: none; opacity: 0; } /* the highlight; in Agenda it follows the day on the reading line */
  .cell { z-index: 1; }
  .cell.today { color: var(--accent); font-weight: 700; }
  .dots { position: absolute; left: 0; right: 0; bottom: 4px; display: flex; justify-content: center; gap: 2px; }
  .dots i { width: 4px; height: 4px; border-radius: 50%; }
</style>
