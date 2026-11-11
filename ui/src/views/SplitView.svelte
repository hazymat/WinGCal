<script lang="ts">
  import HandCircle from '../parts/HandCircle.svelte'
  import { ui, eventsOn, readSetting, writeSetting, type Placed } from '../state.svelte'
  import { addDays, addMonths, sameDay, startOfDay, startOfWeek, prefs, formatTime, dayKey, weekdayShort, shortDate } from '../lib/dates'
  import Icon from '../parts/Icon.svelte'
  import { warpScroll } from '../lib/warp'
  import { glideWheel } from '../lib/glide'
  import { chaseOnView } from '../lib/chase'
  import { createLamp, paneTilt } from '../lib/lamp.svelte'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  // Short, wide windows (SPEC.md › Calendar views › Split): a browsable month on the left, a scrolling day list on the right that follows the selected day
  const today = $derived(ui.today)
  let paneHeight = $state(300), paneWidth = $state(700), listWidth = $state(400)
  const monthStart = $derived(new Date(ui.focus.getFullYear(), ui.focus.getMonth(), 1))
  const gridDays = $derived(Array.from({ length: 42 }, (unused, index) => addDays(startOfWeek(monthStart, prefs.weekStart), index)))
  // Cells stay roughly square: the month takes what the height allows, never more than half the width
  // The handle between the halves sets the month's share of the width (remembered; double-click returns to automatic)
  let monthShare = $state(Number(readSetting('splitMonthShare', '0')))
  const monthWidth = $derived(monthShare > 0 ? Math.max(180, paneWidth * monthShare) : Math.min(paneWidth * 0.5, (paneHeight - 8) / 7 * 7 * 1.45))
  function dragHandle(pointerEvent: PointerEvent) {
    const handle = pointerEvent.currentTarget as HTMLElement, startX = pointerEvent.clientX, startWidth = monthWidth
    handle.setPointerCapture(pointerEvent.pointerId)
    const move = (moveEvent: PointerEvent) => { monthShare = Math.min(0.75, Math.max(0.2, (startWidth + moveEvent.clientX - startX) / paneWidth)) }
    const stop = () => { handle.removeEventListener('pointermove', move); handle.removeEventListener('pointerup', stop); writeSetting('splitMonthShare', String(monthShare.toFixed(3))) }
    handle.addEventListener('pointermove', move); handle.addEventListener('pointerup', stop)
  }
  function resetHandle() { monthShare = 0; writeSetting('splitMonthShare', '0') }
  // Each day's shape: timed events as coloured marks on a 7am-11pm line, all-day and multi-day events as a ribbon along the bottom that runs on into the next day
  const dayStartHour = 7, dayEndHour = 23
  function dayShape(day: Date) {
    const dayEvents = eventsOn(events, day), dayStart = startOfDay(day).getTime() + dayStartHour * 3_600_000, span = (dayEndHour - dayStartHour) * 3_600_000
    const timed = dayEvents.filter(event => !event.allDay).map(event => { const from = Math.max(0, (event.startAt.getTime() - dayStart) / span), to = Math.min(1, (event.endAt.getTime() - dayStart) / span); return { colour: event.colourShown, left: Math.min(from, 0.97) * 100, width: Math.max(3, (to - from) * 100) } })
    const ribbons = dayEvents.filter(event => event.allDay).sort((first, second) => first.startAt.getTime() - second.startAt.getTime() || (second.endAt.getTime() - first.endAt.getTime())).slice(0, 3).map(event => ({ colour: event.colourShown, first: sameDay(event.startAt, day), last: sameDay(new Date(event.endAt.getTime() - 1), day) }))
    return { timed, ribbons }
  }
  const isWeekend = (day: Date) => day.getDay() === 0 || day.getDay() === 6

  // The day list covers a window of days around an anchor; it moves when the selection nears either end
  let anchor = $state(addMonths(ui.focus, -2))
  const listDays = $derived(Array.from({ length: Math.round((addMonths(anchor, 9).getTime() - anchor.getTime()) / 86_400_000) }, (unused, index) => addDays(anchor, index)).map(day => ({ day, list: eventsOn(events, day) })))
  let list: HTMLDivElement | undefined = $state()
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
  // The selected day sits a quarter of the way down, with a glimpse of the days before it above
  const readingLine = () => (list?.clientHeight ?? 0) * 0.25
  function scrollToDay(day: Date, smooth: boolean) { userScrolling = false; const section = list?.querySelector<HTMLElement>(`[data-day="${dayKey(day)}"]`); if (!section || !list) return; if (smooth) warpScroll(list, section.offsetTop - readingLine()); else list.scrollTop = section.offsetTop - readingLine() }
  // The day on the reading line becomes the selected day
  // Today out of sight: a Today pill floats over the top of the list, pointing the way (as in Agenda)
  let todayInView = $state(true)
  const { lamp, update: updateLamp } = createLamp() // tungsten over past days, as in Agenda (lib/lamp)
  function onScroll() {
    if (list) { const todaySection = list.querySelector<HTMLElement>('section.today'); todayInView = !!todaySection && todaySection.offsetTop + todaySection.offsetHeight > list.scrollTop + 20 && todaySection.offsetTop < list.scrollTop + list.clientHeight - 20; const line = list.scrollTop + readingLine() + 6; updateLamp(list, Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.offsetTop + candidate.offsetHeight > line)) }
    if (!userScrolling || !list) return
    const top = list.scrollTop + readingLine() + 6
    const section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.offsetTop + candidate.offsetHeight > top)
    if (!section) return
    const [year, month, date] = section.dataset.day!.split('-').map(Number), day = new Date(year, month - 1, date)
    if (sameDay(day, ui.focus)) return
    focusFromScroll = day.getTime(); ui.focus = day
  }
  // A resize reflows the list: that isn't the person scrolling, so stay on the selected day
  $effect(() => { if (!list) return; const observer = new ResizeObserver(() => { userScrolling = false; scrollToDay(ui.focus, false) }); observer.observe(list); return () => observer.disconnect() })
  function pickDay(day: Date) { focusFromScroll = 0; ui.focus = day }
  // The wheel over the month turns its pages
  function onMonthWheel(event: WheelEvent) { event.preventDefault(); if (Math.abs(event.deltaY) < 4) return; const target = addMonths(monthStart, event.deltaY > 0 ? 1 : -1); pickDay(target.getMonth() === today.getMonth() && target.getFullYear() === today.getFullYear() ? today : target) }
