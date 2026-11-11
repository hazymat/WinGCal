<script lang="ts">
  import CalendarPicker from './CalendarPicker.svelte'
  import { sendToShell } from '../bridge'
  import { addDays, dayKey, parseWhen } from '../lib/dates'
  import Icon from '../parts/Icon.svelte'
  import SaveChanges from './SaveChanges.svelte'
  import type { EditorEvent, EditorCalendar } from './types'
  // One event, shown ready to edit (or a new one): Save sends only what changed; the window stays open until closed
  let { event, calendars, day, time, endDay = '', savedAt, problem = $bindable() }: { event: EditorEvent | null; calendars: EditorCalendar[]; day: string; time: string; endDay?: string; savedAt: number; problem: string } = $props()
  const clock = (date: Date) => `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
  const notesOf = (html: string) => html ? new DOMParser().parseFromString(html.replace(/<(br|\/p|\/li|\/div)\s*\/?>/gi, '\n$&'), 'text/html').body.textContent?.trim() ?? '' : ''
  // New events go only in calendars picked in Settings › Calendars (all writable ones if none are picked)
  const writable = $derived.by(() => { const open = calendars.filter(calendar => !calendar.readOnly), picked = open.filter(calendar => calendar.inSwitcher); return picked.length ? picked : open })
  const calendar = $derived(calendars.find(candidate => candidate.key === (event?.calendarKey ?? draft.calendarKey)))
  const readOnly = $derived(!!event && !!calendar?.readOnly)
  type Draft = { title: string; allDay: boolean; startDate: string; startTime: string; endDate: string; endTime: string; location: string; description: string; calendarKey: string }
  function fromEvent(): Draft {
    if (!event) {
      // A double-clicked day is an all-day event; a double-clicked hour (Week) starts then, for an hour
      const startHour = time || '09:00', endAt = new Date(new Date(`${day || dayKey(new Date())}T${startHour}`).getTime() + 3_600_000)
      return { title: '', allDay: !time, startDate: day || dayKey(new Date()), startTime: startHour, endDate: time ? dayKey(endAt) : endDay || day || dayKey(new Date()), endTime: clock(endAt), location: '', description: '', calendarKey: (writable.find(candidate => candidate.shown) ?? writable[0])?.key ?? '' }
    }
    const startAt = parseWhen(event.start), endAt = parseWhen(event.end), lastDay = event.allDay ? addDays(endAt, -1) : endAt
    return { title: event.title, allDay: event.allDay, startDate: dayKey(startAt), startTime: event.allDay ? '09:00' : clock(startAt), endDate: dayKey(lastDay), endTime: event.allDay ? '10:00' : clock(endAt), location: event.location ?? '', description: notesOf(event.description), calendarKey: event.calendarKey }
  }
  let draft = $state<Draft>(fromEvent()), original = $state<Draft>(fromEvent()), saving = $state(false), confirmingDelete = $state(false), askingSave = $state(false), wholeSeries = $state(false)
  const dirty = $derived(JSON.stringify(draft) !== JSON.stringify(original))
  // The event changed elsewhere (sync, another window): follow it unless this one has unsaved edits
  $effect(() => { const fresh = fromEvent(); if (JSON.stringify(fresh) !== JSON.stringify(original) && !dirty) { draft = fresh; original = fromEvent() } })
  // Save closes the window once Google has the change; Cancel (or Esc) closes it without saving
  $effect(() => { if (savedAt) { saving = false; original = JSON.parse(JSON.stringify(draft)); closeWindow() } })
  const closeWindow = () => sendToShell({ type: 'windowControl', action: 'close' })
  $effect(() => { if (problem) saving = false })
  function withOffset(dateText: string, timeText: string) {
    const local = new Date(`${dateText}T${timeText}:00`), minutes = -local.getTimezoneOffset(), sign = minutes >= 0 ? '+' : '-'
    return `${dateText}T${timeText}:00${sign}${String(Math.floor(Math.abs(minutes) / 60)).padStart(2, '0')}:${String(Math.abs(minutes) % 60).padStart(2, '0')}`
  }
  const draftProblem = $derived(!draft.startDate || !draft.endDate ? 'Pick the dates' : draft.allDay ? (draft.endDate < draft.startDate ? 'Ends before it starts' : '') : new Date(`${draft.endDate}T${draft.endTime}`) <= new Date(`${draft.startDate}T${draft.startTime}`) ? 'Ends before it starts' : '')
  // Moving the start keeps the length, as Google Calendar does
  function moveStart(dateText: string, timeText: string) {
    if (!draft.allDay && dateText && timeText && draft.startDate && draft.startTime) {
      const length = new Date(`${draft.endDate}T${draft.endTime}`).getTime() - new Date(`${draft.startDate}T${draft.startTime}`).getTime(), newEnd = new Date(new Date(`${dateText}T${timeText}`).getTime() + length)
      if (length > 0) { draft.endDate = dayKey(newEnd); draft.endTime = clock(newEnd) }
    } else if (draft.allDay && dateText && draft.startDate) { const days = Math.round((parseWhen(draft.endDate).getTime() - parseWhen(draft.startDate).getTime()) / 86_400_000); draft.endDate = dayKey(addDays(parseWhen(dateText), Math.max(0, days))) }
    draft.startDate = dateText; draft.startTime = timeText
  }
  function save() {
    if (draftProblem || saving || readOnly) return
    problem = ''
    const start = draft.allDay ? draft.startDate : withOffset(draft.startDate, draft.startTime)
    const end = draft.allDay ? dayKey(addDays(parseWhen(draft.endDate), 1)) : withOffset(draft.endDate, draft.endTime)
    if (!event) { if (!draft.calendarKey) { problem = 'Pick a calendar'; return } saving = true; sendToShell({ type: 'createEvent', calendarKey: draft.calendarKey, summary: draft.title || '(No title)', location: draft.location, description: draft.description, allDay: draft.allDay, start, end }); return }
    if (!dirty) return
    const message: { type: string; [field: string]: unknown } = { type: 'saveEvent' }
    if (draft.title !== original.title) message.summary = draft.title
    if (draft.location !== original.location) message.location = draft.location
    if (draft.description !== original.description) message.description = draft.description
    if (draft.allDay !== original.allDay || draft.startDate !== original.startDate || draft.endDate !== original.endDate || (!draft.allDay && (draft.startTime !== original.startTime || draft.endTime !== original.endTime))) Object.assign(message, { allDay: draft.allDay, start, end })
    saving = true; sendToShell(message)
  }
  function onKey(keyEvent: KeyboardEvent) {
    if ((keyEvent.key === 's' && keyEvent.ctrlKey) || (keyEvent.key === 'Enter' && (keyEvent.ctrlKey || !(keyEvent.target as HTMLElement).matches('textarea, button, a')))) { keyEvent.preventDefault(); save() }
  }
  // Esc: backs out of a question first, closes straight away when nothing changed, otherwise asks (Save is the default)
  const unsaved = $derived(dirty && !readOnly)
  function onEscape(keyEvent: KeyboardEvent) {
    // Del (outside a text box) asks to delete the event, as the bin button does
    if (keyEvent.key === 'Delete' && event && !readOnly && !askingSave && !(keyEvent.target as HTMLElement).matches('input, textarea, select')) { keyEvent.preventDefault(); confirmingDelete = true; return }
    if (keyEvent.key !== 'Escape') return
    keyEvent.preventDefault()
    if (confirmingDelete) confirmingDelete = false; else if (askingSave) askingSave = false; else if (unsaved && !saving) askingSave = true; else closeWindow()
  }
  let deleteButton: HTMLButtonElement | undefined = $state(), titleInput: HTMLTextAreaElement | undefined = $state(), form: HTMLDivElement | undefined = $state()
  // The delete question grows the window by whatever no longer fits, rather than leaving it to scroll
  $effect(() => { if (!(confirmingDelete || askingSave) || !form) return; requestAnimationFrame(() => { if (!form) return; const overflow = form.scrollHeight - form.clientHeight; if (overflow > 0) sendToShell({ type: 'growWindow', by: overflow }) }) })
  $effect(() => { if (!event) titleInput?.focus() })
  // The delete question takes the focus, so Enter deletes and Esc backs out
  $effect(() => { if (confirmingDelete) deleteButton?.focus() })
</script>

<svelte:window onkeydown={onEscape} />
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="form" bind:this={form} onkeydown={onKey} style="--colour: {event?.colour || calendar?.colour || '#4f8ef7'}">
  <div class="title-row"><span class="swatch"></span><textarea class="title" rows="1" bind:this={titleInput} bind:value={draft.title} placeholder={event ? 'Title' : 'Add a title'} disabled={readOnly} onkeydown={(keyEvent) => { if (keyEvent.key === 'Enter') keyEvent.preventDefault() }}></textarea></div>
  <div class="row quiet"><Icon name="calendar" size={15} />
    {#if event}<span>{calendar?.name}{calendar ? ' · ' + calendar.account : ''}{readOnly ? ' (read only)' : ''}</span>
    {:else}<CalendarPicker calendars={writable} bind:value={draft.calendarKey} />{/if}
  </div>
  <div class="row"><Icon name="clock" size={15} /><label class="check"><input type="checkbox" bind:checked={draft.allDay} disabled={readOnly} />All day</label></div>
  <div class="row times"><span class="label">Starts</span><input type="date" value={draft.startDate} onchange={(changeEvent) => moveStart(changeEvent.currentTarget.value, draft.startTime)} disabled={readOnly} />{#if !draft.allDay}<input type="time" value={draft.startTime} onchange={(changeEvent) => moveStart(draft.startDate, changeEvent.currentTarget.value)} disabled={readOnly} />{/if}</div>
  <div class="row times"><span class="label">Ends</span><input type="date" bind:value={draft.endDate} disabled={readOnly} />{#if !draft.allDay}<input type="time" bind:value={draft.endTime} disabled={readOnly} />{/if}</div>
  {#if draftProblem}<div class="problem indent">{draftProblem}</div>{/if}
  {#if event?.recurring}<div class="row quiet"><Icon name="repeat" size={15} /><span>Repeats. Changes here apply to this occurrence.</span></div>{/if}
  <div class="row"><Icon name="location" size={15} /><input bind:value={draft.location} placeholder="Add a place" disabled={readOnly} />{#if draft.location}<a class="icon-link" href={'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(draft.location)} target="_blank" rel="noreferrer" title="Open in Maps">Map</a>{/if}</div>
  {#if event?.meetUrl}<div class="row"><Icon name="video" size={15} /><a class="join" href={event.meetUrl} target="_blank" rel="noreferrer">Join video call</a></div>{/if}
  <textarea bind:value={draft.description} placeholder="Add notes" disabled={readOnly}></textarea>
  {#if event?.link}<a class="more" href={event.link} target="_blank" rel="noreferrer" title="Guests, reminders and the rest">Open in Google</a>{/if}
  {#if problem}<div class="problem">{problem}</div>{/if}
  {#if askingSave}
    <SaveChanges saveLabel={event ? 'Save' : 'Add event'} canSave={!draftProblem} onsave={save} ondiscard={closeWindow} />
  {:else if confirmingDelete && event}
    <div class="confirm">
      <b>Delete “{event.title}”?</b>
      {#if event.recurring}<label><input type="radio" name="series" checked={!wholeSeries} onchange={() => wholeSeries = false} />This event</label><label><input type="radio" name="series" checked={wholeSeries} onchange={() => wholeSeries = true} />All events in the series</label>{/if}
      <div class="buttons"><button class="ghost" onclick={() => confirmingDelete = false}>Cancel<kbd>Esc</kbd></button><button class="danger" bind:this={deleteButton} onclick={() => sendToShell({ type: 'deleteEvents', ids: [event.id], series: event.recurring && wholeSeries })}>Delete<kbd>Enter</kbd></button></div>
    </div>
  {:else}
    <div class="buttons">
      {#if event && !readOnly}<button class="ghost icon" onclick={() => confirmingDelete = true} title="Delete"><Icon name="trash" size={16} /><kbd>Del</kbd></button>{/if}
      <span class="grow"></span>
      <button class="ghost" onclick={closeWindow}>{readOnly ? 'Close' : 'Cancel'}<kbd>Esc</kbd></button>
      {#if !readOnly}<button class="primary" disabled={!!draftProblem || saving || (!!event && !dirty)} onclick={save}>{saving ? 'Saving…' : event ? 'Save' : 'Add event'}<kbd>Enter</kbd></button>{/if}
    </div>
  {/if}
</div>

<style>
  .form { flex: 1; min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 8px; padding: 2px 16px 16px; }
  .title-row { display: flex; align-items: center; gap: 11px; margin-bottom: 2px; }
  .swatch { width: 14px; height: 14px; border-radius: 4px; background: var(--colour); flex: none; }
  input:not([type="checkbox"]):not([type="radio"]), textarea { min-width: 0; background: color-mix(in srgb, var(--fg) 6%, transparent); color: var(--fg); border: 1px solid var(--line); border-radius: 7px; padding: 5px 9px; font: inherit; font-size: 13px; outline: none; color-scheme: light dark; }
  input:focus, textarea:focus { border-color: var(--accent); }
  input:disabled, textarea:disabled { opacity: 0.75; }
  .title { flex: 1; font-size: 14px !important; font-weight: 650; line-height: 1.3; padding: 6px 10px !important; resize: none; field-sizing: content; min-height: 0; max-height: 5.4em; overflow-y: auto; margin-left: 0; } /* wraps rather than cutting long titles off */
  .row { display: flex; align-items: center; gap: 10px; min-height: 30px; font-size: 13px; }
  .row + .times { margin-top: -2px; }
  .row :global(svg) { color: var(--fg-quiet); flex: none; }
  .row input:not([type="checkbox"]) { flex: 1; }
  .quiet { color: var(--fg-quiet); }
  .times { padding-left: 25px; }
  .times .label { width: 44px; flex: none; color: var(--fg-quiet); font-size: 12.5px; }
  .times input[type="time"] { flex: 0 0 104px !important; }
  .check { display: flex; align-items: center; gap: 6px; cursor: pointer; }
  .indent { padding-left: 25px; }
  .problem { font-size: 12px; color: var(--danger, #e5534b); }
  textarea { flex: 1; min-height: 90px; margin-left: 25px; resize: none; line-height: 1.45; }
  a { color: var(--accent); text-decoration: none; font-size: 13px; }
  a:hover { text-decoration: underline; }
  .icon-link { flex: none; font-size: 12px; }
  .join { font-weight: 600; }
  .buttons { display: flex; align-items: center; gap: 8px; margin-top: 4px; }
  .grow { flex: 1; }
  .more { align-self: flex-start; margin-left: 25px; padding-left: 0; font-size: 12px; }
  button { height: 32px; padding: 0 16px; border: 0; border-radius: 8px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
  .primary { background: var(--accent); color: white; }
  .primary:disabled { opacity: 0.45; cursor: default; }
  .ghost { background: var(--hover); color: var(--fg); }
  .ghost.icon { display: flex; align-items: center; justify-content: center; gap: 0; padding: 0 10px; color: var(--fg-quiet); }
  /* Each button shows its key */
  kbd { margin-left: 6px; color: inherit; border-color: color-mix(in srgb, currentColor 30%, transparent); opacity: 0.7; }
  button { outline: none; }
  button:focus-visible { outline: none; box-shadow: none; filter: brightness(1.1); }
  .danger { background: #d9453a; color: white; }
  .confirm { display: flex; flex-direction: column; gap: 6px; margin-top: 6px; padding: 10px 12px; border-radius: 10px; background: color-mix(in srgb, #d9453a 12%, transparent); font-size: 13px; }
  .confirm label { display: flex; align-items: center; gap: 8px; cursor: pointer; }
  .confirm .buttons { justify-content: flex-end; }
</style>
