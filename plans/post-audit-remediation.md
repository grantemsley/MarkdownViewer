# Post-Audit Remediation

**Status:** ⏳ Phase 4 in progress - refactor executed & verified on branch `refactor/tab-identity-mainwindow-split` (11 commits, 428 tests green); PR blocked on gh re-auth; Grant reviews & merges, then graduate · Last updated 2026-07-11

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Phase 1: Critical bug fixes | shipped c5c77d5 / bda46be / eb4b623; 309 tests green; 1.3 used IsUserInitiated (see note) |
| ✅ Done | Phase 2: Robustness + dead-code sweep | shipped b8412df / 7b5e403 / bc1b374; 312 tests green; pipe ACL deferred, exported CSP needs manual check (see note) |
| ✅ Done | Phase 3: Author the Fable refactor prompt | written to `_files/fable-refactor-prompt.md`; grounded in Anthropic's Fable-5 prompting guide (see note) |
| ⏳ In progress | Phase 4: Fable refactor hand-off & integration | refactor done + fresh-context-verified on the branch; push/PR blocked on `gh auth login`; then Grant reviews & merges (see note) |

## Goal

Act on the 2026-07-11 Fable-5 code audit. The audit split findings into two natures with
opposite cost/fit profiles: a pile of small, discrete, individually-reviewable fixes (bugs +
robustness + dead code), and one long-horizon structural refactor (tab-identity model + the
2,170-line `MainWindow` god-object). This plan lands the discrete fixes directly (Phases 1-2,
cheap model, small commits, verified against the 302-test suite), then authors the prompt Grant
hands to a **Fable 5** session for the structural refactor (Phase 3). The two-nature split is
deliberate: fixing is write-heavy (output tokens at $50/M), so only the work whose *judgment*
changes the outcome — holding invariants across a big refactor — is worth Fable; the mechanical
fixes are wasted on it.

## Source

Full findings: `_files/audit-2026-07-11.md` (gitignored, not tracked — so the load-bearing detail
is summarized inline below and this plan stands alone). Four parallel Fable-5 agents covered
security, correctness/concurrency, architecture, robustness. Line numbers below were re-verified
against the working tree on 2026-07-11 and corrected where the agents drifted; re-confirm before
editing, since Phase 1 commits shift later anchors.

## Sequencing rationale (the seam between me and Fable)

- **Phase 1-2 avoid the bridge message-identity contract entirely.** Every Tier-2 race (stale
  outline adoption, cross-tab scroll clobber) wants the same fix: carry an explicit tab/runtime
  token through `setDoc` / `scroll` / `headings` instead of matching by active-tab or path string.
  That is Fable's job (Phase 3). If Phase 1 also edited those messages we'd collide. So Phase 1-2
  touch only fixes orthogonal to that contract.
- **The wrong-tab bug (1.1) gets a targeted point-fix now**, even though Fable's identity refactor
  later makes the whole class structurally impossible. It is a shipping HIGH-severity data
  corruption bug; ship the guard immediately, don't wait on a multi-day refactor.
- **Do NOT delete the `TabManager` routing API in the dead-code sweep.** It is dead today, but
  Fable's Phase 3 mission includes *wiring it* as the shipping routing path. Leave it in place.
- **Fable rebases onto committed Phase 1-2 work.** Phases 1-2 land and commit first; the Fable
  prompt (Phase 3) tells it to build on top of those commits, not redo them.

---

## ✅ Phase 1: Critical bug fixes

**Landed 2026-07-11** in commits c5c77d5 (1.1 + 1.3), bda46be (1.2), eb4b623 (1.4); full suite
309 tests green including a new `MaliciousMediaType_FallsBackToText_NoImg` regression test. Two
refinements vs. the sketch below, found while reading the real code:
- **1.3 uses `CoreWebView2NavigationStartingEventArgs.IsUserInitiated`**, not a raw-doc-mode gate.
  `_currentIframeUrl` is not reliably cleared on the markdown/text render paths (only `ShowEmpty`
  and `NavigateRaw` set it), so "empty means not-raw" was unsound. Blocking any non-user-initiated
  frame navigation is a cleaner, state-free discriminator: an injected `<iframe>` auto-loads
  (not user-initiated) and is blocked; a real link click in a raw doc still routes.
