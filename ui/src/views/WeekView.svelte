<script lang="ts">
  import HandArrow from '../parts/HandArrow.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
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
  const allDayOn = (day: Date) => eventsOn(events, day).filter(event => event.allDay)

  // Opens at the working day (an hour before the earliest event, 07:00 at the latest)
  let scroller: HTMLDivElement | undefined = $state()
  $effect(() => {
    days; if (!scroller) return
    const earliest = days.flatMap(day => layout(day).map(item => item.top)).reduce((lowest, top) => Math.min(lowest, top), 7 * 60)
    scroller.scrollTop = Math.max(0, (earliest / 60 - 1) * hourHeight)
  })
</script>

<div class="week" bind:clientWidth={width} bind:clientHeight={height}>
  <div class="head" style="--days: {dayCount}">
    <div class="gutter"></div>
    {#each days as day}
      <div class="dayname" class:today={sameDay(day, today)}><span class="weekday">{weekdayShort(day)}</span><span class="date">{day.getDate()}</span></div>
    {/each}
  </div>
  {#if listMode}
  <div class="lists" style="--days: {dayCount}">
    <div class="gutter"></div>
    {#each days as day}
      <div class="day-list" class:today={sameDay(day, today)}>
        {#each eventsOn(events, day) as event (event.id)}
          {#if event.allDay}<button class="pill" style="--colour: {event.colourShown}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>{event.title}<HandArrow {event} {day} /></button>
          {:else}<button class="line" class:past={event.endAt < now} class:picked={ui.picked.includes(event.id)} data-event={event.id} style="--colour: {event.colourShown}" onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}><span class="dot"></span><span class="time">{formatTime(event.startAt)}</span><span class="title">{event.title}</span></button>{/if}
        {/each}
      </div>
    {/each}
  </div>
  {:else}
  <div class="allday" style="--days: {dayCount}">
    <div class="gutter"></div>
    {#each days as day}<div class="allday-cell">{#each allDayOn(day) as event (event.id)}<button class="pill" style="--colour: {event.colourShown}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>{event.title}<HandArrow {event} {day} /></button>{/each}</div>{/each}
  </div>
  <div class="scroller" bind:this={scroller}>
    <div class="body" style="--days: {dayCount}; height: {hourHeight * 24}px">
      <div class="hours">{#each Array.from({ length: 24 }, (unused, hour) => hour) as hour}<div class="hour" style="top: {hour * hourHeight}px">{hour ? formatHour(hour) : ''}</div>{/each}</div>
      {#each days as day}
        <div class="column" class:today={sameDay(day, today)}>
          {#each layout(day) as item (item.event.id)}
            {@const height = Math.max(18, (item.bottom - item.top) / 60 * hourHeight - 2)}
            <button class="event" class:short={height < 36} class:past={item.event.endAt < now} style="--colour: {item.event.colourShown}; top: {item.top / 60 * hourHeight + 1}px; height: {height}px; left: calc({item.column} * 100% / {item.columns} + 2px); width: calc(100% / {item.columns} - 4px)" class:picked={ui.picked.includes(item.event.id)} onclick={(clickEvent) => onpick(item.event, clickEvent.currentTarget)}>
              <span class="title">{item.event.title}</span><span class="time">{formatTime(item.event.startAt)}{height >= 36 ? ' – ' + formatTime(item.event.endAt) : ''}</span>
            </button>
          {/each}
          {#if sameDay(day, today)}<div class="now" style="top: {minutesIntoDay(now) / 60 * hourHeight}px"></div>{/if}
        </div>
      {/each}
    </div>
  </div>
  {/if}
</div>

<style>
  .week { display: flex; flex-direction: column; height: 100%; min-height: 0; padding: 0 6px 6px; }
  .lists { flex: 1; min-height: 0; display: grid; grid-template-columns: 52px repeat(var(--days), minmax(0, 1fr)); }
  .day-list { display: flex; flex-direction: column; gap: 2px; padding: 2px 3px; border-left: 1px solid var(--line); overflow-y: auto; min-width: 0; }
  .day-list.today { background: var(--today-column); border-radius: 8px; }
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
  .scroller { flex: 1; min-height: 0; overflow-y: auto; border-radius: 0 0 10px 10px; }
  .body { position: relative; }
  .hours { position: relative; }
  .hour { position: absolute; right: 8px; transform: translateY(-50%); font-size: 10.5px; color: var(--fg-quiet); font-variant-numeric: tabular-nums; }
  .column { position: relative; border-left: 1px solid var(--line); background: repeating-linear-gradient(to bottom, transparent 0, transparent 43px, var(--line) 43px, var(--line) 44px); }
  .column.today { background-color: var(--today-column); }
  .event { position: absolute; display: flex; flex-direction: column; align-items: flex-start; gap: 1px; padding: 3px 6px; border: 0; border-left: 3px solid var(--colour); border-radius: 5px; background: color-mix(in srgb, var(--colour) 30%, var(--event-base)); color: var(--fg); font: inherit; font-size: 12px; text-align: left; overflow: hidden; cursor: pointer; box-shadow: 0 1px 3px var(--shadow-soft); }
  .event:hover { background: color-mix(in srgb, var(--colour) 42%, var(--event-base)); z-index: 2; }
  .event.past { opacity: 0.62; }
  .event.short { flex-direction: row; align-items: center; gap: 6px; padding-top: 0; padding-bottom: 0; }
  .event .title { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 100%; }
  .event .time { font-size: 11px; color: var(--fg-quiet); white-space: nowrap; font-variant-numeric: tabular-nums; }
  .now { position: absolute; left: -4px; right: 0; height: 2px; background: var(--now); z-index: 3; pointer-events: none; }
  .now::before { content: ""; position: absolute; left: -1px; top: -4px; width: 10px; height: 10px; border-radius: 50%; background: var(--now); }
</style>
