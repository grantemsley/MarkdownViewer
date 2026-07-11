# Full-text search across the open folder tree

**Status:** ⏳ In progress · Last updated 2026-07-11

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Search engine service + shared ContentRouter helpers | `FileSearchService` + ContentRouter helpers; 35 tests, suite 463 green |
| ✅ Done | Search settings model (`SearchPrefs`) | on AppSettings, no schema bump; `SearchOptions.From`; 9 tests, suite 472 |
| ✅ Done | Sidebar search panel (replace the tree) | box + Ctrl+Shift+F + Enter/button; streamed+batched results; cancellation wired |
| ✅ Done | Result activation (open + scroll-to-match) | `DocRenderedMsg` ack + CoreWebView2Find; launch smoke-test clean |
| ✅ Done | Preferences "Search" section | max size, include/exclude ext, exclude folders, scan-all, hidden; builds green |
| ⏳ In progress | Verify + graduate | manual matrix (user), decision doc, todo/README |

## Goal
Add a cross-tree search to the sidebar: type a word/phrase, and the viewer walks
the active tab's whole folder tree (not the lazy one-level tree it browses with),
matching both **file names** and **file contents**, and lists the hits where the
FOLDER tree normally sits. Clicking a hit opens the file in the current tab and
scrolls to the match. It must stay responsive on a large tree reached over SMB,
so the walk is bounded-parallel, cancellable, and reads as few bytes off the wire
as the filters allow. Search is on-demand grep (no persistent index).

## Design decisions (baked in; alternatives were real)
- **Hand-rolled walk, not bundled ripgrep** (Grant's call). Buys parity with the
  viewer's own file classification/decoding and a tight click -> open-in-this-app
  integration; over SMB we're latency-bound, so rg's raw-match speed edge is small.
- **Allowlist-first content scanning.** Only files whose extension is in the
  viewer's known-text set (`ContentRouter`) get their bytes read and line-scanned.
  Everything else is **filename-matched only** (free - no content read). This is
  the main SMB lever: we never pull a `.png`/`.zip`/`.exe` over the wire. A pref
  (`ScanAllText`, default off) turns on a binary-peek path for unknown extensions.
- **Filename matches apply to every file**, including binaries and over-cap files -
  it costs nothing beyond the directory enumeration we already do.
- **Reuse `CoreWebView2Find`** (the existing find-in-page engine) for
  scroll-to-match. Works uniformly for markdown and text docs (both render into
  `#page`), sidesteps the markdown source-line-mapping problem entirely. Content
  matches only ever occur in allowlisted text, which always renders in `#page`.
- **No settings schema bump.** `SearchPrefs` is added as a new sub-object;
  an old `settings.json` lacking the `search` key deserializes to the property's
  default and `Normalize()` null-coalesces it, exactly like the other sub-prefs.
  Bumping `SettingsSchema.Current` would discard every user's settings (the loader
  has no migration ladder), so we must not.
- **Search is active-tab-scoped and transient in v1.** Running a search replaces
  the active tab's tree with results; switching tabs restores that tab's tree and
  drops the search. Per-tab search persistence is deferred (see Non-goals).
- **Read strategy: cap-then-decode-then-split**, reusing `ContentRouter`'s decode
  chain (BOM -> UTF-8 strict -> cp1252) rather than a bespoke line-streaming
  decoder. With a small size cap and bounded parallelism, holding one capped file
  in memory per worker is fine, and it keeps match semantics identical to what the
  viewer will show.

## Non-goals (v1) - punch out so nobody drifts
Regex; case-sensitive / whole-word toggles; a persistent index; replace-in-files;
subtree-scoped ("search in this folder") search; per-tab search persistence;
scroll-to-match inside raw docs (PDF/HTML iframes). Each is a clean phase-2 add on
top of this structure.

## ✅ Phase 1: Search engine service + shared ContentRouter helpers
The UI-agnostic core, unit-testable in full like `TabManager`/`VaultService`.
Landed: `ContentRouter.IsKnownTextExtension`/`KnownTextExtensions`/`DecodeCappedFile`
(+ `LooksBinary` made public); `src/Services/FileSearchService.cs` (records +
bounded-parallel walk); `tests/.../FileSearchServiceTests.cs` (35 cases). Suite 463 green.

