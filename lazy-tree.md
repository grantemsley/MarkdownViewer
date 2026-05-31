# Lazy folder-tree loading

**Status:** ⬜ Not started · Last updated 2026-05-31

| Status | Phase | Notes |
|---|---|---|
| ⬜ Not started | P1 · VaultNode lazy primitives | add HasChildren/ChildrenLoaded + placeholder; split BuildNode into one-level scan + LoadChildren |
| ⬜ Not started | P2 · One-level open + expand-to-load | Open/OpenAsync scan root only; TreeViewItem.Expanded triggers LoadChildren |
| ⬜ Not started | P3 · Incremental watcher | path→node map; scope FS events to the affected loaded folder; retire full Rescan |
| ⬜ Not started | P4 · Reveal/expand-to-file | path-walk that loads folders on the way; replaces blind DFS |
| ⬜ Not started | P5 · Filter + display over lazy tree | TreeFilter per-folder at load; pref-change re-applies over loaded nodes only |
| ⬜ Not started | P6 · Tests | update Open contract; add LoadChildren / reconcile / reveal / filter-on-load tests |

## Goal

Opening a large/deep folder (e.g. the Windows home dir with the whole AppData
tree) currently freezes the app, and it keeps re-freezing afterward. Convert
the folder tree from an **eager full-recursive scan** to **lazy per-folder
loading**: scan only the root's immediate children at open, and load each
folder's children on demand when the user expands it. Show an expand arrow for
folders that have children via a cheap "peek" rather than a deep scan.

## Why it freezes today (two causes)

1. **Open cost.** `BuildNode` ([VaultService.cs:184](src/Services/VaultService.cs:184))
   recurses the *entire* tree up front (depth cap 64). The manual
   folder-open path `Open()` ([VaultService.cs:45](src/Services/VaultService.cs:45))
   runs it **synchronously on the UI thread** → hard freeze on a 100k–500k-node
   tree. `OpenAsync` walks off-thread but still materializes the whole tree in
   memory.

2. **The watcher re-walks everything on every change.** `StartWatcher` sets
   `IncludeSubdirectories = true` ([VaultService.cs:113](src/Services/VaultService.cs:113));
   any FS event triggers `Rescan()` ([VaultService.cs:311](src/Services/VaultService.cs:311)),
   which calls `BuildNode` on the whole root again plus full-tree
   `CollectExpanded`/`RestoreExpanded`. AppData churns constantly (logs, caches,
   temp), so the tree rebuilds every ~250 ms forever. **Fixing the open alone
   does not fix this** — the watcher must become incremental too.

WPF's TreeView virtualization (already on,
[MainWindow.xaml](src/MainWindow.xaml) `IsVirtualizing`/`Recycling`) only saves
render cost, not scan/memory cost — confirming the scan is the lever.

## Inventory — everything that assumes a fully-materialized tree

These must be touched or they break under partial loading:

| Site | Assumption | Phase |
|---|---|---|
| `BuildNode` recursion ([VaultService.cs:184](src/Services/VaultService.cs:184)) | walks all descendants | P1 |
| `Open` / `OpenAsync` ([VaultService.cs:45](src/Services/VaultService.cs:45),[:73](src/Services/VaultService.cs:73)) | full scan at open | P2 |
| `Rescan` + `CollectExpanded`/`RestoreExpanded` ([VaultService.cs:311](src/Services/VaultService.cs:311)–364) | full rebuild + full expand-state walk | P3 |
| `ExpandToFile` ([VaultService.cs:147](src/Services/VaultService.cs:147)) | every ancestor folder already a node | P4 |
| `SelectActiveInTree`/`SelectNodeByPath` (MainWindow.xaml.cs) | blind DFS over whole tree | P4 |
| `TreeFilter.Apply` ([TreeFilter.cs](src/Services/TreeFilter.cs)) | recurses all children to set `IsVisible` | P5 |
| `VaultNode.RefreshDisplay` ([VaultNode.cs](src/Models/VaultNode.cs)) | walks all children on pref change | P5 |
| `OnVaultTreeChanged` binding (MainWindow.xaml.cs ~421) | binds root; HierarchicalDataTemplate recurses `Children` | P2 |
| Tests: `VaultServiceTests`, `TreeFilterTests` | assert nested children present after `Open` | P6 |

