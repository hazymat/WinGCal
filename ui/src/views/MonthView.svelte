<script lang="ts">
  import HandStar from '../parts/HandStar.svelte'
  import { shortTitle, isBirthday } from '../lib/titles'
  import { untrack } from 'svelte'
  import HandCircle from '../parts/HandCircle.svelte'
  import HandArrow from '../parts/HandArrow.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, sameDay, startOfDay, startOfWeek, formatTime, weekdayShort, prefs, dayMs, dayKey } from '../lib/dates'
  import { warpScroll, isWarping } from '../lib/warp'
  import { dayPicker, inPicked } from '../lib/pickDays'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  const picker = dayPicker() // drag across days, or hold one, for the New event bar
  let selectedDay: Date | null = $state(null) // a click on a day just marks it, softly (today keeps its ring)
  // One endless sheet of weeks, six to a screen: the wheel scrolls through the months above and below, and the month filling the middle is the one shown
  const weekOf = (date: Date) => startOfWeek(new Date(date.getFullYear(), date.getMonth(), 1), prefs.weekStart)
  const firstOfMonth = (date: Date) => new Date(date.getFullYear(), date.getMonth(), 1)
  const padWeeks = 26, maxWeeks = 130
  let firstWeek = $state(addDays(weekOf(ui.focus), -padWeeks * 7)), weekCount = $state(padWeeks * 2 + 6)
  let shownMonth = $state(firstOfMonth(ui.focus))
  let scroller: HTMLDivElement | undefined = $state(), viewHeight = $state(600)
  const rowHeight = $derived(Math.max(40, viewHeight / 6))
  const rowPx = () => Math.max(40, (scroller?.clientHeight ?? viewHeight) / 6)
  const days = $derived(Array.from({ length: weekCount * 7 }, (unused, index) => addDays(firstWeek, index)))
  const rowOf = (date: Date) => Math.round((weekOf(date).getTime() - firstWeek.getTime()) / (7 * dayMs))
  const today = $derived(ui.today)
  // How many event lines fit under a day's number (18 px each)
  const linesPerDay = $derived(Math.max(1, Math.floor((rowHeight - 26) / 18)))
  const isWeekend = (day: Date) => day.getDay() === 0 || day.getDay() === 6
  function dayEvents(day: Date) {
    const all = eventsOn(events, day)
    const fits = all.length > linesPerDay ? linesPerDay - 1 : all.length
    return { shown: all.slice(0, fits), more: all.length - fits }
  }
  const continuesBefore = (event: Placed, day: Date) => event.allDay && event.startAt.getTime() < startOfDay(day).getTime()
  const continuesAfter = (event: Placed, day: Date) => event.allDay && event.endAt.getTime() > startOfDay(day).getTime() + dayMs

  // A month picked elsewhere (arrows, Today, keys) glides there; one far away starts a fresh sheet around it
  let monthFromScroll = 0, placed = false, topRow = padWeeks, reshaping = false, settleTimer = 0
  $effect(() => {
    const target = firstOfMonth(ui.focus)
    untrack(() => {
      if (target.getTime() === monthFromScroll) { monthFromScroll = 0; return }
      if (!scroller) return
      const row = rowOf(target)
      shownMonth = target
      if (!placed || row < 1 || row > weekCount - 7) { placed = true; firstWeek = addDays(weekOf(target), -padWeeks * 7); weekCount = padWeeks * 2 + 6; requestAnimationFrame(() => { if (scroller) scroller.scrollTop = padWeeks * rowPx() }); return }
      warpScroll(scroller, row * rowPx())
    })
  })
  // Keeps the same week at the top when the window is resized
  $effect(() => { const height = rowHeight; untrack(() => { if (scroller && placed) scroller.scrollTop = topRow * height }) })

  function onScroll() {
    if (!scroller || reshaping) return
    const height = rowPx(), row = scroller.scrollTop / height
    topRow = row
    // The month of the day in the middle of the screen is the one shown
    const middle = addDays(firstWeek, Math.floor(row + 3) * 7 + 3), month = firstOfMonth(middle)
    if (month.getTime() !== shownMonth.getTime()) { shownMonth = month; monthFromScroll = month.getTime(); ui.focus = month.getMonth() === today.getMonth() && month.getFullYear() === today.getFullYear() ? today : month }
    // Adds months above or below as they come near, dropping the far end so the sheet stays light
    if (!isWarping(scroller) && (row < 6 || row > weekCount - 12)) {
      let shift = 0
      reshaping = true
      if (row < 6) { firstWeek = addDays(firstWeek, -padWeeks * 7); weekCount = Math.min(maxWeeks, weekCount + padWeeks); shift = padWeeks }
      else if (weekCount + padWeeks > maxWeeks) { firstWeek = addDays(firstWeek, padWeeks * 7); shift = -padWeeks }
      else weekCount += padWeeks
      requestAnimationFrame(() => { if (scroller) scroller.scrollTop += shift * height; reshaping = false })
    }
    // When the wheel stops, settles onto the nearest week
    clearTimeout(settleTimer)
    settleTimer = setTimeout(() => { if (!scroller || isWarping(scroller)) return; const snapTop = Math.round(scroller.scrollTop / rowPx()) * rowPx(); if (Math.abs(snapTop - scroller.scrollTop) > 1) scroller.scrollTo({ top: snapTop, behavior: 'smooth' }) }, 220)
  }
