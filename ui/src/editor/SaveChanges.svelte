<script lang="ts">
  // Esc with unsaved changes asks first: Save has the focus, so Enter saves and closes; Esc again goes back to editing
  let { saveLabel = 'Save', canSave = true, onsave, ondiscard }: { saveLabel?: string; canSave?: boolean; onsave: () => void; ondiscard: () => void } = $props()
  let saveButton: HTMLButtonElement | undefined = $state(), discardButton: HTMLButtonElement | undefined = $state()
  $effect(() => { (canSave ? saveButton : discardButton)?.focus() })
</script>

<div class="ask">
  <b>Save your changes?</b>
  <div class="buttons"><button class="ghost" bind:this={discardButton} onclick={ondiscard}>Don't save</button><button class="primary" bind:this={saveButton} disabled={!canSave} onclick={onsave}>{saveLabel}<kbd>Enter</kbd></button></div>
</div>

<style>
  .ask { display: flex; flex-direction: column; gap: 8px; margin-top: 6px; padding: 10px 12px; border-radius: 10px; background: color-mix(in srgb, var(--accent) 14%, transparent); font-size: 13px; }
  .buttons { display: flex; justify-content: flex-end; gap: 8px; }
  button { height: 32px; padding: 0 16px; border: 0; border-radius: 8px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; outline: none; }
  button:focus-visible { box-shadow: 0 0 0 2px var(--fg); }
  .primary { background: var(--accent); color: white; }
  .primary:disabled { opacity: 0.45; cursor: default; }
  .ghost { background: var(--hover); color: var(--fg); }
  kbd { margin-left: 7px; font: inherit; font-size: 10.5px; font-weight: 600; opacity: 0.6; }
</style>
