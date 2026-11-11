<script lang="ts">
  import type { EditorCalendar } from './types'
  import Icon from '../parts/Icon.svelte'
  // Which calendar an event goes in: a glass button with the calendar's colour, opening a glass list (no Windows drop-down). Only the calendars picked in Settings › Calendars are offered; the account shows only when there's more than one
  let { calendars, value = $bindable(), onchange }: { calendars: EditorCalendar[]; value: string; onchange?: () => void } = $props()
  let open = $state(false), active = $state(0), button: HTMLButtonElement | undefined = $state()
  const chosen = $derived(calendars.find(calendar => calendar.key === value))
  const manyAccounts = $derived(new Set(calendars.map(calendar => calendar.account)).size > 1)
  function toggle() { open = !open; active = Math.max(0, calendars.findIndex(calendar => calendar.key === value)) }
  function choose(key: string) { value = key; open = false; onchange?.(); button?.focus() }
  // Arrows move, Enter picks, Esc closes just the list (not the window)
  function onKey(keyEvent: KeyboardEvent) {
    if (!open) { if (keyEvent.key === 'ArrowDown' || keyEvent.key === ' ') { keyEvent.preventDefault(); toggle() } return }
    keyEvent.stopPropagation()
    if (keyEvent.key === 'Escape') { keyEvent.preventDefault(); open = false }
    else if (keyEvent.key === 'ArrowDown') { keyEvent.preventDefault(); active = Math.min(calendars.length - 1, active + 1) }
    else if (keyEvent.key === 'ArrowUp') { keyEvent.preventDefault(); active = Math.max(0, active - 1) }
    else if (keyEvent.key === 'Enter') { keyEvent.preventDefault(); if (calendars[active]) choose(calendars[active].key) }
  }
</script>

<div class="picker" onkeydown={onKey} role="presentation">
  <button type="button" class="current" class:open bind:this={button} onclick={toggle} aria-haspopup="listbox" aria-expanded={open}>
    <i style="background: {chosen?.colour ?? 'var(--fg-quiet)'}"></i><span class="name">{chosen?.name ?? 'Pick a calendar'}</span>{#if manyAccounts && chosen}<span class="account">{chosen.account}</span>{/if}<Icon name="down" size={13} />
  </button>
  {#if open}
    <div class="scrim" role="presentation" onpointerdown={() => open = false}></div>
    <div class="list" role="listbox">
      {#each calendars as calendar, index}
        {#if manyAccounts && (index === 0 || calendars[index - 1].account !== calendar.account)}<div class="group">{calendar.account}</div>{/if}
        <button type="button" role="option" aria-selected={calendar.key === value} class:active={index === active} onpointerenter={() => active = index} onclick={() => choose(calendar.key)}>
          <i style="background: {calendar.colour}"></i><span class="name">{calendar.name}</span>{#if calendar.key === value}<Icon name="check" size={13} />{/if}
        </button>
      {/each}
    </div>
  {/if}
</div>

<style>
  .picker { position: relative; flex: 1; min-width: 0; }
  button { display: flex; align-items: center; gap: 9px; width: 100%; min-width: 0; border: 0; background: none; color: var(--fg); font: inherit; text-align: left; cursor: pointer; }
  i { flex: none; width: 10px; height: 10px; border-radius: 50%; box-shadow: 0 0 0 2px color-mix(in srgb, currentColor 10%, transparent); }
  .name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .account { flex: none; max-width: 45%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--fg-quiet); font-size: 12px; }
  .current { padding: 6px 10px; border-radius: 8px; background: color-mix(in srgb, var(--fg) 6%, transparent); box-shadow: inset 0 0 0 1px var(--line); }
  .current:hover, .current.open { background: color-mix(in srgb, var(--fg) 10%, transparent); }
  .current.open, .current:focus-visible { box-shadow: inset 0 0 0 1px var(--accent); outline: none; }
  .current :global(svg) { flex: none; color: var(--fg-quiet); }
  .scrim { position: fixed; inset: 0; z-index: 20; }
  .list { position: absolute; z-index: 21; top: calc(100% + 6px); left: 0; right: 0; max-height: 280px; overflow-y: auto; padding: 5px; border-radius: 12px; background: linear-gradient(var(--panel-solid), var(--panel-solid)), linear-gradient(var(--panel-solid), var(--panel-solid)), var(--panel-solid); /* layered: solid even where clear glass makes the panel colour see-through */ box-shadow: inset 0 0 0 1px var(--line), 0 14px 36px var(--shadow); scrollbar-width: thin; animation: drop 140ms ease-out; }
  @keyframes drop { from { opacity: 0; transform: translateY(-4px); } }
  .list button { padding: 7px 10px; border-radius: 8px; }
  .list button.active { background: var(--hover); }
  .list button[aria-selected="true"] { font-weight: 600; }
  .list button :global(svg) { flex: none; color: var(--accent); }
  .group { padding: 8px 10px 3px; font-size: 10.5px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; color: var(--fg-quiet); }
</style>
