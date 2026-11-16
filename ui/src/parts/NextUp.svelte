<script lang="ts">
  import { app, ui, placedEvents, type Placed } from '../state.svelte'
  import { sendToShell } from '../bridge'
  import { prefs, formatTime, sameDay, weekdayShort } from '../lib/dates'
  import { shortTitle } from '../lib/titles'
  import Icon from './Icon.svelte'
  // Next event (SPEC.md › Next event): a countdown to the next timed event from the calendars ticked under Notify (Settings › Calendars), shown from the chosen lead time until ten minutes after it starts, or until dismissed
  // floating: laid over the top of a list (Agenda, Month and days); otherwise a slim row above the view (Month, Week)
  let { floating = false }: { floating?: boolean } = $props()
  const lingerMs = 10 * 60_000
  let now = $state(Date.now()), dismissed = $state<string[]>([])
  const next = $derived.by((): Placed | null => {
    const lead = (ui.lookVersion, prefs.nextLead) * 60_000, notified = new Set(app.calendars.filter(calendar => calendar.notify).map(calendar => calendar.key))
    if (notified.size === 0) return null
    return placedEvents().find(event => !event.allDay && notified.has(event.calendarKey) && !dismissed.includes(event.id + event.start) && event.startAt.getTime() - now <= lead && now < Math.min(event.endAt.getTime(), event.startAt.getTime() + lingerMs)) ?? null
  })
  // Ticks every second in the last two minutes, otherwise every quarter minute
  $effect(() => { const fast = !!next && Math.abs(next.startAt.getTime() - now) < 120_000, timer = setInterval(() => now = Date.now(), fast ? 1000 : 15_000); return () => clearInterval(timer) })
  const countdown = $derived.by(() => {
    if (!next) return ''
    const seconds = Math.round((next.startAt.getTime() - now) / 1000)
    if (seconds <= 0) return seconds > -60 ? 'Now' : `Started ${Math.floor(-seconds / 60)} min ago`
    if (seconds < 120) return `in ${seconds < 60 ? seconds + ' s' : '1 min ' + (seconds - 60) + ' s'}`
    const minutes = Math.ceil(seconds / 60)
    return minutes < 60 ? `in ${minutes} min` : `in ${Math.floor(minutes / 60)} h${minutes % 60 ? ' ' + (minutes % 60) + ' min' : ''}`
  })
  const colour = $derived(!next ? '' : (ui.lookVersion, prefs.nextColour) === 'accent' ? 'var(--accent)' : prefs.nextColour.startsWith('#') ? prefs.nextColour : next.colourShown)
  const when = $derived(!next ? '' : (sameDay(next.startAt, new Date(now)) ? '' : weekdayShort(next.startAt) + ' ') + formatTime(next.startAt))
</script>

{#if next}
  <div class="next-up" class:floating class:soon={next.startAt.getTime() - now < 300_000} style="--colour: {colour}; --strength: {(ui.lookVersion, prefs.nextStrength)}" role="status">
    <button class="open" onclick={() => sendToShell({ type: 'openEditor', id: next.id })} title="Open this event">
      <span class="countdown">{countdown}</span>
      <span class="title">{shortTitle(next)}</span>
      <span class="when">{when}{#if next.meetUrl}<Icon name="video" size={12} />{/if}</span>
    </button>
    <button class="dismiss" onclick={() => dismissed = dismissed.concat(next.id + next.start)} title="Dismiss" aria-label="Dismiss"><Icon name="close" size={14} /></button>
  </div>
{/if}

<style>
  /* Boldness (--strength, 0 to 100): from faint tinted glass to solid colour with white text */
  .next-up { --mix: calc(14% + var(--strength) * 0.86%); display: flex; align-items: stretch; flex: none; margin: 0 10px 6px; border-radius: 10px; background: color-mix(in srgb, var(--colour) var(--mix), var(--panel-solid)); box-shadow: inset 0 0 0 1.5px color-mix(in srgb, var(--colour) calc(55% + var(--strength) * 0.45%), transparent), 0 6px 20px rgba(0, 0, 0, 0.3); color: color-mix(in srgb, #fff calc(var(--strength) * 1.4% - 40%), var(--fg)); font-size: 13px; overflow: hidden; animation: next-in 0.25s ease-out; }
  .next-up.floating { position: absolute; z-index: 3; top: 8px; left: 10px; right: 10px; margin: 0; backdrop-filter: blur(14px) saturate(1.4); background: color-mix(in srgb, var(--colour) var(--mix), color-mix(in srgb, var(--panel-solid) 78%, transparent)); }
  .next-up.soon { animation: next-in 0.25s ease-out, next-soon 2.4s ease-in-out infinite; }
  @keyframes next-in { from { opacity: 0; transform: translateY(-6px); } }
  @keyframes next-soon { 50% { box-shadow: inset 0 0 0 1.5px var(--colour), 0 0 16px color-mix(in srgb, var(--colour) 70%, transparent); } }
  button { border: 0; background: none; color: inherit; font: inherit; cursor: pointer; }
  .open { flex: 1; min-width: 0; display: flex; align-items: baseline; gap: 10px; padding: 8px 4px 8px 12px; text-align: left; }
  .open:hover, .dismiss:hover { background: color-mix(in srgb, var(--fg) 8%, transparent); }
  .countdown { flex: none; font-weight: 750; font-variant-numeric: tabular-nums; }
  .title { min-width: 0; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .when { flex: none; margin-left: auto; display: flex; align-items: center; gap: 4px; font-size: 12px; opacity: 0.75; font-variant-numeric: tabular-nums; white-space: nowrap; }
  .next-up { container-type: inline-size; }
  @container (max-width: 300px) { .when { display: none; } } /* narrow lists: the countdown and the name only */
  .dismiss { flex: none; display: grid; place-items: center; width: 32px; opacity: 0.7; }
</style>
