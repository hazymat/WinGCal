<script lang="ts">
  import { app, ui, toggleCalendar } from '../state.svelte'
  import { sendToShell } from '../bridge'
  // The quick calendar switcher: calendars grouped by account, click to show or hide (same choice as Settings › Calendars)
  const accounts = $derived(Array.from(new Set(app.calendars.map(calendar => calendar.account))))
</script>

<div class="scrim" role="presentation" onpointerdown={() => ui.switcherOpen = false}></div>
<div class="switcher" role="dialog" aria-label="Calendars">
  {#each accounts as account}
    <div class="account">{account}</div>
    {#each app.calendars.filter(calendar => calendar.account === account) as calendar (calendar.key)}
      <button class="calendar" class:off={!calendar.shown} style="--colour: {calendar.colour}" onclick={() => toggleCalendar(calendar.key)} aria-pressed={calendar.shown}>
        <span class="box">{#if calendar.shown}<svg viewBox="0 0 12 12" width="10" height="10"><path d="M2.5 6.2l2.4 2.4 4.6-5" /></svg>{/if}</span>
        <span class="name">{calendar.name}</span>
      </button>
    {/each}
  {/each}
  {#if accounts.length === 0}<div class="none">No calendars yet</div>{/if}
  <button class="manage" onclick={() => { ui.switcherOpen = false; sendToShell({ type: 'openSettings', tab: 'Calendars' }) }}>Accounts and calendars…</button>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; }
  .switcher { position: fixed; z-index: 51; top: 44px; right: 10px; width: 250px; max-height: calc(100vh - 56px); overflow-y: auto; padding: 6px; border-radius: 12px; background: var(--popover); backdrop-filter: blur(28px) saturate(1.5); border: 1px solid var(--line); box-shadow: 0 18px 40px var(--shadow); }
  .account { padding: 8px 8px 4px; font-size: 11px; font-weight: 600; color: var(--fg-quiet); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .calendar { display: flex; align-items: center; gap: 10px; width: 100%; padding: 6px 8px; border: 0; border-radius: 7px; background: none; color: var(--fg); font: inherit; font-size: 13px; text-align: left; cursor: pointer; }
  .calendar:hover { background: var(--hover); }
  .box { display: grid; place-items: center; width: 16px; height: 16px; border-radius: 4px; background: var(--colour); border: 1.5px solid var(--colour); flex: none; }
  .off .box { background: none; }
  .off .name { color: var(--fg-quiet); }
  .box path { fill: none; stroke: #fff; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
  .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .none { padding: 10px 8px; color: var(--fg-quiet); font-size: 13px; }
  .manage { display: block; width: 100%; margin-top: 4px; padding: 8px; border: 0; border-top: 1px solid var(--line); border-radius: 0 0 7px 7px; background: none; color: var(--accent); font: inherit; font-size: 12.5px; text-align: left; cursor: pointer; }
  .manage:hover { background: var(--hover); }
</style>