Not a blocker: no "expand all / collapse all" command exists; the find bar is
WebView-only; `OutlineBuilder.ApplyCollapse` operates on the heading outline,
not the vault tree.

## ⬜ Phase 1 — VaultNode lazy primitives

- Add to `VaultNode` ([VaultNode.cs](src/Models/VaultNode.cs)):
  - `bool HasChildren` (init) — drives whether an expand arrow shows.
  - `bool ChildrenLoaded` (mutable) — guards against re-scanning.
  - A placeholder concept: when `HasChildren` is true but not yet loaded, the
    folder holds **one dummy `VaultNode`** so WPF renders the expander. (WPF
    shows the toggle only when `Children` is non-empty; the dummy is the
    standard lazy-TreeView idiom and avoids overriding the control template.)
- Split `BuildNode` into:
  - `ScanOneLevel(DirectoryInfo)` — builds a folder node and its **immediate**
    child folder + file nodes, keeping the existing reparse-point skip and the
    `UnauthorizedAccessException`/`IOException` swallow. For each child **folder**,
    set `HasChildren` via a cheap peek and add the dummy placeholder.
  - `Peek(path)` — `Directory.EnumerateFileSystemEntries(path).Any()`
    (lazy, stops at first hit; cheap vs `GetDirectories()` materializing an
    array). Skip reparse-point-only contents for accuracy where practical;
    accept a rare false arrow on a folder whose sole entry is a junction.
  - `LoadChildren(VaultNode folder)` — if `!ChildrenLoaded`, scan that folder
    one level, replace the placeholder with the real children, set
    `ChildrenLoaded = true`, apply the filter to the new children (P5), register
    the folder in the path→node map (P3).
- Keep `MaxScanDepth`/reparse handling at each load level (cycle safety still
  matters even one level at a time).

## ⬜ Phase 2 — One-level open + expand-to-load

- `Open`/`OpenAsync` call `ScanOneLevel` on the root only. Open becomes cheap
  enough that the sync `Open()` path no longer freezes; keep `OpenAsync` for the
  startup overlap but it now does trivial work.
- Wire expansion → load: add a class handler at the TreeView for
  `TreeViewItem.Expanded`
  (`FolderTree.AddHandler(TreeViewItem.ExpandedEvent, ...)`) in
  MainWindow.xaml.cs; resolve the bound `VaultNode` and call
  `_vault.LoadChildren(node)`. Keeps filesystem knowledge in `VaultService`, not
  the model.
- Decide load thread: a single folder level is usually fast enough to load
  synchronously on expand; if a folder is pathologically wide, load on a worker
  and marshal back (mirror the `_openGeneration`/token guard pattern). Start
  synchronous; revisit only if a wide folder stutters.
- **Verify:** opening the home dir is instant; expanding AppData loads only that
  level.

## ⬜ Phase 3 — Incremental watcher

- Maintain `Dictionary<string, VaultNode>` of **loaded folders** (keyed by full
  path, `OrdinalIgnoreCase`); populate in `LoadChildren`, seed with the root,
  clear on `Open`/`DisposeWatcher`.
- Replace the full `Rescan` with a **scoped refresh**: on an FS event for path
  `P`, look up `P`'s parent folder in the map.
  - Not present (parent unloaded/collapsed) → **drop the event** (it'll be
    scanned fresh on expand). This is what kills the AppData-churn re-freeze.
  - Present → **reconcile that one folder's children**: re-scan its level and
    merge by name — add new entries in sorted position, remove gone ones,
    **preserve existing child nodes** (and their `IsExpanded`/`ChildrenLoaded`/
    loaded subtrees) so a sibling change doesn't collapse or reload anything.
