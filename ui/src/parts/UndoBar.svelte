<script lang="ts">
  import { onShellMessage, sendToShell } from '../bridge'
  import { ui } from '../state.svelte'
  import Icon from './Icon.svelte'
  // After every change to an event (and every undo or redo) a bar says what happened, with Undo (or Redo) and a doughnut counting down until it goes; hovering holds it. Ctrl+Z and Ctrl+Y work at any time, and Settings › History reaches further back (SPEC.md › Undo)
  let shown = $state<null | { action: string; id: number; summary: string; key: number }>(null)
  onShellMessage('history', message => shown = { action: message.action, id: message.id, summary: message.summary, key: Date.now() })
  const undoing = $derived(shown?.action !== 'undid')
  function act() { if (!shown) return; sendToShell({ type: undoing ? 'undo' : 'redo', id: shown.id }) }
  function onKey(event: KeyboardEvent) {
    if ((event.target as HTMLElement).closest('input, textarea')) return
    const key = event.key.toLowerCase()
    if ((event.ctrlKey || event.metaKey) && key === 'z' && !event.shiftKey) { event.preventDefault(); sendToShell({ type: 'undo' }) }
    else if ((event.ctrlKey || event.metaKey) && (key === 'y' || (key === 'z' && event.shiftKey))) { event.preventDefault(); sendToShell({ type: 'redo' }) }
    else if (event.key === 'Escape' && shown && !ui.picked.length && !ui.pickedDays) shown = null
  }
  const above = $derived(ui.picked.length > 0 || !!ui.pickedDays) // sits above the selection bar while that's up
</script>

<svelte:window onkeydown={onKey} />
{#if shown}
  {#key shown.key}
    <div class="undo-bar" class:above role="status">
      <svg class="doughnut" viewBox="0 0 20 20" aria-hidden="true"><circle cx="10" cy="10" r="7" class="track" /><circle cx="10" cy="10" r="7" class="left" onanimationend={() => shown = null} /></svg>
      <span class="what">{shown.action === 'undid' ? 'Undone: ' : shown.action === 'redid' ? 'Redone: ' : ''}{shown.summary}</span>
      <button class="go" onclick={act}><Icon name={undoing ? 'undo' : 'redo'} size={15} />{undoing ? 'Undo' : 'Redo'}<kbd>{undoing ? 'Ctrl+Z' : 'Ctrl+Y'}</kbd></button>
      <button class="cancel" onclick={() => shown = null} title="Hide (Esc)"><Icon name="close" size={13} /></button>
    </div>
  {/key}
{/if}

<style>
  .undo-bar { position: fixed; left: 50%; bottom: 18px; z-index: 46; transform: translateX(-50%); display: flex; align-items: center; gap: 4px; max-width: calc(100vw - 24px); padding: 5px 6px 5px 10px; border-radius: 999px; background: var(--panel-solid); box-shadow: inset 0 0 0 1px var(--line), 0 10px 30px var(--shadow); font-size: 13px; white-space: nowrap; animation: rise 160ms ease-out; transition: bottom 200ms ease-out; }
  .undo-bar.above { bottom: 62px; }
  @keyframes rise { from { opacity: 0; transform: translate(-50%, 8px); } }
  .what { min-width: 0; margin: 0 6px 0 4px; overflow: hidden; text-overflow: ellipsis; }
  .doughnut { flex: none; width: 18px; height: 18px; transform: rotate(-90deg); }
  .doughnut circle { fill: none; stroke-width: 3; }
  .track { stroke: var(--line); }
  .left { stroke: var(--accent); stroke-dasharray: 44 44; stroke-dashoffset: 0; animation: countdown 8s linear forwards; }
  .undo-bar:hover .left { animation-play-state: paused; }
  @keyframes countdown { to { stroke-dashoffset: 44; } }
  button { display: flex; align-items: center; gap: 6px; height: 30px; padding: 0 10px; border: 0; border-radius: 999px; background: none; color: var(--fg); font: inherit; cursor: pointer; flex: none; }
  button:hover { background: var(--hover); }
  .go { color: var(--accent); font-weight: 600; }
  .cancel { width: 30px; padding: 0; justify-content: center; color: var(--fg-quiet); }
</style>
