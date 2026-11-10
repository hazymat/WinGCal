<script lang="ts">
  import { sendToShell } from '../bridge'
  import { app, type TaskItem } from '../state.svelte'
  import { parseWhen, startOfDay, shortDate } from '../lib/dates'
  import Icon from './Icon.svelte'
  let { overlay = false, onclose }: { overlay?: boolean; onclose: () => void } = $props()

  // Google Tasks beside the calendar (SPEC.md › Tasks): every shown list, grouped by account when there's more than one
  const shownLists = $derived(app.tasks.lists.filter(list => list.shown))
  const accountCount = $derived(new Set(app.tasks.lists.map(list => list.account)).size)
  let openCompleted = $state<Record<string, boolean>>({})
  let drafts = $state<Record<string, string>>({})
  const today = startOfDay(new Date())
  // Top-level tasks in Google's order, each followed by its subtasks
  function ordered(listKey: string, completed: boolean) {
    const items = app.tasks.items.filter(item => item.listKey === listKey && item.completed === completed)
    if (completed) return items.sort((first, second) => second.completedAt.localeCompare(first.completedAt)).map(item => ({ item, child: false }))
    const byPosition = (first: TaskItem, second: TaskItem) => first.position.localeCompare(second.position)
    const ids = new Set(items.map(item => item.id.split('|').pop()))
    return items.filter(item => !item.parent || !ids.has(item.parent)).sort(byPosition).flatMap(parent => [{ item: parent, child: false }].concat(items.filter(item => item.parent && item.parent === parent.id.split('|').pop()).sort(byPosition).map(item => ({ item, child: true }))))
  }
  function dueLabel(due: string) {
    if (!due) return null
    const offset = Math.round((parseWhen(due).getTime() - today.getTime()) / 86_400_000)
    return { text: offset === 0 ? 'Today' : offset === 1 ? 'Tomorrow' : offset === -1 ? 'Yesterday' : shortDate(parseWhen(due)), late: offset < 0 }
  }
  function toggle(item: TaskItem) { item.completed = !item.completed; item.completedAt = item.completed ? new Date().toISOString() : ''; sendToShell({ type: 'taskDone', id: item.id, done: item.completed }) }
  function remove(item: TaskItem) { app.tasks.items = app.tasks.items.filter(candidate => candidate.id !== item.id); sendToShell({ type: 'taskDelete', id: item.id }) }
  function add(listKey: string) { const title = (drafts[listKey] ?? '').trim(); if (!title) return; sendToShell({ type: 'taskAdd', listKey, title }); drafts[listKey] = '' }
</script>

