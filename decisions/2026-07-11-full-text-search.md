# Full-text folder-tree search: hand-rolled, allowlist-first, transient-per-tab

*Decided 2026-07-11.*

**Decision.** A cross-tree search in the sidebar: type a word/phrase, walk the
active tab's whole folder tree, match both **file names** and **file contents**,
and list hits where the FOLDER tree normally sits. Click a hit to open the file in
the current tab and (for a content hit) scroll to the match.

Load-bearing choices:

- **Hand-rolled walk, not bundled ripgrep.** A UI-agnostic `FileSearchService`
  (unit-tested like `TabManager`/`VaultService`): a lazy recursive enumerator feeds
  `Parallel.ForEachAsync` (default DOP 8, above core count to hide SMB latency).
  Chosen over shelling out to ripgrep for parity with the viewer's own file
  classification/decoding and a tight click -> open-in-this-app integration; over
  SMB the walk is I/O-bound, so rg's raw-match speed edge is small.
- **Allowlist-first content scanning.** Only files whose extension is in the
  viewer's known-text set (`ContentRouter.KnownTextExtensions`, within a size cap)
  get their bytes read; every other file is **filename-matched only** (free). This
  is the main SMB lever: no `.png`/`.zip`/`.exe` is ever pulled over the wire. A
  `ScanAllText` pref (default off) adds a binary-peek path for unknown extensions.
- **Filename matches apply to every file** (incl. binaries and over-cap files) -
  it costs nothing beyond the directory enumeration already happening.
- **Scroll-to-match reuses `CoreWebView2Find`.** A content-hit click opens the file
  and, once bridge.js acks the render (`DocRenderedMsg`), the host highlights the
  term and scrolls to the first match. Works uniformly for markdown and text (both
  render into `#page`) and sidesteps markdown source-line mapping; content hits only
  ever occur in allowlisted text, which always renders there.
- **No settings schema bump.** `SearchPrefs` is additive; a v2 `settings.json`
  lacking the key coalesces via `AppSettings.Normalize`. Bumping
  `SettingsSchema.Current` would discard every user's settings (the loader has no
  migration ladder). The `SearchPrefs -> SearchOptions` resolution lives in Services
  (`SearchOptions.From`) so Models stays pure.
- **Search is active-tab-scoped and transient.** Running a search replaces that
  tab's tree with results; any tab transition (`TransitionTo`) clears it and the
  arriving tab shows its own tree.

**Why.** Full-text-over-a-tree is a solved problem, so ripgrep was the obvious
prior-art candidate; it lost on integration, not capability - the whole value is
"find in MY viewer's terms and open in MY viewer at the match," and SMB caps rg's
speed upside. The allowlist/size filters aren't just UX conveniences: they are the
mechanism that keeps the walk responsive over a network share, because bytes-pulled
is the dominant cost. Reusing the existing find-in-page engine avoided building a
second highlight/scroll path and the markdown source-map it would have needed.

**Consequences / caveats.**
- Not an index: on-demand grep, re-walked each search. Fine for interactive use;
  a persistent index is the natural phase-2 for repeat searches over slow shares.
- `.jsonl` transcripts are name-matched only (not in the text allowlist; they render
  specially, so a content hit's line wouldn't map to the rendered view).
- Scroll-to-match doesn't reach inside raw docs (PDF/HTML iframes) - but those never
  content-match (not allowlisted / binary), so it never comes up.
- Global hit cap (`MaxTotalHits`, default 5000) stops the walk and marks the result
  truncated rather than running unbounded; surfaced in the status line, not silent.
- Deferred (phase 2): regex, case-sensitive/whole-word toggles, subtree-scoped
  search, per-tab search persistence, a persistent index.