- **1.2's fallback path was verified safe.** A rejected image block falls to `ExtractResult`'s
  raw-text branch, which `AppendResult` wraps in a code fence (`AppendFence`), so the literal
  `<iframe` renders inert even under Markdig raw-HTML passthrough. The regression test therefore
  asserts "no `<img>` emitted" (the breakout is closed), not on the fenced intermediate markdown.

The four findings that are either data-corrupting or a real security hole. Each is a small,
contained diff. Commit each as its own logical unit. Add a cheap regression test where the logic
is pure (1.2). Manually verify 1.1 and 1.3 (WPF/WebView2 behavior, not unit-testable).

**1.1 — Wrong-tab stomp on startup scan continuation** (HIGH)
`src/MainWindow.xaml.cs` `InitializeAsync`, the `await scanTask` continuation (~line 316-333).

The generation guard `_vault.IsCurrentGeneration(scanGeneration)` re-resolves `_vault` through
`_active`. If the user switches tabs during the WebView2 warm-up await, the snapshot from tab A's
`VaultService` is compared against tab B's *different* instance (both usually at generation 1), the
stale continuation passes, and `FinishOpenVault(A)` runs against tab B — rendering A's doc in B and
permanently poisoning `Vaults.LastFile[B.root]`.

Fix: before `await scanTask`, capture the runtime and vault the scan belongs to:
```
var scanRuntime = _active;
var scanVault = _active.Vault;
```
(capture right where `scanGeneration = _vault.CaptureGeneration();` is taken, ~line 205). Then gate
on identity, not just generation:
```
await scanTask;
if (scanRuntime != _active || !scanVault.IsCurrentGeneration(scanGeneration))
    return;
```
Verify: 2-tab saved session, different folders each with a file; launch with no arg; within the
warm-up window click tab B and stay. Before fix: B's label flips to A's filename and A's doc
renders in B. After fix: B is untouched, A finishes silently into its own (inactive) runtime.

**1.2 — Unescaped transcript `media_type` → HTML injection** (MEDIUM, one line)
`src/Services/TranscriptService.cs` `TryRenderImage` (~line 627-634).

`srcAttr = "data:" + media + ";base64," + data;` interpolates `media` (JSON `source.media_type`)
raw into `<img src="...">`. `media` only passes `StartsWith("image/")`, so
`image/png"><iframe src=https://evil` breaks out and injects a live element (which then chains into
1.3). Fix: strict-validate the MIME instead of the loose prefix check — a real image MIME cannot
contain `" < >`:
```
if (!Regex.IsMatch(media, @"^image/[A-Za-z0-9.+-]+$")) return false;
```
Replace the existing `if (!media.StartsWith("image/", ...)) return false;` in the base64 branch with
this. `alt` already goes through `EscapeHtml`, so no other change. Add a unit test in the
transcript test file: a base64 image block whose `media_type` contains `">` returns false (falls
back to text), a clean `image/png` returns true.

**1.3 — Injected-iframe zero-click browser launch** (MEDIUM)
`src/MainWindow.xaml.cs` `Frame_NavigationStarting` (~line 1761-1789).

An `<iframe src="https://evil">` inside untrusted markdown/transcript content is created live by
bridge.js's `innerHTML` (iframes navigate on insertion), fires `FrameCreated` →
`Frame_NavigationStarting`, hits the `http(s)` branch, and calls `TryOpenExternal` →
`Process.Start` of the real browser — zero clicks, on render. The handler already distinguishes the
*intentional* raw-doc iframe via `_currentIframeUrl` (set by `NavigateRaw`); the gap is that in
markdown/transcript mode `_currentIframeUrl` is empty, so an injected iframe is treated like a
legit in-doc link.

