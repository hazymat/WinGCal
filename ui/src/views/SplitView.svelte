<script lang="ts">
  import HandCircle from '../parts/HandCircle.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, addMonths, sameDay, startOfDay, startOfWeek, prefs, formatTime, dayKey, weekdayShort, shortDate } from '../lib/dates'
  import Icon from '../parts/Icon.svelte'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  // Short, wide windows (SPEC.md › Calendar views › Split): a browsable month on the left, a scrolling day list on the right that follows the selected day
  const today = startOfDay(new Date())
  let paneHeight = $state(300), paneWidth = $state(700)
  const monthStart = $derived(new Date(ui.focus.getFullYear(), ui.focus.getMonth(), 1))
  const gridDays = $derived(Array.from({ length: 42 }, (unused, index) => addDays(startOfWeek(monthStart, prefs.weekStart), index)))
  // Cells stay roughly square: the month takes what the height allows, never more than half the width
  const monthWidth = $derived(Math.min(paneWidth * 0.5, (paneHeight - 8) / 7 * 7 * 1.45))
  const dots = (day: Date) => Array.from(new Set(eventsOn(events, day).map(event => event.colourShown))).slice(0, 3)
  const isWeekend = (day: Date) => day.getDay() === 0 || day.getDay() === 6

  // The day list covers a window of days around an anchor; it moves when the selection nears either end
  let anchor = $state(addMonths(ui.focus, -2))
  const listDays = $derived(Array.from({ length: Math.round((addMonths(anchor, 9).getTime() - anchor.getTime()) / 86_400_000) }, (unused, index) => addDays(anchor, index)).map(day => ({ day, list: eventsOn(events, day) })))
  let list: HTMLDivElement
  let focusFromScroll = 0 // the day the list itself chose, so it isn't scrolled back to
  let userScrolling = false // only the person's own scrolling picks the day; layout shifts (fonts, resizes) don't
  const dayLabel = (day: Date) => { const offset = Math.round((day.getTime() - today.getTime()) / 86_400_000); return offset === 0 ? 'Today' : offset === 1 ? 'Tomorrow' : offset === -1 ? 'Yesterday' : `${weekdayShort(day)} ${shortDate(day)}` }
  const allDayLabel = (event: Placed, day: Date) => { const total = Math.round((event.endAt.getTime() - event.startAt.getTime()) / 86_400_000); if (total <= 1) return 'All day'; return `Day ${Math.round((startOfDay(day).getTime() - event.startAt.getTime()) / 86_400_000) + 1} of ${total}` }

  $effect(() => {
    const focus = ui.focus
    if (focus.getTime() === focusFromScroll) return
    if (focus < addDays(anchor, 14) || focus > addMonths(anchor, 8)) { anchor = addMonths(focus, -2); requestAnimationFrame(() => scrollToDay(focus, false)); return }
    requestAnimationFrame(() => scrollToDay(focus, true))
  })
  $effect(() => { document.fonts?.ready.then(() => { if (!userScrolling) scrollToDay(ui.focus, false) }) })
  function scrollToDay(day: Date, smooth: boolean) { userScrolling = false; const section = list?.querySelector<HTMLElement>(`[data-day="${dayKey(day)}"]`); if (section) list.scrollTo({ top: section.offsetTop, behavior: smooth ? 'smooth' : 'instant' }) }
  // The top day in the list becomes the selected day
  function onScroll() {
    if (!userScrolling) return
    const top = list.scrollTop + 6
    const section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.offsetTop + candidate.offsetHeight > top)
    if (!section) return
    const [year, month, date] = section.dataset.day!.split('-').map(Number), day = new Date(year, month - 1, date)
    if (sameDay(day, ui.focus)) return
    focusFromScroll = day.getTime(); ui.focus = day
  }
  function pickDay(day: Date) { focusFromScroll = 0; ui.focus = day }
  // The wheel over the month turns its pages
  function onMonthWheel(event: WheelEvent) { event.preventDefault(); if (Math.abs(event.deltaY) < 4) return; const target = addMonths(monthStart, event.deltaY > 0 ? 1 : -1); pickDay(target.getMonth() === today.getMonth() && target.getFullYear() === today.getFullYear() ? today : target) }
</script>

