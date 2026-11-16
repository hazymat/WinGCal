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
  // The colour is its own (a warm amber unless one is chosen), so it never reads as one of the events
  const colour = $derived(!next ? '' : (ui.lookVersion, prefs.nextColour) === 'accent' ? 'var(--accent)' : prefs.nextColour === 'calendar' ? next.colourShown : prefs.nextColour.startsWith('#') ? prefs.nextColour : '#ffb224')
  const secondsLeft = $derived(next ? Math.round((next.startAt.getTime() - now) / 1000) : 0)
  // The dial: what's left of the lead time as a ring, and the time to go in its biggest unit
  const share = $derived(Math.max(0, Math.min(1, secondsLeft / (prefs.nextLead * 60))))
  const dial = $derived(secondsLeft <= 0 ? { number: 'GO', unit: 'now' } : secondsLeft < 100 ? { number: String(secondsLeft), unit: 'sec' } : secondsLeft < 5970 ? { number: String(Math.ceil(secondsLeft / 60)), unit: 'min' } : { number: (secondsLeft / 3600).toFixed(secondsLeft < 36000 ? 1 : 0).replace('.0', ''), unit: 'hrs' })
  const when = $derived(!next ? '' : (sameDay(next.startAt, new Date(now)) ? '' : weekdayShort(next.startAt) + ' ') + formatTime(next.startAt))
</script>

{#if next}
  <div class="next-up" class:floating class:soon={secondsLeft < 300} style="--colour: {colour}; --strength: {(ui.lookVersion, prefs.nextStrength)}" role="status">
    <button class="open" onclick={() => sendToShell({ type: 'openEditor', id: next.id })} title="Open this event">
      <span class="dial">
        <svg viewBox="0 0 40 40" aria-hidden="true"><circle class="track" cx="20" cy="20" r="17" /><circle class="left" cx="20" cy="20" r="17" pathLength="100" stroke-dasharray="{share * 100} 100" /></svg>
        <span class="number">{dial.number}</span><span class="unit">{dial.unit}</span>
      </span>
      <span class="words"><span class="title">{shortTitle(next)}</span><span class="when">{secondsLeft > 0 ? 'starts ' + when : countdown}</span></span>
    </button>
    {#if next.meetUrl}<button class="join" onclick={() => sendToShell({ type: 'openUrl', url: next.meetUrl })} title="Join the video call" aria-label="Join the video call"><Icon name="video" size={15} /></button>{/if}
    <button class="dismiss" onclick={() => dismissed = dismissed.concat(next.id + next.start)} title="Dismiss" aria-label="Dismiss"><Icon name="close" size={13} /></button>
  </div>
{/if}

<style>
  /* A countdown capsule, nothing like an event row: a solid bright pill, as wide as its words, with a dial that runs down as the event nears. Boldness (--strength) takes it from a tinted pill to full colour */
  .next-up { --mix: calc(45% + var(--strength) * 0.55%); --ink: #1a1206; display: flex; align-items: stretch; flex: none; align-self: center; width: fit-content; max-width: calc(100% - 20px); margin: 0 auto 6px; border-radius: 999px; background: linear-gradient(135deg, color-mix(in srgb, var(--colour) var(--mix), var(--panel-solid)), color-mix(in srgb, color-mix(in srgb, var(--colour) 78%, #ff3d6e) var(--mix), var(--panel-solid))); box-shadow: 0 6px 22px color-mix(in srgb, var(--colour) 45%, transparent), inset 0 1px 0 rgba(255, 255, 255, 0.45); color: var(--ink); overflow: hidden; animation: next-in 0.35s cubic-bezier(0.2, 1.4, 0.4, 1); }
  .next-up.floating { position: absolute; z-index: 3; top: 8px; left: 50%; translate: -50% 0; margin: 0; }
  .next-up.soon { animation: next-in 0.35s cubic-bezier(0.2, 1.4, 0.4, 1), next-soon 1.6s ease-in-out infinite; }
  @keyframes next-in { from { opacity: 0; scale: 0.8; } }
  @keyframes next-soon { 50% { box-shadow: 0 6px 30px 4px color-mix(in srgb, var(--colour) 75%, transparent), inset 0 1px 0 rgba(255, 255, 255, 0.45); scale: 1.03; } }
  button { border: 0; background: none; color: inherit; font: inherit; cursor: pointer; }
  .open { min-width: 0; display: flex; align-items: center; gap: 10px; padding: 5px 4px 5px 6px; text-align: left; }
  .dial { position: relative; flex: none; width: 44px; height: 44px; display: flex; flex-direction: column; align-items: center; justify-content: center; border-radius: 50%; background: var(--ink); color: #fff; line-height: 1; }
  .dial svg { position: absolute; inset: 0; width: 100%; height: 100%; rotate: -90deg; }
  .dial circle { fill: none; stroke-width: 3; }
  .track { stroke: rgba(255, 255, 255, 0.16); }
  .left { stroke: var(--colour); stroke-linecap: round; transition: stroke-dasharray 1s linear; }
  .number { font-size: 16px; font-weight: 800; font-variant-numeric: tabular-nums; letter-spacing: -0.02em; }
  .unit { margin-top: 1px; font-size: 8px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; opacity: 0.75; }
  .words { min-width: 0; display: flex; flex-direction: column; gap: 1px; }
  .title { font-size: 13.5px; font-weight: 750; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .when { display: flex; align-items: center; gap: 4px; font-size: 11px; font-weight: 600; opacity: 0.72; white-space: nowrap; }
  .join { flex: none; align-self: center; display: grid; place-items: center; width: 30px; height: 30px; margin: 0 2px; border-radius: 50%; background: var(--ink); color: #fff; }
  .join:hover { scale: 1.1; }
  .dismiss { flex: none; display: grid; place-items: center; width: 34px; padding-right: 6px; opacity: 0.6; }
  .dismiss:hover { opacity: 1; }
</style>
