<script lang="ts">
  import { sendToShell } from '../bridge'
  import { addDays, dayKey, parseWhen } from '../lib/dates'
  import Icon from '../parts/Icon.svelte'
  import type { EditorEvent, EditorCalendar } from './types'
  // One event, shown ready to edit (or a new one): Save sends only what changed; the window stays open until closed
  let { event, calendars, day, time, savedAt, problem = $bindable() }: { event: EditorEvent | null; calendars: EditorCalendar[]; day: string; time: string; savedAt: number; problem: string } = $props()
  const clock = (date: Date) => `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
  const notesOf = (html: string) => html ? new DOMParser().parseFromString(html.replace(/<(br|\/p|\/li|\/div)\s*\/?>/gi, '\n$&'), 'text/html').body.textContent?.trim() ?? '' : ''
  const writable = $derived(calendars.filter(calendar => !calendar.readOnly))
  const calendar = $derived(calendars.find(candidate => candidate.key === (event?.calendarKey ?? draft.calendarKey)))
  const readOnly = $derived(!!event && !!calendar?.readOnly)
  type Draft = { title: string; allDay: boolean; startDate: string; startTime: string; endDate: string; endTime: string; location: string; description: string; calendarKey: string }
  function fromEvent(): Draft {
    if (!event) {
      // A double-clicked day is an all-day event; a double-clicked hour (Week) starts then, for an hour
      const startHour = time || '09:00', endAt = new Date(new Date(`${day || dayKey(new Date())}T${startHour}`).getTime() + 3_600_000)
      return { title: '', allDay: !time, startDate: day || dayKey(new Date()), startTime: startHour, endDate: time ? dayKey(endAt) : day || dayKey(new Date()), endTime: clock(endAt), location: '', description: '', calendarKey: (writable.find(candidate => candidate.shown) ?? writable[0])?.key ?? '' }
    }
    const startAt = parseWhen(event.start), endAt = parseWhen(event.end), lastDay = event.allDay ? addDays(endAt, -1) : endAt
    return { title: event.title, allDay: event.allDay, startDate: dayKey(startAt), startTime: event.allDay ? '09:00' : clock(startAt), endDate: dayKey(lastDay), endTime: event.allDay ? '10:00' : clock(endAt), location: event.location ?? '', description: notesOf(event.description), calendarKey: event.calendarKey }
  }
  let draft = $state<Draft>(fromEvent()), original = $state<Draft>(fromEvent()), saving = $state(false), justSaved = $state(false), confirmingDelete = $state(false), wholeSeries = $state(false)
  const dirty = $derived(JSON.stringify(draft) !== JSON.stringify(original))
  // The event changed elsewhere (sync, another window): follow it unless this one has unsaved edits
  $effect(() => { const fresh = fromEvent(); if (JSON.stringify(fresh) !== JSON.stringify(original) && !dirty) { draft = fresh; original = fromEvent() } })
  $effect(() => { if (savedAt) { saving = false; original = JSON.parse(JSON.stringify(draft)); justSaved = true; setTimeout(() => justSaved = false, 1600) } })
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
    if ((keyEvent.key === 's' && keyEvent.ctrlKey) || (keyEvent.key === 'Enter' && (keyEvent.ctrlKey || !(keyEvent.target as HTMLElement).matches('textarea')))) { keyEvent.preventDefault(); save() }
  }
  let titleInput: HTMLInputElement | undefined = $state()
  $effect(() => { if (!event) titleInput?.focus() })
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="form" onkeydown={onKey} style="--colour: {event?.colour || calendar?.colour || '#4f8ef7'}">
  <div class="title-row"><span class="swatch"></span><input class="title" bind:this={titleInput} bind:value={draft.title} placeholder={event ? 'Title' : 'Add a title'} disabled={readOnly} /></div>
  <div class="row quiet"><Icon name="calendar" size={15} />
    {#if event}<span>{calendar?.name}{calendar ? ' · ' + calendar.account : ''}{readOnly ? ' (read only)' : ''}</span>
    {:else}<select bind:value={draft.calendarKey}>{#each writable as option}<option value={option.key}>{option.name} · {option.account}</option>{/each}</select>{/if}
  </div>
  <div class="row"><Icon name="clock" size={15} /><label class="check"><input type="checkbox" bind:checked={draft.allDay} disabled={readOnly} />All day</label></div>
  <div class="row times"><span class="label">Starts</span><input type="date" value={draft.startDate} onchange={(changeEvent) => moveStart(changeEvent.currentTarget.value, draft.startTime)} disabled={readOnly} />{#if !draft.allDay}<input type="time" value={draft.startTime} onchange={(changeEvent) => moveStart(draft.startDate, changeEvent.currentTarget.value)} disabled={readOnly} />{/if}</div>
  <div class="row times"><span class="label">Ends</span><input type="date" bind:value={draft.endDate} disabled={readOnly} />{#if !draft.allDay}<input type="time" bind:value={draft.endTime} disabled={readOnly} />{/if}</div>
  {#if draftProblem}<div class="problem indent">{draftProblem}</div>{/if}
  {#if event?.recurring}<div class="row quiet"><Icon name="repeat" size={15} /><span>Repeats. Changes here apply to this occurrence.</span></div>{/if}
  <div class="row"><Icon name="location" size={15} /><input bind:value={draft.location} placeholder="Add a place" disabled={readOnly} />{#if draft.location}<a class="icon-link" href={'https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(draft.location)} target="_blank" rel="noreferrer" title="Open in Maps">Map</a>{/if}</div>
  {#if event?.meetUrl}<div class="row"><Icon name="video" size={15} /><a class="join" href={event.meetUrl} target="_blank" rel="noreferrer">Join video call</a></div>{/if}
  <textarea bind:value={draft.description} placeholder="Add notes" disabled={readOnly}></textarea>
  {#if problem}<div class="problem">{problem}</div>{/if}
  {#if confirmingDelete && event}
    <div class="confirm">
      <b>Delete “{event.title}”?</b>
      {#if event.recurring}<label><input type="radio" name="series" checked={!wholeSeries} onchange={() => wholeSeries = false} />This event</label><label><input type="radio" name="series" checked={wholeSeries} onchange={() => wholeSeries = true} />All events in the series</label>{/if}
      <div class="buttons"><button class="ghost" onclick={() => confirmingDelete = false}>Cancel</button><button class="danger" onclick={() => sendToShell({ type: 'deleteEvents', ids: [event.id], series: event.recurring && wholeSeries })}>Delete</button></div>
    </div>
  {:else}
    <div class="buttons">
      {#if event && !readOnly}<button class="ghost icon" onclick={() => confirmingDelete = true} title="Delete"><Icon name="trash" size={16} /></button>{/if}
      {#if event?.link}<a class="more" href={event.link} target="_blank" rel="noreferrer" title="Guests, reminders and the rest">Open in Google</a>{/if}
      <span class="grow"></span>
      {#if justSaved}<span class="saved"><Icon name="check" size={14} />Saved</span>{/if}
      {#if !readOnly}<button class="primary" disabled={!!draftProblem || saving || (!!event && !dirty)} onclick={save}>{saving ? 'Saving…' : event ? 'Save' : 'Add event'}</button>{/if}
    </div>
  {/if}
</div>

<style>
  .form { flex: 1; min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 6px; padding: 4px 16px 14px; }
  .title-row { display: flex; align-items: center; gap: 10px; margin-bottom: 4px; }
  .swatch { width: 14px; height: 14px; border-radius: 4px; background: var(--colour); flex: none; }
  input:not([type="checkbox"]):not([type="radio"]), textarea, select { min-width: 0; background: color-mix(in srgb, var(--fg) 6%, transparent); color: var(--fg); border: 1px solid var(--line); border-radius: 7px; padding: 5px 9px; font: inherit; font-size: 13px; outline: none; color-scheme: light dark; }
  input:focus, textarea:focus, select:focus { border-color: var(--accent); }
  input:disabled, textarea:disabled { opacity: 0.75; }
  .title { flex: 1; font-size: 17px !important; font-weight: 650; padding: 7px 10px !important; }
  .row { display: flex; align-items: center; gap: 10px; min-height: 30px; font-size: 13px; }
  .row :global(svg) { color: var(--fg-quiet); flex: none; }
  .row input:not([type="checkbox"]), .row select { flex: 1; }
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
  .buttons { display: flex; align-items: center; gap: 8px; margin-top: 6px; }
  .grow { flex: 1; }
  .more { font-size: 12px; }
  button { height: 32px; padding: 0 16px; border: 0; border-radius: 8px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
  .primary { background: var(--accent); color: white; }
  .primary:disabled { opacity: 0.45; cursor: default; }
  .ghost { background: var(--hover); color: var(--fg); }
  .ghost.icon { display: grid; place-items: center; width: 34px; padding: 0; color: var(--fg-quiet); }
  .danger { background: #d9453a; color: white; }
  .saved { display: flex; align-items: center; gap: 4px; font-size: 12px; color: var(--fg-quiet); }
  .confirm { display: flex; flex-direction: column; gap: 6px; margin-top: 6px; padding: 10px 12px; border-radius: 10px; background: color-mix(in srgb, #d9453a 12%, transparent); font-size: 13px; }
  .confirm label { display: flex; align-items: center; gap: 8px; cursor: pointer; }
  .confirm .buttons { justify-content: flex-end; }
</style>
