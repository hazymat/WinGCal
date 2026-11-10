<script lang="ts">
  import HandCircle from '../parts/HandCircle.svelte'
  import HandArrow from '../parts/HandArrow.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, sameDay, startOfDay, startOfWeek, formatTime, weekdayShort, prefs, dayMs, dayKey } from '../lib/dates'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  // Six weeks from the week holding the 1st, so every month fits and the grid never jumps in height
  const firstOfMonth = $derived(new Date(ui.focus.getFullYear(), ui.focus.getMonth(), 1))
  const gridStart = $derived(startOfWeek(firstOfMonth, prefs.weekStart))
  const days = $derived(Array.from({ length: 42 }, (unused, index) => addDays(gridStart, index)))
  const today = startOfDay(new Date())
  let gridHeight = $state(600)
  // How many event lines fit under a day's number (18 px each)
  const linesPerDay = $derived(Math.max(1, Math.floor((gridHeight / 6 - 26) / 18)))
  const isWeekend = (day: Date) => day.getDay() === 0 || day.getDay() === 6
  function dayEvents(day: Date) {
    const all = eventsOn(events, day)
    const fits = all.length > linesPerDay ? linesPerDay - 1 : all.length
    return { shown: all.slice(0, fits), more: all.length - fits }
  }
  const continuesBefore = (event: Placed, day: Date) => event.allDay && event.startAt.getTime() < startOfDay(day).getTime()
  const continuesAfter = (event: Placed, day: Date) => event.allDay && event.endAt.getTime() > startOfDay(day).getTime() + dayMs
</script>

<div class="month">
  <div class="weekdays">{#each days.slice(0, 7) as day}<div>{weekdayShort(day)}</div>{/each}</div>
  <div class="grid" bind:clientHeight={gridHeight}>
    {#each days as day (day.getTime())}
      {@const list = dayEvents(day)}
      <div class="day" class:other={day.getMonth() !== firstOfMonth.getMonth()} class:weekend={isWeekend(day)} class:sunday={day.getDay() === 0} class:today={sameDay(day, today)}>
        <button class="number" onclick={() => { ui.focus = day; ui.view = 'week' }} title="Open this week">{day.getDate() === 1 ? day.toLocaleDateString(undefined, { day: 'numeric', month: 'short' }) : day.getDate()}{#if sameDay(day, today)}<HandCircle seed={dayKey(day)} />{/if}</button>
        {#each list.shown as event (event.id)}
          {#if event.allDay}
            <button class="pill" class:picked={ui.picked.includes(event.id)} data-event={event.id} class:joined-left={continuesBefore(event, day)} class:joined-right={continuesAfter(event, day)} style="--colour: {event.colourShown}" onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>{continuesBefore(event, day) && day.getDay() !== prefs.weekStart ? '' : event.title}<HandArrow {event} {day} /></button>
          {:else}
            <button class="line" class:past={event.endAt < new Date()} style="--colour: {event.colourShown}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}><span class="dot"></span><span class="time">{formatTime(event.startAt)}</span><span class="title">{event.title}</span></button>
          {/if}
        {/each}
        {#if list.more > 0}<button class="more" onclick={() => { ui.focus = day; ui.view = 'agenda' }}>+{list.more} more</button>{/if}
      </div>
    {/each}
  </div>
</div>

<style>
  .month { display: flex; flex-direction: column; height: 100%; min-height: 0; }
  .weekdays { display: grid; grid-template-columns: repeat(7, 1fr); padding: 0 6px; }
  .weekdays div { padding: 2px 8px 6px; font-size: 11px; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--fg-quiet); }
  .grid { flex: 1; min-height: 0; display: grid; grid-template-columns: repeat(7, minmax(0, 1fr)); grid-template-rows: repeat(6, minmax(0, 1fr)); gap: 1px; margin: 0 6px 6px; border-radius: 10px; overflow: hidden; background: var(--line); }
  .day { container: day / inline-size; position: relative; display: flex; flex-direction: column; gap: 1px; min-width: 0; min-height: 0; padding: 3px 4px; background: var(--cell); overflow: hidden; }
  .day.weekend { background: var(--cell-weekend); }
  .day.other { background: var(--cell-other); }
  .day.other > :not(.number) { opacity: 0.6; }
  .number { align-self: flex-start; min-width: 22px; height: 22px; padding: 0 6px; border: 0; border-radius: 11px; background: none; color: var(--fg); font: inherit; font-size: 12.5px; font-weight: 600; cursor: pointer; font-variant-numeric: tabular-nums; }
  .other .number { color: var(--fg-quiet); font-weight: 500; }
  .number:hover { background: var(--hover); }
  .today .number { background: var(--accent); color: var(--on-accent); }
  .today { box-shadow: inset 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent); }
  .line, .pill, .more { display: flex; align-items: center; gap: 5px; height: 17px; flex: none; min-width: 0; padding: 0 4px; border: 0; border-radius: 4px; background: none; color: var(--fg); font: inherit; font-size: 12px; text-align: left; cursor: pointer; white-space: nowrap; }
  .line:hover, .more:hover { background: var(--hover); }
  .line.past { color: var(--fg-quiet); }
  .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--colour); flex: none; }
  .time { color: var(--fg-quiet); font-variant-numeric: tabular-nums; flex: none; font-size: 11px; }
  .title { overflow: hidden; text-overflow: ellipsis; }
  .pill { background: color-mix(in srgb, var(--colour) 78%, transparent); color: #fff; font-weight: 600; text-shadow: 0 1px 1px rgba(0, 0, 0, 0.25); overflow: hidden; text-overflow: ellipsis; display: block; line-height: 17px; }
  .pill:hover { filter: brightness(1.1); }
  .pill.joined-left { border-top-left-radius: 0; border-bottom-left-radius: 0; margin-left: -4px; }
  .pill.joined-right { border-top-right-radius: 0; border-bottom-right-radius: 0; margin-right: -5px; }
  .more { color: var(--fg-quiet); font-size: 11.5px; font-weight: 600; }
</style>