<aside class="tasks" class:overlay>
  <header><Icon name="tasks" size={16} /><span>Tasks</span><button class="close" onclick={onclose} title="Hide tasks"><Icon name="close" size={14} /></button></header>
  <div class="lists">
    {#if app.tasks.lists.length === 0}
      <div class="empty">No task lists yet. Allow task access for an account in Settings.<button class="ghost" onclick={() => sendToShell({ type: 'openSettings', tab: 'tasks' })}>Open Settings › Tasks</button></div>
    {/if}
    {#each shownLists as list, index (list.key)}
      {#if accountCount > 1 && (index === 0 || shownLists[index - 1].account !== list.account)}<div class="account">{list.account}</div>{/if}
      <section>
        <h4>{list.name}</h4>
        {#each ordered(list.key, false) as { item, child } (item.id)}
          {@const due = dueLabel(item.due)}
          <div class="task" class:child>
            <button class="box" onclick={() => toggle(item)} aria-label="Mark done"></button>
            <div class="text"><span class="title">{item.title || '(untitled)'}</span>{#if item.notes}<span class="notes">{item.notes}</span>{/if}</div>
            {#if due}<span class="due" class:late={due.late}>{due.text}</span>{/if}
            <button class="delete" onclick={() => remove(item)} title="Delete"><Icon name="trash" size={13} /></button>
          </div>
        {/each}
        <div class="add"><Icon name="plus" size={14} /><input placeholder="Add a task" bind:value={drafts[list.key]} onkeydown={(event) => { if (event.key === 'Enter') add(list.key) }} /></div>
        {#if ordered(list.key, true).length > 0}
          <button class="completed-toggle" onclick={() => openCompleted[list.key] = !openCompleted[list.key]}>{openCompleted[list.key] ? '▾' : '▸'} Completed ({ordered(list.key, true).length})</button>
          {#if openCompleted[list.key]}
            {#each ordered(list.key, true) as { item } (item.id)}
              <div class="task done">
                <button class="box on" onclick={() => toggle(item)} aria-label="Mark not done"><Icon name="check" size={12} /></button>
                <div class="text"><span class="title">{item.title}</span></div>
                <button class="delete" onclick={() => remove(item)} title="Delete"><Icon name="trash" size={13} /></button>
              </div>
            {/each}
          {/if}
        {/if}
      </section>
    {/each}
  </div>
</aside>

<style>
  .tasks { display: flex; flex-direction: column; min-height: 0; height: 100%; border-left: 1px solid var(--line); }
  .tasks.overlay { position: fixed; top: 44px; right: 8px; bottom: 8px; width: min(320px, calc(100vw - 16px)); z-index: 40; border: 1px solid var(--line); border-radius: 12px; background: var(--panel-solid); box-shadow: 0 12px 32px var(--shadow); }
  header { display: flex; align-items: center; gap: 7px; padding: 4px 6px 6px 12px; font-weight: 650; color: var(--fg); }
  header span { flex: 1; }
  .close { display: grid; place-items: center; width: 26px; height: 24px; border: 0; border-radius: 6px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .close:hover { background: var(--hover); color: var(--fg); }
  .lists { flex: 1; min-height: 0; overflow-y: auto; padding: 0 8px 10px 10px; }
  .empty { display: flex; flex-direction: column; align-items: flex-start; gap: 10px; padding: 8px 4px; color: var(--fg-quiet); font-size: 12.5px; }
  .account { margin: 10px 2px 2px; font-size: 11px; color: var(--fg-quiet); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  section { margin-bottom: 10px; }
  h4 { margin: 6px 2px 4px; font-size: 12px; font-weight: 700; letter-spacing: 0.05em; text-transform: uppercase; color: var(--accent); }
  .task { display: flex; align-items: flex-start; gap: 9px; padding: 4px 4px; border-radius: 7px; }
  .task:hover { background: var(--hover); }
  .task.child { padding-left: 26px; }
  .box { flex: none; display: grid; place-items: center; width: 17px; height: 17px; margin-top: 1px; padding: 0; border: 1.5px solid var(--fg-quiet); border-radius: 50%; background: none; color: var(--on-accent); cursor: pointer; }
  .box:hover { border-color: var(--accent); background: var(--accent-soft); }
  .box.on { background: var(--accent); border-color: var(--accent); }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .title { font-size: 13px; line-height: 1.35; overflow-wrap: anywhere; }
  .notes { font-size: 11.5px; color: var(--fg-quiet); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .done .title { color: var(--fg-quiet); text-decoration: line-through; }
  .due { flex: none; margin-top: 1px; padding: 0 6px; border-radius: 8px; background: var(--panel); font-size: 11px; line-height: 17px; color: var(--fg-quiet); }
  .due.late { color: var(--danger); background: color-mix(in srgb, var(--danger) 12%, transparent); }
  .delete { flex: none; display: none; place-items: center; width: 22px; height: 20px; padding: 0; border: 0; border-radius: 5px; background: none; color: var(--fg-quiet); cursor: pointer; }
  .task:hover .delete { display: grid; }
  .delete:hover { color: var(--danger); background: var(--hover); }
  .add { display: flex; align-items: center; gap: 9px; padding: 3px 5px; color: var(--fg-quiet); }
  .add input { flex: 1; min-width: 0; border: 0; background: none; padding: 3px 0; font-size: 13px; }
  .add input:focus { border: 0; }
  .completed-toggle { margin: 4px 0 2px; padding: 2px 4px; border: 0; background: none; color: var(--fg-quiet); font: inherit; font-size: 12px; cursor: pointer; }
  .completed-toggle:hover { color: var(--fg); }
</style>
