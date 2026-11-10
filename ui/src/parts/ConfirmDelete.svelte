<script lang="ts">
  import { ui, placedEvents } from '../state.svelte'
  import { sendToShell } from '../bridge'
  // The Delete question as a glass dialog in the window (never a Windows message box). Repeating events ask: this one, or the whole series
  const chosen = $derived(ui.confirmDelete ? placedEvents().filter(event => ui.confirmDelete!.includes(event.id)).filter((event, index, all) => all.findIndex(other => other.id === event.id) === index) : [])
  const repeating = $derived(chosen.some(event => event.recurring))
  let wholeSeries = $state(false)
  function close() { ui.confirmDelete = null; wholeSeries = false }
  function confirm() { sendToShell({ type: 'deleteEvents', ids: chosen.map(event => event.id), series: repeating && wholeSeries }); ui.picked = ui.picked.filter(id => !chosen.some(event => event.id === id)); close() }
</script>
<svelte:window onkeydown={(event) => { if (!ui.confirmDelete) return; if (event.key === 'Escape') { event.stopPropagation(); close() } else if (event.key === 'Enter') confirm() }} />

{#if ui.confirmDelete && chosen.length > 0}
  <div class="scrim" role="presentation" onpointerdown={close}></div>
  <div class="dialog" role="alertdialog" aria-label="Delete">
    <h3>{chosen.length === 1 ? `Delete “${chosen[0].title}”?` : `Delete ${chosen.length} events?`}</h3>
    {#if chosen.length > 1}<ul>{#each chosen.slice(0, 6) as event}<li style="--colour: {event.colourShown}"><i></i>{event.title}</li>{/each}{#if chosen.length > 6}<li class="quiet">and {chosen.length - 6} more</li>{/if}</ul>{/if}
    {#if repeating}
      <div class="choices" role="radiogroup">
        <label><input type="radio" name="series" checked={!wholeSeries} onchange={() => wholeSeries = false} />{chosen.length === 1 ? 'This event' : 'Only these occurrences'}</label>
        <label><input type="radio" name="series" checked={wholeSeries} onchange={() => wholeSeries = true} />{chosen.length === 1 ? 'All events in the series' : 'Whole series, for the repeating ones'}</label>
      </div>
    {/if}
    <div class="buttons"><button class="ghost" onclick={close}>Cancel</button><button class="danger" onclick={confirm}>Delete</button></div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; z-index: 60; background: rgba(0, 0, 0, 0.25); }
  .dialog { position: fixed; z-index: 61; left: 50%; top: 50%; transform: translate(-50%, -50%); width: min(340px, calc(100vw - 24px)); max-height: calc(100vh - 24px); overflow-y: auto; padding: 16px 18px 14px; border-radius: 14px; background: var(--popover); backdrop-filter: blur(28px) saturate(1.5); border: 1px solid var(--line); box-shadow: 0 18px 40px var(--shadow); color: var(--fg); animation: appear 140ms ease-out; }
  @keyframes appear { from { opacity: 0; transform: translate(-50%, -48%); } }
  h3 { margin: 0 0 8px; font-size: 15px; font-weight: 650; line-height: 1.35; text-transform: none; letter-spacing: 0; overflow-wrap: anywhere; }
  ul { margin: 0 0 8px; padding: 0; list-style: none; font-size: 12.5px; }
  li { display: flex; align-items: center; gap: 8px; padding: 2px 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  li i { flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--colour); }
  .quiet { color: var(--fg-quiet); padding-left: 16px; }
  .choices { display: flex; flex-direction: column; gap: 6px; margin: 4px 0 6px; font-size: 13px; }
  .choices label { display: flex; align-items: center; gap: 8px; cursor: pointer; }
  .buttons { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; }
  .buttons button { height: 30px; padding: 0 14px; border: 0; border-radius: 8px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
  .ghost { background: var(--hover); color: var(--fg); }
  .danger { background: #d9453a; color: white; }
</style>