Fix: only apply in-vault / external routing when a raw HTML doc is actually being displayed;
otherwise cancel the sub-frame navigation silently (a markdown render should never spawn a
navigating frame). Concretely, gate the routing on raw-doc mode and cancel-without-launch
otherwise. First confirm how raw mode is tracked (`_currentIframeUrl` non-empty is the current
signal; verify it is cleared when switching to a markdown/transcript doc — if not, that clearing is
part of this fix). Shape:
```
// Only a raw HTML doc legitimately drives sub-frame navigation.
if (string.IsNullOrEmpty(_currentIframeUrl))
{
    e.Cancel = true;   // injected iframe in a rendered doc: block, never launch
    return;
}
```
placed after the about:/blob: early-out and before the in-vault/external branches (which keep their
existing behavior for the raw-doc case). Verify: a `.md` containing
`<iframe src="https://example.com"></iframe>` renders with no browser launch; a link *click* inside
a raw `.html` doc still opens correctly.

**1.4 — Disposed VaultService restarts a never-disposed FileSystemWatcher** (MEDIUM)
`src/Services/VaultService.cs` `Dispose` (~line 570) and the `OpenAsync` continuation (~line
138-142).

`Dispose()` calls only `DisposeWatcher()`; it neither bumps `_openGeneration` nor sets a flag. An
in-flight `OpenAsync` continuation (`gen != _openGeneration` still false) then proceeds past line
138, assigns `RootNode`, fires `TreeChanged`, and calls `StartWatcher()` — a fresh live watcher on
a dead service that nothing disposes. Queued `BeginInvoke` callbacks also resurrect the debounce
`DispatcherTimer` via `EnsureDebounce`. Fix: add a `_disposed` flag, set it in `Dispose()`, and
check it at the three resurrection points:
```
private volatile bool _disposed;
public void Dispose() { _disposed = true; DisposeWatcher(); }
```
Then guard the continuation (`if (_disposed || gen != _openGeneration) return;` before
`StartWatcher`), and early-return in `EnsureDebounce` and `Flush` when `_disposed`. Verify: restore
a session whose active tab roots a slow/large folder, Ctrl+W it before the scan finishes; no watcher
survives (a debug log in `StartWatcher` should not fire for the closed tab).

**Close-out:** `dotnet build`, `dotnet test` (302 green + the new 1.2 test), commit each fix
separately, then update this phase's status in the table and the header together.

---

## ✅ Phase 2: Robustness + dead-code sweep

**Landed 2026-07-11** in b8412df (single-instance), 7b5e403 (file cap + null-settings + tests),
bc1b374 (MainWindow/CSP/dead-code); suite 312 tests green (3 new regression tests). Notes:
- **Pipe hot-spin confirmed real and root-fixed.** Reading `App.xaml.cs` showed a non-owning
  second instance (failed hand-off) still started a server because the gate was `_instanceMutex
  != null`. Fixed at the source (start the server only when we own the mutex) plus a listen-loop
  back-off and a per-connection read timeout as defense.
- **Pipe ACL (`PipeSecurity`) deferred** → belongs in `todo.md` `💡` at plan close-out. It needs
  the `System.IO.Pipes.AccessControl` package; the finding is LOW and bounded (the path only
  reaches the viewer, never `Process.Start`), so adding a dependency in this sweep wasn't worth it.
- **Per-image base64 cap dropped as redundant.** Rejecting an oversized image would fall it back to
  a raw-text fence containing the *same* payload (no size win). The 50 MB `ReadTextFile` cap bounds
  the whole transcript file, which bounds total image bytes — the right layer.
- **⚠ Exported-HTML CSP needs a manual check.** The nonce'd CSP on `BuildStandaloneHtml` output is
  standard, but I can't verify end-to-end without a browser: **export a doc containing a code block
  + a mermaid diagram, open it in a browser, and confirm both still render** (highlight.js and
  mermaid run under `script-src cdnjs 'nonce' 'unsafe-eval'`). If mermaid breaks, it likely needs an
  additional directive.

Lower-severity leaks/crashes/silent-failures and the dead-code list. All small and independent;
batch into a few logical commits (robustness, security defense-in-depth, deletions). Verify each
Tier-3 finding actually reproduces before "fixing" — at least one (pipe hot-spin) depends on
startup flow the audit did not fully trace.

