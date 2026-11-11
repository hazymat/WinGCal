<script lang="ts">
  import HandStar from '../parts/HandStar.svelte'
  import { shortTitle, isBirthday } from '../lib/titles'
  import HandArrow from '../parts/HandArrow.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { carry } from '../lib/carry.svelte'
  import { dayPicker, inPicked, swallowClick } from '../lib/pickDays'
  import { addDays, sameDay, startOfDay, startOfWeek, formatTime, formatHour, weekdayShort, prefs, minutesIntoDay, dayKey } from '../lib/dates'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  let width = $state(800), height = $state(600)
  // Too short for a time grid (a strip along the screen's edge): each day lists its events instead
  const listMode = $derived(height < 300)
  // Seven days when there's room, else three or one starting at the focus day
  const dayCount = $derived(width >= 560 ? 7 : width >= 360 ? 3 : 1)
  const firstDay = $derived(dayCount === 7 ? startOfWeek(ui.focus, prefs.weekStart) : startOfDay(ui.focus))
  const days = $derived(Array.from({ length: dayCount }, (unused, index) => addDays(firstDay, index)))
  const hourHeight = 44
  let now = $state(new Date())
  $effect(() => { const timer = setInterval(() => now = new Date(), 60_000); return () => clearInterval(timer) })
  const today = $derived(startOfDay(now))

  // Timed events of a day laid out side by side where they overlap: each cluster splits its width into as many columns as it needs
  function layout(day: Date) {
    const dayStart = startOfDay(day).getTime()
    const timed = eventsOn(events, day).filter(event => !event.allDay).map(event => ({ event, top: Math.max(0, (event.startAt.getTime() - dayStart) / 60000), bottom: Math.min(1440, (event.endAt.getTime() - dayStart) / 60000), column: 0, columns: 1 }))
    let cluster: typeof timed = [], clusterEnd = -1
    const columnEnds: number[] = []
    const closeCluster = () => { const count = Math.max(1, columnEnds.length); for (const item of cluster) item.columns = count; cluster = []; columnEnds.length = 0 }
    for (const item of timed) {
      if (item.top >= clusterEnd) closeCluster()
      let column = columnEnds.findIndex(end => end <= item.top)
      if (column < 0) { column = columnEnds.length; columnEnds.push(0) }
      columnEnds[column] = Math.max(item.bottom, item.top + 20); item.column = column
      cluster.push(item); clusterEnd = Math.max(clusterEnd, item.bottom)
    }
    closeCluster()
    return timed
  }
  const continuesBefore = (event: Placed, day: Date) => event.startAt.getTime() < startOfDay(day).getTime()
  const continuesAfter = (event: Placed, day: Date) => event.endAt.getTime() > addDays(startOfDay(day), 1).getTime()
  const allDayOn = (day: Date) => eventsOn(events, day).filter(event => event.allDay)

  // Sideways wheel (tilt wheel or Shift+wheel) moves a whole page of days: the old page slides slowly out as the new one slides in beside it, no fade; up and down anywhere scrolls the hours
  let sideways = 0, lastPage = 0, page: HTMLDivElement | undefined = $state(), slider: HTMLDivElement | undefined = $state(), leaving: HTMLElement | null = null
  const slideMs = 900, slideEase = 'cubic-bezier(0.45, 0, 0.2, 1)'
  function turnPage(direction: number) {
    if (!slider) { ui.focus = addDays(ui.focus, direction * dayCount); return }
    leaving?.remove()
    const snapshot = slider.cloneNode(true) as HTMLElement, scrollTop = scroller?.scrollTop ?? 0
    snapshot.classList.add('leaving'); slider.parentElement!.append(snapshot); leaving = snapshot
    const oldScroller = snapshot.querySelector<HTMLElement>('.scroller'); if (oldScroller) oldScroller.scrollTop = scrollTop
    ui.focus = addDays(ui.focus, direction * dayCount)
    snapshot.animate([{ transform: 'none' }, { transform: `translateX(${-direction * 100}%)` }], { duration: slideMs, easing: slideEase, fill: 'forwards' }).finished.then(() => { snapshot.remove(); if (leaving === snapshot) leaving = null }, () => {})
    slider.animate([{ transform: `translateX(${direction * 100}%)` }, { transform: 'none' }], { duration: slideMs, easing: slideEase })
  }
  function onWheel(wheelEvent: WheelEvent) {
    if (Math.abs(wheelEvent.deltaX) <= Math.abs(wheelEvent.deltaY)) { if (scroller && !scroller.contains(wheelEvent.target as Node)) scroller.scrollTop += wheelEvent.deltaY; return }
    wheelEvent.preventDefault()
    if (performance.now() - lastPage < 450) { sideways = 0; return } // one page per flick
    sideways += wheelEvent.deltaX
    if (Math.abs(sideways) < 40) return
    const direction = Math.sign(sideways); sideways = 0; lastPage = performance.now()
    turnPage(direction)
  }
  // Hours for a new event: drag down a day (on into the next days to run across them) or press and hold an hour; the bar then offers New event. Quarter hours
  const allDayPicker = dayPicker()
  let selectedDay: Date | null = $state(null) // a click on a day just marks it, softly (today keeps its ring)
  const selectDay = (click: MouseEvent, day: Date) => { if (!(click.target as HTMLElement).closest('[data-event]')) selectedDay = day }
  let timeStart: { day: Date; minute: number } | null = null, timePicking = false, timeHold = 0, timePress = { x: 0, y: 0 }
  function slotAt(pointer: PointerEvent) {
    const column = (document.elementFromPoint(pointer.clientX, pointer.clientY) as HTMLElement | null)?.closest<HTMLElement>('.column[data-key]'); if (!column) return null
    const [year, month, date] = column.dataset.key!.split('-').map(Number), minute = Math.max(0, Math.min(1425, Math.floor((pointer.clientY - column.getBoundingClientRect().top) / hourHeight * 4) * 15))
    return { day: new Date(year, month - 1, date), minute }
  }
  const timeOf = (slot: { day: Date; minute: number }) => new Date(slot.day.getFullYear(), slot.day.getMonth(), slot.day.getDate(), 0, slot.minute)
  function pickTimes(first: Date, second: Date, length: number) { const [from, to] = first <= second ? [first, second] : [second, first]; ui.picked = []; ui.pickedDays = { from, to: new Date(to.getTime() + length * 60_000), timed: true } }
  function timeDown(pointer: PointerEvent) {
    if (pointer.button !== 0 || (pointer.target as HTMLElement).closest('[data-event]')) return
    timeStart = slotAt(pointer); timePicking = false; timePress = { x: pointer.clientX, y: pointer.clientY }; clearTimeout(timeHold)
    const held = timeStart; if (held) timeHold = setTimeout(() => { if (timeStart === held) { timePicking = true; const start = timeOf(held); pickTimes(start, start, 60) } }, prefs.holdMs) // a held hour: an hour from there
  }
  function timeMove(pointer: PointerEvent) {
    if (!timeStart || (!timePicking && Math.hypot(pointer.clientX - timePress.x, pointer.clientY - timePress.y) < 6)) return
    const over = slotAt(pointer); if (!over) return
    clearTimeout(timeHold)
    if (!timePicking) (pointer.currentTarget as HTMLElement).setPointerCapture(pointer.pointerId)
    timePicking = true; pickTimes(timeOf(timeStart), timeOf(over), 15)
  }
  function timeUp() { clearTimeout(timeHold); if (timeStart && timePicking) swallowClick(); timeStart = null; timePicking = false }
  // The part of a day the dragged hours cover (minutes from midnight), and whether the label goes there
  function pickedOn(day: Date) {
    const picked = ui.pickedDays; if (!picked?.timed) return null
    const dayStart = startOfDay(day).getTime(), top = Math.max(0, (picked.from.getTime() - dayStart) / 60_000), bottom = Math.min(1440, (picked.to.getTime() - dayStart) / 60_000)
    return bottom > top ? { top, bottom, first: sameDay(day, picked.from) } : null
  }
  // Opens at the working day (an hour before the earliest event, 07:00 at the latest)
  let scroller: HTMLDivElement | undefined = $state()
  $effect(() => {
    days; if (!scroller) return
    const earliest = days.flatMap(day => layout(day).map(item => item.top)).reduce((lowest, top) => Math.min(lowest, top), 7 * 60)
    scroller.scrollTop = Math.max(0, (earliest / 60 - 1) * hourHeight)
  })
