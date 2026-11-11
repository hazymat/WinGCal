<script lang="ts">
  import { onShellMessage, sendToShell } from '../bridge'
  import { ui } from '../state.svelte'
  import { prefs } from '../lib/dates'
  import Icon from '../parts/Icon.svelte'
  import EventForm from './EventForm.svelte'
  import BatchForm from './BatchForm.svelte'
  import type { EditorEvent, EditorCalendar } from './types'
  // A glass event window (SPEC.md › Event windows): ?window=event | new | batch. The shell sends the look, then what to show
  const mode = new URLSearchParams(location.search).get('window') ?? 'event'
  let glass = $state(false), payload = $state<{ events: EditorEvent[]; calendars: EditorCalendar[]; day: string; time: string } | null>(null), problem = $state(''), savedAt = $state(0)
  onShellMessage('skin', message => { ui.skin = message.skin || 'auto'; ui.glassTransparency = message.glassTransparency ?? -1; ui.glassTintHue = message.glassTintHue ?? 0 })
  onShellMessage('window', message => glass = !!message.glass)
  onShellMessage('prefs', message => { prefs.weekStart = message.weekStart ?? prefs.weekStart; if (typeof message.hour12 === 'boolean') prefs.hour12 = message.hour12 })
  onShellMessage('editor', message => payload = { events: message.events, calendars: message.calendars, day: message.day, time: message.time })
  onShellMessage('saved', () => { problem = ''; savedAt = Date.now() })
  onShellMessage('error', message => problem = message.text)
  const prefersDark = matchMedia('(prefers-color-scheme: dark)')
  let systemDark = $state(prefersDark.matches)
  prefersDark.addEventListener('change', () => systemDark = prefersDark.matches)
  $effect(() => { document.documentElement.dataset.skin = ui.skin; document.documentElement.classList.toggle('glass', glass); document.documentElement.classList.add('editor-window') })
  // The same glass tint as the calendar window (Settings › Appearance › Glass transparency and Tint colour)
  $effect(() => {
    const light = ui.skin === 'light' || (ui.skin === 'auto' && !systemDark), opacity = ui.glassTransparency < 0 ? (light ? 0.42 : 0.38) : 1 - ui.glassTransparency / 100
    const colour = ui.glassTintHue ? (light ? `hsl(${ui.glassTintHue} 45% 92%` : `hsl(${ui.glassTintHue} 40% 12%`) : (light ? 'rgb(242 245 250' : 'rgb(12 14 19')
    document.documentElement.style.setProperty('--glass-tint', `${colour} / ${opacity.toFixed(2)})`)
  })
  sendToShell({ type: 'ready' })
  const heading = $derived(mode === 'new' ? 'New event' : mode === 'batch' ? `Change ${payload?.events.length ?? ''} events` : 'Event')
  // The forms handle Esc themselves (close, or ask first when something changed)
  const formShown = $derived(!!payload && (mode !== 'event' || payload.events.length > 0))
  function drag(event: MouseEvent) { if (event.button === 0 && !(event.target as HTMLElement).closest('button, input, select, textarea, a')) sendToShell({ type: 'dragWindow' }) }
</script>
<svelte:window onkeydown={(event) => { if (event.key === 'Escape' && !formShown) sendToShell({ type: 'windowControl', action: 'close' }) }} ondragstart={(dragEvent) => { if (!(dragEvent.target as HTMLElement).closest?.('input, textarea')) dragEvent.preventDefault() }} />

<div class="frame">
  <header role="presentation" onmousedown={drag}>
    <span class="heading">{heading}</span>
    <button onclick={() => sendToShell({ type: 'windowControl', action: 'minimise' })} aria-label="Minimise"><Icon name="minimise" size={15} /></button>
    <button class="close" onclick={() => sendToShell({ type: 'windowControl', action: 'close' })} aria-label="Close"><Icon name="close" size={15} /></button>
  </header>
  {#if payload}
    {#if mode === 'batch'}<BatchForm events={payload.events} calendars={payload.calendars} bind:problem />
    {:else if mode === 'new' || payload.events.length > 0}<EventForm event={mode === 'new' ? null : payload.events[0]} calendars={payload.calendars} day={payload.day} time={payload.time} {savedAt} bind:problem />
    {:else}<p class="gone">This event isn't in your calendar any more.</p>{/if}
  {/if}
</div>

<style>
  :global(html.editor-window), :global(html.editor-window body) { background: transparent; height: 100%; margin: 0; overflow: hidden; }
  .frame { display: flex; flex-direction: column; height: 100vh; background: var(--popover); color: var(--fg); box-sizing: border-box; border-radius: 8px; }
  :global(html.editor-window.glass) .frame { background: var(--glass-tint); }
  header { display: flex; align-items: center; gap: 2px; height: 38px; padding: 0 6px 0 16px; flex: none; }
  .heading { flex: 1; font-size: 12px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; color: var(--fg-quiet); }
  header button { display: grid; place-items: center; width: 34px; height: 28px; border: 0; border-radius: 6px; background: none; color: var(--fg-quiet); cursor: pointer; }
  header button:hover { background: var(--hover); color: var(--fg); }
  header .close:hover { background: #c42b1c; color: white; }
  .gone { padding: 16px; color: var(--fg-quiet); }
</style>
