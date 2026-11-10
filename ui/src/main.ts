import { mount } from 'svelte'
import App from './App.svelte'
import './tokens.css'
import './skins.css'
export default mount(App, { target: document.getElementById('app')! })
