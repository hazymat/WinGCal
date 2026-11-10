<script lang="ts">
  import { ui } from '../state.svelte'
  import { sendToShell } from '../bridge'
  import Icon from './Icon.svelte'
  // Next to the view switcher: a little dropdown of the skins, each name written in its own skin's look
  const skins: [string, string][] = [['auto', 'Glass'], ['dark', 'Glass dark'], ['light', 'Glass light'], ['paper', 'Paper'], ['technical', 'Technical'], ['wall', 'Wall calendar']]
  let open = $state(false), menuLeft = $state(0), menuTop = $state(0)
  function toggle(event: MouseEvent) { const box = (event.currentTarget as HTMLElement).getBoundingClientRect(); menuLeft = Math.max(8, Math.min(box.left, innerWidth - 198)); menuTop = box.bottom + 6; open = !open }
  function choose(skin: string) { open = false; ui.skin = skin; sendToShell({ type: 'setSkin', skin }) }
</script>
<svelte:window onkeydown={(event) => { if (event.key === 'Escape') open = false }} />

<button class="tool" class:on={open} onclick={toggle} title="Skin" aria-haspopup="menu" aria-expanded={open}><Icon name="skin" size={16} /></button>
{#if open}
  <div class="scrim" role="presentation" onpointerdown={() => open = false}></div>
  <div class="menu" role="menu" style="left: {menuLeft}px; top: {menuTop}px">
    {#each skins as [skin, label]}
      <button class="sample {skin}" class:chosen={ui.skin === skin} role="menuitemradio" aria-checked={ui.skin === skin} onclick={() => choose(skin)}><span>{label}</span>{#if ui.skin === skin}<Icon name="check" size={13} />{/if}</button>
    {/each}
  </div>
{/if}

<style>
  .tool { display: grid; place-items: center; width: 30px; height: 28px; border: 0; border-radius: 8px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .tool:hover, .tool.on { background: var(--hover); color: var(--fg); }
  .scrim { position: fixed; inset: 0; z-index: 50; }
  .menu { position: fixed; z-index: 51; width: 190px; display: flex; flex-direction: column; gap: 4px; padding: 6px; border-radius: 12px; background: var(--panel-solid); box-shadow: inset 0 0 0 1px var(--line), 0 12px 32px var(--shadow); }
  .sample { display: flex; align-items: center; justify-content: space-between; height: 36px; padding: 0 12px; border: 0; border-radius: 8px; cursor: pointer; font-size: 14px; text-align: left; box-shadow: inset 0 0 0 1px rgba(128, 128, 128, 0.25); }
  .sample:hover { filter: brightness(1.08); }
  .sample.chosen { box-shadow: inset 0 0 0 2px var(--accent); }
  /* Each name in its skin's own colours and lettering */
  .auto { background: linear-gradient(135deg, rgba(60, 90, 140, 0.55), rgba(30, 34, 44, 0.75)); color: #eef2f8; font-family: "Inter Variable", system-ui, sans-serif; font-weight: 600; }
  .dark { background: rgba(22, 24, 30, 0.9); color: #e8eaef; font-family: "Inter Variable", system-ui, sans-serif; font-weight: 600; }
  .light { background: rgba(246, 248, 252, 0.95); color: #1d2230; font-family: "Inter Variable", system-ui, sans-serif; font-weight: 600; }
  .paper { background: #fbfaf6 repeating-linear-gradient(transparent 0 11px, rgba(70, 110, 190, 0.18) 11px 12px); color: #2b3a67; font-family: "Caveat", cursive; font-size: 20px; font-weight: 600; }
  .technical { background: #1b1f24; background-image: linear-gradient(rgba(120, 170, 220, 0.12) 1px, transparent 1px), linear-gradient(90deg, rgba(120, 170, 220, 0.12) 1px, transparent 1px); background-size: 8px 8px; color: #f2b24a; font-family: "JetBrains Mono Variable", monospace; font-size: 12.5px; letter-spacing: 0.04em; text-transform: uppercase; }
  .wall { background: #efe4cc; color: #3b2e22; font-family: "Fraunces Variable", Georgia, serif; font-size: 15px; font-weight: 600; }
</style>
