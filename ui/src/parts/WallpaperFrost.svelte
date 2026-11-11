<script lang="ts">
  import { ui } from '../state.svelte'
  // Clear glass (SPEC.md › Window › Clear glass): behind each frosted panel, the wallpaper exactly where it sits on screen, blurred by Panel frost.
  // Under the page's own tint and grain, so the panel reads as frosted glass over the desktop. Screen pixels → page pixels via devicePixelRatio
  // Glass thickness bends the light: the desktop behind a panel is magnified a little about its middle, so it no longer lines up with what's around it, and each edge catches a thin bright line, a pale wash of the colours just beyond it
  const scale = $derived(window.devicePixelRatio || 1)
  const blur = $derived((ui.wallpaper?.frost ?? 0) * 0.4)
  const thickness = $derived(ui.glassThickness / 100) // Frost colour (below) desaturates the blurred desktop, 0 = grey
  // The picture placed for one panel, magnified about the panel's middle by `zoom`
  function placed(rect: { left: number; top: number; width: number; height: number }, zoom: number) {
    const paper = ui.wallpaper!, left = (paper.monitor.x - paper.origin.x) / scale - rect.left, top = (paper.monitor.y - paper.origin.y) / scale - rect.top, width = paper.monitor.width / scale, height = paper.monitor.height / scale
    const middleX = rect.width / 2, middleY = rect.height / 2
    return `left: ${middleX + (left - middleX) * zoom}px; top: ${middleY + (top - middleY) * zoom}px; width: ${width * zoom}px; height: ${height * zoom}px; background-image: url(${paper.image})`
  }
</script>

{#if ui.wallpaper?.image && blur > 0}
  <div class="wallpaper-frost" aria-hidden="true">
    {#each ui.frostRects as rect}
      <div class="clip" style="left: {rect.left}px; top: {rect.top}px; width: {rect.width}px; height: {rect.height}px">
        <div class="picture" style="{placed(rect, 1 + thickness * 0.1)}; filter: blur({blur}px) saturate({(1 + blur / 80) * ui.frostColour / 100})"></div>
        {#if thickness > 0}<div class="rim" style="--rim: {1 + thickness * 1.5}px; opacity: {0.45 + thickness * 0.55}"><div class="picture" style="{placed(rect, 1 + thickness * 0.3)}; filter: blur(0.6px) saturate(0.45) brightness(2.4)"></div></div>{/if}
      </div>
    {/each}
  </div>
{/if}

<style>
  .wallpaper-frost { position: fixed; inset: 0; z-index: -1; pointer-events: none; }
  .clip { position: absolute; overflow: hidden; border-radius: 8px; }
  .picture { position: absolute; background-size: 100% 100%; }
  /* The edge of the glass: a few pixels of brighter, bent light, coloured by what lies beyond the panel */
  .rim { position: absolute; inset: 0; border-radius: inherit; padding: var(--rim); mask: linear-gradient(#000 0 0) content-box exclude, linear-gradient(#000 0 0); }
</style>