**Robustness (Tier 3):**
- **Named-pipe hot-spin** — `src/Services/SingleInstanceServer.cs` `ListenLoopAsync` + the startup
  gate in `src/App.xaml.cs`. **Verify first:** read `App.xaml.cs`'s mutex/instance gate; a second
  process should detect the held mutex and `TrySignal`+exit, never reach `ListenLoopAsync`. If the
  gate is correct this is a non-issue (mark it so). If a second process *can* reach the listen loop
  when the name is held, `new NamedPipeServerStream(...,1,...)` throws, the bare `catch {}` swallows
  it, and the `while` loop spins a core. Fix if real: add a short back-off/`Task.Delay` in the
  non-cancellation catch.
- **Pipe read has no per-connection timeout** — `SingleInstanceServer.cs:44`
  `reader.ReadToEndAsync(ct)`; a client that connects and stalls wedges the listener. Add a linked
  `CancellationTokenSource` with a few-second timeout around the read.
- **Whole-file read+render on the UI thread, unbounded** — `src/Services/ContentRouter.cs`
  `ReadTextFile`/`ReadAllBytes` reached from `OpenFile`. Add a size cap (reject/placeholder over N
  MB) so a multi-GB `.log`/`.jsonl` can't freeze/OOM the window; also cap the base64 image payload
  size in `TranscriptService.TryRenderImage`. (Moving the read fully off-thread with cancellation is
  larger — note it as a `💡` follow-up if the cap alone is insufficient.)
- **Null-list settings NRE surfaces as a bogus "WebView2 init failed" dialog** —
  `src/Models/AppSettings.cs` `Normalize` replaces null sub-objects but not null lists/dicts
  (`Tabs.Sessions`, `Vaults.Recents/LastFile`, `Transcripts.VisibleCategories`). Coalesce those to
  empty in `Normalize`. Add a unit test: settings JSON with `"sessions": null` loads to defaults,
  no throw.
- **Drag-drop NRE → crash** — `src/MainWindow.xaml.cs` drop handler (~line 1989);
  `GetData(FileDrop)` can return null for delayed-render sources (Outlook attachments) despite
  `GetDataPresent`. Null-guard before `.Length`.
- **Exported temp HTML accumulates forever** — `MainWindow.xaml.cs` `OpenRenderedInBrowser` writes
  `%TEMP%\MarkdownViewer-*.html`, never cleaned. Best-effort sweep of prior `MarkdownViewer-*.html`
  on write (or on startup).
- **Static theme event pins the window; save timer never stopped** — `MainWindow.xaml.cs:122`
  (`ApplicationThemeManager.Changed +=`) and the `_settingsSaveTimer`. Unsubscribe the theme handler
  and stop the timer in `MainWindow_Closed`. Latent today (single window); cheap to make correct.

**Security defense-in-depth (all LOW):**
- **`img-src https:`** in `src/WebAssets/render.html` allows zero-click tracking beacons from
  untrusted markdown. Tighten to what the app actually needs (`'self' https://app.local data: blob:`)
  unless remote images are an intended feature — confirm intent before removing `https:`.
- **Exported HTML has no CSP and runs untrusted `<script>` at `file://`** — `BuildStandaloneHtml`
  (`MainWindow.xaml.cs` ~1119). Inject a restrictive `<meta http-equiv="Content-Security-Policy">`
  into the exported document (this is a user-initiated escalation path, not zero-click, so it's
  lower priority than the img-src one-liner).
- **Single-instance pipe has no ACL** — `SingleInstanceServer.cs`. Add a `PipeSecurity` DACL scoped
  to the current user on the `NamedPipeServerStream`. Bounded impact (the path only reaches the
  viewer, never `Process.Start`), so lowest priority in this batch.

**Dead code — delete (do NOT touch the TabManager routing API):**
- `TabRuntime.NeedsRerender` (`MainWindow.xaml.cs:543,582`) — write-only.
- `_pendingNavigation` (`MainWindow.xaml.cs:52`, guard ~1747) — only ever set to null.
- `ContentRouter.RawBrowserExts` — unreferenced.
- `Models/VaultNode.cs:137-144` `HeadingNode` — superseded by `HeadingViewModel`.
- `SettingsService.cs:96` `CrashLogPath` — unused; `App.xaml.cs:23-26` hand-rebuilds the same path
  (collapse to the one property to kill the drift).
