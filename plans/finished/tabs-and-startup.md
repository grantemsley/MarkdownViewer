# Tabbed viewing, single-instance & faster startup

**Status:** ✅ Done · Last updated 2026-06-14 · all phases shipped & verified; #4 loading overlay deferred → `todo.md` Proposed

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | 1a. TabManager (pure logic + tests) | 16 tests green: routing (new-tab/replace), close math, session round-trip |
| ✅ Done | 1b. Wire TabManager into MainWindow | Per-tab runtime + gated vault events + SwitchToTab; verified (single-tab + switching) |
| ✅ Done | 2. Tab strip UI | Strip + switch + new/close + middle-click + keyboard; accent active-tab highlight; verified |
| ✅ Done | 3. New-tab affordances | Middle-click + right-click "Open in new tab"; two-line folder/file tab titles (✕ stays put); verified |
| ✅ Done | 4. Session restore | Reopen all tabs (active eager, rest lazy); drops gone roots; verified. Known limit: a restored folder-only tab opens that folder's last file (fix attempted + reverted) |
| ✅ Done | 5. Single-instance | Mutex + named pipe; default on; incoming file → new tab (pref); window activates; verified (hand-off + new tab + focus) |
| ✅ Done | 6. Preferences | Tabs / single-instance / incoming-file toggles; fixed same-folder replace-open render bug; verified |
| ✅ Done | 7. Startup latency | #2 early WebView2 + #3 ReadyToRun landed; #4 overlay deferred → todo Proposed (WebView2 airspace) |

## Goal

Add tabbed viewing where **each tab switches the whole window** (its own folder
tree + outline + rendered document), make it an optional, default-on feature,
then layer single-instance launch on top so double-clicking files reuses a warm
window instead of cold-starting a new process. Finish with three
tabs-independent startup-latency wins. The throughline is the perf complaint:
the cold-start delay is dominated by WebView2 spin-up paid once per process —
tabs + single-instance let repeated opens skip it, and #2/#3/#4 trim what's left.

## Decisions

