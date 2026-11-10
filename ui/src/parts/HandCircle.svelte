<script lang="ts">
  import { handCircle } from '../lib/hand'
  // Paper skin only (skins.css shows it): a pen circle round the thing it sits in
  let { seed }: { seed: string } = $props()
</script>

<svg class="hand-circle" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true"><path d={handCircle(seed)} /></svg>

<style>
  .hand-circle { display: none; position: absolute; inset: -5px -7px; width: calc(100% + 14px); height: calc(100% + 10px); overflow: visible; pointer-events: none; }
  path { fill: none; stroke: currentColor; stroke-width: 1.7; stroke-linecap: round; vector-effect: non-scaling-stroke; }
</style>
