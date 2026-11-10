<script lang="ts">
  import { ui } from '../state.svelte'
  // Clear glass (SPEC.md › Window › Clear glass): behind each frosted panel, the wallpaper exactly where it sits on screen, blurred by Panel frost.
  // Under the page's own tint and grain, so the panel reads as frosted glass over the desktop. Screen pixels → page pixels via devicePixelRatio
  const scale = $derived(window.devicePixelRatio || 1)
  const blur = $derived((ui.wallpaper?.frost ?? 0) * 0.4)
</script>

{#if ui.wallpaper?.image && blur > 0}
  {@const paper = ui.wallpaper}
  <div class="wallpaper-frost" aria-hidden="true">
    {#each ui.frostRects as rect}
      <div class="clip" style="left: {rect.left}px; top: {rect.top}px; width: {rect.width}px; height: {rect.height}px">
        <div class="picture" style="left: {(paper.monitor.x - paper.origin.x) / scale - rect.left}px; top: {(paper.monitor.y - paper.origin.y) / scale - rect.top}px; width: {paper.monitor.width / scale}px; height: {paper.monitor.height / scale}px; background-image: url({paper.image}); filter: blur({blur}px) saturate({1 + blur / 80})"></div>
      </div>
    {/each}
  </div>
{/if}

<style>
  .wallpaper-frost { position: fixed; inset: 0; z-index: -1; pointer-events: none; }
  .clip { position: absolute; overflow: hidden; border-radius: 8px; }
  .picture { position: absolute; background-size: 100% 100%; }
</style>