- `AppSettings.cs:123` `WindowPrefs.SidebarTab` — "legacy; ignored on load" but still serialized;
  stop serializing.
- `bridge.js:535-537` — "find" no-op stub.

**Close-out:** build + full test run, logical-unit commits, update this phase's status in both
places.

---

## ✅ Phase 3: Author the Fable refactor prompt

**Done 2026-07-11.** Prompt written to `_files/fable-refactor-prompt.md` (gitignored). Grounded in
Anthropic's live Fable-5 prompting guide
(`platform.claude.com/.../prompting-claude-fable-5`) and the "Fable orchestrates, cheaper models
execute" pattern. Patterns baked in: crisp complete spec (Fable is strongest on well-specified
problems); explicit delegation of mechanical sub-work to Haiku/Sonnet subagents while Fable holds the
design; fresh-context verifier subagents; ground-progress-against-tool-results (anti-fabrication);
state boundaries + checkpoint-only-when-genuinely-blocked; run at `high`/`xhigh` effort, long and
async. Deliberately avoided any "echo/explain your reasoning" instruction, which triggers Fable's
`reasoning_extraction` refusal and silently falls back to Opus. The tree-search feature is included
only as a design-accommodation (shape the bridge/panel to admit it later), not a build target.

Deliverable: a single prompt saved to `_files/fable-refactor-prompt.md` (gitignored) that Grant
copies into a fresh **Fable 5** session (a separate run this plan does not execute). Grant runs it,
reviews the resulting branch/PR, and integrates.

**3a — Ground the prompt in Anthropic's current guidance (do this before drafting).** Fetch and
read, then cite in the prompt's construction notes:
- `platform.claude.com/docs/en/build-with-claude/prompt-engineering/prompting-claude-fable-5`
  (Fable-5-specific: effort, instruction-following, long runs, memory, scaffolding).
- The general prompt-engineering guide and the **subagent / orchestration** guidance — the
  documented "Fable 5 orchestrates, cheaper models execute" pattern. The prompt must explicitly
  instruct Fable to **delegate mechanical sub-work to lower-tier subagents** (Haiku/Sonnet) where
  judgment isn't needed — the behavior-preserving extractions, test scaffolding, mechanical
  find/replace across call sites — while Fable itself holds the design and invariants. This is the
  whole cost justification for using Fable here; if it doesn't delegate, it's an expensive way to do
  cheap work.
  (If the docs pages are blocked, fall back to the self-hosted research stack per global CLAUDE.md:
  `searxng` for search, `crawl4ai`/`scrapling` to fetch.)

**3b — The prompt must contain, at minimum:**
- **Mission**, two coupled workstreams:
  1. *Tab-identity model* — carry an explicit tab/runtime token through the C#↔JS bridge
     (`setDoc` / `scroll` / `headings`) and into the `/__vault/<id>/<rel>` URL, so doc/scroll/outline
     resolve by identity, not by "active tab" or path string. This structurally eliminates the
     wrong-tab class and fixes Tier-2 races 2.1 (stale outline adopted permanently; `showEmpty` posts
     no headings) and 2.2 (cross-tab scroll clobber when two tabs share a file). Also fold in 2.3
     (text-file reload resets scroll — `ShowText` ignores `_active.ScrollTop`) and 2.4 (restore
     clamps short before images/mermaid grow the doc) since they live in the same scroll/render code.
  2. *MainWindow god-object extraction* — pull `VaultUrlScheme` (VaultOrigin/VaultFileUrl/
     VaultDirBase/TryVaultRel/SplitAnchor/InjectBaseTag), `DocumentRenderer`/`HtmlExporter`, and a
     pure `LinkRouter` out of the 2,170-line `MainWindow.xaml.cs`, each unit-tested; make the tested
     `TabManager` routing API (`OpenInNewTab`/`OpenInCurrent`/`OpenFile`) the *shipping* path
     (`HandleIncomingFile`/`OpenNodeInNewTab` route through it); extract the 5-step tab-switch ritual
     into one `TransitionTo`; define bridge messages as C# records + one generic `Send<T>` and drop
     the pointless headings round-trip; clone `SortPrefs` per tab and apply prefs across all
     runtimes; split `OpenVaultCore` (no settings writes) from the Recents/Current wrapper.
