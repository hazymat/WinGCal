<script lang="ts">
  import HandCircle from '../parts/HandCircle.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, sameDay, startOfDay, formatTime, weekdayShort, shortDate, monthName, dayKey } from '../lib/dates'
  import MiniMonth from '../parts/MiniMonth.svelte'
  import Icon from '../parts/Icon.svelte'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  let height = $state(600)
  const today = $derived(ui.today)
  // From the focus day onwards, eight weeks at a time: scrolling near the end adds more, without end. Empty days are skipped, except the focus day itself
  let span = $state(56), list: HTMLDivElement | undefined = $state(), topDay = $state(ui.focus)
  $effect(() => { topDay = ui.focus; span = 56; if (list) list.scrollTop = 0 })
  // The month above follows the day at the top of the list
  function onListScroll() {
    if (!list) return
    if (list.scrollTop + list.clientHeight > list.scrollHeight - 600) span += 56
    const top = list.scrollTop + 6, section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.offsetTop + candidate.offsetHeight > top)
    if (section) { const [year, month, date] = section.dataset.day!.split('-').map(Number); if (!sameDay(topDay, new Date(year, month - 1, date))) topDay = new Date(year, month - 1, date) }
  }
  // Keep adding while the list doesn't yet reach past the bottom (quiet stretches), up to ten years
  $effect(() => { days.length; requestAnimationFrame(() => { if (list && span < 3650 && list.scrollHeight < list.clientHeight + 600) span += 56 }) })
  const days = $derived(Array.from({ length: span }, (unused, index) => addDays(ui.focus, index)).map(day => ({ day, list: eventsOn(events, day) })).filter((entry, index) => index === 0 || entry.list.length > 0))
  const allDayLabel = (event: Placed, day: Date) => { const total = Math.round((event.endAt.getTime() - event.startAt.getTime()) / 86_400_000); if (total <= 1) return 'All day'; const dayNumber = Math.round((startOfDay(day).getTime() - event.startAt.getTime()) / 86_400_000) + 1; return `Day ${dayNumber} of ${total}` }
</script>

<div class="agenda" bind:clientHeight={height}>
  {#if height > 520}<div class="mini"><MiniMonth {events} follow={topDay} /></div>{/if}
  <div class="list" bind:this={list} onscroll={onListScroll}>
    {#each days as { day, list }, index (day.getTime())}
      {#if index === 0 || day.getDate() === 1 || days[index - 1].day.getMonth() !== day.getMonth()}{#if index > 0}<div class="month-break">{monthName(day)}</div>{/if}{/if}
      <section class="day" class:today={sameDay(day, today)} data-day={dayKey(day)} data-newday={dayKey(day)}>
        <div class="when"><span class="weekday">{weekdayShort(day)}</span><span class="date">{day.getDate()}{#if sameDay(day, today)}<HandCircle seed={dayKey(day)} />{/if}</span></div>
        <div class="items">
          {#if list.length === 0}<div class="empty">{sameDay(day, today) ? 'Nothing on today' : 'Nothing on ' + shortDate(day)}</div>{/if}
          {#each list as event (event.id)}
            <button class="item" class:past={event.endAt < new Date()} style="--colour: {event.colourShown}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>
              <span class="bar"></span>
              <span class="time">{event.allDay ? allDayLabel(event, day) : formatTime(event.startAt) + ' – ' + formatTime(event.endAt)}</span>
              <span class="title">{event.title}</span>
              {#if event.location}<span class="where"><Icon name="location" size={12} />{event.location}</span>{/if}
              {#if event.meetUrl}<span class="where"><Icon name="video" size={12} />Video call</span>{/if}
            </button>
          {/each}
        </div>
      </section>
    {/each}
  </div>
</div>

<style>
  .agenda { display: flex; flex-direction: column; height: 100%; min-height: 0; }
  .mini { padding: 0 14px 8px; border-bottom: 1px solid var(--line); }
  .list { flex: 1; min-height: 0; overflow-y: auto; padding: 4px 10px 12px; }
  .month-break { margin: 14px 6px 4px; font-size: 11px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--fg-quiet); }
  .day { display: grid; grid-template-columns: 46px 1fr; gap: 8px; padding: 6px 4px; border-radius: 10px; }
  .day.today { background: var(--today-column); animation: today-pulse 3s ease-in-out infinite; border-radius: 8px; }
  .when { display: flex; flex-direction: column; align-items: center; padding-top: 2px; color: var(--fg-quiet); }
  .weekday { font-size: 10.5px; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; }
  .date { font-size: 20px; font-weight: 600; color: var(--fg); font-variant-numeric: tabular-nums; line-height: 1.15; }
  .today .date { color: var(--accent); }
  .items { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
  .empty { padding: 7px 4px; color: var(--fg-quiet); font-size: 12.5px; }
  .item { display: grid; grid-template-columns: 4px auto 1fr; grid-template-rows: auto auto; column-gap: 10px; align-items: center; padding: 6px 8px; border: 0; border-radius: 8px; background: var(--panel); color: var(--fg); font: inherit; text-align: left; cursor: pointer; min-width: 0; }
  .item:hover { background: var(--panel-strong); }
  .item.past { opacity: 0.6; }
  .bar { grid-row: 1 / span 2; align-self: stretch; border-radius: 2px; background: var(--colour); }
  .time { font-size: 12px; color: var(--fg-quiet); white-space: nowrap; font-variant-numeric: tabular-nums; }
  .title { font-weight: 600; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .where { grid-column: 2 / span 2; display: flex; align-items: center; gap: 4px; font-size: 11.5px; color: var(--fg-quiet); white-space: nowrap; overflow: hidden; }
</style>
