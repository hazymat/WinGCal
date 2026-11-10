<script lang="ts">
  import { ui, app, type ViewMode } from '../state.svelte'
  import { sendToShell } from '../bridge'
  import Icon from './Icon.svelte'
  // Small windows (SPEC.md › Strip): the strip keeps only the month, arrows, Today, this menu and the pin; everything else is in here
  let { views }: { views: [ViewMode, string][] } = $props()
  const skins: [string, string][] = [['auto', 'Glass'], ['dark', 'Glass dark'], ['light', 'Glass light'], ['paper', 'Paper'], ['technical', 'Technical'], ['wall', 'Wall calendar']]
  const viewNames: Record<string, string> = { auto: 'Auto', month: 'Month', week: 'Week', agenda: 'Agenda', split: 'Month and days' }
  let open = $state(false), menuRight = $state(0), menuTop = $state(0)
  function toggle(event: MouseEvent) { const box = (event.currentTarget as HTMLElement).getBoundingClientRect(); menuRight = Math.max(8, innerWidth - box.right); menuTop = box.bottom + 6; open = !open }
  function run(action: () => void) { open = false; action() }
</script>
<svelte:window onkeydown={(event) => { if (event.key === 'Escape') open = false }} />

<button class="tool" class:on={open} onclick={toggle} title="More" aria-haspopup="menu" aria-expanded={open}><Icon name="more" size={16} /></button>
{#if open}
  <div class="scrim" role="presentation" onpointerdown={() => open = false}></div>
  <div class="menu" role="menu" style="right: {menuRight}px; top: {menuTop}px">
    <div class="group">View</div>
    {#each views as [view]}<button role="menuitemradio" aria-checked={ui.view === view} onclick={() => run(() => ui.view = view)}><Icon name={view} size={15} /><span>{viewNames[view]}</span>{#if ui.view === view}<Icon name="check" size={13} />{/if}</button>{/each}
    <div class="group">Skin</div>
    {#each skins as [skin, label]}<button role="menuitemradio" aria-checked={ui.skin === skin} onclick={() => run(() => { ui.skin = skin; sendToShell({ type: 'setSkin', skin }) })}><Icon name="skin" size={15} /><span>{label}</span>{#if ui.skin === skin}<Icon name="check" size={13} />{/if}</button>{/each}
    <div class="line"></div>
    <button role="menuitem" onclick={() => run(() => ui.switcherOpen = true)}><Icon name="calendar" size={15} /><span>Calendars…</span></button>
    <button role="menuitem" onclick={() => run(() => sendToShell({ type: 'refresh' }))}><Icon name="refresh" size={15} /><span>{app.syncing ? 'Refreshing…' : 'Refresh'}</span></button>
    <button role="menuitem" onclick={() => run(() => sendToShell({ type: 'openSettings' }))}><Icon name="settings" size={15} /><span>Settings</span></button>
  </div>
{/if}

<style>
  .tool { display: grid; place-items: center; width: 30px; height: 28px; border: 0; border-radius: 8px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .tool:hover, .tool.on { background: var(--hover); color: var(--fg); }
  .scrim { position: fixed; inset: 0; z-index: 50; }
  /* Always solid, like the skin menu */
  .menu { position: fixed; z-index: 51; width: 200px; max-height: calc(100vh - 60px); overflow-y: auto; padding: 6px; border-radius: 12px; background: #23262e; color: #e8eaef; box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.1), 0 12px 32px rgba(0, 0, 0, 0.45); }
  .group { padding: 6px 8px 3px; font-size: 10.5px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; color: #9aa0ab; }
  .line { height: 1px; margin: 5px 6px; background: rgba(255, 255, 255, 0.1); }
  button[role^="menuitem"] { display: flex; align-items: center; gap: 9px; width: 100%; height: 30px; padding: 0 8px; border: 0; border-radius: 7px; background: none; color: inherit; font: inherit; font-size: 13px; text-align: left; cursor: pointer; }
  button[role^="menuitem"] span { flex: 1; }
  button[role^="menuitem"]:hover { background: rgba(255, 255, 255, 0.08); }
</style>
