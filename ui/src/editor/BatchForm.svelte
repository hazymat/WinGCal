<script lang="ts">
  import { sendToShell } from '../bridge'
  import { dayKey } from '../lib/dates'
  import SaveChanges from './SaveChanges.svelte'
  import type { EditorEvent, EditorCalendar } from './types'
  // Several picked events at once: tick what to change; everything else stays as it is
  let { events, calendars, problem = $bindable() }: { events: EditorEvent[]; calendars: EditorCalendar[]; problem: string } = $props()
  let change = $state({ date: false, start: false, end: false, place: false, calendar: false })
  let values = $state({ date: dayKey(new Date()), start: '09:00', end: '10:00', place: '', calendar: '' })
  let saving = $state(false), askingSave = $state(false)
  const closeWindow = () => sendToShell({ type: 'windowControl', action: 'close' })
  // Esc closes when nothing is ticked, otherwise asks first (Change is the default); Esc again goes back
  function onEscape(keyEvent: KeyboardEvent) { if (keyEvent.key !== 'Escape') return; keyEvent.preventDefault(); if (askingSave) askingSave = false; else if (anything && !saving) askingSave = true; else closeWindow() }
  $effect(() => { if (problem) saving = false })
  // Google moves events only between calendars of one account
  const accounts = $derived(Array.from(new Set(events.map(event => calendars.find(calendar => calendar.key === event.calendarKey)?.account))))
  const targets = $derived(accounts.length === 1 ? calendars.filter(calendar => calendar.account === accounts[0] && !calendar.readOnly) : [])
  $effect(() => { if (!values.calendar && events[0]) values.calendar = events[0].calendarKey })
  const anything = $derived(Object.values(change).some(Boolean))
  function apply() {
    if (!anything || saving) return
    problem = ''; saving = true
    sendToShell({ type: 'batchSave', date: change.date ? values.date : '', startTime: change.start ? values.start : '', endTime: change.end ? values.end : '', location: change.place ? values.place : null, calendarKey: change.calendar ? values.calendar : '' })
  }
</script>

<svelte:window onkeydown={onEscape} />
<div class="form">
  <ul>{#each events.slice(0, 8) as event}<li style="--colour: {event.colour || calendars.find(calendar => calendar.key === event.calendarKey)?.colour}"><i></i>{event.title}</li>{/each}{#if events.length > 8}<li class="quiet">and {events.length - 8} more</li>{/if}</ul>
  <p class="quiet">Tick what to change. Everything else stays as it is.</p>
  <label class="row"><input type="checkbox" bind:checked={change.date} /><span class="label">Date</span><input type="date" bind:value={values.date} oninput={() => change.date = true} /></label>
  <p class="hint">Each moves to this day, keeping its time and length.</p>
  <label class="row"><input type="checkbox" bind:checked={change.start} /><span class="label">Starts at</span><input type="time" bind:value={values.start} oninput={() => change.start = true} /></label>
  <p class="hint">Keeps each one's length. All-day events become an hour long.</p>
  <label class="row"><input type="checkbox" bind:checked={change.end} /><span class="label">Ends at</span><input type="time" bind:value={values.end} oninput={() => change.end = true} /></label>
  <label class="row"><input type="checkbox" bind:checked={change.place} /><span class="label">Place</span><input bind:value={values.place} placeholder="Leave empty to clear it" oninput={() => change.place = true} /></label>
  {#if targets.length > 1}<label class="row"><input type="checkbox" bind:checked={change.calendar} /><span class="label">Calendar</span><select bind:value={values.calendar} onchange={() => change.calendar = true}>{#each targets as target}<option value={target.key}>{target.name}</option>{/each}</select></label>{/if}
  {#if events.some(event => event.recurring)}<p class="hint">Repeating events change only these occurrences, and can't move to another calendar.</p>{/if}
  {#if problem}<p class="problem">{problem}</p>{/if}
  {#if askingSave}<SaveChanges saveLabel={`Change ${events.length} events`} onsave={apply} ondiscard={closeWindow} />
  {:else}<div class="buttons"><button class="ghost" onclick={closeWindow}>Cancel<kbd>Esc</kbd></button><button class="primary" disabled={!anything || saving} onclick={apply}>{saving ? 'Changing…' : `Change ${events.length} events`}</button></div>{/if}
</div>

<style>
  .form { flex: 1; min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 4px; padding: 2px 16px 14px; font-size: 13px; }
  ul { margin: 0 0 4px; padding: 0; list-style: none; font-size: 12.5px; }
  li { display: flex; align-items: center; gap: 8px; padding: 1px 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  li i { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--colour); }
  .quiet { margin: 2px 0 6px; color: var(--fg-quiet); font-size: 12px; }
  .row { display: flex; align-items: center; gap: 8px; min-height: 32px; cursor: pointer; }
  .label { width: 70px; flex: none; }
  .row input:not([type="checkbox"]), .row select { flex: 1; min-width: 0; background: color-mix(in srgb, var(--fg) 6%, transparent); color: var(--fg); border: 1px solid var(--line); border-radius: 7px; padding: 5px 9px; font: inherit; outline: none; color-scheme: light dark; }
  .row input:focus, .row select:focus { border-color: var(--accent); }
  .hint { margin: -2px 0 4px 99px; font-size: 11.5px; color: var(--fg-quiet); }
  .problem { color: var(--danger, #e5534b); font-size: 12px; }
  .buttons { display: flex; justify-content: flex-end; gap: 8px; margin-top: 10px; }
  .ghost { background: var(--hover); color: var(--fg); }
  kbd { margin-left: 7px; font: inherit; font-size: 10.5px; font-weight: 600; opacity: 0.6; }
  button { height: 32px; padding: 0 16px; border: 0; border-radius: 8px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; background: var(--accent); color: white; }
  button:disabled { opacity: 0.45; cursor: default; }
</style>
