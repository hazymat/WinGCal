<script lang="ts">
  import HandCircle from '../parts/HandCircle.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { addDays, sameDay, startOfDay, formatTime, weekdayShort, shortDate, monthName, dayKey } from '../lib/dates'
  import MiniMonth from '../parts/MiniMonth.svelte'
  import Icon from '../parts/Icon.svelte'
  import { warpScroll, isWarping } from '../lib/warp'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  let height = $state(600)
  const today = $derived(ui.today)
  // The focus day sits a quarter of the way down, with a glimpse of the last few weeks above it; scrolling near the end adds eight weeks more, without end. Empty days are skipped, except the focus day and today
  let lookBack = $state(30), span = $state(56), list: HTMLDivElement | undefined = $state(), topDay = $state(ui.focus)
  const readingLine = () => (list?.clientHeight ?? 0) * 0.25
  $effect(() => { const focus = ui.focus; topDay = focus; span = 56; lookBack = 30; requestAnimationFrame(() => scrollToDay(focus, false)) })
  function scrollToDay(day: Date, smooth: boolean) { const section = list?.querySelector<HTMLElement>(`[data-day="${dayKey(day)}"]`); if (!list) return; if (!section) { ui.focus = day; return } if (smooth) warpScroll(list, section.offsetTop - readingLine()); else list.scrollTop = section.offsetTop - readingLine() }
  let growingBack = false, todayInView = $state(true)
  // The month above follows the day on the reading line
  function onListScroll() {
    if (!list) return
    if (list.scrollTop + list.clientHeight > list.scrollHeight - 600) span += 56
    // Scrolling up reaches further back, eight weeks at a time, without the list jumping
    if (list.scrollTop < 300 && lookBack < 3650 && !growingBack && !isWarping(list)) { growingBack = true; const before = list.scrollHeight; lookBack += 56; requestAnimationFrame(() => { if (list) list.scrollTop += list.scrollHeight - before; growingBack = false }) }
    const todaySection = list.querySelector<HTMLElement>('.day.today')
    todayInView = !!todaySection && todaySection.offsetTop + todaySection.offsetHeight > list.scrollTop + 20 && todaySection.offsetTop < list.scrollTop + list.clientHeight - 20
    const line = list.scrollTop + readingLine(), section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.offsetTop + candidate.offsetHeight > line)
    if (section) { const [year, month, date] = section.dataset.day!.split('-').map(Number); if (!sameDay(topDay, new Date(year, month - 1, date))) topDay = new Date(year, month - 1, date) }
  }
  // The wheel over the month flips a month at a time; the list glides to that month's first day with something on
  let lastFlip = 0, flipMonth = new Date()
  function monthWheel(wheelEvent: WheelEvent) {
    wheelEvent.preventDefault()
    if (Math.abs(wheelEvent.deltaY) < 4 || performance.now() - lastFlip < 260 || !list) return
    lastFlip = performance.now()
    const from = isWarping(list) ? flipMonth : topDay, target = new Date(from.getFullYear(), from.getMonth() + (wheelEvent.deltaY > 0 ? 1 : -1), 1), offset = Math.round((target.getTime() - ui.focus.getTime()) / 86_400_000)
    flipMonth = target
    if (offset < -lookBack) lookBack = -offset + 28
    if (offset + 40 > span) span = offset + 70
    requestAnimationFrame(() => { if (!list) return; const key = dayKey(target), section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.dataset.day! >= key); if (section && section.dataset.day!.slice(0, 7) === key.slice(0, 7)) warpScroll(list, section.offsetTop - readingLine()); else ui.focus = target })
  }
  // Keep adding while the list doesn't yet reach past the bottom (quiet stretches), up to ten years
  $effect(() => { days.length; requestAnimationFrame(() => { if (list && span < 3650 && list.scrollHeight < list.clientHeight + 600) span += 56 }) })
  const days = $derived(Array.from({ length: span + lookBack }, (unused, index) => addDays(ui.focus, index - lookBack)).map(day => ({ day, list: eventsOn(events, day) })).filter(entry => entry.list.length > 0 || sameDay(entry.day, ui.focus) || sameDay(entry.day, today)))
  const allDayLabel = (event: Placed, day: Date) => { const total = Math.round((event.endAt.getTime() - event.startAt.getTime()) / 86_400_000); if (total <= 1) return 'All day'; const dayNumber = Math.round((startOfDay(day).getTime() - event.startAt.getTime()) / 86_400_000) + 1; return `Day ${dayNumber} of ${total}` }
</script>

<div class="agenda" bind:clientHeight={height}>
  {#if height > 520}<div class="mini" onwheel={monthWheel}><MiniMonth {events} follow={topDay} /></div>{/if}
  <div class="list-wrap">
  {#if !todayInView}<button class="jump-today" onclick={() => scrollToDay(today, true)} title="Back to today (T)"><Icon name={topDay > today ? 'up' : 'down'} size={12} />Today</button>{/if}
  <div class="list" bind:this={list} onscroll={onListScroll}>
    {#each days as { day, list }, index (day.getTime())}
      {#if index > 0 && days[index - 1].day.getMonth() !== day.getMonth()}<div class="month-break">{monthName(day)}</div>{/if}
      <section class="day" class:today={sameDay(day, today)} class:gone={day < today} data-day={dayKey(day)} data-newday={dayKey(day)}>
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
</div>

<style>
  .agenda { display: flex; flex-direction: column; height: 100%; min-height: 0; }
  .mini { padding: 0 14px 8px; border-bottom: 1px solid var(--line); }
  .list-wrap { position: relative; flex: 1; min-height: 0; display: flex; flex-direction: column; }
  /* Back to today: floats at the top of the list whenever it has scrolled away from today, pointing the way */
  .jump-today { position: absolute; z-index: 2; top: 8px; left: 50%; transform: translateX(-50%); display: flex; align-items: center; gap: 5px; height: 26px; padding: 0 12px 0 9px; border: 0; border-radius: 13px; background: var(--accent); color: var(--on-accent); font: inherit; font-size: 12px; font-weight: 700; cursor: pointer; box-shadow: 0 4px 14px rgba(0, 0, 0, 0.35); animation: jump-in 0.2s ease-out; }
  .jump-today:hover { filter: brightness(1.1); }
  @keyframes jump-in { from { opacity: 0; transform: translate(-50%, -6px); } }
  .list { position: relative; flex: 1; min-height: 0; overflow-y: auto; padding: 4px 10px 60vh; /* room below so even the last day can reach the reading line */ mask-image: linear-gradient(transparent, #000 10%, #000 88%, transparent); }
  /* Days drift in from below and fade as they pass the top, so today reads as the middle of a moving strip (Chromium scroll-driven animation; without it the list is plain) */
  .day, .month-break { animation: agenda-drift linear both; animation-timeline: view(); animation-range: cover 0% cover 100%; }
  .day.gone .items, .day.gone .when { opacity: 0.55; }
  .month-break { margin: 14px 6px 4px; font-size: 11px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--fg-quiet); }
  .day { display: grid; grid-template-columns: 46px 1fr; gap: 8px; padding: 6px 4px; border-radius: 10px; }
  .day.today { background: var(--today-column); border-radius: 8px; animation: agenda-drift linear both, today-pulse 3s ease-in-out infinite; animation-timeline: view(), auto; animation-range: cover 0% cover 100%, normal; }
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