1. **Expose the text-classification + decode from `ContentRouter`** (single source
   of truth; today the ext maps and `DecodeBytes` are private). Add:
   - `public static bool IsKnownTextExtension(string ext)` - true when `ext` is in
     `MarkdownExts` ∪ `HighlightLang.Keys` ∪ `PlainTextExts` (the same union the
     text viewer renders). Used to build the default allowlist and to gate scanning.
   - `public static IReadOnlyCollection<string> KnownTextExtensions { get; }` - the
     union above, for seeding `SearchPrefs` defaults.
   - `public static string DecodeCappedFile(string path, long maxBytes, out bool truncated)`
     - factor the existing capped-read + `DecodeBytes` out of `ReadTextFile` so both
     the viewer and search share one decoder. `ReadTextFile` becomes a thin caller.

2. **New file `src/Services/FileSearchService.cs`** - static, no WPF/WebView/Vault
   coupling. Public surface:
   ```csharp
   public enum SearchHitKind { FileName, Content }
   public sealed record SearchHit(int Line, string Preview, int MatchStart, int MatchLength);
   public sealed record SearchFileResult(
       string FullPath, string RelPath, bool NameMatched,
       IReadOnlyList<SearchHit> Hits, int FilesScannedSoFar);
   public sealed record SearchSummary(
       int FilesScanned, int FilesMatched, int TotalHits, bool Truncated, bool Cancelled);
   public sealed record SearchOptions(
       long MaxFileBytes, IReadOnlySet<string> AllowedExtensions,
       IReadOnlySet<string> ExcludedDirNames, bool IncludeHidden, bool ScanAllText,
       int MaxDegreeOfParallelism, int MaxHitsPerFile, int MaxTotalHits);
   public static Task<SearchSummary> SearchAsync(
       string root, string query, SearchOptions options,
       IProgress<SearchFileResult> onFile, CancellationToken ct);
   ```

