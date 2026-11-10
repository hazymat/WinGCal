<script lang="ts">
  import { handLine, handHead } from '../lib/hand'
  // Paper skin only (skins.css shows it): an all-day event drawn as a pen line under its name, with an arrowhead on its last day
  let { seed, head = true }: { seed: string; head?: boolean } = $props()
</script>

<span class="hand-arrow" aria-hidden="true">
  <svg class="shaft" viewBox="0 0 100 10" preserveAspectRatio="none"><path d={handLine(seed)} /></svg>
  {#if head}<svg class="head" viewBox="0 0 12 12"><path d={handHead(seed)} /></svg>{/if}
</span>

<style>
  .hand-arrow { display: none; position: absolute; left: 0; right: 0; bottom: 0; height: 7px; pointer-events: none; color: var(--colour); }
  .shaft { position: absolute; inset: 0; width: 100%; height: 100%; overflow: visible; }
  .head { position: absolute; right: 0; top: 50%; width: 9px; height: 9px; transform: translateY(-50%); overflow: visible; }
  path { fill: none; stroke: currentColor; stroke-width: 1.5; stroke-linecap: round; stroke-linejoin: round; vector-effect: non-scaling-stroke; }
</style>
