<script lang="ts" module>
  export type IconName = 'pin' | 'settings' | 'minimise' | 'maximise' | 'restore' | 'close' | 'previous' | 'next' | 'refresh' | 'plus' | 'month' | 'week' | 'agenda' | 'split' | 'auto' | 'location' | 'video' | 'repeat' | 'clock' | 'calendar' | 'edit' | 'trash' | 'lock' | 'offline' | 'tasks' | 'check' | 'skin' | 'copy' | 'more' | 'up' | 'down'
</script>
<script lang="ts">
  // SVG icons: text glyphs render off-centre with colour fringes in WebView2
  let { name, size = 18 }: { name: IconName; size?: number } = $props()
</script>

<svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" class="icon">
  {#if name === 'pin'}<path d="M9 4h6l-1 6 3 3H7l3-3zM12 13v7" class="stroke" />
  {:else if name === 'settings'}<circle cx="12" cy="12" r="3" class="stroke" /><path d="M12 3.5v2.2M12 18.3v2.2M3.5 12h2.2M18.3 12h2.2M6 6l1.6 1.6M16.4 16.4L18 18M6 18l1.6-1.6M16.4 7.6L18 6" class="stroke" />
  {:else if name === 'minimise'}<path d="M6 12h12" class="stroke thin" />
  {:else if name === 'maximise'}<rect x="6.5" y="6.5" width="11" height="11" rx="1.5" class="stroke thin" />
  {:else if name === 'restore'}<rect x="6" y="8.5" width="9.5" height="9.5" rx="1.5" class="stroke thin" /><path d="M9 8.5V7.5A1.5 1.5 0 0 1 10.5 6h6A1.5 1.5 0 0 1 18 7.5v6a1.5 1.5 0 0 1-1.5 1.5h-1" class="stroke thin" />
  {:else if name === 'close'}<path d="M7 7l10 10M17 7L7 17" class="stroke thin" />
  {:else if name === 'previous'}<path d="M14.5 6l-6 6 6 6" class="stroke" />
  {:else if name === 'next'}<path d="M9.5 6l6 6-6 6" class="stroke" />
  {:else if name === 'up'}<path d="M6 14.5l6-6 6 6" class="stroke" />
  {:else if name === 'down'}<path d="M6 9.5l6 6 6-6" class="stroke" />
  {:else if name === 'refresh'}<path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4.5v3.2h-3.2" class="stroke" />
  {:else if name === 'tasks'}<path d="M4.5 7l1.6 1.6L9 5.7M4.5 13.5l1.6 1.6L9 12.2M12 7h7.5M12 13.5h7.5M12 19h5" class="stroke" />
  {:else if name === 'check'}<path d="M6 12.5l4 4 8-9" class="stroke" />
  {:else if name === 'plus'}<path d="M12 5v14M5 12h14" class="stroke" />
  {:else if name === 'month'}<rect x="4" y="5" width="16" height="15" rx="2" class="stroke" /><path d="M4 9.5h16M9.3 9.5V20M14.7 9.5V20M4 14.7h16" class="stroke thin" />
  {:else if name === 'week'}<rect x="4" y="5" width="16" height="15" rx="2" class="stroke" /><path d="M4 9.5h16M8 9.5V20M12 9.5V20M16 9.5V20" class="stroke thin" />
  {:else if name === 'skin'}<path d="M12 4.5a7.5 7.5 0 1 0 0 15c1.2 0 1.6-.9 1.2-1.8-.5-1-.1-2.2 1.2-2.2h1.6a3.5 3.5 0 0 0 3.5-3.5c0-4.2-3.4-7.5-7.5-7.5z" class="stroke" /><circle cx="8.2" cy="11" r="1.1" /><circle cx="11" cy="8" r="1.1" /><circle cx="15" cy="8.6" r="1.1" />
  {:else if name === 'split'}<rect x="3.5" y="5" width="17" height="14" rx="2.5" class="stroke" /><path d="M11 5v14M5.8 9h2.9M5.8 12h2.9M5.8 15h2.9M13.5 9.5h4.5M13.5 13h3" class="stroke" />
  {:else if name === 'agenda'}<path d="M5 6.5h2M10 6.5h9M5 12h2M10 12h9M5 17.5h2M10 17.5h9" class="stroke" />
  {:else if name === 'auto'}<rect x="4" y="5" width="16" height="14" rx="2" class="stroke" /><path d="M8.5 15.5l3.5-7 3.5 7M9.8 13h4.4" class="stroke thin" />
  {:else if name === 'location'}<path d="M12 21s-6.5-6.2-6.5-11a6.5 6.5 0 0 1 13 0c0 4.8-6.5 11-6.5 11z" class="stroke" /><circle cx="12" cy="10" r="2.2" class="stroke" />
  {:else if name === 'video'}<rect x="3.5" y="7" width="12" height="10" rx="2" class="stroke" /><path d="M15.5 10.5l5-3v9l-5-3" class="stroke" />
  {:else if name === 'repeat'}<path d="M5 11V9.5A2.5 2.5 0 0 1 7.5 7H18M15.5 4.5L18 7l-2.5 2.5M19 13v1.5a2.5 2.5 0 0 1-2.5 2.5H6M8.5 19.5L6 17l2.5-2.5" class="stroke" />
  {:else if name === 'clock'}<circle cx="12" cy="12" r="8" class="stroke" /><path d="M12 7.5V12l3 2" class="stroke" />
  {:else if name === 'calendar'}<rect x="4" y="5.5" width="16" height="14.5" rx="2" class="stroke" /><path d="M4 10h16M8.5 3.5v4M15.5 3.5v4" class="stroke" />
  {:else if name === 'edit'}<path d="M5 19l1-4.5L15.5 5l3.5 3.5L9.5 18z M13.5 7l3.5 3.5" class="stroke" />
  {:else if name === 'more'}<circle cx="6" cy="12" r="1.6" /><circle cx="12" cy="12" r="1.6" /><circle cx="18" cy="12" r="1.6" />
  {:else if name === 'copy'}<rect x="8.5" y="8.5" width="11" height="11" rx="2" class="stroke" /><path d="M15.5 8.5V6a1.5 1.5 0 0 0-1.5-1.5H6A1.5 1.5 0 0 0 4.5 6v8A1.5 1.5 0 0 0 6 15.5h2.5" class="stroke" />
  {:else if name === 'trash'}<path d="M5 7h14M10 7V5h4v2M7 7l1 12.5h8L17 7" class="stroke" />
  {:else if name === 'lock'}<rect x="6" y="10.5" width="12" height="9" rx="2" class="stroke" /><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5" class="stroke" />
  {:else if name === 'offline'}<path d="M4 9.5a12 12 0 0 1 16 0M7 13a7.5 7.5 0 0 1 10 0M10 16.4a3 3 0 0 1 4 0M4.5 4.5l15 15" class="stroke" />{/if}
</svg>

<style>
  .icon { display: block; fill: currentColor; flex: none; }
  .thin { stroke-width: 1.3; }
  .stroke { fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
</style>