</script>

<div class="month" onwheel={(wheelEvent) => { if (scroller && !scroller.contains(wheelEvent.target as Node)) scroller.scrollTop += wheelEvent.deltaY }}>
  <div class="weekdays">{#each days.slice(0, 7) as day}<div>{weekdayShort(day)}</div>{/each}</div>
  <div class="scroller" bind:this={scroller} bind:clientHeight={viewHeight} onscroll={onScroll}>
  <div class="grid" style="--row: {rowHeight}px" onpointerdown={picker.down} onpointermove={picker.move} onpointerup={picker.up} onpointercancel={picker.up} oncontextmenu={picker.menu} role="grid" tabindex="-1">
    {#each days as day (day.getTime())}
      {@const list = dayEvents(day)}
      <div class="day" data-newday={dayKey(day)} data-key={dayKey(day)} class:picked-day={(ui.pickedDays, inPicked(day))} class:other={day.getMonth() !== shownMonth.getMonth() || day.getFullYear() !== shownMonth.getFullYear()} class:weekend={isWeekend(day)} class:sunday={day.getDay() === 0} class:today={sameDay(day, today)} class:today-ring={sameDay(day, today)} class:selected-day={!!selectedDay && sameDay(day, selectedDay)} onclick={(click) => { if (!(click.target as HTMLElement).closest('button')) selectedDay = day }} role="presentation">
        <button class="number" onclick={() => { ui.focus = day; ui.view = 'week' }} title="Open this week">{day.getDate() === 1 ? day.toLocaleDateString(undefined, { day: 'numeric', month: 'short' }) : day.getDate()}{#if sameDay(day, today)}<HandCircle seed={dayKey(day)} />{/if}</button>
        {#each list.shown as event (event.id)}
          {#if event.allDay}
            <button class="pill" class:span={continuesBefore(event, day) || continuesAfter(event, day)} class:picked={ui.picked.includes(event.id)} data-event={event.id} class:joined-left={continuesBefore(event, day)} class:joined-right={continuesAfter(event, day)} style="--colour: {event.colourShown}" onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>{#if !isBirthday(event) && !(continuesBefore(event, day) && day.getDay() !== prefs.weekStart)}<HandStar seed={event.id} />{/if}{continuesBefore(event, day) && day.getDay() !== prefs.weekStart ? '' : shortTitle(event)}<HandArrow {event} {day} /></button>
          {:else}
            <button class="line" class:past={event.endAt < new Date()} style="--colour: {event.colourShown}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}><span class="dot"></span><span class="time">{formatTime(event.startAt)}</span><span class="title">{shortTitle(event)}</span></button>
          {/if}
        {/each}
        {#if list.more > 0}<button class="more" onclick={() => { ui.focus = day; ui.view = 'agenda' }}>+{list.more} more</button>{/if}
      </div>
    {/each}
  </div>
  </div>
</div>

<style>
  .month { display: flex; flex-direction: column; height: 100%; min-height: 0; }
  .weekdays { display: grid; grid-template-columns: repeat(7, 1fr); padding: 0 6px; }
  .weekdays div { padding: 2px 8px 6px; font-size: 11px; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--fg-quiet); }
  .scroller { flex: 1; min-height: 0; margin: 0 6px 6px; border-radius: 10px; overflow-y: auto; scrollbar-width: none; }
  .grid { display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); grid-auto-rows: calc(var(--row) - 1px); gap: 1px; background: var(--line); }
  .day { container: day / inline-size; position: relative; transition: background-color 0.35s; display: flex; flex-direction: column; gap: 1px; min-width: 0; min-height: 0; padding: 3px 4px; background: var(--cell); overflow: hidden; }
  .day.weekend { background: var(--cell-weekend); }
  .day.other { background: var(--cell-other); }
  .day.other > :not(.number) { opacity: 0.6; transition: opacity 0.35s; }
  .number { align-self: flex-start; min-width: 22px; height: 22px; padding: 0 6px; border: 0; border-radius: 11px; background: none; color: var(--fg); font: inherit; font-size: 12.5px; font-weight: 600; cursor: pointer; font-variant-numeric: tabular-nums; }
  .other .number { color: var(--fg-quiet); font-weight: 500; }
  .number:hover { background: var(--hover); }
  .today .number { background: var(--accent); color: var(--on-accent); }
  .today { animation: today-pulse 3s ease-in-out infinite; } /* tokens.css */
  .day.selected-day { background: color-mix(in srgb, var(--fg) 8%, var(--cell)); box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--fg) 22%, transparent); }
  .day.picked-day { background: var(--accent-soft); box-shadow: inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent); } /* days picked for a new event */
  .line, .pill, .more { display: flex; align-items: center; gap: 5px; height: 17px; flex: none; min-width: 0; padding: 0 4px; border: 0; border-radius: 4px; background: none; color: var(--fg); font: inherit; font-size: 12px; text-align: left; cursor: pointer; white-space: nowrap; }
  .line:hover, .more:hover { background: var(--hover); }
  .line.past { color: var(--fg-quiet); }
  .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--colour); flex: none; }
  .time { color: var(--fg-quiet); font-variant-numeric: tabular-nums; flex: none; font-size: 11px; }
  .title { overflow: hidden; text-overflow: ellipsis; }
  .pill { background: color-mix(in srgb, var(--colour) 78%, transparent); color: #fff; font-weight: 600; text-shadow: 0 1px 1px rgba(0, 0, 0, 0.25); overflow: hidden; text-overflow: ellipsis; display: block; line-height: 17px; }
  .pill:hover { filter: brightness(1.1); }
  :global(:root[data-skin="paper"]) .line, :global(:root[data-skin="paper"]) .more { font-size: 10px; }
  :global(:root[data-skin="paper"]) .line .time { font-size: 9.5px; } /* typed text stays small beside the handwriting */
  .pill.joined-left { border-top-left-radius: 0; border-bottom-left-radius: 0; margin-left: -4px; }
  .pill.joined-right { border-top-right-radius: 0; border-bottom-right-radius: 0; margin-right: -5px; }
  .more { color: var(--fg-quiet); font-size: 11.5px; font-weight: 600; }
  /* A multi-day event is a thread rather than a block: its name over a thin line in its colour, a dot where it starts and a cap where it ends (Paper draws its own pen line) */
  :global(:root:not([data-skin="paper"])) .pill.span { position: relative; background: none; color: color-mix(in srgb, var(--colour) 55%, var(--fg)); text-shadow: none; }
  :global(:root:not([data-skin="paper"])) .pill.span::after { content: ""; position: absolute; left: 0; right: 0; bottom: 1px; height: 2px; background: var(--colour); opacity: 0.75; }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-left) { padding-left: 11px; }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-left)::before { content: ""; position: absolute; left: 2px; top: 50%; width: 6px; height: 6px; margin-top: -4px; border-radius: 50%; background: var(--colour); }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-left)::after { left: 4px; border-radius: 1px 0 0 1px; }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-right)::after { right: 6px; }
  :global(:root:not([data-skin="paper"])) .pill.span.joined-left:not(.joined-right)::before { content: ""; position: absolute; right: 2px; bottom: -1px; width: 6px; height: 6px; border-radius: 50%; background: var(--colour); } /* the end */
  :global(:root:not([data-skin="paper"])) .pill.span:hover { filter: none; background: var(--hover); }
</style>
