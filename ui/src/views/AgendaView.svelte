<script lang="ts">
  import HandCircle from '../parts/HandCircle.svelte'
  import { ui, eventsOn, type Placed } from '../state.svelte'
  import { prefs, addDays, sameDay, startOfDay, formatTime, weekdayShort, shortDate, monthName, dayKey } from '../lib/dates'
  import MiniMonth from '../parts/MiniMonth.svelte'
  import Icon from '../parts/Icon.svelte'
  import { warpScroll, isWarping } from '../lib/warp'
  import { carry } from '../lib/carry.svelte'
  import { glideWheel } from '../lib/glide'
  import { chaseOnView } from '../lib/chase'
  import { createLamp, paneTilt } from '../lib/lamp.svelte'
  import { slide } from 'svelte/transition'
  import { tick, untrack } from 'svelte'
  let { events, onpick }: { events: Placed[]; onpick: (event: Placed, target: HTMLElement) => void } = $props()

  let height = $state(600)
  const today = $derived(ui.today)
  // The focus day sits a quarter of the way down, with a glimpse of the last few weeks above it; scrolling near the end adds eight weeks more, without end. Empty days are skipped, except the focus day and today
  let lookBack = $state(30), span = $state(56), list: HTMLDivElement | undefined = $state(), topDay = $state(ui.focus)
  const readingLine = () => (list?.clientHeight ?? 0) * 0.25
  // While the list rebuilds around a newly picked day, its scrolling isn't read (it would flick the month above back and forth)
  let settling = false
  $effect(() => { const focus = ui.focus; topDay = focus; wheelDay = null; span = 56; lookBack = 30; settling = true; requestAnimationFrame(() => { scrollToDay(focus, false); requestAnimationFrame(() => { settling = false; onListScroll() }) }) })
  function scrollToDay(day: Date, smooth: boolean) { const section = list?.querySelector<HTMLElement>(`[data-day="${dayKey(day)}"]`); if (!list) return; if (!section) { ui.focus = day; return } if (smooth) warpScroll(list, section.offsetTop - readingLine()); else list.scrollTop = section.offsetTop - readingLine() }
  let growingBack = false, todayInView = $state(true)
  const { lamp, update: updateLamp } = createLamp() // tungsten over past days (lib/lamp)
  // The month above follows the day on the reading line
  function onListScroll() {
    if (!list) return
    if (list.scrollTop + list.clientHeight > list.scrollHeight - 600) span += 56
    // Scrolling up reaches further back, eight weeks at a time, without the list jumping
    if (list.scrollTop < 300 && lookBack < 3650 && !growingBack && !isWarping(list)) { growingBack = true; const before = list.scrollHeight; lookBack += 56; requestAnimationFrame(() => { if (list) list.scrollTop += list.scrollHeight - before; growingBack = false }) }
    const todaySection = list.querySelector<HTMLElement>('.day.today')
    todayInView = !!todaySection && todaySection.offsetTop + todaySection.offsetHeight > list.scrollTop + 20 && todaySection.offsetTop < list.scrollTop + list.clientHeight - 20
    const line = list.scrollTop + readingLine() + 6, section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.offsetTop + candidate.offsetHeight > line)
    updateLamp(list, section)
    if (section && !settling) { const [year, month, date] = section.dataset.day!.split('-').map(Number); if (!sameDay(topDay, new Date(year, month - 1, date))) topDay = new Date(year, month - 1, date) }
  }
  // The wheel over the month turns a month per notch; the list glides to the 1st of that month, or the next day with something on
  let wheelDay: Date | null = $state(null), wheelPending = 0
  function monthWheel(wheelEvent: WheelEvent) {
    wheelEvent.preventDefault()
    if (!list) return
    const delta = Math.abs(wheelEvent.deltaY) >= Math.abs(wheelEvent.deltaX) ? wheelEvent.deltaY : wheelEvent.deltaX
    // A mouse notch (its size varies with display scaling) turns one month at once; a touchpad's small deltas add up to one
    if (Math.abs(delta) >= 50) wheelPending = Math.sign(delta) * 100; else wheelPending += delta
    const steps = Math.trunc(wheelPending / 100); if (!steps) return
    wheelPending = 0
    const shownMonth = wheelDay ?? topDay, target = new Date(shownMonth.getFullYear(), shownMonth.getMonth() + Math.sign(steps), 1), offset = Math.round((target.getTime() - ui.focus.getTime()) / 86_400_000)
    wheelDay = target
    if (offset < -lookBack) lookBack = -offset + 28
    if (offset + 40 > span) span = offset + 70
    tick().then(() => requestAnimationFrame(() => { if (!list) return; const key = dayKey(target), section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.dataset.day! >= key); if (section) warpScroll(list, section.offsetTop - readingLine()) }))
  }
  // The strip's month heading follows the day being read, so the month isn't shown twice
  $effect(() => { ui.readingDay = wheelDay ?? topDay; return () => { ui.readingDay = null } })
  // A day clicked in the month above: the list warps there (to it, or the next day with something on), making room first if it's beyond what's drawn
  // A clicked day with nothing on opens up as an empty slot (sliding in) so the list has somewhere to land
  let openedDay = $state<Date | null>(null)
  function glideToDay(target: Date) {
    const offset = Math.round((target.getTime() - ui.focus.getTime()) / 86_400_000)
    wheelDay = target
    if (!openedDay || !sameDay(openedDay, target)) { sliding = true; clearTimeout(slideTimer); slideTimer = setTimeout(() => sliding = false, 700); openedDay = target }
    if (offset < -lookBack) lookBack = -offset + 28
    if (offset + 40 > span) span = offset + 70
    requestAnimationFrame(() => { if (!list) return; const key = dayKey(target), section = Array.from(list.querySelectorAll<HTMLElement>('[data-day]')).find(candidate => candidate.dataset.day! >= key); if (section) warpScroll(list, section.offsetTop - readingLine()) })
  }
  // Keep adding while the list doesn't yet reach past the bottom (quiet stretches), up to ten years
  $effect(() => { days.length; requestAnimationFrame(() => { if (list && span < 3650 && list.scrollHeight < list.clientHeight + 600) span += 56 }) })
  const days = $derived(Array.from({ length: span + lookBack }, (unused, index) => addDays(ui.focus, index - lookBack)).map(day => ({ day, list: eventsOn(events, day) })).filter(entry => carry.active || entry.list.length > 0 || sameDay(entry.day, ui.focus) || sameDay(entry.day, today) || (!!openedDay && sameDay(entry.day, openedDay))))
  // While events are carried, every day opens up (empty ones as dashed boxes to drop into), sliding the others aside; afterwards they close again
  let sliding = $state(false), slideTimer = 0
  $effect(() => { carry.active; sliding = true; clearTimeout(slideTimer); slideTimer = setTimeout(() => sliding = false, 700) })
  // As that room opens (or closes), the day under the pointer stays exactly where it is and the days above and below move apart around it, so a lift and a still release land back home
  let anchorFrame = 0, carryWas = false
  $effect.pre(() => { const opening = carry.active; untrack(() => {
    if (opening === carryWas || !list) return
    carryWas = opening
    const section = document.elementsFromPoint(carry.x, carry.y).map(node => node.closest<HTMLElement>('[data-day]')).find(found => found && list!.contains(found)) ?? list.querySelector<HTMLElement>(`[data-day="${carry.originDay}"]`)
    if (!section) return
    const key = section.dataset.day!, offset = section.offsetTop - list.scrollTop, until = performance.now() + 800
    cancelAnimationFrame(anchorFrame)
    const hold = () => { const now = list?.querySelector<HTMLElement>(`[data-day="${key}"]`); if (list && now) list.scrollTop = now.offsetTop - offset; if (performance.now() < until) anchorFrame = requestAnimationFrame(hold) }
    tick().then(hold) // straight after the new days are drawn, before they're painted
  }) })
  const allDayLabel = (event: Placed, day: Date) => { const total = Math.round((event.endAt.getTime() - event.startAt.getTime()) / 86_400_000); if (total <= 1) return 'All day'; const dayNumber = Math.round((startOfDay(day).getTime() - event.startAt.getTime()) / 86_400_000) + 1; return `Day ${dayNumber} of ${total}` }
