<script lang="ts">
  import { sendToShell, onShellMessage } from './bridge'
  import { watchFrostPanels } from './lib/frost'
  import { app, ui, writeSetting, placedEvents, hint, type Placed, type ViewMode } from './state.svelte'
  import { addDays, addMonths, monthName, startOfDay, startOfWeek, prefs, shortDate } from './lib/dates'
  import { sampleAccounts, sampleCalendars, sampleEvents, sampleTasks } from './lib/sample'
  import MonthView from './views/MonthView.svelte'
  import WeekView from './views/WeekView.svelte'
  import AgendaView from './views/AgendaView.svelte'
  import SplitView from './views/SplitView.svelte'
  import EventCard from './parts/EventCard.svelte'
  import CalendarSwitcher from './parts/CalendarSwitcher.svelte'
  import ResizeEdges from './parts/ResizeEdges.svelte'
  import Icon from './parts/Icon.svelte'
  import TasksPanel from './parts/TasksPanel.svelte'
  import WhatsNew from './parts/WhatsNew.svelte'

  // ---- From the shell ----
  onShellMessage('window', message => app.window = { glass: !!message.glass, clear: !!message.clear, frameless: !!message.frameless, maximised: !!message.maximised, pinned: !!message.pinned })
  onShellMessage('skin', message => {
    ui.skin = message.skin || 'auto'; ui.accent = message.accent ?? ''; ui.glassTransparency = message.glassTransparency ?? -1; ui.glassTintHue = message.glassTintHue ?? 0; ui.windowTint = message.windowTint ?? ui.windowTint; ui.panelTint = message.panelTint ?? ui.panelTint; ui.lookVersion++
    writeSetting('windowTint', String(ui.windowTint)); writeSetting('panelTint', String(ui.panelTint))
    writeSetting('skin', ui.skin); writeSetting('accent', ui.accent); writeSetting('glassTransparency', String(ui.glassTransparency)); writeSetting('glassTintHue', String(ui.glassTintHue))
  })
  onShellMessage('calendars', message => { app.accounts = message.accounts; app.calendars = message.calendars; app.sampleData = !!message.sample })
  onShellMessage('events', message => app.events = message.events)
  onShellMessage('sync', message => { app.syncing = !!message.syncing; app.offline = !!message.offline })
  onShellMessage('prefs', message => { prefs.weekStart = message.weekStart ?? prefs.weekStart; if (typeof message.hour12 === 'boolean') prefs.hour12 = message.hour12; ui.lookVersion++ })
  onShellMessage('update', message => app.updateReady = message.version ?? '')
  onShellMessage('tasks', message => app.tasks = { enabled: !!message.enabled, lists: message.lists ?? [], items: message.items ?? [] })
  onShellMessage('tip', message => ui.tip = { title: message.title ?? '', text: message.text ?? '' })
  sendToShell({ type: 'ready' })
  watchFrostPanels()
  // Clear glass: how much tint the window and the panels get (0-100), from Settings › Appearance
  $effect(() => { const rootStyle = document.documentElement.style; rootStyle.setProperty('--clear-window-tint', String(ui.windowTint / 100)); rootStyle.setProperty('--clear-panel-tint', String(ui.panelTint / 100)) })

  // Outside WebView2 (vite dev, mockups): invented calendars
  const params = new URLSearchParams(location.search)
  if (!(window as any).chrome?.webview || params.has('sample')) {
    app.accounts = sampleAccounts; app.calendars = sampleCalendars; app.events = sampleEvents(); app.sampleData = true
    app.window.glass = params.has('glass'); app.window.clear = params.has('clear'); app.window.pinned = params.get('pinned') !== '0'
    if (params.get('panelTint')) ui.panelTint = Number(params.get('panelTint')); if (params.get('windowTint')) ui.windowTint = Number(params.get('windowTint'))
    if (params.get('skin')) ui.skin = params.get('skin')!
    if (params.get('view')) ui.view = params.get('view') as ViewMode
    if (params.has('tasks')) app.tasks = sampleTasks()
  }

  // ---- Look (from WinSqueeze): skin, accent, glass tint ----
  // Glass belongs to Auto, Dark and Light; Paper, Technical and Wall calendar are opaque
  const glassSkin = $derived(['auto', 'dark', 'light'].includes(ui.skin))
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)')
  let systemDark = $state(prefersDark.matches)
  prefersDark.addEventListener('change', () => systemDark = prefersDark.matches)
  $effect(() => { document.documentElement.dataset.skin = ui.skin; sendToShell({ type: 'theme', dark: ui.skin === 'auto' ? systemDark : ui.skin === 'dark' || ui.skin === 'technical' }) })
  $effect(() => {
    const rootStyle = document.documentElement.style, accent = ui.accent
    if (!accent) { for (const name of ['--accent', '--accent-soft', '--on-accent']) rootStyle.removeProperty(name); return }
    const channels = [1, 3, 5].map(start => parseInt(accent.slice(start, start + 2), 16))
    rootStyle.setProperty('--accent', `rgb(${channels.join(', ')})`); rootStyle.setProperty('--accent-soft', `rgba(${channels.join(', ')}, 0.15)`)
    rootStyle.setProperty('--on-accent', 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2] > 150 ? '#06141c' : '#ffffff')
  })
  $effect(() => {
    const rootStyle = document.documentElement.style, light = ui.skin === 'light' || (ui.skin === 'auto' && !systemDark)
    ui.lookVersion
    const defaultTint = ui.glassTransparency < 0 && !ui.glassTintHue
    const opacity = ui.glassTransparency < 0 ? (light ? 0.42 : 0.38) : 1 - ui.glassTransparency / 100
    const colour = ui.glassTintHue ? (light ? `hsl(${ui.glassTintHue} 45% 92%` : `hsl(${ui.glassTintHue} 40% 12%`) : (light ? 'rgb(242 245 250' : 'rgb(12 14 19')
    // A nudge then the real value next frame forces a fresh paint of the glass
    rootStyle.setProperty('--glass-tint', `${colour} / ${(opacity + 0.004).toFixed(3)})`)
    requestAnimationFrame(() => { if (defaultTint) { rootStyle.removeProperty('--glass-tint'); return } rootStyle.setProperty('--glass-tint', `${colour} / ${opacity.toFixed(2)})`) })
  })
  $effect(() => { ui.skin; ui.glassTransparency; ui.glassTintHue; ui.lookVersion; systemDark; app.window.glass; app.window.frameless; requestAnimationFrame(() => sendToShell({ type: 'edgeColour', colour: getComputedStyle(document.body).backgroundColor })) })
  $effect(() => { document.documentElement.classList.toggle('glass', app.window.glass && glassSkin); document.documentElement.classList.toggle('clear', app.window.glass && app.window.clear && glassSkin); document.documentElement.classList.toggle('frameless', app.window.frameless); document.documentElement.classList.toggle('maximised', app.window.maximised) })

  // Frameless: drag from empty parts of the title strip (unpinned only); double-click maximises
  function dragWindow(event: MouseEvent) {
    if (!app.window.frameless || event.button !== 0 || (event.target as HTMLElement).closest('button, input, select, a, .no-drag')) return
    if (app.window.pinned) { hint('Pinned in place. Unpin to move or resize'); return }
    if (event.detail === 2) { sendToShell({ type: 'windowControl', action: 'maximise' }); return }
    sendToShell({ type: 'dragWindow' })
  }
  function togglePin() { app.window.pinned = !app.window.pinned; sendToShell({ type: 'setPinned', on: app.window.pinned }); hint(app.window.pinned ? 'Pinned: stays on the desktop, even after Win+D' : 'Unpinned: move and resize it like any window') }

  // ---- Layout follows the window (SPEC.md › Calendar views) ----
  let width = $state(innerWidth), height = $state(innerHeight)
  // Short and wide (about 1.6 to 3.2 times as wide as tall) gets the split month + day list
  const autoView = $derived(width < 460 ? 'agenda' : height < 560 && width / height >= 1.6 && width / height <= 3.2 ? 'split' : height < 430 ? 'week' : 'month')
  const shownView = $derived(ui.view === 'auto' ? autoView : ui.view)
  $effect(() => writeSetting('view', ui.view))
  const narrow = $derived(width < 560)
  const events = $derived(placedEvents())
  const shortMonth = (date: Date) => date.toLocaleDateString(undefined, { month: 'short' })
  // Shorter as the window narrows: "November 2026", "Nov 2026", "Nov"
  const heading = $derived(width < 300 ? shortMonth(ui.focus) : width < 460 ? `${shortMonth(ui.focus)} ${ui.focus.getFullYear()}` : shownView === 'week' && width >= 560 ? weekHeading(startOfWeek(ui.focus, prefs.weekStart)) : `${monthName(ui.focus)} ${ui.focus.getFullYear()}`)
  function weekHeading(weekStart: Date) { const weekEnd = addDays(weekStart, 6); return weekStart.getMonth() === weekEnd.getMonth() ? `${monthName(weekStart)} ${weekStart.getFullYear()}` : `${shortDate(weekStart)} – ${shortDate(weekEnd)}` }
  function step(direction: number) {
    if (shownView === 'month' || shownView === 'split') ui.focus = addMonths(ui.focus, direction)
    else if (shownView === 'week') ui.focus = addDays(ui.focus, direction * (width >= 560 ? 7 : width >= 360 ? 3 : 1))
    else ui.focus = addDays(ui.focus, direction * 7)
  }
  const isToday = $derived(startOfDay(new Date()).getTime() === ui.focus.getTime())
  const views: [ViewMode, string][] = [['auto', 'Auto: picks by window size'], ['month', 'Month'], ['week', 'Week'], ['agenda', 'Agenda'], ['split', 'Month and days']]
  // Tasks: a column beside the calendar when there's room, otherwise a panel over it
  const tasksColumn = $derived(app.tasks.enabled && ui.tasksOpen && width >= 760)
  let tasksOverlay = $state(false)
  function toggleTasks() { if (width >= 760) { ui.tasksOpen = !ui.tasksOpen; writeSetting('tasksOpen', ui.tasksOpen ? '1' : '0') } else tasksOverlay = !tasksOverlay }
  const shownCalendars = $derived(app.calendars.filter(calendar => calendar.shown))
  function pick(event: Placed, target: HTMLElement) { const box = target.getBoundingClientRect(); ui.selectedEvent = { event, left: box.right + 6 > innerWidth - 340 ? box.left - 346 : box.right + 6, top: box.top - 10 } }
  function onKey(event: KeyboardEvent) {
    if ((event.target as HTMLElement).closest('input, textarea')) return
    if (event.key === 'Escape') { ui.selectedEvent = null; ui.switcherOpen = false; tasksOverlay = false }
    else if (event.key === 'ArrowLeft' || event.key === 'PageUp') step(-1)
    else if (event.key === 'ArrowRight' || event.key === 'PageDown') step(1)
    else if (event.key === 't') ui.focus = startOfDay(new Date())
    else if (event.key === 'm') ui.view = 'month'
    else if (event.key === 'w') ui.view = 'week'
    else if (event.key === 'a') ui.view = 'agenda'
  }