</script>

<div class="week" bind:this={page} bind:clientWidth={width} bind:clientHeight={height} onwheel={onWheel}>
  <div class="slider" bind:this={slider}>
  <div class="head" style="--days: {dayCount}">
    <div class="gutter"></div>
    {#each days as day}
      <div class="dayname" class:today={sameDay(day, today)} class:selected-day={!!selectedDay && sameDay(day, selectedDay)} onclick={() => selectedDay = day} role="presentation"><span class="weekday">{weekdayShort(day)}</span><span class="date">{day.getDate()}</span></div>
    {/each}
  </div>
  {#if listMode}
  <div class="lists" style="--days: {dayCount}">
    <div class="gutter"></div>
    {#each days as day}
      <div class="day-list" class:today={sameDay(day, today)} class:today-ring={sameDay(day, today)} class:selected-day={!!selectedDay && sameDay(day, selectedDay)} onclick={(click) => selectDay(click, day)} role="presentation">
        {#each eventsOn(events, day) as event (event.id)}
          {#if event.allDay}<button class="pill" style="--colour: {event.colourShown}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>{#if !isBirthday(event)}<HandStar seed={event.id} />{/if}{shortTitle(event)}<HandArrow {event} {day} /></button>
          {:else}<button class="line" class:past={event.endAt < now} class:picked={ui.picked.includes(event.id)} data-event={event.id} style="--colour: {event.colourShown}" onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}><span class="dot"></span><span class="time">{formatTime(event.startAt)}</span><span class="title">{shortTitle(event)}</span></button>{/if}
        {/each}
      </div>
    {/each}
  </div>
  {:else}
  <div class="allday" style="--days: {dayCount}" onpointerdown={allDayPicker.down} onpointermove={allDayPicker.move} onpointerup={allDayPicker.up} onpointercancel={allDayPicker.up} oncontextmenu={allDayPicker.menu} role="grid" tabindex="-1">
    <div class="gutter"></div>
    {#each days as day, dayIndex}<div class="allday-cell" data-key={dayKey(day)} class:picked-day={(ui.pickedDays, inPicked(day))}>{#each allDayOn(day) as event (event.id)}{@const before = continuesBefore(event, day)}{@const after = continuesAfter(event, day)}<button class="pill" class:span={before || after} class:joined-left={before} class:joined-right={after} style="--colour: {event.colourShown}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>{#if before && dayIndex > 0}&nbsp;{:else}{#if !isBirthday(event)}<HandStar seed={event.id} />{/if}{shortTitle(event)}{/if}<HandArrow {event} {day} /></button>{/each}</div>{/each}
  </div>
  <div class="scroller" bind:this={scroller}>
    <div class="body" style="--days: {dayCount}; height: {hourHeight * 24}px" onpointerdown={timeDown} onpointermove={timeMove} onpointerup={timeUp} onpointercancel={timeUp} role="grid" tabindex="-1">
      <div class="hours">{#each Array.from({ length: 24 }, (unused, hour) => hour) as hour}<div class="hour" style="top: {hour * hourHeight}px">{hour ? formatHour(hour) : ''}</div>{/each}</div>
      {#each days as day}
        <div class="column" data-newday={dayKey(day)} data-key={dayKey(day)} data-hour-height={hourHeight} class:today={sameDay(day, today)} class:today-ring={sameDay(day, today)} class:selected-day={!!selectedDay && sameDay(day, selectedDay)} onclick={(click) => selectDay(click, day)} role="presentation">
          {#if pickedOn(day)}{@const span = pickedOn(day)!}<div class="time-pick" style="top: {span.top / 60 * hourHeight}px; height: {(span.bottom - span.top) / 60 * hourHeight}px">{#if span.first}<span>{formatTime(ui.pickedDays!.from)} – {formatTime(ui.pickedDays!.to)}</span>{/if}</div>{/if}
          {#each layout(day) as item (item.event.id)}
            {@const height = Math.max(18, (item.bottom - item.top) / 60 * hourHeight - 2)}
            <button class="event" class:short={height < 36} class:past={item.event.endAt < now} style="--colour: {item.event.colourShown}; top: {item.top / 60 * hourHeight + 1}px; height: {height}px; left: calc({item.column} * 100% / {item.columns} + 2px); width: calc(100% / {item.columns} - 4px)" class:picked={ui.picked.includes(item.event.id)} data-event={item.event.id} onclick={(clickEvent) => onpick(item.event, clickEvent.currentTarget)}>
              <span class="title">{shortTitle(item.event)}</span><span class="time">{formatTime(item.event.startAt)}{height >= 36 ? ' – ' + formatTime(item.event.endAt) : ''}</span>
            </button>
          {/each}
          {#if carry.active && carry.previewTop >= 0 && carry.targetDay === dayKey(day)}<div class="drop-preview" style="top: {carry.previewTop / 60 * hourHeight + 1}px; height: {Math.max(18, carry.previewLength / 60 * hourHeight - 2)}px"></div>{/if}
          {#if sameDay(day, today)}<div class="now" style="top: {minutesIntoDay(now) / 60 * hourHeight}px"></div>{/if}
        </div>
      {/each}
    </div>
  </div>
  {/if}
  </div>
</div>

<style>
  .week { position: relative; display: flex; flex-direction: column; height: 100%; min-height: 0; padding: 0 6px 6px; overflow: hidden; }
  .lists { flex: 1; min-height: 0; display: grid; grid-template-columns: 52px repeat(var(--days), minmax(0, 1fr)); }
  .day-list { display: flex; flex-direction: column; gap: 2px; padding: 2px 3px; border-left: 1px solid var(--line); overflow-y: auto; min-width: 0; }
  .day-list.today { background: var(--today-column); border-radius: 8px; animation: today-pulse 3s ease-in-out infinite; }
  .line { display: flex; align-items: center; gap: 5px; height: 19px; flex: none; padding: 0 4px; border: 0; border-radius: 4px; background: none; color: var(--fg); font: inherit; font-size: 12px; text-align: left; cursor: pointer; white-space: nowrap; min-width: 0; }
  .line:hover { background: var(--hover); }
  .line.past { color: var(--fg-quiet); }
  .line .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--colour); flex: none; }
  .line .time { color: var(--fg-quiet); font-size: 11px; flex: none; font-variant-numeric: tabular-nums; }
  .line .title { overflow: hidden; text-overflow: ellipsis; }
  .head, .allday, .body { display: grid; grid-template-columns: 52px repeat(var(--days), minmax(0, 1fr)); }
  .dayname { display: flex; align-items: baseline; justify-content: center; gap: 6px; padding: 2px 0 6px; color: var(--fg-quiet); }
  .weekday { font-size: 11px; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; }
  .date { font-size: 18px; font-weight: 600; color: var(--fg); font-variant-numeric: tabular-nums; }
  .dayname.today .date { background: var(--accent); color: var(--on-accent); border-radius: 14px; padding: 0 8px; }
  .dayname.today .weekday { color: var(--accent); }
  .allday { border-bottom: 1px solid var(--line); padding-bottom: 4px; }
  .allday-cell { display: flex; flex-direction: column; gap: 2px; padding: 0 2px; min-width: 0; }
  .pill { flex: none; height: 19px; padding: 0 6px; border: 0; border-radius: 4px; background: color-mix(in srgb, var(--colour) 78%, transparent); color: #fff; font: inherit; font-size: 12px; font-weight: 600; text-align: left; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; cursor: pointer; text-shadow: 0 1px 1px rgba(0, 0, 0, 0.25); }
  .pill.joined-left { border-top-left-radius: 0; border-bottom-left-radius: 0; margin-left: -3px; }
  .pill.joined-right { border-top-right-radius: 0; border-bottom-right-radius: 0; margin-right: -3px; }
  /* A multi-day event is a thread rather than a block: its name over a thin line in its colour, a dot where it starts and a cap where it ends (Paper draws its own pen line) */
  :global(:root:not([data-skin="paper"])) .pill.span { position: relative; background: none; color: color-mix(in srgb, var(--colour) 55%, var(--fg)); text-shadow: none; }
  :global(:root:not([data-skin="paper"])) .pill.span::after { content: ""; position: absolute; left: 0; right: 0; bottom: 1px; height: 2px; background: var(--colour); opacity: 0.75; }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-left) { padding-left: 11px; }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-left)::before { content: ""; position: absolute; left: 2px; top: 50%; width: 6px; height: 6px; margin-top: -4px; border-radius: 50%; background: var(--colour); }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-left)::after { left: 4px; border-radius: 1px 0 0 1px; }
  :global(:root:not([data-skin="paper"])) .pill.span:not(.joined-right)::after { right: 6px; }
  :global(:root:not([data-skin="paper"])) .pill.span.joined-left:not(.joined-right)::before { content: ""; position: absolute; right: 2px; bottom: -1px; width: 6px; height: 6px; border-radius: 50%; background: var(--colour); } /* the end */
  :global(:root:not([data-skin="paper"])) .pill.span:hover { filter: none; background: var(--hover); }
  .scroller { flex: 1; min-height: 0; overflow-y: auto; border-radius: 0 0 10px 10px; }
  .body { position: relative; }
  .hours { position: relative; }
  .hour { position: absolute; right: 8px; transform: translateY(-50%); font-size: 10.5px; color: var(--fg-quiet); font-variant-numeric: tabular-nums; }
  .column { position: relative; border-left: 1px solid var(--line); background: repeating-linear-gradient(to bottom, transparent 0, transparent 43px, var(--line) 43px, var(--line) 44px); }
  .column.today { background-color: var(--today-column); animation: today-pulse 3s ease-in-out infinite; }
  .event { position: absolute; display: flex; flex-direction: column; align-items: flex-start; gap: 1px; padding: 3px 6px; border: 0; border-left: 3px solid var(--colour); border-radius: 5px; background: color-mix(in srgb, var(--colour) 30%, var(--event-base)); color: var(--fg); font: inherit; font-size: 12px; text-align: left; overflow: hidden; cursor: pointer; box-shadow: 0 1px 3px var(--shadow-soft); }
  .event:hover { background: color-mix(in srgb, var(--colour) 42%, var(--event-base)); z-index: 2; }
  .event.past { opacity: 0.62; }
  .event.short { flex-direction: row; align-items: center; gap: 6px; padding-top: 0; padding-bottom: 0; }
  .event .title { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 100%; }
  .event .time { font-size: 11px; color: var(--fg-quiet); white-space: nowrap; font-variant-numeric: tabular-nums; }
  /* Where a carried event would land in the hours */
  .column.selected-day, .day-list.selected-day { background-color: color-mix(in srgb, var(--fg) 6%, transparent); box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--fg) 20%, transparent); }
  .dayname.selected-day .date { text-decoration: underline 2px color-mix(in srgb, var(--fg) 40%, transparent); text-underline-offset: 4px; }
  .slider { flex: 1; min-height: 0; display: flex; flex-direction: column; }
  .week :global(.leaving) { position: absolute; inset: 0 6px 6px; pointer-events: none; } /* the old page, sliding out */
  .allday-cell.picked-day { background: var(--accent-soft); border-radius: 5px; box-shadow: inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent); }
  /* Hours dragged for a new event */
  .time-pick { position: absolute; left: 2px; right: 2px; z-index: 2; padding: 2px 6px; border-radius: 5px; background: var(--accent-soft); box-shadow: inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 75%, transparent); color: var(--accent); font-size: 11px; font-weight: 600; pointer-events: none; overflow: hidden; white-space: nowrap; box-sizing: border-box; }
  .drop-preview { position: absolute; left: 2px; right: 2px; border: 1.5px dashed var(--accent); border-radius: 5px; background: color-mix(in srgb, var(--accent) 14%, transparent); pointer-events: none; transition: top 0.12s ease-out; }
  .now { position: absolute; left: -4px; right: 0; height: 2px; background: var(--now); z-index: 3; pointer-events: none; }
  .now::before { content: ""; position: absolute; left: -1px; top: -4px; width: 10px; height: 10px; border-radius: 50%; background: var(--now); }
</style>
