<script lang="ts">
  import { sendToShell } from '../bridge'
  import { ui, placedEvents } from '../state.svelte'
  import { formatTime, weekdayShort, shortDate } from '../lib/dates'
  import Icon from './Icon.svelte'
  // Press and hold (or Ctrl/Shift-click) picks several events; their actions float in a rounded bar near the bottom, over the calendar (nothing moves up)
  const count = $derived(ui.picked.length)
  let copied = $state(false)
  function copy() {
    const chosen = placedEvents().filter(event => ui.picked.includes(event.id)).sort((first, second) => first.startAt.getTime() - second.startAt.getTime())
    navigator.clipboard?.writeText(chosen.map(event => `${weekdayShort(event.startAt)} ${shortDate(event.startAt)}${event.allDay ? '' : ' ' + formatTime(event.startAt) + ' – ' + formatTime(event.endAt)}  ${event.title}`).join('\n'))
    copied = true; setTimeout(() => copied = false, 1400)
  }
  function remove() { sendToShell({ type: 'deleteEvents', ids: ui.picked.slice() }); ui.picked = [] }
</script>

{#if count > 0}
  <div class="selection-bar" role="toolbar" aria-label="Selected events">
    <span class="count">{count}<span class="label">{' '}{count === 1 ? 'event' : 'events'}</span>{' '}selected</span>
    <button onclick={() => sendToShell({ type: 'batchEdit', ids: ui.picked.slice() })} title="Change the date, time, place or calendar of all of them"><Icon name="edit" size={15} /><span class="label">Edit…</span></button>
    <button onclick={copy}><Icon name="copy" size={15} /><span class="label">{copied ? 'Copied' : 'Copy'}</span></button>
    <button class="danger" onclick={remove}><Icon name="trash" size={15} /><span class="label">Delete</span></button>
    <button class="clear" onclick={() => ui.picked = []} title="Clear selection (Esc)"><Icon name="close" size={14} /></button>
  </div>
{/if}

<style>
  .selection-bar { position: fixed; left: 50%; bottom: 18px; z-index: 45; transform: translateX(-50%); display: flex; align-items: center; gap: 4px; max-width: calc(100vw - 24px); padding: 5px 6px 5px 16px; border-radius: 999px; background: var(--panel-solid); box-shadow: inset 0 0 0 1px var(--line), 0 10px 30px var(--shadow); font-size: 13px; white-space: nowrap; animation: rise 160ms ease-out; }
  @keyframes rise { from { opacity: 0; transform: translate(-50%, 8px); } }
  .count { margin-right: 8px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; }
  button { display: flex; align-items: center; gap: 6px; height: 30px; padding: 0 12px; border: 0; border-radius: 999px; background: none; color: var(--fg); font: inherit; cursor: pointer; }
  button:hover { background: var(--hover); }
  .danger:hover { color: var(--danger); }
  .clear { width: 30px; padding: 0; justify-content: center; color: var(--fg-quiet); }
  /* Narrow windows: icons only, so the bar always fits */
  @media (max-width: 460px) { .label { display: none; } button { padding: 0 9px; } }
</style>
