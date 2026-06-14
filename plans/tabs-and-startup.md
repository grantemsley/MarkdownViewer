# Tabbed viewing, single-instance & faster startup

**Status:** ⏳ In progress · Last updated 2026-06-14 · #2/#3 landed; tab feature (Ph 1–6) + #4 parked pending a run/verify session

| Status | Phase | Notes |
|---|---|---|
| ⬜ Not started | 1. Tab model & switching core | **PARKED** — large MainWindow refactor, no UI test coverage; needs verify-as-we-go (see body) |
| ⬜ Not started | 2. Tab strip UI | Parked behind Phase 1 |
| ⬜ Not started | 3. New-tab affordances | Parked behind Phase 1 |
| ⬜ Not started | 4. Session restore | Parked behind Phase 1 |
| ⬜ Not started | 5. Single-instance | Parked behind Phase 1 (default-on; a bug breaks every launch — verify needed) |
| ⬜ Not started | 6. Preferences | Parked behind Phase 1 |
| ⏳ In progress | 7. Startup latency | #2 early WebView2 + #3 ReadyToRun landed; #4 overlay parked (airspace — see body) |

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

**Out of scope (deferred, not v1):** drag-to-reorder tabs, tab pinning, per-tab
zoom, multiple top-level windows (single-instance is one window), tab groups.

## ⬜ Phase 1: Tab model & switching core

> **Parked (grind, 2026-06-14).** This and Phases 2–6 are a large MainWindow-level
> WPF feature with **no unit-test coverage of the UI wiring** and an explicit need
> to be run to verify. Grinding it blind would (a) risk silently regressing the
> working single-pane app (tests wouldn't catch it), and (b) stack the strip /
> affordances / restore / single-instance on an unverified foundation. Best done
> in a session where each phase can be launched and eyeballed — verify Phase 1 is
> behavior-preserving, then build the visible layers. Nothing here is blocked on a
> *decision* (all locked above); it's blocked on **GUI verification**, which is
> Grant's to do. Resume by implementing Phase 1 with the app runnable between steps.

The structural refactor everything else builds on. Today `MainWindow` holds a
single `_vault` (`VaultService`) + `_currentMdFile` + `_currentIframeUrl` +
outline + scroll. Extract that into a per-tab bundle and let `MainWindow` own a
collection.

- **New `DocumentTab`** (e.g. `src/Models/DocumentTab.cs` or `ViewModels.cs`):
  owns `VaultService Vault`, `string? CurrentFile`, `string? CurrentIframeUrl`,
  outline roots (`List<HeadingViewModel>`), `double ScrollTop`, a cached render
  payload (the last `setDoc` object) for instant re-show, `bool NeedsRerender`
  (set when an inactive tab's file changes on disk), and a computed `Title`.
- **MainWindow** gains `ObservableCollection<DocumentTab> _tabs` and
  `DocumentTab _activeTab`. Replace direct `_vault`/`_currentMdFile` reads with
  `_activeTab.Vault` / `_activeTab.CurrentFile`. The shared, app-level state
  (WebView2, `_settings`, find, update banner, Open popup) stays on MainWindow.
- **Per-tab vault events:** when a tab is created, subscribe its vault's
  `TreeChanged` / `ActiveFileChanged` / `FolderChildrenChanged`. Handlers drive
  the UI **only for the active tab** (the FolderTree is bound to the active tab's
  `RootNode`); an inactive tab's reconcile just mutates its own (unbound) tree,
  and an inactive tab's file-content change sets `NeedsRerender`.
- **Switch active tab:** save the outgoing tab's `ScrollTop` (JS round-trip via a
  new `getScroll`/`scroll` bridge message), set `_activeTab`, rebind
  `FolderTree.ItemsSource` → new tab's `RootNode` and `OutlineTree.ItemsSource` →
  new tab's outline, re-post its doc (from cache, or re-render if `NeedsRerender`),
  then restore `ScrollTop`.
- **Feature flag** `_settings.Tabs.Enabled` (default true), read once at startup.
  When false: exactly one implicit tab, no strip — identical to today.
- Risk: this touches most of `MainWindow.xaml.cs`. Do it behind the flag and keep
  the single-tab path behaving identically so nothing regresses.

## ⬜ Phase 2: Tab strip UI

- A **header-only** strip (custom `ItemsControl`, not a `TabControl` — the content
  area is the shared WebView2/sidebar, not per-tab WPF content) in a new `Auto`
  row of the outer grid, **below `ui:TitleBar`**, full width.
- Each header: file-type icon + label + close `✕`; active tab highlighted.
  **Middle-click** a header closes that tab. A trailing **"+"** opens a blank tab.
- **Keyboard** (in `MainWindow_KeyDown`): Ctrl+T / Ctrl+W / Ctrl+Tab /
  Ctrl+Shift+Tab / Ctrl+1–9.
- **Closing the last tab closes the window.** Overflow scrolls horizontally.
- The whole strip is collapsed when `Tabs.Enabled` is false.

## ⬜ Phase 3: New-tab affordances

- **Middle-click** a file/folder row in the sidebar → open in a new tab
  (`PreviewMouseDown`, `MiddleButton`). File → new tab on that file; folder → new
  tab rooted at that folder, no file.
- **Right-click context menu:** add **"Open in new tab"** to the `VaultNode`
  context menu for both files and folders (the menu already exists in
  `MainWindow.xaml`).
- Both affordances are hidden/disabled when tabs are off.

## ⬜ Phase 4: Session restore

- **Settings:** `TabsPrefs.Sessions` = `List<TabSession>` where `TabSession =
  { string VaultRoot, string? File }`, plus `ActiveIndex`. Persisted debounced on
  change and on close (reuse `ScheduleSave`).
- **On launch (tabs on):** recreate a tab per `TabSession`. Only the **active**
  tab opens its vault + renders eagerly; the rest construct lazily and
  scan/render on first activation — so N restored tabs don't pay N cold renders.
- **Tabs off:** restore only the last active file (today's behavior), ignoring the
  session list.
- Drop sessions whose `VaultRoot` no longer exists.

## ⬜ Phase 5: Single-instance

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

## ⬜ Phase 6: Preferences

New **"TABS & WINDOW"** section in `PreferencesWindow` (pattern: `ToggleSwitch`
+ `Load()`/`Persist()`):

- **Use tabs** (default on) — subtitle "restart to apply".
- **Single instance (reuse one window)** (default on) — "restart to apply".
- **When a file is opened into the window:** New tab / Replace current (default
  New tab) — only meaningful with tabs on.

Plus the `AppSettings` model additions (`TabsPrefs`, `SingleInstancePrefs`, or a
combined `WindowingPrefs`) and Load/Persist wiring.

## ⏳ Phase 7: Startup latency (#2 / #3 / #4)

Independent of tabs; each shippable on its own.

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
- **⛔ #4 — loading overlay (parked):** a naive WPF overlay over the WebView2 won't
  work — WebView2 is **HWND-hosted (airspace)**, so WPF content in that cell paints
  *behind* the web content (the Find bar already dodges this with a `Popup`). A
  correct overlay needs either a `Popup` (own HWND) sized over the WebView region,
  or keeping the WebView2 hidden until first paint and showing a WPF panel in its
  place — and *which* airspace approach actually works needs a GUI run to confirm.
  Parked rather than ship a blind overlay that could stick over content or hide
  uselessly behind a blank WebView2.
