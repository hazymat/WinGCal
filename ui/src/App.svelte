<script lang="ts">
  import { sendToShell, onShellMessage } from './bridge'
  import { watchFrostPanels } from './lib/frost'
  import { app, ui, writeSetting, placedEvents, hint, type Placed, type ViewMode } from './state.svelte'
  import { addDays, addMonths, monthName, startOfDay, startOfWeek, prefs, shortDate, parseWhen, dayKey, dayMs } from './lib/dates'
  import { carry } from './lib/carry.svelte'
  import { sampleAccounts, sampleCalendars, sampleEvents, sampleTasks } from './lib/sample'
  import MonthView from './views/MonthView.svelte'
  import WeekView from './views/WeekView.svelte'
  import AgendaView from './views/AgendaView.svelte'
  import SplitView from './views/SplitView.svelte'
  import CalendarSwitcher from './parts/CalendarSwitcher.svelte'
  import ResizeEdges from './parts/ResizeEdges.svelte'
  import Icon from './parts/Icon.svelte'
  import TasksPanel from './parts/TasksPanel.svelte'
  import WhatsNew from './parts/WhatsNew.svelte'
  import SkinPicker from './parts/SkinPicker.svelte'
  import SelectionBar from './parts/SelectionBar.svelte'
  import UndoBar from './parts/UndoBar.svelte'
  import WallpaperFrost from './parts/WallpaperFrost.svelte'
  import MoreMenu from './parts/MoreMenu.svelte'
  import ConfirmDelete from './parts/ConfirmDelete.svelte'

  // ---- From the shell ----
  onShellMessage('window', message => app.window = { glass: !!message.glass, clear: !!message.clear, frameless: !!message.frameless, maximised: !!message.maximised, pinned: !!message.pinned })
  onShellMessage('skin', message => {
    ui.skin = message.skin || 'auto'; ui.accent = message.accent ?? ''; ui.glassTransparency = message.glassTransparency ?? -1; ui.glassTintHue = message.glassTintHue ?? 0; ui.windowTint = message.windowTint ?? ui.windowTint; ui.panelGrain = message.panelGrain ?? ui.panelGrain; ui.panelTint = message.panelTint ?? ui.panelTint; ui.eventTint = message.eventTint ?? ui.eventTint; ui.glassThickness = message.glassThickness ?? ui.glassThickness; ui.frostColour = message.frostColour ?? ui.frostColour; ui.lookVersion++
    writeSetting('windowTint', String(ui.windowTint)); writeSetting('panelTint', String(ui.panelTint)); writeSetting('panelGrain', String(ui.panelGrain)); writeSetting('eventTint', String(ui.eventTint)); writeSetting('glassThickness', String(ui.glassThickness)); writeSetting('frostColour', String(ui.frostColour))
    writeSetting('skin', ui.skin); writeSetting('accent', ui.accent); writeSetting('glassTransparency', String(ui.glassTransparency)); writeSetting('glassTintHue', String(ui.glassTintHue))
  })
  // Clear glass frost drawn from the wallpaper: the picture comes once, then only where the window sits on its monitor
  onShellMessage('wallpaper', message => { if (message.off) { ui.wallpaper = null; return } ui.wallpaper = { image: message.image ?? ui.wallpaper?.image ?? '', monitor: message.monitor, origin: message.origin, frost: message.frost } })
  onShellMessage('calendars', message => { app.accounts = message.accounts; app.calendars = message.calendars; app.sampleData = !!message.sample })
  onShellMessage('events', message => app.events = message.events)
  onShellMessage('sync', message => { app.syncing = !!message.syncing; app.offline = !!message.offline })
  onShellMessage('prefs', message => { prefs.weekStart = message.weekStart ?? prefs.weekStart; prefs.holdMs = message.holdMs ?? prefs.holdMs; prefs.smoothWheel = message.smoothWheel ?? prefs.smoothWheel; prefs.stripLight = message.stripLight ?? prefs.stripLight; if (typeof message.hour12 === 'boolean') prefs.hour12 = message.hour12; ui.lookVersion++ })
  onShellMessage('update', message => app.updateReady = message.version ?? '')
  onShellMessage('tasks', message => app.tasks = { enabled: !!message.enabled, lists: message.lists ?? [], items: message.items ?? [] })
  onShellMessage('tip', message => ui.tip = { title: message.title ?? '', text: message.text ?? '' })
  sendToShell({ type: 'ready' })
  watchFrostPanels()
  // Ctrl+wheel scales the whole window (Settings › Appearance › Scale), one 5% step per notch; nothing scrolls meanwhile
  addEventListener('wheel', (wheelEvent) => { if (!wheelEvent.ctrlKey) return; wheelEvent.preventDefault(); wheelEvent.stopImmediatePropagation(); if (wheelEvent.deltaY) sendToShell({ type: 'scaleStep', steps: wheelEvent.deltaY < 0 ? 1 : -1 }) }, { capture: true, passive: false })
  // Clear glass: how much tint the window and the panels get (0-100), from Settings › Appearance
  $effect(() => { const rootStyle = document.documentElement.style; rootStyle.setProperty('--clear-window-tint', String(ui.windowTint / 100)); rootStyle.setProperty('--clear-panel-tint', String(ui.panelTint / 100)); rootStyle.setProperty('--clear-grain', String(ui.panelGrain / 100 * 0.6)); rootStyle.setProperty('--event-tint', `${ui.eventTint}%`) })

  // Outside WebView2 (vite dev, mockups): invented calendars
  const params = new URLSearchParams(location.search)
  if (!(window as any).chrome?.webview || params.has('sample')) {
    app.accounts = sampleAccounts; app.calendars = sampleCalendars; app.events = sampleEvents(); app.sampleData = true
    app.window.glass = params.has('glass'); app.window.clear = params.has('clear'); app.window.pinned = params.get('pinned') !== '0'
    if (params.get('panelTint')) ui.panelTint = Number(params.get('panelTint')); if (params.get('windowTint')) ui.windowTint = Number(params.get('windowTint')); if (params.get('panelGrain')) ui.panelGrain = Number(params.get('panelGrain')); if (params.get('eventTint')) ui.eventTint = Number(params.get('eventTint')); if (params.get('thickness')) ui.glassThickness = Number(params.get('thickness')); if (params.get('frostColour')) ui.frostColour = Number(params.get('frostColour'))
    if (params.get('skin')) ui.skin = params.get('skin')!; if (params.get('tintHue')) ui.glassTintHue = Number(params.get('tintHue'))
    if (params.get('view')) ui.view = params.get('view') as ViewMode
    if (params.get('picked')) ui.picked = params.get('picked')!.split(',')
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
  // Clear glass: Tint colour colours the window and panel tint too (their amounts are Window tint and Panel tint)
  $effect(() => {
    const rootStyle = document.documentElement.style, light = ui.skin === 'light' || (ui.skin === 'auto' && !systemDark)
    if (!ui.glassTintHue) { rootStyle.removeProperty('--clear-rgb'); return }
    const swatch = document.createElement('i'); swatch.style.color = light ? `hsl(${ui.glassTintHue} 60% 88%)` : `hsl(${ui.glassTintHue} 55% 20%)`; document.body.append(swatch)
    const channels = getComputedStyle(swatch).color.match(/\d+/g)!.slice(0, 3).join(' '); swatch.remove()
    rootStyle.setProperty('--clear-rgb', channels)
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
  // Small windows: the strip keeps the month, arrows, Today, a More menu and the pin (SPEC.md › Strip)
  const compact = $derived(width < 440)
  const events = $derived(placedEvents())
  const shortMonth = (date: Date) => date.toLocaleDateString(undefined, { month: 'short' })
  // Shorter as the window narrows: "November 2026", "Nov 2026", "Nov"
  // The month name is left out rather than cut to a stub ("N…") when the strip is too crowded for it
  let headingRoom = $state(0), headingNatural = $state(0)
  const headingDay = $derived(shownView === 'agenda' && ui.readingDay ? ui.readingDay : ui.focus)
  const heading = $derived(width < 380 ? shortMonth(headingDay) : width < 460 ? `${shortMonth(headingDay)} ${headingDay.getFullYear()}` : shownView === 'week' && width >= 560 ? weekHeading(startOfWeek(ui.focus, prefs.weekStart)) : `${monthName(headingDay)} ${headingDay.getFullYear()}`)
  function weekHeading(weekStart: Date) { const weekEnd = addDays(weekStart, 6); return weekStart.getMonth() === weekEnd.getMonth() ? `${monthName(weekStart)} ${weekStart.getFullYear()}` : `${shortDate(weekStart)} – ${shortDate(weekEnd)}` }
  function step(direction: number) {
    if (shownView === 'month' || shownView === 'split') ui.focus = addMonths(ui.focus, direction)
    else if (shownView === 'week') ui.focus = addDays(ui.focus, direction * (width >= 560 ? 7 : width >= 360 ? 3 : 1))
    else { const month = addMonths(headingDay, direction); ui.focus = month.getMonth() === ui.today.getMonth() && month.getFullYear() === ui.today.getFullYear() ? ui.today : month } // Agenda: the month the heading shows
  }
  const isToday = $derived(startOfDay(new Date()).getTime() === ui.focus.getTime())
  // The Today pill shows only while today is out of view (Agenda has its own, over its list)
  const todayShown = $derived.by(() => {
    const today = ui.today, focus = ui.focus
    if (shownView === 'agenda') return true
    if (shownView === 'week') { const dayCount = width >= 560 ? 7 : width >= 360 ? 3 : 1, firstDay = dayCount === 7 ? startOfWeek(focus, prefs.weekStart) : startOfDay(focus); return today >= firstDay && today < addDays(firstDay, dayCount) }
    return today.getMonth() === focus.getMonth() && today.getFullYear() === focus.getFullYear()
  })
  const views: [ViewMode, string][] = [['auto', 'Auto: picks by window size'], ['month', 'Month'], ['week', 'Week'], ['agenda', 'Agenda'], ['split', 'Month and days']]
  // Tasks: a column beside the calendar when there's room, otherwise a panel over it
  const tasksColumn = $derived(app.tasks.enabled && ui.tasksOpen && width >= 760)
  let tasksOverlay = $state(false)
  function toggleTasks() { if (width >= 760) { ui.tasksOpen = !ui.tasksOpen; writeSetting('tasksOpen', ui.tasksOpen ? '1' : '0') } else tasksOverlay = !tasksOverlay }
  const shownCalendars = $derived(app.calendars.filter(calendar => calendar.shown))
  let pickModifier = false // Ctrl or Shift held on the click: add to the selection rather than open the card
  function pick(event: Placed, target: HTMLElement) {
    if (pickModifier) { const open = ui.selectedEvent?.event.id; if (open && !ui.picked.includes(open) && open !== event.id) ui.picked.push(open); ui.selectedEvent = null; ui.picked = ui.picked.includes(event.id) ? ui.picked.filter(id => id !== event.id) : ui.picked.concat(event.id); return }
    if (heldId === event.id) { heldId = ''; return } // the click that ends a press and hold
    // While events are picked, a click adds or removes one (× or Esc ends picking)
    if (ui.picked.length > 0) { ui.picked = ui.picked.includes(event.id) ? ui.picked.filter(id => id !== event.id) : ui.picked.concat(event.id); return }
    sendToShell({ type: 'openEditor', id: event.id }) // its own glass window (SPEC.md › Event windows)
  }
  // Press and hold an event (Settings › View › Hold to pick up): it's picked (SPEC.md › Multi-select) and lifts, with every other picked event, to be dragged to another day (SPEC.md › Moving events). Moving the pointer first (scrolling) cancels it
  let holdTimer = 0, heldId = '', holdStart = { x: 0, y: 0 }, holdPointer = 0
  function startHold(pointer: PointerEvent) {
    pickModifier = pointer.ctrlKey || pointer.shiftKey || pointer.metaKey
    clearTimeout(holdTimer); heldId = ''
    const element = (pointer.target as HTMLElement).closest?.<HTMLElement>('[data-event]')
    if (!element || pointer.button !== 0) return
    holdStart = { x: pointer.clientX, y: pointer.clientY }; holdPointer = pointer.pointerId
    holdTimer = setTimeout(() => { holdTimer = 0; const id = element.dataset.event!; heldId = id; ui.selectedEvent = null; if (!ui.picked.includes(id)) ui.picked = ui.picked.concat(id); lift(element) }, prefs.holdMs)
  }
  function moveHold(pointer: PointerEvent) {
    if (carry.active) { carry.x = pointer.clientX; carry.y = pointer.clientY; if (Math.hypot(pointer.clientX - holdStart.x, pointer.clientY - holdStart.y) > 8) carryMoved = true; aim(); return }
    if (holdTimer && Math.hypot(pointer.clientX - holdStart.x, pointer.clientY - holdStart.y) > 6) { clearTimeout(holdTimer); holdTimer = 0 }
  }
  function endHold() { clearTimeout(holdTimer); holdTimer = 0; if (carry.active) drop(true) }
  let liftMinute = -1, carryScroller: HTMLElement | null = null, carryMoved = false // picked up and let go without moving the pointer: it goes back where it was, whatever moved underneath
  const minuteAt = (cell: HTMLElement, clientY: number) => (clientY - cell.getBoundingClientRect().top) / Number(cell.dataset.hourHeight) * 60
  const scrollParent = (node: HTMLElement | null) => { for (; node; node = node.parentElement) if (/(auto|scroll)/.test(getComputedStyle(node).overflowY) && node.scrollHeight > node.clientHeight) return node; return null }
  function lift(element: HTMLElement) {
    const cell = element.closest<HTMLElement>('[data-newday]'); if (!cell) return
    carry.ids = ui.picked.slice(); carry.originDay = carry.targetDay = cell.dataset.newday!; carry.x = holdStart.x; carry.y = holdStart.y; carry.minutes = 0; carry.previewTop = -1; carryMoved = false
    liftMinute = cell.dataset.hourHeight ? minuteAt(cell, holdStart.y) : -1; carryScroller = scrollParent(element)
    carry.active = true; document.documentElement.classList.add('carrying')
    try { document.body.setPointerCapture(holdPointer) } catch { /* the pointer already went up */ }
    requestAnimationFrame(edgeScroll)
  }
  // Where the carried events would land: the day under the pointer, and in Week's hours the time too (moved in quarter hours)
  function aim() {
    const cell = document.elementsFromPoint(carry.x, carry.y).map(node => node.closest<HTMLElement>('[data-newday]')).find(found => found) ?? null
    document.querySelectorAll('[data-drop]').forEach(node => { if (node !== cell) node.removeAttribute('data-drop') })
    if (!cell) { carry.targetDay = ''; carry.previewTop = -1; return }
    cell.setAttribute('data-drop', ''); carry.targetDay = cell.dataset.newday!
    const first = events.find(event => event.id === carry.ids[0])
    if (cell.dataset.hourHeight && liftMinute >= 0 && first && !first.allDay) {
      carry.minutes = Math.round((minuteAt(cell, carry.y) - liftMinute) / 15) * 15
      carry.previewTop = first.startAt.getHours() * 60 + first.startAt.getMinutes() + carry.minutes; carry.previewLength = (first.endAt.getTime() - first.startAt.getTime()) / 60000
    } else { carry.minutes = 0; carry.previewTop = -1 }
  }
  // Holding near the top or bottom of the list scrolls it, faster the closer to (or past) the edge
  function edgeScroll() {
    if (!carry.active) return
    if (carryScroller) {
      const box = carryScroller.getBoundingClientRect(), edge = 56
      const push = carry.y < box.top + edge ? carry.y - box.top - edge : carry.y > box.bottom - edge ? carry.y - box.bottom + edge : 0
      if (push) { carryScroller.scrollTop += Math.max(-28, Math.min(28, push * 0.3)); aim() }
    }
    requestAnimationFrame(edgeScroll)
  }
  // Dropped on another day (or time): moved at once here, then in Google; dropped where it started, it stays picked
  function drop(place: boolean) {
    place = place && carryMoved
    const days = place && carry.targetDay ? Math.round((parseWhen(carry.targetDay).getTime() - parseWhen(carry.originDay).getTime()) / dayMs) : 0, minutes = place && carry.targetDay ? carry.minutes : 0
    if (days || minutes) {
      const localText = (date: Date) => `${dayKey(date)}T${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}:00`
      const shifted = (text: string, allDay: boolean) => allDay ? dayKey(addDays(parseWhen(text), days)) : localText(new Date(addDays(parseWhen(text), days).getTime() + minutes * 60000))
      app.events = app.events.map(event => carry.ids.includes(event.id) ? Object.assign({}, event, { start: shifted(event.start, event.allDay), end: shifted(event.end, event.allDay) }) : event)
      sendToShell({ type: 'moveEvents', ids: carry.ids.slice(), days, minutes }); ui.picked = []
    }
    carry.active = false; carry.targetDay = ''; carry.previewTop = -1; carryScroller = null
    document.documentElement.classList.remove('carrying'); document.querySelectorAll('[data-drop]').forEach(node => node.removeAttribute('data-drop'))
  }
  const carried = $derived(carry.active ? events.filter(event => carry.ids.includes(event.id)) : [])
  // Double-click an empty spot of a day: a new event that day (all day), or at that time in Week
  function newEventAt(pointer: MouseEvent) {
    const target = pointer.target as HTMLElement
    if (target.closest('[data-event], .strip, .selection-bar')) return
    const cell = target.closest<HTMLElement>('[data-newday]'); if (!cell) return
    let time = ''
    if (cell.dataset.hourHeight) { const minutes = Math.min(1425, Math.max(0, Math.floor((pointer.clientY - cell.getBoundingClientRect().top) / Number(cell.dataset.hourHeight) * 4) * 15)); time = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}` }
    sendToShell({ type: 'newEvent', day: cell.dataset.newday, time })
  }
  function onKey(event: KeyboardEvent) {
    if ((event.target as HTMLElement).closest('input, textarea')) return
    if (event.key === 'Escape' && carry.active) { (document.activeElement as HTMLElement | null)?.blur?.(); drop(false); return }
    if (event.key === 'Escape') { (document.activeElement as HTMLElement | null)?.blur?.(); ui.selectedEvent = null; ui.picked = []; ui.pickedDays = null; ui.switcherOpen = false; tasksOverlay = false } // the blur: no focus ring left behind on the event that was held
    else if (event.key === 'ArrowLeft' || event.key === 'PageUp') step(-1)
    else if (event.key === 'ArrowRight' || event.key === 'PageDown') step(1)
    else if (event.key === 't') ui.focus = startOfDay(new Date())
    else if (event.key === 'm') ui.view = 'month'
    else if (event.key === 'w') ui.view = 'week'
    else if (event.key === 'a') ui.view = 'agenda'
  }
</script>

<svelte:window bind:innerWidth={width} bind:innerHeight={height} onkeydown={onKey} ondragstart={(dragEvent) => { if (!(dragEvent.target as HTMLElement).closest?.('input, textarea')) dragEvent.preventDefault() }} ondblclick={newEventAt} onpointerdowncapture={startHold} onpointermove={moveHold} onpointerup={endHold} onpointercancel={() => { clearTimeout(holdTimer); holdTimer = 0; if (carry.active) drop(false) }} />

<WallpaperFrost />
<div class="shell" class:pinned={app.window.pinned}>
  {#if app.window.frameless && !app.window.pinned && !app.window.maximised}<ResizeEdges />{/if}
  <header class="strip" role="presentation" onmousedown={dragWindow} data-frost>
    <div class="heading" bind:clientWidth={headingRoom}>
      <span class="today measure" aria-hidden="true" bind:clientWidth={headingNatural}>{heading}</span>
      <button class="today" class:on={isToday} class:cut={headingNatural > headingRoom - (width >= 220 ? 64 : 0) - (todayShown ? 0 : 60)} onclick={() => ui.focus = startOfDay(new Date())} title="Back to today (T)">{headingNatural > headingRoom - (width >= 220 ? 64 : 0) - (todayShown ? 0 : 60) ? `${shortMonth(headingDay)} ${headingDay.getFullYear()}` : heading}</button>
      {#if width >= 220}
        <button class="step" onclick={() => step(-1)} aria-label="Previous"><Icon name="previous" size={16} /></button>
        <button class="step" onclick={() => step(1)} aria-label="Next"><Icon name="next" size={16} /></button>
        {#if !todayShown}<button class="back-today" onclick={() => ui.focus = startOfDay(new Date())} title="Back to today (T)">Today</button>{/if}
      {/if}
    </div>
    <div class="tools">
      {#if app.offline}<span class="offline" title="Can't reach Google: showing what was saved last time"><Icon name="offline" size={15} />{narrow ? '' : 'Offline'}</span>{/if}
      {#if !narrow}
        <div class="views" role="group" aria-label="View">
          <!-- No Auto button: the view the window picks shows lit with a small dot; clicking a view keeps it, clicking the kept view again goes back to following the window -->
          {#each views.slice(1) as [view, label]}<button class:on={shownView === view} class:auto={ui.view === 'auto' && shownView === view} onclick={() => ui.view = ui.view === view ? 'auto' : view} title={ui.view === 'auto' && shownView === view ? `${label} (picked for this window size). Click to keep it` : ui.view === view ? `${label}. Click again to follow the window size` : label} aria-pressed={shownView === view}><Icon name={view} size={16} /></button>{/each}
        </div>
        <SkinPicker />
      {/if}
      <!-- Below 560 px the view buttons and skin picker go: the menu holds them (and below 440 px everything else too) -->
      {#if narrow}<MoreMenu {views} />{/if}
      {#if !compact}
      <button class="dots" class:on={ui.switcherOpen} onclick={() => ui.switcherOpen = !ui.switcherOpen} title="Calendars">{#each shownCalendars.slice(0, width < 320 ? 3 : 5) as calendar}<i style="background: {calendar.colour}"></i>{/each}{#if shownCalendars.length === 0}<Icon name="calendar" size={16} />{/if}</button>
      {#if app.tasks.enabled}<button class="tool" class:on={tasksColumn || tasksOverlay} onclick={toggleTasks} title="Tasks"><Icon name="tasks" size={16} /></button>{/if}
      {#if width >= 400}<button class="tool" class:spinning={app.syncing} onclick={() => sendToShell({ type: 'refresh' })} title="Refresh from Google"><Icon name="refresh" size={16} /></button>{/if}
      <button class="tool" onclick={() => sendToShell({ type: 'openSettings' })} title="Settings"><Icon name="settings" size={16} /></button>
      {/if}
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
</div>

<WhatsNew />
<SelectionBar />
<UndoBar />
<!-- What's being carried: up to three stacked, with a count when there are more than one -->
{#if carry.active && carried.length}
  <div class="carried" style="left: {carry.x}px; top: {carry.y}px" aria-hidden="true">
    {#each carried.slice(0, 3) as event, index (event.id)}<div class="carried-item" style="--colour: {event.colourShown}; --index: {index}"><span class="carried-bar"></span><span class="carried-title">{event.title}</span></div>{/each}
    {#if carried.length > 1}<span class="carried-count">{carried.length}</span>{/if}
  </div>
{/if}
<ConfirmDelete />

<style>
  .shell { position: relative; display: flex; flex-direction: column; height: 100vh; }
  .strip { display: flex; align-items: center; gap: 8px; padding: 6px 6px 6px 12px; min-height: 36px; -webkit-user-select: none; user-select: none; }
  .heading { flex: 1; min-width: 0; position: relative; display: flex; align-items: center; gap: 2px; }
  .today { min-width: 0; padding: 2px 8px; margin-right: 4px; border: 0; border-radius: 8px; background: none; color: var(--fg); font: inherit; font-size: 18px; font-weight: 650; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; cursor: pointer; letter-spacing: -0.01em; }
  .today:hover { background: var(--hover); }
  .today.measure { position: absolute; visibility: hidden; pointer-events: none; }
  .today.cut { font-size: 15px; } /* short of room: "Nov 2026", never nothing */
  /* Back to today: only while away from it, quiet until hovered */
  /* Back to today: shown only while away from it, in the accent so it's hard to miss */
  .back-today { flex: none; height: 22px; padding: 0 10px; margin-left: 6px; border: 0; border-radius: 11px; background: var(--accent-soft); box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--accent) 45%, transparent); color: var(--accent); font: inherit; font-size: 11.5px; font-weight: 700; cursor: pointer; }
  .back-today:hover { background: var(--accent); color: var(--on-accent); }
  .step, .tool, .window-controls button { display: grid; place-items: center; width: 32px; height: 30px; flex: none; border: 0; border-radius: 7px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .step:hover, .tool:hover, .window-controls button:hover { background: var(--hover); color: var(--fg); }
  .tools { display: flex; align-items: center; gap: 2px; flex: none; }
  .views { display: flex; padding: 2px; margin-right: 6px; border-radius: 9px; background: var(--panel); }
  .views button { display: grid; place-items: center; width: 30px; height: 26px; border: 0; border-radius: 7px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .views button:hover { color: var(--fg); }
  .views button.on { background: var(--panel-strong); color: var(--fg); box-shadow: 0 1px 2px var(--shadow-soft); }
  .views button { position: relative; }
  .views button.auto::after { content: ""; position: absolute; bottom: 2px; left: 50%; width: 4px; height: 4px; margin-left: -2px; border-radius: 50%; background: var(--accent); } /* picked by the window size */
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
  .notice { display: flex; align-items: center; justify-content: flex-end; gap: 10px; margin: 0 8px 6px; padding: 6px 10px; border-radius: 10px; background: var(--accent-soft); font-size: 13px; }
  .notice span { min-width: 0; text-align: right; }
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
  /* Carrying events: a lifted stack that follows the pointer, wiggling as it comes up; the originals fade where they were, and the day under the pointer is outlined */
  .carried { position: fixed; z-index: 1000; pointer-events: none; transform: translate(-24px, -50%); animation: carry-lift 0.36s ease-out; }
  .carried-item { position: relative; display: flex; align-items: center; gap: 8px; width: 220px; margin-top: calc(var(--index) * -30px); padding: 7px 10px; border-radius: 8px; background: color-mix(in srgb, var(--colour) 28%, var(--panel-solid)); box-shadow: 0 10px 28px rgba(0, 0, 0, 0.4); color: var(--fg); font-size: 12.5px; font-weight: 600; transform: translate(calc(var(--index) * 5px), calc(var(--index) * 5px)) rotate(calc(var(--index) * 2deg)); z-index: calc(3 - var(--index)); }
  .carried-bar { width: 4px; align-self: stretch; border-radius: 2px; background: var(--colour); flex: none; }
  .carried-title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .carried-count { position: absolute; right: -8px; top: -8px; min-width: 20px; height: 20px; padding: 0 6px; border-radius: 10px; background: var(--accent); color: var(--on-accent); font-size: 11px; font-weight: 700; display: grid; place-items: center; z-index: 5; }
  @keyframes carry-lift { 0% { transform: translate(-24px, -50%) scale(1); } 25% { transform: translate(-24px, -50%) scale(1.06) rotate(-2.5deg); } 50% { transform: translate(-24px, -50%) scale(1.06) rotate(2deg); } 75% { transform: translate(-24px, -50%) scale(1.04) rotate(-1deg); } 100% { transform: translate(-24px, -50%) scale(1.03); } }
  :global(html.carrying [data-event].picked) { opacity: 0.35; }
  :global(html.carrying .selection-bar) { opacity: 0; pointer-events: none; }
  :global(html.carrying), :global(html.carrying *) { cursor: grabbing; }
  :global([data-newday][data-drop]) { outline: 2px dashed var(--accent); outline-offset: -2px; background-color: color-mix(in srgb, var(--accent) 10%, transparent); }
</style>
