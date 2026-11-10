import { mount } from 'svelte'
import App from './App.svelte'
import EditorApp from './editor/EditorApp.svelte'
import './tokens.css'
import './skins.css'
// The calendar, or one of its glass event windows (?window=event | new | batch)
const editor = new URLSearchParams(location.search).has('window')
export default mount(editor ? EditorApp : App, { target: document.getElementById('app')! })