</script>

<div class="split" bind:clientHeight={paneHeight} bind:clientWidth={paneWidth}>
  <div class="month" style="width: {monthWidth}px" onwheel={onMonthWheel}>
    {#each gridDays.slice(0, 7) as day}<span class="weekday" class:weekend={isWeekend(day)}>{day.toLocaleDateString(undefined, { weekday: 'narrow' })}</span>{/each}
    {#each gridDays as day (day.getTime())}
      <button class="cell" data-newday={dayKey(day)} class:weekend={isWeekend(day)} class:sunday={day.getDay() === 0} class:other={day.getMonth() !== monthStart.getMonth()} class:today={sameDay(day, today)} class:selected={sameDay(day, ui.focus)} onclick={() => pickDay(day)}>
        <span class="number">{day.getDate()}</span>{#if sameDay(day, today) || sameDay(day, ui.focus)}<HandCircle seed={dayKey(day)} />{/if}<span class="hours">{#each dayShape(day).timed as mark}<i style="left: {mark.left}%; width: {mark.width}%; background: {mark.colour}"></i>{/each}</span><span class="ribbons">{#each dayShape(day).ribbons as ribbon}<i class:first={ribbon.first} class:last={ribbon.last} style="background: {ribbon.colour}"></i>{/each}</span>
      </button>
    {/each}
  </div>
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="handle" onpointerdown={dragHandle} ondblclick={resetHandle} title="Drag to resize; double-click for automatic"><i></i></div>
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
  <div class="days-wrap">
  {#if !todayInView}<button class="jump-today" onclick={() => pickDay(today)} title="Back to today (T)"><Icon name={ui.focus > today ? 'up' : 'down'} size={12} />Today</button>{/if}
  <div class="lamp" class:on={lamp.warmed} style="height: min(100%, {lamp.height}px)" aria-hidden="true"></div>
  <div class="days" class:warmed={lamp.warmed} class:lit={(ui.lookVersion, prefs.stripLight > 0)} style:--strip={(ui.lookVersion, prefs.stripLight / 50)} bind:this={list} bind:clientWidth={listWidth} onscroll={onScroll} onwheel={(wheelEvent) => { userScrolling = true; if (list) glideWheel(list, wheelEvent) }} onpointerdown={() => userScrolling = true} onkeydown={() => userScrolling = true} role="region" aria-label="Days" tabindex="-1">
    {#each listDays.filter(entry => entry.list.length > 0 || sameDay(entry.day, today) || sameDay(entry.day, ui.focus)) as { day, list: dayEvents } (day.getTime())}
      <section data-day={dayKey(day)} data-newday={dayKey(day)} class:today={sameDay(day, today)} class:today-chase={sameDay(day, today)} use:chaseOnView={sameDay(day, today)} class:gone={day < today}>
        <h3 class="warm">{dayLabel(day)}</h3>
        <div class="day-items warm" class:two={listWidth >= 480 && dayEvents.length > 1}>
        {#if dayEvents.length === 0}<div class="empty">Nothing on</div>{/if}
        {#each dayEvents as event (event.id)}
          <button class="item" class:past={event.endAt < new Date()} style="--colour: {event.colourShown}; {paneTilt(event.id + dayKey(day))}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>
            <i class="dot"></i>
            <span class="time">{event.allDay ? allDayLabel(event, day) : formatTime(event.startAt) + ' – ' + formatTime(event.endAt)}{#if event.meetUrl}<Icon name="video" size={12} />{/if}</span>
            <span class="title">{event.title}</span>
          </button>
        {/each}
        </div>
      </section>
    {/each}
  </div>
  </div>
</div>

<style>
  .split { display: flex; gap: 0; height: 100%; min-height: 0; padding: 0 10px 16px 12px; box-sizing: border-box; }
  .month { flex: none; display: grid; grid-template-columns: repeat(7, 1fr); grid-template-rows: auto repeat(6, 1fr); gap: 3px; align-content: stretch; }
  .weekday { text-align: center; font-size: 11px; font-weight: 600; color: var(--fg-quiet); padding: 2px 0 4px; border-bottom: 1px solid var(--line); }
  .weekday.weekend { color: var(--fg-quiet); opacity: 0.75; }
  .cell { position: relative; min-height: 0; border: 0; border-radius: 8px; background: var(--cell); color: var(--fg); font: inherit; font-size: clamp(11px, 2.2vh, 15px); font-variant-numeric: tabular-nums; cursor: pointer; padding: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
  .cell.weekend { background: var(--cell-weekend); }
  .cell.other { background: none; color: var(--fg-quiet); opacity: 0.55; }
  .cell:hover { background: var(--hover); }
  .cell.today { animation: today-pulse 3s ease-in-out infinite; }
  section.today { animation: agenda-drift linear both, today-pulse 3s ease-in-out infinite; animation-timeline: view(), auto; animation-range: cover 0% cover 100%, normal; }
  section.today { border-radius: 8px; padding: 2px 4px 6px; }
  .cell.today .number { color: var(--accent); font-weight: 750; }
  .cell.selected { box-shadow: inset 0 0 0 1.5px var(--fg); }
  .cell.today.selected { box-shadow: none; }
  .hours { position: absolute; left: 14%; right: 14%; bottom: 26%; height: 4px; border-radius: 2px; background: color-mix(in srgb, var(--fg) 8%, transparent); }
  .hours i { position: absolute; top: 0; bottom: 0; border-radius: 2px; min-width: 3px; }
  .ribbons { position: absolute; left: -1.5px; right: -1.5px; bottom: 5px; display: flex; flex-direction: column-reverse; gap: 2px; } /* the longest-running stays on the bottom lane so a trip lines up across days */
  .ribbons i { height: 3px; opacity: 0.85; }
  .ribbons i.first { margin-left: 8px; border-top-left-radius: 2px; border-bottom-left-radius: 2px; }
  .ribbons i.last { margin-right: 8px; border-top-right-radius: 2px; border-bottom-right-radius: 2px; }
  .handle { flex: none; width: 14px; display: grid; place-items: center; cursor: col-resize; touch-action: none; }
  .handle i { width: 3px; height: 36px; border-radius: 2px; background: var(--line); transition: background 0.15s, height 0.15s; }
  .handle:hover i, .handle:active i { background: var(--accent); height: 56px; }
  .days-wrap { flex: 1; min-width: 0; position: relative; display: flex; }
  .jump-today { position: absolute; z-index: 2; top: 8px; left: 50%; transform: translateX(-50%); display: flex; align-items: center; gap: 5px; height: 26px; padding: 0 12px 0 9px; border: 1px solid color-mix(in srgb, var(--accent) 40%, transparent); border-radius: 13px; background: color-mix(in srgb, var(--panel-solid) 55%, transparent); backdrop-filter: blur(14px) saturate(1.5); color: var(--accent); font: inherit; font-size: 12px; font-weight: 600; cursor: pointer; box-shadow: 0 4px 18px rgba(0, 0, 0, 0.28), inset 0 1px 0 rgba(255, 255, 255, 0.08); animation: jump-in 0.2s ease-out; }
  .jump-today:hover { background: color-mix(in srgb, var(--accent) 22%, color-mix(in srgb, var(--panel-solid) 55%, transparent)); }
  @keyframes jump-in { from { opacity: 0; transform: translate(-50%, -6px); } }
  .days { flex: 1; min-width: 0; overflow-y: auto; position: relative; scrollbar-width: thin; mask-image: linear-gradient(transparent, #000 10%, #000 88%, transparent); }
  /* As Agenda: days drift in from below and fade as they pass the top */
  section { animation: agenda-drift linear both; animation-timeline: view(); animation-range: cover 0% cover 100%; }
  section.gone .day-items, section.gone h3 { opacity: 0.55; }
  section { padding-bottom: 8px; }
  h3 { margin: 0; padding: 2px 4px 3px; font-size: 11.5px; font-weight: 700; letter-spacing: 0.04em; text-transform: uppercase; color: var(--fg-quiet); }
  .today h3 { color: var(--accent); }
  .empty { padding: 1px 4px 2px 10px; font-size: 12px; color: var(--fg-quiet); }
  .day-items { display: grid; grid-template-columns: 1fr; gap: 4px 6px; padding: 0 2px; }
  .day-items.two { grid-template-columns: 1fr 1fr; }
  .item { display: flex; align-items: center; gap: 8px; min-width: 0; width: 100%; padding: 5px 8px; border: 0; border-radius: 8px; background: color-mix(in srgb, var(--colour) var(--event-tint, 0%), var(--panel)); box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.06); color: var(--fg); font: inherit; font-size: 12.5px; line-height: 1.35; text-align: left; cursor: pointer; }
  .item:hover { background: color-mix(in srgb, var(--colour) var(--event-tint, 0%), var(--panel-strong)); }
  .item.past { opacity: 0.55; }
  .dot { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--colour); }
  .time { flex: none; display: flex; align-items: center; gap: 4px; font-size: 11.5px; color: var(--fg-quiet); font-variant-numeric: tabular-nums; white-space: nowrap; }
  .title { min-width: 0; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
