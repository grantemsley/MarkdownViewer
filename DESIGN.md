# MarkdownViewer — Design decisions

The durable "why it's built this way" record: load-bearing decisions, the
alternatives rejected, and known limitations. See `.claude/rules/doc-conventions.md`
for what belongs here vs. `todo.md` / `plans/`.

## Updates: notify-only check, not a full auto-updater

**Decision.** Update support is a *notify-only* check (`UpdateService`): once at
startup it asks the GitHub Releases API for the latest release, compares the tag
to the running assembly version, and — if newer — shows a dismissible banner whose
"Download" button opens the release page. It never downloads or installs.

**Why.** The app ships as a single, framework-dependent, portable `.exe` attached
to a GitHub Release. The polished auto-updaters all trade that away:

- **Velopack** (the modern Squirrel successor; the "proper" answer) and **MSIX +
  App Installer** both move the app to an *install-based* model (lives in
  `%LocalAppData%` / packaged, managed install lifecycle). MSIX additionally needs
  code signing. A running `.exe` can't cleanly overwrite itself, so seamless
  in-place update essentially requires that model.
- We chose to keep the portable single-exe identity. Notify-only closes the real
  gap (users not knowing an update exists) without giving up portability or taking
  on an install framework + signing.

**Rejected for now:** Velopack (revisit if/when we accept becoming an installed
app — it's the well-trodden path and would give silent delta updates), MSIX
(only worth it alongside Store / first-class context-menu integration), and a
self-replacing downloader (the swap-the-running-exe dance is fragile for little
gain over "open the release page").

**Known limitations.**
- The exe is unsigned, so a future download-based flow would meet SmartScreen/AV
  friction until signed. Notify-only sidesteps this (the browser does the
  download).
- Opt-out via Preferences → "Check for updates on startup" (default on). The
  check is one unauthenticated GitHub API GET, throttled to at most once per 24h
  (`UpdatePrefs.LastCheckUtc` + `UpdateService.CheckInterval`). The daily timer is
  stamped only when GitHub is actually reached, so an offline launch retries next
  time rather than burning the day's slot. All failures are swallowed as "up to date".

## Tabbed viewing, single-instance & startup (plans/finished/tabs-and-startup.md)

**Tab model.** Each tab switches the *whole window* — its own `VaultService`
(folder tree + watcher), open file, and outline. Tabs are **independent** (no
shared tree, even for same-folder tabs). One **shared WebView2**; switching
re-binds the sidebar and re-renders the active tab's doc. The decision logic
lives in a pure, UI-agnostic **`TabManager`** (+ `TabState`/`TabSession`) so it's
unit-tested without WPF; `MainWindow` is the thin view (per-tab `TabRuntime`,
vault events gated to the active tab). Optional, **default on**
(`TabsPrefs.Enabled`, startup-time); off = the old single pane.

**Single-instance** (`SingleInstanceServer`, default on): a per-user named mutex
+ named pipe. A second launch hands its file path to the owner and exits; the
owner opens it per `OpenIncomingInNewTab` (new tab / replace) and takes the
foreground. The second process grants foreground rights via
`AllowSetForegroundWindow` so a plain `Activate()` works — the earlier
`Topmost` nudge broke other windows' focus. Hand-off failure → normal launch
(worst case a second window, never a hang).

**Startup latency:** WebView2 env creation kicked off in the `MainWindow` field
initializer (overlaps window paint); `PublishReadyToRun` trims JIT. A blank-pane
loading overlay was **deferred** — WebView2 is HWND-hosted (airspace), so a WPF
overlay needs a `Popup`/hide-until-paint approach + GUI iteration.

**Per-tab scroll restore.** A tab switch re-renders the doc, which resets the
WebView's scroll to top. The offset is preserved by *live-tracking* rather than
capturing at switch time: `bridge.js` reports `#scroll`'s offset (rAF-throttled,
tagged with the doc path) and the shell stores the latest on the active
`TabRuntime.ScrollTop`; on switch-back it's passed into `setDoc` and restored
after layout settles (the same double-rAF the reload path uses). Only a *re-show*
of the tab's current doc restores (`isNavigation == false`); a genuine navigation
resets to 0. **Live-tracking over synchronous capture-at-switch** keeps the switch
path synchronous — an `await ExecuteScriptAsync` at switch time would add a round
trip and make rapid switching re-entrant. Scroll reports are **path-matched**
against the active file so a stale report from a just-left doc can't clobber the
new tab. *Known limitation:* two tabs open to the **same file** can momentarily
share a restored offset on fast A→B→A switching (path-match can't tell them
apart); self-corrects on the next scroll.

**Known limitations.** A restored *folder-only* tab opens that folder's last file
(lazy-open reuses the folder-open path; a `restoreLastFile:false` attempt didn't
take and was reverted) — filed in `todo.md` Proposed.
