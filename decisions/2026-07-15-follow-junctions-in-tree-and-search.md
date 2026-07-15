# Follow junctions/symlinks in the tree and the search walk

**Date:** 2026-07-15
**Status:** Adopted (shipped in v1.0.0.1)

## Decision

Reparse points (junctions, symlinks) are treated as ordinary folders by the
folder tree and by the full-text search walk. The previous blanket skip is gone
from all three sites (`VaultService.PopulateChildren`, `VaultService.ReconcileFolder`,
`FileSearchService.EnumerateFiles`).

Search, which genuinely recurses, follows them behind a cycle guard: a visited-set
keyed on each directory's resolved real path (`DirectoryInfo.ResolveLinkTarget(returnFinalTarget: true)`
for a reparse point, `FullName` otherwise).

## Why

Reported symptom: opening `C:\Users\claude\.claude` showed no `docs` node, because
`docs` is a junction to a repo elsewhere.

The skip's stated rationale had two halves, and both had aged out:

- *"Silently pulls in external content"* already contradicted this project's own
  serving-layer decision. `plans/finished/same-origin-vault.md` records that
  reparse points are followed **by design** when serving `/__vault/`, because
  "blocking reparse points would break legitimately symlinked vaults, which are
  common". The result was incoherent: a junctioned file could be opened and
  rendered, just never listed.
- *"Following one that points to an ancestor recurses forever"* only applies to a
  full-recursion walk. The tree went lazy (one level per expand) in the lazy-tree
  work, so a junction cycle there costs another click, not a hang. `MaxScanDepth`,
  which that plan mentions, no longer exists in the code for the same reason.

So the cycle risk is real for search and not for the tree, and the two surfaces
are treated differently on purpose.

This does not widen the vault boundary. `VaultPaths.ResolveWithinRoot` is
unchanged and was already following reparse points by design.

## Alternatives rejected

- **Fix the tree only.** `FileSearchService`'s skip was explicitly coupled
  ("Same guard VaultService uses"). Leaving it would make a junctioned subtree
  browsable but unsearchable, which is a worse and more confusing state than the
  original bug.
- **Ancestor-only cycle check** (skip a junction whose target is an ancestor of
  the current path). Misses mutual junctions (`a/to-b` -> `b`, `b/to-a` -> `a`),
  which form a cycle with no ancestor relationship. Pinned by
  `Search_TwoJunctionsPointingAtEachOther_Terminates`.
- **Canonicalize every directory** (`GetFinalPathNameByHandle` via P/Invoke). Costs
  a syscall per directory on a walk explicitly tuned for SMB latency, and buys
  nothing: every cycle must cross a reparse point, so keying reparse targets is
  sufficient to terminate.
- **Keep a depth cap.** Retired by lazy loading; reintroducing one would cap
  legitimately deep trees to defend against a case the guard already handles.

## Constraints this creates

- A directory reached *through* a junction keys on its virtual path, not the
  target's real path. Dedup is therefore per link-target, not per alias. That is
  enough to terminate, and is the documented limit of the guard.
- **Alias-wins:** if a junction aliases a sibling folder inside the same root and
  is walked first, it claims the target and the real folder is skipped, so hits
  report under the junction path. Accepted: the files are still found exactly
  once, and the alternative (reporting both) is worse.

## Non-goal

**Making junctioned subtrees live-update.** `FileSystemWatcher` does not traverse
reparse points. Verified 2026-07-15 by direct measurement: with
`IncludeSubdirectories = true` on a root containing a junction, a control write
directly under the root fired, while a write into the junction target and a write
via the junction path were both silent. A junctioned subtree is therefore
browsable and searchable but stale until reopened. Fixing it needs a per-junction
watcher or polling; filed in `todo.md` (P3) rather than built, since no one has
been bitten yet.

## Notes

Tests live alongside the code they pin (`VaultServiceTests`, `FileSearchServiceTests`),
using `TestJunction` (`mklink /J`, which needs no elevation, so they run
unprivileged and on CI). Each was verified to fail without the code it guards: the
four junction-visibility tests fail with the fix reverted, and the three cycle
tests fail with only the guard disabled (they pass against the *old* code
vacuously, since skipping junctions trivially satisfies "terminates, no
duplicates").