<div class="split" bind:clientHeight={paneHeight} bind:clientWidth={paneWidth}>
  <div class="month" style="width: {monthWidth}px" onwheel={onMonthWheel}>
    {#each gridDays.slice(0, 7) as day}<span class="weekday" class:weekend={isWeekend(day)}>{day.toLocaleDateString(undefined, { weekday: 'narrow' })}</span>{/each}
    {#each gridDays as day (day.getTime())}
      <button class="cell" class:weekend={isWeekend(day)} class:sunday={day.getDay() === 0} class:other={day.getMonth() !== monthStart.getMonth()} class:today={sameDay(day, today)} class:selected={sameDay(day, ui.focus)} onclick={() => pickDay(day)}>
        <span class="number">{day.getDate()}</span>{#if sameDay(day, today) || sameDay(day, ui.focus)}<HandCircle seed={dayKey(day)} />{/if}<span class="dots">{#each dots(day) as colour}<i style="background: {colour}"></i>{/each}</span>
      </button>
    {/each}
  </div>
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
  <div class="days" bind:this={list} onscroll={onScroll} onwheel={() => userScrolling = true} onpointerdown={() => userScrolling = true} onkeydown={() => userScrolling = true} role="region" aria-label="Days" tabindex="-1">
    {#each listDays as { day, list: dayEvents } (day.getTime())}
      <section data-day={dayKey(day)} class:today={sameDay(day, today)}>
        <h3>{dayLabel(day)}</h3>
        {#if dayEvents.length === 0}<div class="empty">Nothing on</div>{/if}
        {#each dayEvents as event (event.id)}
          <button class="item" class:past={event.endAt < new Date()} style="--colour: {event.colourShown}" onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>
            <i class="dot"></i>
            <span class="time">{event.allDay ? allDayLabel(event, day) : formatTime(event.startAt) + ' – ' + formatTime(event.endAt)}{#if event.meetUrl}<Icon name="video" size={12} />{/if}</span>
            <span class="title">{event.title}</span>
          </button>
        {/each}
      </section>
    {/each}
  </div>
</div>

<style>
  .split { display: flex; gap: 14px; height: 100%; min-height: 0; padding: 0 10px 10px 12px; }
  .month { flex: none; display: grid; grid-template-columns: repeat(7, 1fr); grid-template-rows: auto repeat(6, 1fr); gap: 3px; align-content: stretch; }
  .weekday { text-align: center; font-size: 11px; font-weight: 600; color: var(--fg-quiet); padding: 2px 0 4px; border-bottom: 1px solid var(--line); }
  .weekday.weekend { color: var(--fg-quiet); opacity: 0.75; }
  .cell { position: relative; min-height: 0; border: 0; border-radius: 8px; background: var(--cell); color: var(--fg); font: inherit; font-size: clamp(11px, 2.2vh, 15px); font-variant-numeric: tabular-nums; cursor: pointer; padding: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
  .cell.weekend { background: var(--cell-weekend); }
  .cell.other { background: none; color: var(--fg-quiet); opacity: 0.55; }
  .cell:hover { background: var(--hover); }
  .cell.today .number { color: var(--accent); font-weight: 750; }
  .cell.selected { box-shadow: inset 0 0 0 1.5px var(--fg); }
  .cell.today.selected { box-shadow: inset 0 0 0 1.5px var(--accent); background: var(--accent-soft); }
  .dots { position: absolute; left: 0; right: 0; bottom: 12%; display: flex; justify-content: center; gap: 2px; }
  .dots i { width: 4px; height: 4px; border-radius: 50%; }
  .days { flex: 1; min-width: 0; overflow-y: auto; position: relative; scrollbar-width: thin; mask-image: linear-gradient(transparent, #000 8px, #000 calc(100% - 14px), transparent); }
  section { padding-bottom: 6px; }
  h3 { margin: 0; padding: 3px 2px 5px; font-size: 15px; font-weight: 700; letter-spacing: 0.02em; text-transform: uppercase; }
  .today h3 { color: var(--accent); }
  .empty { padding: 2px 4px 6px 22px; font-size: 12.5px; color: var(--fg-quiet); }
  .item { display: grid; grid-template-columns: 12px 1fr; column-gap: 10px; width: 100%; padding: 4px 4px 5px; border: 0; border-radius: 8px; background: none; color: var(--fg); font: inherit; text-align: left; cursor: pointer; }
  .item:hover { background: var(--hover); }
  .item.past { opacity: 0.55; }
  .dot { grid-row: 1; align-self: center; width: 11px; height: 11px; border-radius: 50%; background: var(--colour); }
  .time { display: flex; align-items: center; gap: 5px; font-size: 12.5px; color: var(--fg-quiet); font-variant-numeric: tabular-nums; }
  .title { grid-column: 2; font-size: 14px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
