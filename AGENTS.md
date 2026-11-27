# WinGCal: working notes

Read `SPEC.md` before any work. It is the source of truth; propose changes to it rather than silently diverging.

Read `docs/DESIGN.md` too: the design rules shared by all four apps (tray notifications, dot colours, cards). It is identical in every repo; change it in all four together.

UI and UX across all of Mat's apps: follow Rules for every app in https://github.com/hazymat/sift/blob/main/docs/ui-consistency.md (undo and history, the selection bar, press-hold to choose, dragging, keys, hints). A change here that adds or alters a shared pattern updates that section in the same piece of work.

## Dev environment

- Build and test on Windows (the app can't be run on Linux; it can be compiled there with `-p:EnableWindowsTargeting=true`). The test PC and its details live in the project's private notes, not in this public repo.
- UI: `cd ui && npm ci && npm run build` (builds into `app/wwwroot`). `npm run dev` shows invented calendars and tasks (no Google account); `?skin=wall&view=split&tasks` picks a skin, view and the tasks panel.
- Shell: `dotnet build app/WinGCal.csproj`. Google sign-in needs a client: CI builds it in from the repository secrets `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`; locally put `{"clientId": "…", "clientSecret": "…"}` in `%LOCALAPPDATA%\WinGCal-data\google-client.json`. Never commit either.
- A `portable.txt` next to the exe keeps settings, tokens and logs beside it (handy for test copies).

## Git identity (required)

Never credit Claude/Anthropic anywhere: no `Co-Authored-By` trailers, no "Generated with" lines, no session links, no Claude author. Before the first commit in any clone run:

```
git config user.name hazymat && git config user.email 7063284+hazymat@users.noreply.github.com
```

The `No AI attribution` workflow fails any PR or push whose commits credit Claude; fix the commits, don't merge past it.

## Releases

Every push to main publishes a dev pre-release (Velopack, `.github/workflows/build.yml`). A stable release is promoted by running the workflow by hand with `stable_version` set to an existing dev version. A commit trailer `Impact: feature` or `Impact: improvement` puts its line first in What's new.

## Code style

- Compact code, descriptive variable names (no single-letter names), concise useful comments only.
- No spread operator in new code where a plain alternative reads as well. Never use CSS `!important`.

## Layout

- `/app`: C# .NET 8 WPF shell hosting WebView2. `Google/` sign-in, Calendar and Tasks APIs, the store and export; `Shell/` pin to desktop, monitor setups, updater and crash log; `Settings/` settings and paths.
- `/ui`: TypeScript + Vite + Svelte 5 front end. `views/` Month, Week, Agenda, Split; `parts/` cards, switcher, tasks; `tokens.css` glass skins, `skins.css` Paper, Technical and Wall calendar.
- `/docs`: notes and screenshots.
