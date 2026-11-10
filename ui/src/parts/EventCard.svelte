<script lang="ts">
  import { ui, calendarByKey } from '../state.svelte'
  import { sendToShell } from '../bridge'
  import { formatTime, longDate, sameDay, addDays } from '../lib/dates'
  import Icon from './Icon.svelte'
  const selected = $derived(ui.selectedEvent!)
  const event = $derived(selected.event)
  const calendar = $derived(calendarByKey(event.calendarKey))
  // All-day ends are exclusive: a one-day event ends "tomorrow"
  const when = $derived(event.allDay
    ? (sameDay(addDays(event.endAt, -1), event.startAt) ? longDate(event.startAt) : `${longDate(event.startAt)} – ${longDate(addDays(event.endAt, -1))}`)
    : `${longDate(event.startAt)} · ${formatTime(event.startAt)} – ${formatTime(event.endAt)}`)
  // Descriptions are HTML from Google: shown as text with line breaks kept, never as live markup
  const descriptionText = $derived(event.description ? new DOMParser().parseFromString(event.description.replace(/<(br|\/p|\/li|\/div)\s*\/?>/gi, '\n$&'), 'text/html').body.textContent?.trim() ?? '' : '')
  let card: HTMLDivElement | undefined = $state()
  let position = $state({ left: 0, top: 0 })
  // Beside the clicked event, kept inside the window
  $effect(() => {
    if (!card) return
    const width = card.offsetWidth, height = card.offsetHeight
    position = { left: Math.max(8, Math.min(selected.left, innerWidth - width - 8)), top: Math.max(44, Math.min(selected.top, innerHeight - height - 8)) }
  })
</script>

<div class="scrim" role="presentation" onpointerdown={() => ui.selectedEvent = null}></div>
<div class="card" bind:this={card} style="left: {position.left}px; top: {position.top}px; --colour: {event.colourShown}" role="dialog" aria-label={event.title}>
  <div class="actions">
    {#if !calendar?.readOnly}
      <button onclick={() => sendToShell({ type: 'editEvent', id: event.id })} title="Edit"><Icon name="edit" size={16} /></button>
      <button onclick={() => sendToShell({ type: 'deleteEvent', id: event.id })} title="Delete"><Icon name="trash" size={16} /></button>
    {/if}
    <button onclick={() => ui.selectedEvent = null} title="Close"><Icon name="close" size={16} /></button>
  </div>
  <div class="title-row"><span class="swatch"></span><h3>{event.title}</h3></div>
  <div class="row"><Icon name="clock" size={15} /><span>{when}</span></div>
  {#if event.recurring}<div class="row quiet"><Icon name="repeat" size={15} /><span>Repeats</span></div>{/if}
  {#if event.location}<div class="row"><Icon name="location" size={15} /><a href={'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(event.location)} target="_blank" rel="noreferrer">{event.location}</a></div>{/if}
  {#if event.meetUrl}<div class="row"><Icon name="video" size={15} /><a class="join" href={event.meetUrl} target="_blank" rel="noreferrer">Join video call</a></div>{/if}
  {#if descriptionText}<p class="description">{descriptionText}</p>{/if}
  <div class="row quiet"><Icon name="calendar" size={15} /><span>{calendar?.name}{calendar ? ' · ' + calendar.account : ''}</span></div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; }
  .card { position: fixed; z-index: 51; width: min(340px, calc(100vw - 16px)); max-height: calc(100vh - 60px); overflow-y: auto; padding: 12px 14px 14px; border-radius: 14px; background: var(--popover); backdrop-filter: blur(28px) saturate(1.5); border: 1px solid var(--line); box-shadow: 0 18px 40px var(--shadow); }
  .actions { display: flex; justify-content: flex-end; gap: 2px; margin: -4px -6px 2px 0; }
  .actions button { display: grid; place-items: center; width: 30px; height: 28px; border: 0; border-radius: 6px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .actions button:hover { background: var(--hover); color: var(--fg); }
  .title-row { display: flex; align-items: flex-start; gap: 10px; margin-bottom: 8px; }
  .swatch { width: 14px; height: 14px; margin-top: 4px; border-radius: 4px; background: var(--colour); flex: none; }
  h3 { margin: 0; font-size: 17px; font-weight: 600; line-height: 1.3; color: var(--fg); text-transform: none; letter-spacing: 0; }
  .row { display: flex; align-items: center; gap: 10px; padding: 3px 0; font-size: 13px; color: var(--fg); }
  .row :global(svg) { color: var(--fg-quiet); }
  .quiet { color: var(--fg-quiet); }
  a { color: var(--accent); text-decoration: none; }
  a:hover { text-decoration: underline; }
  .join { font-weight: 600; }
  .description { margin: 8px 0 6px 25px; font-size: 12.5px; line-height: 1.45; white-space: pre-line; color: var(--fg); }
</style>
