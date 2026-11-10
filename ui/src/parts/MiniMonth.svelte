<script lang="ts">
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, addMonths, sameDay, startOfDay, startOfWeek, prefs, monthName } from '../lib/dates'
  import Icon from './Icon.svelte'
  let { events }: { events: Placed[] } = $props()
  let shownMonth = $state(new Date(ui.focus.getFullYear(), ui.focus.getMonth(), 1))
  const days = $derived(Array.from({ length: 42 }, (unused, index) => addDays(startOfWeek(shownMonth, prefs.weekStart), index)))
  const today = startOfDay(new Date())
  // Up to three dots under a day, one per calendar with something on
  const dots = (day: Date) => Array.from(new Set(eventsOn(events, day).map(event => event.colourShown))).slice(0, 3)
</script>

<div class="mini-month">
  <div class="mini-head"><span>{monthName(shownMonth)} {shownMonth.getFullYear()}</span><button onclick={() => shownMonth = addMonths(shownMonth, -1)} aria-label="Previous month"><Icon name="previous" size={14} /></button><button onclick={() => shownMonth = addMonths(shownMonth, 1)} aria-label="Next month"><Icon name="next" size={14} /></button></div>
  <div class="cells">
    {#each days.slice(0, 7) as day}<span class="weekday">{day.toLocaleDateString(undefined, { weekday: 'narrow' })}</span>{/each}
    {#each days as day (day.getTime())}
      <button class="cell" class:other={day.getMonth() !== shownMonth.getMonth()} class:today={sameDay(day, today)} class:focus={sameDay(day, ui.focus)} onclick={() => ui.focus = day}>
        {day.getDate()}<span class="dots">{#each dots(day) as colour}<i style="background: {colour}"></i>{/each}</span>
      </button>
    {/each}
  </div>
</div>

<style>
  .mini-head { display: flex; align-items: center; gap: 2px; font-weight: 600; font-size: 13px; padding: 2px 2px 4px; }
  .mini-head span { flex: 1; }
  .mini-head button { display: grid; place-items: center; width: 26px; height: 24px; border: 0; border-radius: 6px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .mini-head button:hover { background: var(--hover); color: var(--fg); }
  .cells { display: grid; grid-template-columns: repeat(7, 1fr); row-gap: 1px; }
  .weekday { text-align: center; font-size: 10.5px; font-weight: 600; color: var(--fg-quiet); padding-bottom: 2px; }
  .cell { position: relative; height: 28px; border: 0; border-radius: 8px; background: none; color: var(--fg); font: inherit; font-size: 12px; font-variant-numeric: tabular-nums; cursor: pointer; padding: 0 0 6px; }
  .cell:hover { background: var(--hover); }
  .cell.other { color: var(--fg-quiet); opacity: 0.6; }
  .cell.focus { background: var(--accent-soft); }
  .cell.today { color: var(--accent); font-weight: 700; }
  .dots { position: absolute; left: 0; right: 0; bottom: 4px; display: flex; justify-content: center; gap: 2px; }
  .dots i { width: 4px; height: 4px; border-radius: 50%; }
</style>