</script>

<svelte:window bind:innerWidth={width} bind:innerHeight={height} onkeydown={onKey} />

<div class="shell" class:pinned={app.window.pinned}>
  {#if app.window.frameless && !app.window.pinned && !app.window.maximised}<ResizeEdges />{/if}
  <header class="strip" role="presentation" onmousedown={dragWindow} data-frost>
    <div class="heading">
      <button class="today" class:on={isToday} onclick={() => ui.focus = startOfDay(new Date())} title="Back to today (T)">{heading}</button>
      {#if width >= 320}
        <button class="step" onclick={() => step(-1)} aria-label="Previous"><Icon name="previous" size={16} /></button>
        <button class="step" onclick={() => step(1)} aria-label="Next"><Icon name="next" size={16} /></button>
      {/if}
    </div>
    <div class="tools">
      {#if app.offline}<span class="offline" title="Can't reach Google: showing what was saved last time"><Icon name="offline" size={15} />{narrow ? '' : 'Offline'}</span>{/if}
      {#if !narrow}
        <div class="views" role="group" aria-label="View">
          {#each views as [view, label]}<button class:on={ui.view === view} onclick={() => ui.view = view} title={label} aria-pressed={ui.view === view}><Icon name={view} size={16} /></button>{/each}
        </div>
      {/if}
      <button class="dots" class:on={ui.switcherOpen} onclick={() => ui.switcherOpen = !ui.switcherOpen} title="Calendars">{#each shownCalendars.slice(0, width < 320 ? 3 : 5) as calendar}<i style="background: {calendar.colour}"></i>{/each}{#if shownCalendars.length === 0}<Icon name="calendar" size={16} />{/if}</button>
      {#if app.tasks.enabled}<button class="tool" class:on={tasksColumn || tasksOverlay} onclick={toggleTasks} title="Tasks"><Icon name="tasks" size={16} /></button>{/if}
      {#if width >= 400}<button class="tool" class:spinning={app.syncing} onclick={() => sendToShell({ type: 'refresh' })} title="Refresh from Google"><Icon name="refresh" size={16} /></button>{/if}
      <button class="tool" onclick={() => sendToShell({ type: 'openSettings' })} title="Settings"><Icon name="settings" size={16} /></button>
      <button class="tool pin" class:on={app.window.pinned} onclick={togglePin} aria-pressed={app.window.pinned} title={app.window.pinned ? 'Pinned to the desktop (click to unpin and move)' : 'Pin to the desktop'}><Icon name="pin" size={15} /></button>
      {#if app.window.frameless && !app.window.pinned}
        <div class="window-controls">
          <button onclick={() => sendToShell({ type: 'windowControl', action: 'minimise' })} aria-label="Minimise"><Icon name="minimise" size={16} /></button>
          <button class="close" onclick={() => sendToShell({ type: 'windowControl', action: 'close' })} aria-label="Close"><Icon name="close" size={16} /></button>
        </div>
      {/if}
    </div>
  </header>
  {#each app.accounts.filter(account => account.error) as account (account.email)}<div class="notice problem" data-frost><span><b>Couldn't read {account.email}.</b> {account.error}</span><button class="ghost" onclick={() => sendToShell({ type: 'refresh' })}>Try again</button></div>{/each}
  {#if ui.tip}<div class="notice" data-frost><span><b>{ui.tip.title}</b> {ui.tip.text}</span><button class="ghost" onclick={() => ui.tip = null}>OK</button></div>{/if}
  {#if app.updateReady}<div class="notice" data-frost><span>WinGCal {app.updateReady} is ready.</span><button class="primary" onclick={() => sendToShell({ type: 'applyUpdate' })}>Restart to update</button></div>{/if}
  <div class="body">
  <main class="view" data-frost>
    {#if app.accounts.length === 0}
      <div class="welcome">
        <Icon name="calendar" size={40} />
        <h2>Your Google calendars, on the desktop</h2>
        <button class="primary" onclick={() => sendToShell({ type: 'addAccount' })}>Sign in with Google</button>
        <span class="quiet">Add more accounts later in Settings</span>
      </div>
    {:else if shownView === 'month'}<MonthView {events} onpick={pick} />
    {:else if shownView === 'week'}<WeekView {events} onpick={pick} />
    {:else if shownView === 'split'}<SplitView {events} onpick={pick} />
    {:else}<AgendaView {events} onpick={pick} />{/if}
  </main>
  {#if tasksColumn}<div class="tasks-column" data-frost><TasksPanel onclose={toggleTasks} /></div>{/if}
  </div>
  {#if tasksOverlay && !tasksColumn && app.tasks.enabled}<TasksPanel overlay onclose={() => tasksOverlay = false} />{/if}
  {#if ui.hint}<div class="hint" role="status">{#if app.window.pinned}<Icon name="lock" size={14} />{/if}{ui.hint}</div>{/if}
  {#if ui.switcherOpen}<CalendarSwitcher />{/if}
  {#if ui.selectedEvent}<EventCard />{/if}
</div>

<WhatsNew />

<style>
  .shell { position: relative; display: flex; flex-direction: column; height: 100vh; }
  .strip { display: flex; align-items: center; gap: 8px; padding: 6px 6px 6px 12px; min-height: 36px; -webkit-user-select: none; user-select: none; }
  .heading { flex: 1; min-width: 0; display: flex; align-items: center; gap: 2px; }
  .today { min-width: 0; padding: 2px 8px; margin-right: 4px; border: 0; border-radius: 8px; background: none; color: var(--fg); font: inherit; font-size: 18px; font-weight: 650; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; cursor: pointer; letter-spacing: -0.01em; }
  .today:hover { background: var(--hover); }
  .step, .tool, .window-controls button { display: grid; place-items: center; width: 32px; height: 30px; flex: none; border: 0; border-radius: 7px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .step:hover, .tool:hover, .window-controls button:hover { background: var(--hover); color: var(--fg); }
  .tools { display: flex; align-items: center; gap: 2px; flex: none; }
  .views { display: flex; padding: 2px; margin-right: 6px; border-radius: 9px; background: var(--panel); }
  .views button { display: grid; place-items: center; width: 30px; height: 26px; border: 0; border-radius: 7px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .views button:hover { color: var(--fg); }
  .views button.on { background: var(--panel-strong); color: var(--fg); box-shadow: 0 1px 2px var(--shadow-soft); }
  .dots { display: flex; align-items: center; gap: 3px; height: 30px; padding: 0 9px; margin-right: 2px; border: 0; border-radius: 15px; background: var(--panel); color: var(--fg-quiet); cursor: pointer; }
  .dots:hover, .dots.on { background: var(--panel-strong); }
  .dots i { width: 9px; height: 9px; border-radius: 50%; box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.12); }
  .spinning :global(svg) { animation: spin 1s linear infinite; }
  @keyframes spin { to { transform: rotate(360deg); } }
  /* The WinSqueeze pin: tilted and quiet when off, upright and lit in the accent when pinned */
  .pin { transform: rotate(45deg); opacity: 0.6; transition: opacity 120ms, transform 120ms, background 120ms; }
  .pin.on { transform: none; opacity: 1; color: var(--accent); background: var(--accent-soft); }
  .window-controls { display: flex; gap: 2px; margin-left: 4px; }
  .window-controls button { width: 36px; }
  .window-controls .close:hover { background: #c42b1c; color: #fff; }
  .offline { display: flex; align-items: center; gap: 5px; margin-right: 8px; font-size: 12px; color: var(--warn); }
  .notice { display: flex; align-items: center; gap: 10px; margin: 0 8px 6px; padding: 6px 10px; border-radius: 10px; background: var(--accent-soft); font-size: 13px; }
  .notice span { flex: 1; }
  .notice.problem { background: color-mix(in srgb, var(--danger) 18%, transparent); }
  .body { flex: 1; min-height: 0; display: flex; }
  .view { flex: 1; min-width: 0; min-height: 0; }
  .tasks-column { flex: none; width: clamp(240px, 26vw, 320px); padding-bottom: 6px; }
  .tool.on { color: var(--accent); background: var(--accent-soft); }
  .welcome { height: 100%; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px; padding: 20px; text-align: center; color: var(--fg-quiet); }
  .welcome h2 { color: var(--fg); font-size: 18px; margin: 4px 0; }
  .welcome .primary { padding: 8px 18px; font-size: 14px; }
  .hint { position: fixed; top: 46px; right: 12px; z-index: 60; display: flex; align-items: center; gap: 7px; padding: 7px 12px; border-radius: 9px; background: var(--popover); backdrop-filter: blur(24px); border: 1px solid var(--line); box-shadow: 0 8px 24px var(--shadow); font-size: 12.5px; animation: hint-in 160ms ease-out; }
  @keyframes hint-in { from { opacity: 0; transform: translateY(-4px); } }
</style>