- Keep the 250 ms debounce and the active-file reload path (`_pendingChanged`,
  `ActiveFileChanged`) — only the tree-rebuild half changes.
- Watcher buffer overflow ([VaultService.cs:122](src/Services/VaultService.cs:122)):
  instead of a whole-disk rescan, **reconcile every folder currently in the
  map** (bounded by what's loaded, not what's on disk).
- Retire `Rescan`, `CollectExpanded`, `RestoreExpanded` — expand state now lives
  on the persisted nodes, so there's nothing to snapshot/restore.

## ⬜ Phase 4 — Reveal / expand-to-file with on-demand load

- Replace `ExpandToFile` + `SelectActiveInTree`/`SelectNodeByPath` with one
  **path-walking reveal**: split the target path into segments; from the root,
  for each segment ensure the current folder is loaded (`LoadChildren` if not),
  find the matching child, set `IsExpanded`, descend; select the final node.
- This drives:
  - opening a file from elsewhere (auto-expand to it),
  - cold-start restore of the last-opened file,
  - the open `todo.md` item "folder containing the open file auto-expands."
- Replaces the blind full-tree DFS, which can no longer find unloaded nodes.

## ⬜ Phase 5 — Filter + display over the lazy tree

- `TreeFilter.Apply` ([TreeFilter.cs](src/Services/TreeFilter.cs)): keep the
  per-node logic, but call it (a) on a folder's **newly loaded children** inside
  `LoadChildren`, and (b) on a **pref change**, re-walking only the loaded nodes
  (cheap). Don't recurse into unloaded folders — they'll be filtered when loaded.
- `DisplayName` already reads `UiPrefs.Instance.ShowExtensions` live
  ([VaultNode.cs:60](src/Models/VaultNode.cs:60)), so newly-loaded rows show the
  right label automatically — no work needed there. Only `IsVisible` (the
  ShowHidden / ShowNonMarkdown filter) must be applied on load, which (a) above
  covers; a folder expanded after a restrictive pref change comes back already
  filtered.
- `RefreshDisplay`: walk loaded children only (no behavior change needed beyond
  operating over the partial tree).

## ⬜ Phase 6 — Tests

- Update `VaultServiceTests`:
  - `Open_BuildsRecursiveTree` → `Open_ScansRootLevelOnly` (assert immediate
    children present, nested **not** loaded until `LoadChildren`).
  - `Open_FolderWithEmptySubfolder...` → assert the empty subfolder shows
    `HasChildren == false` (no arrow).
  - New: `LoadChildren` populates one level and flips `ChildrenLoaded`; a folder
    with descendants reports `HasChildren == true` via peek.
  - New: reconcile merges add/remove while preserving an expanded/loaded
    sibling's subtree.
  - New: reveal-path loads intermediate folders and selects the leaf.
- `TreeFilterTests`: still valid for the per-node contract; add a case proving
  filter runs on load (children loaded after a restrictive pref come back
  filtered).
- Pure-helper testability is preserved — `Peek`, `ScanOneLevel`, reconcile, and
  reveal should be unit-testable without WebView2/WPF.

## Risks / watch-outs

- **Reconcile correctness** (P3) is the subtle part: merging by name while
  preserving node identity and child state. Get sorting and case-insensitive
  comparison right; cover rename (delete+create) and atomic-save paths already
  handled by `OnFsRenamed`.
- **Dummy placeholder leaks**: ensure the placeholder is never selectable,
  never counted, and is always replaced on first load.
- **Wide first level**: a root with thousands of immediate children still does
  thousands of peeks at open — fast (one `FindFirstFile` each) but if it ever
  stutters, move the root scan/peeks to a worker (P2 note).
- **Reparse-point arrows**: a folder whose only entry is a junction may show a
  false arrow; documented trade-off, low impact.
