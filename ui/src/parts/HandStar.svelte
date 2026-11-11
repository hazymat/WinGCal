<script lang="ts">
  import { handStar } from '../lib/hand'
  // Paper skin only (skins.css shows it): a pen star before an all-day event's name
  let { seed }: { seed: string } = $props()
</script>

<svg class="hand-star" viewBox="0 0 12 12" aria-hidden="true"><path d={handStar(seed)} /></svg>

<style>
  .hand-star { display: none; width: 10px; height: 10px; margin: 0 3px -1px 0; overflow: visible; color: var(--colour); }
  path { fill: none; stroke: currentColor; stroke-width: 1.3; stroke-linecap: round; stroke-linejoin: round; }
</style>