Locked with Grant (★ = his answer to a design question; the rest are my defaults
for the lower-stakes choices — **veto any of these and I'll adjust**):

- **Tabs** are an optional feature, **default on**, toggled in Preferences;
  flipping it takes effect on **next launch** (startup-time decision).
- A tab switches the **whole window**: its own vault/folder-tree, open file,
  outline, and scroll. ★ **Independent per tab** — each tab owns its tree,
  expansion and scroll (a `VaultService`/watcher per tab); same-folder tabs do
  **not** share a tree.
- **One** WebView2, shared across all tabs. Switching tabs rebinds the sidebar
  and re-posts the active tab's document; rendered HTML is **cached per tab** so
  switches are instant, and **scroll position is preserved per tab**.
- ★ On launch, **restore all open tabs** (each tab's folder + file + which was
  active). Only the **active** tab renders at startup; background tabs scan/render
  lazily on first activation (keeps cold-start fast).
- **Sidebar single-click navigates within the current tab** (does not spawn a
  tab). **Middle-click** and the right-click **"Open in new tab"** always make a
  new tab. A **file** → new tab opens that file (its folder as the tab's root); a
  **folder** → new tab rooted at that folder, no file selected.
- ★ **Incoming file** (a file opened into the running window, e.g. double-click
  with single-instance on) defaults to **a new tab**; user-settable to **replace
  the current tab**. (Middle-/right-click always new-tab, regardless.)
- ★ The **"+"** button opens a **blank tab** ("open a folder" state).
- **Single-instance**, **default on**, Preferences toggle; also a startup-time
  decision (restart to apply). A 2nd launch hands its file path to the running
  window (which activates/raises) and exits. When off → today's multi-process
  behavior.
- **Tab strip:** full-width, directly below the title bar. Tab label = file name
  (folder name when no file); tooltip = full path. **Middle-click a tab closes
  it**; **closing the last tab closes the window**. Overflow = horizontal scroll.
- **Keyboard:** Ctrl+T (blank tab), Ctrl+W (close tab), Ctrl+Tab /
  Ctrl+Shift+Tab (next/prev), Ctrl+1–9 (jump to tab N).
- When **tabs are off**: no strip; middle-click / "Open in new tab" are
  hidden/no-op; behaves exactly like today.
- **Settings** additions are additive (no schema bump where possible, so existing
  settings survive).
- **Testable architecture (Tier 1, agreed 2026-06-14):** the tab logic lives in a
  pure, UI-agnostic **`TabManager`** (+ `TabState`/`TabSession` DTOs) with **no
  WPF/WebView2 dependencies**, unit-tested with xUnit (open-routing, new-tab vs
  replace, middle-click→new-tab, close/last-tab, active-index math, session
  save/restore). `MainWindow` becomes a thin view that drives `VaultService` +
  WebView2 off `TabManager` state. This makes the tab *decisions* auto-verifiable
  by Claude; only the binding/rendering stays manual. Chosen over end-to-end UI
  automation (FlaUI), which needs an interactive desktop and may not run headless.

**Out of scope (deferred, not v1):** drag-to-reorder tabs, tab pinning, per-tab
zoom, multiple top-level windows (single-instance is one window), tab groups.

## ✅ Phase 1a: TabManager (pure logic + tests)

The testable heart, split out per the Tier-1 decision so it carries no WPF and
is fully unit-tested before any view wiring.

- **`TabState`** (Models): plain data — `VaultRoot`, `File`, computed `Title`
  (file name → folder name → "New tab"). No `VaultService`/WebView here; the heavy
  vault/WebView wiring stays in the view and is created/torn down as tabs activate.
- **`TabSession`** (Models): serializable DTO `{ VaultRoot, File }` for restore.
- **`TabManager`** (Services): owns `List<TabState>` + `ActiveIndex`; methods
  `OpenBlankTab`, `OpenInNewTab(root,file)`, `OpenInCurrent(root,file)`,
  `OpenFile(root,file,OpenMode)` (the new-tab-vs-replace routing),
  `CloseTab(index)` (returns false when the last tab closes → caller closes the
  window; adjusts `ActiveIndex`), `Activate(index)`, `Serialize()`,
  `Restore(sessions, activeIndex, rootExists)`.
- **Tests:** new-tab vs replace; middle-click always new tab; close math
  (close-active selects a neighbor, close-last signals window close); blank tab;
  session round-trip; restore drops sessions whose root no longer exists.

## ✅ Phase 1b: Wire TabManager into MainWindow

**Landed + verified.** `_vault` is a property over the active tab's runtime
(`_active.Vault`); each tab owns a `VaultService` built by `CreateRuntime`, which
wires `TreeChanged`/`FolderChildrenChanged`/`ActiveFileChanged` **gated to the
active tab** (an inactive tab's on-disk change just flags `NeedsRerender`);
disposal disposes every tab's vault. `SwitchToTab` saves the outgoing tab's
file/outline into its runtime and rebinds the sidebar + content to the new tab
(`SaveActiveViewState`/`LoadActiveViewState`/`ActivateCurrentTab`). With one tab
the path is behaviour-identical to before (verified by launch). Per-tab scroll
restore was deferred (switching re-renders) — a later refinement, **since landed
(2026-06-14): live-tracked offset restored on switch-back; see `DESIGN.md`.**

## ✅ Phase 2: Tab strip UI

- A **header-only** strip (custom `ItemsControl`, not a `TabControl` — the content
  area is the shared WebView2/sidebar, not per-tab WPF content) in a new `Auto`
  row of the outer grid, **below `ui:TitleBar`**, full width.
- Each header: file-type icon + label + close `✕`; active tab highlighted.
  **Middle-click** a header closes that tab. A trailing **"+"** opens a blank tab.
- **Keyboard** (in `MainWindow_KeyDown`): Ctrl+T / Ctrl+W / Ctrl+Tab /
  Ctrl+Shift+Tab / Ctrl+1–9.
- **Closing the last tab closes the window.** Overflow scrolls horizontally.
- The whole strip is collapsed when `Tabs.Enabled` is false.

## ✅ Phase 3: New-tab affordances

- **Middle-click** a file/folder row in the sidebar → open in a new tab
  (`PreviewMouseDown`, `MiddleButton`). File → new tab on that file; folder → new
  tab rooted at that folder, no file.
- **Right-click context menu:** add **"Open in new tab"** to the `VaultNode`
  context menu for both files and folders (the menu already exists in
  `MainWindow.xaml`).
- Both affordances are hidden/disabled when tabs are off.

## ✅ Phase 4: Session restore

**Landed + verified.** `TabsPrefs.Sessions` (`List<TabSession>` of root+file) +
`ActiveIndex`, persisted via `PersistTabs()` on every tab/file change and on
close. On a plain launch (no file arg, tabs on) `RestoreTabsFromSession` rebuilds
all tabs — active one opened eagerly, the rest lazily on first activation
(`ActivateCurrentTab` opens an unopened tab's vault on demand). Tabs whose root no
longer exists are dropped (`TabManager.Restore`). A file arg or tabs-off falls
back to the single-folder path.

**Known limitation (accepted):** a restored **folder-only** tab opens that
folder's last-viewed file rather than staying file-less, because lazy-open reuses
the normal folder-open path (which restores the last file as a convenience). A
`restoreLastFile:false` threading fix was attempted and **reverted** — it didn't
take and the deviation is minor.

## ✅ Phase 5: Single-instance

- **`App.xaml.cs` / a small `SingleInstance` helper:** at startup acquire a
  per-user named `Mutex`. If **not** first → connect to a `NamedPipeClientStream`,
  send the file-path arg (or a bare "focus"), then exit(0). If **first** → run a
  `NamedPipeServerStream` listener on a background thread; on a message, marshal
  to the UI thread → open the file via the **incoming-file pref** (new tab /
  replace) and **activate/raise** the window (restore if minimized, bring to
  front).
- `_settings.SingleInstance.Enabled` (default true), read at startup. Off →
  today's multi-process behavior. Toggling needs a restart (it's a startup mutex).
- Edge cases: per-user pipe name; tolerate the holder mid-exit (fall back to
  normal launch if the pipe connect fails); single-file exe friendly (no extra
  process).

## ✅ Phase 6: Preferences

New **"TABS & WINDOW"** section in `PreferencesWindow` (pattern: `ToggleSwitch`
+ `Load()`/`Persist()`):

- **Use tabs** (default on) — subtitle "restart to apply".
- **Single instance (reuse one window)** (default on) — "restart to apply".
- **When a file is opened into the window:** New tab / Replace current (default
  New tab) — only meaningful with tabs on.

Plus the `AppSettings` model additions (`TabsPrefs`, `SingleInstancePrefs`, or a
combined `WindowingPrefs`) and Load/Persist wiring.

## ✅ Phase 7: Startup latency (#2 / #3 / #4)

Independent of tabs; each shippable on its own. #2/#3 landed; **#4 deferred** (see
below) → filed in `todo.md` `## Proposed`.

- **✅ #2 — earlier WebView2 env (landed):** `CoreWebView2Environment.CreateAsync`
  is now started as a `MainWindow` field initializer (`_envTask`, runs during
  construction, before the window is shown), and `InitializeAsync` just `await`s
  it — overlapping env creation with window layout/first paint. The async helper
  captures errors into the task so they still surface in `InitializeAsync`'s
  try/catch. (`EnsureCoreWebView2Async` still runs after `Loaded`.)
- **✅ #3 — ReadyToRun (landed):** `<PublishReadyToRun>true</PublishReadyToRun>` in
  the csproj. Verified it composes with `PublishSingleFile` + framework-dependent
  (`dotnet publish -r win-x64` → exe ~14.2 MB vs ~12.1 MB; the ~2 MB is the R2R
  native images). Only applies at publish with a RID; local build/test ignore it.
- **⛔ #4 — loading overlay (deferred → `todo.md` Proposed):** a naive WPF overlay over the WebView2 won't
  work — WebView2 is **HWND-hosted (airspace)**, so WPF content in that cell paints
  *behind* the web content (the Find bar already dodges this with a `Popup`). A
  correct overlay needs either a `Popup` (own HWND) sized over the WebView region,
  or keeping the WebView2 hidden until first paint and showing a WPF panel in its
  place — and *which* airspace approach actually works needs a GUI run to confirm.
  Parked rather than ship a blind overlay that could stick over content or hide
  uselessly behind a blank WebView2.