</script>

<div class="agenda" bind:clientHeight={height}>
  {#if height > 520}<div class="mini" onwheel={monthWheel}><MiniMonth {events} follow={wheelDay ?? topDay} onday={glideToDay} range={ui.pickedDays} onrange={(from, to) => ui.pickedDays = { from, to }} /></div>{/if}
  <div class="list-wrap">
  {#if !todayInView}<button class="jump-today" onclick={() => { wheelDay = null; scrollToDay(today, true) }} title="Back to today (T)"><Icon name={topDay > today ? 'up' : 'down'} size={12} />Today</button>{/if}
  <div class="lamp" class:on={lamp.warmed} style="height: min(100%, {lamp.height}px)" aria-hidden="true"></div>
  <div class="list" class:warmed={lamp.warmed} class:lit={(ui.lookVersion, prefs.stripLight > 0)} style:--strip={(ui.lookVersion, prefs.stripLight / 50)} bind:this={list} onscroll={onListScroll} onwheel={(wheelEvent) => { wheelDay = null; if (list) glideWheel(list, wheelEvent) }}>
    {#each days as { day, list }, index (day.getTime())}
      <div class="entry" transition:slide={{ duration: sliding ? 340 : 0 }}>
      {#if index > 0 && days[index - 1].day.getMonth() !== day.getMonth()}<div class="month-break">{monthName(day)}</div>{/if}
      <section class="day" class:today={sameDay(day, today)} class:today-chase={sameDay(day, today)} use:chaseOnView={sameDay(day, today)} class:gone={day < today} data-day={dayKey(day)} data-newday={dayKey(day)}>
        <div class="when warm"><span class="weekday">{weekdayShort(day)}</span><span class="date">{day.getDate()}{#if sameDay(day, today)}<HandCircle seed={dayKey(day)} />{/if}</span></div>
        <div class="items warm">
          {#if list.length === 0 && carry.active}<div class="slot">{shortDate(day)}</div>
          {:else if list.length === 0}<div class="empty">{sameDay(day, today) ? 'Nothing on today' : 'Nothing on ' + shortDate(day)}</div>{/if}
          {#each list as event (event.id)}
            <button class="item" class:past={event.endAt < new Date()} style="--colour: {event.colourShown}; {paneTilt(event.id + dayKey(day))}" class:picked={ui.picked.includes(event.id)} data-event={event.id} onclick={(clickEvent) => onpick(event, clickEvent.currentTarget)}>
              <span class="bar"></span>
              <span class="time">{event.allDay ? allDayLabel(event, day) : formatTime(event.startAt) + ' – ' + formatTime(event.endAt)}</span>
              <span class="title">{event.title}</span>
              {#if event.location}<span class="where"><Icon name="location" size={12} />{event.location}</span>{/if}
              {#if event.meetUrl}<span class="where"><Icon name="video" size={12} />Video call</span>{/if}
            </button>
          {/each}
          {#if carry.active && list.length > 0 && carry.targetDay === dayKey(day) && carry.targetDay !== carry.originDay}<div class="slot" transition:slide={{ duration: 180 }}>Move here</div>{/if}
        </div>
      </section>
      </div>
    {/each}
  </div>
  </div>
</div>

<style>
  .agenda { display: flex; flex-direction: column; height: 100%; min-height: 0; }
  .mini { padding: 0 14px 8px; border-bottom: 1px solid var(--line); }
  .list-wrap { position: relative; flex: 1; min-height: 0; display: flex; flex-direction: column; }
  /* Back to today: a small pane of glass floating at the top of the list whenever it has scrolled away from today, pointing the way */
  .jump-today { position: absolute; z-index: 2; top: 8px; left: 50%; transform: translateX(-50%); display: flex; align-items: center; gap: 5px; height: 26px; padding: 0 12px 0 9px; border: 1px solid color-mix(in srgb, var(--accent) 40%, transparent); border-radius: 13px; background: color-mix(in srgb, var(--panel-solid) 55%, transparent); backdrop-filter: blur(14px) saturate(1.5); color: var(--accent); font: inherit; font-size: 12px; font-weight: 600; cursor: pointer; box-shadow: 0 4px 18px rgba(0, 0, 0, 0.28), inset 0 1px 0 rgba(255, 255, 255, 0.08); animation: jump-in 0.2s ease-out; }
  .jump-today:hover { background: color-mix(in srgb, var(--accent) 22%, color-mix(in srgb, var(--panel-solid) 55%, transparent)); }
  @keyframes jump-in { from { opacity: 0; transform: translate(-50%, -6px); } }
  .list { position: relative; flex: 1; min-height: 0; overflow-y: auto; padding: 4px 10px 60vh; /* room below so even the last day can reach the reading line */ mask-image: linear-gradient(transparent, #000 10%, #000 88%, transparent); }
  /* Days drift in from below and fade as they pass the top, so today reads as the middle of a moving strip (Chromium scroll-driven animation; without it the list is plain) */
  .day, .month-break { animation: agenda-drift linear both; animation-timeline: view(); animation-range: cover 0% cover 100%; }
  .day.gone .items, .day.gone .when { opacity: 0.55; }
  .month-break { margin: 14px 6px 4px; font-size: 11px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; color: var(--fg-quiet); }
  .day { display: grid; grid-template-columns: 46px 1fr; gap: 8px; padding: 6px 4px; border-radius: 10px; }
  .day.today { background: var(--today-column); border-radius: 8px; animation: agenda-drift linear both, today-pulse-bright 3s ease-in-out infinite; animation-timeline: view(), auto; animation-range: cover 0% cover 100%, normal; }
  .when { display: flex; flex-direction: column; align-items: center; padding-top: 2px; color: var(--fg-quiet); }
  .weekday { font-size: 10.5px; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; }
  .date { font-size: 20px; font-weight: 600; color: var(--fg); font-variant-numeric: tabular-nums; line-height: 1.15; }
  .today .date { color: var(--accent); }
  .items { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
  /* Room to drop into while carrying: an empty day, or the end of the day under the pointer */
  .slot { min-height: 30px; display: flex; align-items: center; padding: 0 10px; border: 1.5px dashed color-mix(in srgb, var(--fg) 28%, transparent); border-radius: 8px; color: var(--fg-quiet); font-size: 12px; box-sizing: border-box; }
  :global([data-drop]) .slot { border-color: var(--accent); color: var(--accent); background: color-mix(in srgb, var(--accent) 10%, transparent); }
  .empty { padding: 7px 4px; color: var(--fg-quiet); font-size: 12.5px; }
  .item { display: grid; grid-template-columns: 4px auto 1fr; grid-template-rows: auto auto; column-gap: 10px; align-items: center; padding: 6px 8px; border: 0; border-radius: 8px; background: color-mix(in srgb, var(--colour) var(--event-tint, 0%), var(--panel)); /* Event tint, Settings › Appearance */ color: var(--fg); font: inherit; text-align: left; cursor: pointer; min-width: 0; }
  .item:hover { background: color-mix(in srgb, var(--colour) var(--event-tint, 0%), var(--panel-strong)); }
  .item.past { opacity: 0.6; }
  .bar { grid-row: 1 / span 2; align-self: stretch; border-radius: 2px; background: var(--colour); }
  .time { font-size: 12px; color: var(--fg-quiet); white-space: nowrap; font-variant-numeric: tabular-nums; }
  .title { font-weight: 600; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .where { grid-column: 2 / span 2; display: flex; align-items: center; gap: 4px; font-size: 11.5px; color: var(--fg-quiet); white-space: nowrap; overflow: hidden; }
</style>