3. **The walk (producer).** A private `IEnumerable<string> EnumerateFiles(root, options, ct)`
   using an explicit directory stack (not `Directory.EnumerateFiles(..., AllDirectories)`,
   which throws on the first denied dir and can't skip subtrees). Per directory:
   swallow `UnauthorizedAccessException`/`IOException` and continue; **skip reparse
   points** (junction/symlink cycle guard - same rule as `VaultService.PopulateChildren`);
   skip directories whose name is in `ExcludedDirNames`; skip hidden dirs unless
   `IncludeHidden`. Yields file paths lazily so `ct` cancellation stops enumeration.

4. **The scan (consumers).** `await Parallel.ForEachAsync(EnumerateFiles(...), new
   ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
   CancellationToken = ct }, ...)`. DOP defaults higher than core count (8) to hide
   SMB per-file latency. Per file:
   - **Filename match:** `Path.GetFileName(path).IndexOf(query, OrdinalIgnoreCase) >= 0`
     -> `NameMatched = true`. Applies to *every* file, no read.
   - **Content match** only if the extension is allowed (or `ScanAllText` and, for
     unknown exts, `!ContentRouter.LooksBinary(path)`) **and** file size ≤
     `MaxFileBytes` (size comes free from enumeration - stat once). Read via
     `ContentRouter.DecodeCappedFile`, split into lines, and for each line
     `IndexOf(query, OrdinalIgnoreCase)`; collect up to `MaxHitsPerFile` hits with
     line number, a trimmed preview, and match offset/length.
   - If `NameMatched` or any content hits, report one `SearchFileResult` via
     `onFile` (natural batching: one report per matched file, never per line).
   - Maintain thread-safe counters (`Interlocked`) for scanned/matched/total hits;
     when `TotalHits` reaches `MaxTotalHits`, set a `truncated` flag and stop
     reporting further hits (cancel the loop). Return `SearchSummary` at the end;
     `OperationCanceledException` -> `Cancelled = true`.

5. **Tests** in `tests/MarkdownViewer.Tests` (`FileSearchServiceTests.cs`), over a
   temp dir tree: filename-only match on a binary/over-cap file; content match with
   correct line numbers; case-insensitivity; extension allowlist excludes a
   non-text file's contents; size cap skips content but still name-matches; excluded
   dir name is not descended; hidden file skipped unless `IncludeHidden`; reparse
   point not followed (skip on filesystems without symlink perms); `MaxHitsPerFile`
   and `MaxTotalHits` caps + `Truncated`; cancellation returns `Cancelled`.

## ✅ Phase 2: Search settings model (`SearchPrefs`)
Landed: `SearchPrefs` on `AppSettings` (+ `Search ??= new()` / `Search.Normalize()`
in `AppSettings.Normalize`, no schema bump); the `SearchPrefs -> SearchOptions`
resolution lives in Services as `SearchOptions.From` (keeps Models pure); 9 tests.

1. In `src/Models/AppSettings.cs`, add `public SearchPrefs Search { get; set; } = new();`
   to `AppSettings`, and `Search ??= new();` in `Normalize()` (with the other
   coalesces). **Do not touch `SettingsSchema.Current`.**
2. `SearchPrefs` with sane prepopulated defaults:
   ```csharp
   public class SearchPrefs
   {
       public long MaxFileBytes { get; set; } = 5L * 1024 * 1024;      // 5 MB
       public List<string> IncludeExtensions { get; set; } = new();    // empty = ContentRouter default text set
       public List<string> ExcludeExtensions { get; set; } = new();    // subtracted from the effective set
       public List<string> ExcludeFolders { get; set; } =
           new() { ".git", "node_modules", "bin", "obj", ".vs" };
       public bool ScanAllText { get; set; }                           // default false (allowlist-only)
       public bool IncludeHidden { get; set; }                         // default false
       public int MaxDegreeOfParallelism { get; set; } = 8;
       public int MaxHitsPerFile { get; set; } = 50;
       public int MaxTotalHits { get; set; } = 5000;
       public void Normalize() { /* clamp DOP 1..64, MaxFileBytes >= 64KB, caps >= 1 */ }
   }
   ```
   Empty `IncludeExtensions` means "use `ContentRouter.KnownTextExtensions`"; a
   non-empty list overrides it. `ExcludeExtensions` is always subtracted. A pure
   builder `SearchPrefs.ToOptions()` -> `SearchOptions` resolves the effective
   allowed-extension set once, so the walk doesn't recompute it per file.
3. Call `Search.Normalize()` from `AppSettings.Normalize()`.

## ✅ Phase 3: Sidebar search panel (replace the tree)
Landed: search box + 🔍 button + status line + results `ListBox` in the FOLDER
pane (`MainWindow.xaml`); `SearchRowVM` in `ViewModels.cs`; `RunSearch`/`ClearSearch`/
`CancelSearch` + an 80 ms flush timer that batches streamed results; `Ctrl+Shift+F`
focus (ahead of the `Ctrl+F` arm); cancellation on new search / Esc / tab transition
(`TransitionTo`) / window close. Result click opens the file (scroll deferred to P4).

All wiring is in `MainWindow.xaml` / `MainWindow.xaml.cs`; the FOLDER pane is the
`DockPanel` at `MainWindow.xaml` Grid.Row 0 (the `FolderTree` TreeView).

1. **XAML.** In the FOLDER `DockPanel`, dock a search row under the "FOLDER" header:
   a `ui:TextBox x:Name="SearchBox"` (PlaceholderText "Search files (Enter)") plus a
   small search/clear button. Add a `ListView x:Name="SearchResults"` sharing the
   fill area with `FolderTree`, `Visibility="Collapsed"` by default. Only one of
   `FolderTree` / `SearchResults` is visible at a time. Header text swaps to
   "SEARCH RESULTS" with a live count / spinner while active.
2. **Results template.** `SearchResults` is bound to
   `ObservableCollection<SearchResultRow>` where a row is either a **file header**
   (RelPath + hit count, or "(filename match)") or a **match line** (line no +
   trimmed preview with the matched span emphasized). Simplest structure: an
   `ItemsControl` of file groups, each group an inner list of its hit rows; a
   filename-only group is a single clickable header. Keep it flat/non-collapsible in
   v1.
3. **Trigger + gating.** Add to `MainWindow_KeyDown` (next to the `Ctrl+F` arm at
   `MainWindow.xaml.cs:1937`): `Ctrl+Shift+F` -> focus/show the search box (swap the
   pane to search mode). `SearchBox` `KeyDown`: **Enter** runs the search, **Esc**
   clears it and restores the tree. A search button mirrors Enter. Require
   `query.Trim().Length >= 3`; below that, Enter is a no-op (brief hint). No
   search-as-you-type.
4. **Run + stream.** On Enter: cancel any in-flight search (`CancellationTokenSource`
   swap), clear the results collection, snapshot `root = _vault.Root` and
   `_settings.Search.ToOptions()`, then `FileSearchService.SearchAsync(root, query,
   opts, progress, cts.Token)`. `progress` is a `Progress<SearchFileResult>`
   (captures the UI context) that appends rows to the collection; add a light
   `DispatcherTimer` buffer-flush (~80 ms) so a fast tree can't flood the UI thread
   with per-file dispatches. Show a running result count + spinner; on completion
   show the `SearchSummary` line ("N matches in M files · scanned K · truncated?").
5. **Cancellation lifecycle.** Cancel the search on: new search, Esc/clear, tab
   switch (`SwitchToTab`/`TransitionTo`), tab close (`CloseTabAt`), and window close.
   Restoring the tree (`OnVaultTreeChanged`/`LoadActiveViewState` show `FolderTree`)
   also hides `SearchResults`.

## ✅ Phase 4: Result activation (open + scroll-to-match)
Landed: `DocRenderedMsg` inbound record + parse arm; bridge.js `postDocRendered`
after markdown/text render; host stashes `_pendingFindTerm`/`_pendingFindPath` on a
content-hit click and, on the matching ack for the active tab, runs
`ScrollToSearchMatch` (CoreWebView2Find highlight + scroll to first match).
Filename-only rows just open at top. +1 parse test (suite 473).

1. **Click.** A match row or filename-only header click -> `OpenFile(fullPath)`
   (the existing single-tab navigation; results panel stays visible so the user can
   work down the list). Store the query as `_pendingFindTerm` and the target path as
   `_pendingFindPath` when the clicked row is a **content** match; leave them null
   for a filename-only match (nothing to scroll to).
2. **docRendered ack.** Add an inbound bridge message so the find runs only after the
   new doc is actually in the DOM (avoids finding in the previous doc):
   - `BridgeMessages.cs`: `public sealed record DocRenderedMsg(string TabId, string Path);`
     add a `"docRendered"` arm to `BridgeInbound.Parse` (tabId + path, mirroring
     `scroll`).
   - `bridge.js`: at the end of `setMarkdown` and `setText`, post
     `{ type:"docRendered", tabId: currentTabId, path: scrollPath }`.
   - `WebView_WebMessageReceived`: on `DocRenderedMsg`, if it names the active tab
     and `_pendingFindPath` (OrdinalIgnoreCase), run the find and clear the pending
     state.
3. **Run the find** by reusing the find-in-page engine: pre-fill `FindBox.Text =
   term`, `OpenFindBar()` (its `TextChanged` calls `_find.StartAsync` and scrolls to
   the first match), or call `_find.StartAsync` directly with a fresh
   `CreateFindOptions()`. The user lands on the file with the match highlighted and
   `1/N` shown, and can Enter/Shift+Enter through matches. Note: content matches only
   occur in markdown/text (rendered into `#page`), which is exactly where
   `CoreWebView2Find` operates - raw/image never content-match.

## ✅ Phase 5: Preferences "Search" section
Landed: a SEARCH group in `PreferencesWindow` (max file size MB, include/exclude
extension lists, exclude folders, scan-all-text, search-hidden) wired through
`Load`/`Persist` (+ `ParseCommaList`, `Search.Normalize()` on save). DOP/hit caps
stay settings.json-only. Reads live from `_settings.Search` so the next search
picks up changes with no extra wiring.

Add a "Search" group to `PreferencesWindow` (find it and mirror an existing section's
pattern): **Max file size to search** (MB, numeric, clamps to `SearchPrefs.Normalize`),
**Include extensions** / **Exclude extensions** (comma lists; empty include = default
text set, with the resolved default shown as placeholder/hint), **Exclude folders**
(comma list, prefilled with the defaults), and toggles for **Scan all text files**
and **Include hidden files**. Persist through the same save path the other prefs use;
`SearchPrefs.Normalize` runs on load. Leave DOP / hit caps out of the UI (settings.json
only) unless they prove worth surfacing.

## ⏳ Phase 6: Verify + graduate
1. **Manual matrix** (real trees, incl. one over an SMB share): filename hit on a
   non-text file; content hit opens + scrolls to the line; phrase with spaces;
   case-insensitivity; a big excluded dir (`node_modules`) is skipped; size cap skips
   a huge `.log` for content but still name-matches it; cancel a long search
   mid-flight (Esc / new query / tab switch) with no hang or stale rows; empty and
   "too many results (truncated)" states; unreadable/denied folder doesn't abort the
   walk. Use `/run` to drive the app.
2. **Decision doc** `decisions/2026-07-11-full-text-search.md`: hand-rolled vs
   ripgrep, allowlist-first, transient-per-tab, CoreWebView2Find reuse.
3. Update `todo.md` (clear the search line; file any deferred phase-2 items under
   `## Proposed` as `💡`), note README/screenshots, then move this plan to
   `plans/finished/`.
</content>
</invoke>