- **Context**: repo path, that it is a WPF/WebView2 C# app, the 302-test suite is the safety net,
  the full audit lives at `_files/audit-2026-07-11.md`, and Phases 1-2 already shipped the discrete
  fixes (build on those commits, do not redo them).
- **Invariants to preserve** (behavior-preserving refactor): the shared single WebView2 across
  tabs; same-origin vault serving through `VaultPaths.ResolveWithinRoot` (the traversal gate);
  the sandboxed raw-`.html` `srcdoc` path; render.html CSP posture; all six viewer kinds render
  identically.
- **Method**: work on a branch; keep the suite green throughout and *add* tests for every extracted
  pure unit and for the identity fixes; delegate mechanical sub-steps to cheaper subagents (per 3a);
  land in reviewable commits, not one mega-diff.
- **Out of scope**: anything Phases 1-2 already fixed; new features from `todo.md`.
- **Deliverable from Fable**: a branch + PR (or a series of commits) Grant can review, with a short
  summary of what moved where and which invariants were checked.

**3c — After drafting**, review the prompt against 3a's guidance (is the delegation instruction
concrete? are invariants unambiguous? is success measurable — tests green + specific races gone?),
then save it to `_files/fable-refactor-prompt.md` and tell Grant it's ready to hand off.

---

## ⏳ Phase 4: Fable refactor hand-off & integration

Not executed by this plan's author - this is Grant's step. Grant runs the Phase-3 prompt
(`_files/fable-refactor-prompt.md`) in a Fable 5 session, reviews the branch/PR it produces, runs the
test suite, and integrates. At that point this plan graduates: load-bearing decisions (the
tab-identity model, the extraction boundaries) → dated files in `decisions/`; any deferred follow-on
→ `todo.md` (`💡` for Claude-surfaced items); then move this plan to `plans/finished/`.

**2026-07-11 - Fable run executed.** Both workstreams landed on branch
`refactor/tab-identity-mainwindow-split`: 11 reviewable commits, suite 312 → 428 green at every
commit, and a fresh-context verifier confirmed all 16 invariant/race checks (its one Low finding
fixed in the final commit). Races 2.1/2.2 are closed structurally (2.1: the headings round-trip no
longer exists, outline is populated host-side; 2.2: scroll gated on tab token + path,
regression-tested); 2.3/2.4 fixed in the same pass. The tab-identity decision is recorded in
`decisions/2026-07-11-tab-identity-model.md` (on the branch). **Remaining:** `gh auth login -h
github.com` (keyring token invalid), then
`git push -u origin refactor/tab-identity-mainwindow-split` and
`gh pr create --title "Tab identity + MainWindow decomposition (post-audit refactor)"
--body-file _files/pr-body.md`; Grant reviews the PR (body lists the known edge-case behavior
deltas), spot-checks the app manually (viewer kinds, tab switching, scroll restore on an
image-heavy doc), merges - then graduate this plan.

## Decisions baked in

- **Two-nature split** (discrete fixes by a cheap model now; structural refactor by Fable later)
  over one big Fable "fix everything" run — fixing is output-token-heavy, so Fable is reserved for
  the work whose judgment (holding invariants across the refactor) actually earns its price.
- **Point-fix the wrong-tab bug now** rather than waiting for the identity refactor to obviate it —
  ship the HIGH-severity safety immediately.
- **Phase 1-2 stay off the bridge message-identity contract** to avoid colliding with Fable's
  Phase-3 work; Tier-2 races are entirely Fable's.
- **Keep the dead `TabManager` routing API** — Fable wires it rather than us deleting it.
- **Audit + Fable prompt live in gitignored `_files/`** (Grant's call), so this tracked plan
  self-summarizes the load-bearing detail.
