<script lang="ts">
  import { ui, calendarByKey } from '../state.svelte'
  import { sendToShell } from '../bridge'
  import { formatTime, longDate, sameDay, addDays, dayKey, parseWhen } from '../lib/dates'
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
  // Editing in place: the edit button, or a click on the title, time, place or notes, turns the card into a form; Save sends only what changed
  const editable = $derived(!calendar?.readOnly)
  let editing = $state(false), saving = $state(false)
  let draft = $state({ title: '', allDay: false, startDate: '', startTime: '', endDate: '', endTime: '', location: '', description: '' })
  const clock = (date: Date) => `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
  function startEditing(focus = 'title') {
    if (!editable || editing) return
    const lastDay = event.allDay ? addDays(event.endAt, -1) : event.endAt
    draft = { title: event.title, allDay: event.allDay, startDate: dayKey(event.startAt), startTime: event.allDay ? '09:00' : clock(event.startAt), endDate: dayKey(lastDay), endTime: event.allDay ? '10:00' : clock(event.endAt), location: event.location ?? '', description: descriptionText }
    editing = true
    requestAnimationFrame(() => card?.querySelector<HTMLElement>(`[data-field="${focus}"]`)?.focus())
  }
  // "2026-11-10T14:00:00+00:00": local time with its offset, as Google wants it
  function withOffset(dateText: string, timeText: string) {
    const local = new Date(`${dateText}T${timeText}:00`), minutes = -local.getTimezoneOffset(), sign = minutes >= 0 ? '+' : '-'
    return `${dateText}T${timeText}:00${sign}${String(Math.floor(Math.abs(minutes) / 60)).padStart(2, '0')}:${String(Math.abs(minutes) % 60).padStart(2, '0')}`
  }
  const draftProblem = $derived(!editing ? '' : !draft.startDate || !draft.endDate ? 'Pick the dates' : draft.allDay ? (draft.endDate < draft.startDate ? 'Ends before it starts' : '') : new Date(`${draft.endDate}T${draft.endTime}`) <= new Date(`${draft.startDate}T${draft.startTime}`) ? 'Ends before it starts' : '')
  function save() {
    if (draftProblem || saving) return
    const message: { type: string; [field: string]: unknown } = { type: 'updateEvent', id: event.id }
    if (draft.title !== event.title) message.summary = draft.title
    if (draft.location !== (event.location ?? '')) message.location = draft.location
    if (draft.description !== descriptionText) message.description = draft.description
    const start = draft.allDay ? draft.startDate : withOffset(draft.startDate, draft.startTime)
    const end = draft.allDay ? dayKey(addDays(parseWhen(draft.endDate), 1)) : withOffset(draft.endDate, draft.endTime)
    const timesChanged = draft.allDay !== event.allDay || (draft.allDay ? draft.startDate !== dayKey(event.startAt) || end !== dayKey(event.endAt) : new Date(start).getTime() !== event.startAt.getTime() || new Date(end).getTime() !== event.endAt.getTime())
    if (timesChanged) Object.assign(message, { allDay: draft.allDay, start, end })
    if (Object.keys(message).length > 2) sendToShell(message)
    ui.selectedEvent = null
  }
  // Moving the start keeps the length, as Google Calendar does
  function moveStart(dateText: string, timeText: string) {
    if (!draft.allDay && dateText && timeText && draft.startDate && draft.startTime) {
      const length = new Date(`${draft.endDate}T${draft.endTime}`).getTime() - new Date(`${draft.startDate}T${draft.startTime}`).getTime(), newEnd = new Date(new Date(`${dateText}T${timeText}`).getTime() + length)
      if (length > 0) { draft.endDate = dayKey(newEnd); draft.endTime = clock(newEnd) }
    } else if (draft.allDay && dateText && draft.startDate) { const days = Math.round((parseWhen(draft.endDate).getTime() - parseWhen(draft.startDate).getTime()) / 86_400_000); draft.endDate = dayKey(addDays(parseWhen(dateText), Math.max(0, days))) }
    draft.startDate = dateText; draft.startTime = timeText
  }
  function onFormKey(keyEvent: KeyboardEvent) {
    if (keyEvent.key === 'Escape') { keyEvent.stopPropagation(); keyEvent.preventDefault(); editing = false }
    else if (keyEvent.key === 'Enter' && !(keyEvent.target as HTMLElement).matches('textarea')) { keyEvent.preventDefault(); save() }
  }
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
<div class="card" class:editing bind:this={card} style="left: {position.left}px; top: {position.top}px; --colour: {event.colourShown}" role="dialog" aria-label={event.title}>
  {#if editing}
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div class="form" onkeydown={onFormKey}>
      <div class="title-row"><span class="swatch"></span><input class="title-input" data-field="title" bind:value={draft.title} placeholder="Title" /></div>
      <div class="row"><Icon name="clock" size={15} /><label class="check"><input type="checkbox" bind:checked={draft.allDay} />All day</label></div>
      <div class="row times">
        <span class="label">Starts</span><input type="date" data-field="when" value={draft.startDate} onchange={(changeEvent) => moveStart(changeEvent.currentTarget.value, draft.startTime)} />
        {#if !draft.allDay}<input type="time" value={draft.startTime} onchange={(changeEvent) => moveStart(draft.startDate, changeEvent.currentTarget.value)} />{/if}
      </div>
      <div class="row times">
        <span class="label">Ends</span><input type="date" bind:value={draft.endDate} />
        {#if !draft.allDay}<input type="time" bind:value={draft.endTime} />{/if}
      </div>
      {#if draftProblem}<div class="problem">{draftProblem}</div>{/if}
      {#if event.recurring}<div class="row quiet"><Icon name="repeat" size={15} /><span>Changes this occurrence only</span></div>{/if}
      <div class="row"><Icon name="location" size={15} /><input data-field="location" bind:value={draft.location} placeholder="Add a place" /></div>
      <textarea data-field="description" bind:value={draft.description} placeholder="Add notes" rows="3"></textarea>
      <div class="buttons">
        <button class="ghost more" onclick={() => sendToShell({ type: 'editEvent', id: event.id })} title="Guests, reminders and the rest">More in Google Calendar</button>
        <button class="ghost" onclick={() => editing = false}>Cancel</button>
        <button class="primary" disabled={!!draftProblem} onclick={save}>Save</button>
      </div>
    </div>
  {:else}
    <div class="actions">
      {#if editable}
        <button onclick={() => startEditing()} title="Edit"><Icon name="edit" size={16} /></button>
        <button onclick={() => sendToShell({ type: 'deleteEvent', id: event.id })} title="Delete"><Icon name="trash" size={16} /></button>
      {/if}
      <button onclick={() => ui.selectedEvent = null} title="Close"><Icon name="close" size={16} /></button>
    </div>
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_noninteractive_element_interactions -->
    <div class="title-row"><span class="swatch"></span><h3 class:editable onclick={() => startEditing('title')}>{event.title}</h3></div>
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div class="row" class:editable onclick={() => startEditing('when')}><Icon name="clock" size={15} /><span>{when}</span></div>
    {#if event.recurring}<div class="row quiet"><Icon name="repeat" size={15} /><span>Repeats</span></div>{/if}
    {#if event.location}<div class="row"><Icon name="location" size={15} /><a href={'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(event.location)} target="_blank" rel="noreferrer">{event.location}</a></div>{/if}
    {#if event.meetUrl}<div class="row"><Icon name="video" size={15} /><a class="join" href={event.meetUrl} target="_blank" rel="noreferrer">Join video call</a></div>{/if}
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_noninteractive_element_interactions -->
    {#if descriptionText}<p class="description" class:editable onclick={() => startEditing('description')}>{descriptionText}</p>{/if}
    <div class="row quiet"><Icon name="calendar" size={15} /><span>{calendar?.name}{calendar ? ' · ' + calendar.account : ''}</span></div>
  {/if}
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
  .editable { cursor: text; border-radius: 6px; }
  .editable:hover { background: var(--hover); }
  .form { display: flex; flex-direction: column; gap: 4px; }
  .form input:not([type="checkbox"]), .form textarea { min-width: 0; background: var(--panel); color: var(--fg); border: 1px solid var(--line); border-radius: 6px; padding: 4px 8px; font: inherit; font-size: 13px; outline: none; color-scheme: light dark; }
  .form input:focus, .form textarea:focus { border-color: var(--accent); }
  .title-input { flex: 1; font-size: 16px !important; font-weight: 600; }
  .row input:not([type="checkbox"]) { flex: 1; }
  .times .label { width: 44px; flex: none; color: var(--fg-quiet); font-size: 12.5px; padding-left: 25px; box-sizing: content-box; }
  .times input[type="time"] { flex: 0 0 96px; }
  .check { display: flex; align-items: center; gap: 6px; cursor: pointer; }
  .problem { padding-left: 25px; font-size: 12px; color: var(--danger); }
  .form textarea { margin: 4px 0 0 25px; resize: vertical; line-height: 1.4; }
  .buttons { display: flex; gap: 6px; justify-content: flex-end; align-items: center; margin-top: 8px; }
  .buttons .more { margin-right: auto; font-size: 12px; padding-left: 0; }
  .description { margin: 8px 0 6px 25px; font-size: 12.5px; line-height: 1.45; white-space: pre-line; color: var(--fg); }
</style>
