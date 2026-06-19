# Tabbed viewing: one shared WebView2, independent tab state bundles

*Decided 2026-06-14.*

**Decision.** Each tab switches the whole window - its own `VaultService` (folder tree +
watcher), open file, and outline. Tabs are independent (no shared tree, even for same-folder
tabs). One shared WebView2; switching re-binds the sidebar and re-renders the active tab's
doc. Decision logic lives in a pure, UI-agnostic `TabManager` (+ `TabState`/`TabSession`) so
it is unit-tested without WPF; `MainWindow` is the thin view (per-tab `TabRuntime`, vault
events gated to the active tab). Tabs are optional, default on (`TabsPrefs.Enabled`,
startup-time); off = the old single pane.

Single-instance (`SingleInstanceServer`, default on): a per-user named mutex + named pipe. A
second launch hands its file path to the owner and exits; the owner opens it per
`OpenIncomingInNewTab` (new tab / replace) and takes the foreground. The second process
grants foreground rights via `AllowSetForegroundWindow` so a plain `Activate()` works.
Hand-off failure = normal launch (worst case a second window, never a hang).

Startup: WebView2 env creation kicked off in the `MainWindow` field initializer (overlaps
window paint); `PublishReadyToRun` trims JIT. A blank-pane loading overlay was deferred -
WebView2 is HWND-hosted (airspace), so a WPF overlay needs a Popup/hide-until-paint approach
plus GUI iteration.

**Why.** One shared WebView2 because multiple WebView2 instances = multiple browser processes
= unacceptable cold-start penalty. Tab state in `TabManager` (not `MainWindow`) because the
decision logic is non-trivial (routing, middle-click, session round-trip, close math) and
deserves unit testing without WPF standing in the way (16 tests).

`AllowSetForegroundWindow` was required because a plain `Topmost` nudge blocked other
windows' focus when routing incoming files.

**Consequences / caveats.**
- Session restore: restored tabs are literal - no auto-open of the last file within a
  folder-only tab on launch. Only the active tab's file renders immediately; others render
  lazily on first click.
- A restored folder-only tab opens the sidebar with a "pick a file" placeholder rather than
  auto-opening the last file (lazy-open reuses the folder-open path; a `restoreLastFile:false`
  attempt didn't take and was reverted). Filed in `todo.md` Proposed.
- Preferences: "Use tabs" requires restart; "Single instance" requires restart; "Open files
  from outside in a new tab" is live. All stored in `AppSettings`.
