<script lang="ts">
  import { sendToShell } from '../bridge'
  import { ui, placedEvents } from '../state.svelte'
  import { formatTime, weekdayShort, shortDate, dayKey, sameDay } from '../lib/dates'
  import Icon from './Icon.svelte'
  // Press and hold (or Ctrl/Shift-click) picks several events; days held or dragged across in any view (hours in Week) pick days. Their actions float in a rounded bar near the bottom, each with its key, and Cancel (Esc) is always there
  const count = $derived(ui.picked.length)
  let copied = $state(false)
  function copy() {
    const chosen = placedEvents().filter(event => ui.picked.includes(event.id)).sort((first, second) => first.startAt.getTime() - second.startAt.getTime())
    navigator.clipboard?.writeText(chosen.map(event => `${weekdayShort(event.startAt)} ${shortDate(event.startAt)}${event.allDay ? '' : ' ' + formatTime(event.startAt) + ' – ' + formatTime(event.endAt)}  ${event.title}`).join('\n'))
    copied = true; setTimeout(() => copied = false, 1400)
  }
  const edit = () => sendToShell({ type: 'batchEdit', ids: ui.picked.slice() })
  const remove = () => ui.confirmDelete = ui.picked.slice()
  const days = $derived(ui.pickedDays), dayLabel = (day: Date) => `${weekdayShort(day)} ${shortDate(day)}`
  const clock = (time: Date) => `${String(time.getHours()).padStart(2, '0')}:${String(time.getMinutes()).padStart(2, '0')}`
  function newEvent() { if (!days) return; sendToShell({ type: 'newEvent', day: dayKey(days.from), time: days.timed ? clock(days.from) : '', endDay: dayKey(days.to), endTime: days.timed ? clock(days.to) : '' }); ui.pickedDays = null }
  // The keys the bar shows (Esc is the app's: it clears either)
  function onKey(event: KeyboardEvent) {
    if ((event.target as HTMLElement).closest('input, textarea') || ui.confirmDelete || event.altKey) return
    const plain = !event.ctrlKey && !event.metaKey
    if (count > 0) {
      if (plain && event.key.toLowerCase() === 'e') { event.preventDefault(); edit() }
      else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'c') { event.preventDefault(); copy() }
      else if (plain && event.key === 'Delete') { event.preventDefault(); remove() }
    } else if (days && plain && (event.key === 'Enter' || event.key.toLowerCase() === 'n')) { event.preventDefault(); newEvent() }
  }
</script>

<svelte:window onkeydown={onKey} />
{#if count > 0}
  <div class="selection-bar" role="toolbar" aria-label="Selected events">
    <span class="count">{count}<span class="label">{' '}{count === 1 ? 'event' : 'events'}</span>{' '}selected</span>
    <button onclick={edit} title="Change the date, time, place or calendar of all of them"><Icon name="edit" size={15} /><span class="label">Edit…</span><kbd>E</kbd></button>
    <button onclick={copy}><Icon name="copy" size={15} /><span class="label">{copied ? 'Copied' : 'Copy'}</span><kbd>Ctrl+C</kbd></button>
    <button class="danger" onclick={remove}><Icon name="trash" size={15} /><span class="label">Delete</span><kbd>Del</kbd></button>
    <button class="cancel" onclick={() => ui.picked = []}><span class="label">Cancel</span><kbd>Esc</kbd></button>
  </div>
{:else if days}
  <div class="selection-bar" role="toolbar" aria-label="Picked days">
    <span class="count">{days.timed ? `${dayLabel(days.from)} ${formatTime(days.from)} – ${sameDay(days.from, days.to) ? '' : dayLabel(days.to) + ' '}${formatTime(days.to)}` : sameDay(days.from, days.to) ? dayLabel(days.from) : days.from.getMonth() === days.to.getMonth() ? `${weekdayShort(days.from)} ${days.from.getDate()} – ${dayLabel(days.to)}` : `${dayLabel(days.from)} – ${dayLabel(days.to)}`}</span>
    <button class="go" onclick={newEvent}><Icon name="plus" size={15} />New event<kbd>Enter</kbd></button>
    <button class="cancel" onclick={() => ui.pickedDays = null}>Cancel<kbd>Esc</kbd></button>
  </div>
{/if}

<style>
  .selection-bar { position: fixed; left: 50%; bottom: 18px; z-index: 45; transform: translateX(-50%); display: flex; align-items: center; gap: 2px; max-width: calc(100vw - 24px); padding: 5px 6px 5px 16px; border-radius: 999px; background: var(--panel-solid); box-shadow: inset 0 0 0 1px var(--line), 0 10px 30px var(--shadow); font-size: 13px; white-space: nowrap; animation: rise 160ms ease-out; }
  @keyframes rise { from { opacity: 0; transform: translate(-50%, 8px); } }
  .count { margin-right: 8px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; }
  button { display: flex; align-items: center; gap: 6px; height: 30px; padding: 0 10px; border: 0; border-radius: 999px; background: none; color: var(--fg); font: inherit; cursor: pointer; }
  button:hover { background: var(--hover); }
  .danger:hover { color: var(--danger); }
  .go { color: var(--accent); font-weight: 600; }
  .cancel { color: var(--fg-quiet); }
  kbd { margin-left: 2px; }
  /* Narrow windows: icons and keys only, so the bar always fits */
  @media (max-width: 460px) { .label { display: none; } button { padding: 0 7px; gap: 4px; } .selection-bar { padding-left: 12px; } }
</style>
