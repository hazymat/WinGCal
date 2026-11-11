<script lang="ts">
  import { untrack } from 'svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, addMonths, sameDay, startOfDay, startOfWeek, prefs, monthName } from '../lib/dates'
  import Icon from './Icon.svelte'
  let { events, follow, ontoday }: { events: Placed[]; follow?: Date; ontoday?: () => void } = $props()
  let shownMonth = $state(new Date(ui.focus.getFullYear(), ui.focus.getMonth(), 1))
  // Follows the day list's scrolling (agenda), turning the page when the list reaches another month
  $effect(() => { const day = follow; if (day) untrack(() => { if (day.getMonth() !== shownMonth.getMonth() || day.getFullYear() !== shownMonth.getFullYear()) shownMonth = new Date(day.getFullYear(), day.getMonth(), 1) }) })
  const days = $derived(Array.from({ length: 42 }, (unused, index) => addDays(startOfWeek(shownMonth, prefs.weekStart), index)))
  const today = $derived(ui.today)
  // Up to three dots under a day, one per calendar with something on
  const dots = (day: Date) => Array.from(new Set(eventsOn(events, day).map(event => event.colourShown))).slice(0, 3)
</script>

<div class="mini-month">
  <div class="mini-head"><span>{monthName(shownMonth)} {shownMonth.getFullYear()}</span>{#if !sameDay(ui.focus, today) || (follow && !sameDay(follow, today)) || shownMonth.getMonth() !== today.getMonth() || shownMonth.getFullYear() !== today.getFullYear()}<button class="back-today" onclick={() => { ui.focus = today; shownMonth = new Date(today.getFullYear(), today.getMonth(), 1); ontoday?.() }} title="Back to today (T)">Today</button>{/if}<button onclick={() => shownMonth = addMonths(shownMonth, -1)} aria-label="Previous month"><Icon name="previous" size={14} /></button><button onclick={() => shownMonth = addMonths(shownMonth, 1)} aria-label="Next month"><Icon name="next" size={14} /></button></div>
  <div class="cells">
    {#each days.slice(0, 7) as day}<span class="weekday">{day.toLocaleDateString(undefined, { weekday: 'narrow' })}</span>{/each}
    {#each days as day (day.getTime())}
      <button class="cell" class:other={day.getMonth() !== shownMonth.getMonth()} class:today={sameDay(day, today)} class:focus={sameDay(day, follow ?? ui.focus)} onclick={() => ui.focus = day}>
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
  /* Back to today: only while away from it, quiet until hovered */
  .mini-head .back-today { width: auto; height: 20px; padding: 0 8px; margin-right: 4px; border-radius: 10px; box-shadow: inset 0 0 0 1px var(--line); font: inherit; font-size: 11px; font-weight: 600; }
  .cells { display: grid; grid-template-columns: repeat(7, 1fr); row-gap: 1px; }
  .weekday { text-align: center; font-size: 10.5px; font-weight: 600; color: var(--fg-quiet); padding-bottom: 2px; }
  .cell { position: relative; height: 28px; border: 0; border-radius: 8px; background: none; color: var(--fg); font: inherit; font-size: 12px; font-variant-numeric: tabular-nums; cursor: pointer; padding: 0 0 6px; }
  .cell:hover { background: var(--hover); }
  .cell.other { color: var(--fg-quiet); opacity: 0.6; }
  .cell.focus { background: var(--accent-soft); transition: background 0.2s; } /* in Agenda it moves with the day on the reading line */
  .cell.today { color: var(--accent); font-weight: 700; }
  .dots { position: absolute; left: 0; right: 0; bottom: 4px; display: flex; justify-content: center; gap: 2px; }
  .dots i { width: 4px; height: 4px; border-radius: 50%; }
</style>
