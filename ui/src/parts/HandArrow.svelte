<script lang="ts">
  import { handLine, handHead } from '../lib/hand'
  import { sameDay, dayKey } from '../lib/dates'
  import type { Placed } from '../state.svelte'
  // Paper skin only (skins.css shows it): an all-day event drawn as a pen line under its name. One day: just the line. Several days: an arrowhead at each end, on its first and last day
  let { event, day }: { event: Placed; day: Date } = $props()
  const severalDays = $derived(event.endAt.getTime() - event.startAt.getTime() > 1.5 * 86_400_000)
  const startHead = $derived(severalDays && sameDay(day, event.startAt))
  const endHead = $derived(severalDays && sameDay(day, new Date(event.endAt.getTime() - 1)))
  const seed = $derived(event.id + dayKey(day))
</script>

<span class="hand-arrow" aria-hidden="true">
  <svg class="shaft" viewBox="0 0 100 10" preserveAspectRatio="none"><path d={handLine(seed)} /></svg>
  {#if startHead}<svg class="head start" viewBox="0 0 12 12"><path d={handHead(seed + 'start')} /></svg>{/if}
  {#if endHead}<svg class="head" viewBox="0 0 12 12"><path d={handHead(seed)} /></svg>{/if}
</span>

<style>
  .hand-arrow { display: none; position: absolute; left: 0; right: 0; bottom: 0; height: 7px; pointer-events: none; color: var(--colour); }
  .shaft { position: absolute; inset: 0; width: 100%; height: 100%; overflow: visible; }
  .head { position: absolute; right: 0; top: 50%; width: 9px; height: 9px; transform: translateY(-50%); overflow: visible; }
  .head.start { right: auto; left: 0; transform: translateY(-50%) scaleX(-1); }
  path { fill: none; stroke: currentColor; stroke-width: 1.5; stroke-linecap: round; stroke-linejoin: round; vector-effect: non-scaling-stroke; }
</style>
