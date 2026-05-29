# MarkdownViewer Project Notes

## Current state

**Version:** v0.6.0, fully compiled and published

**Status:** Feature-complete; all user-requested fixes and known bugs resolved

**Test suite:** 146 tests passing (all green, including 67 unit tests). Fixed stale test data in TranscriptEndToEndTests that was causing spurious failures.

**Last session work:** Fixed 14 items (5 new user requests + 9 known bugs):
- Dark mode selection colors (now stays consistent active/inactive)
- Outline filtering (removed H1, H2, etc.; kept natural tree structure)
- App icon (generated via `Generate-Icon.ps1`)
- File associations (via installer scripts for .md and .jsonl)
- Context menu cleanup (removed "More tools...", Back, Forward, Save as)
- Find bar UI reworked to float over content with Popup (not grid row) and fixed keyboard capture
- Sidebar width and overflow handling improved
- Hidden-files attribute added to tree filter

All items tested and committed incrementally. Cleanup completed (deleted leftover test folder, tidied todo.md).

## Decisions

- **Testing convention:** Logic that could live in pure static helpers is extracted to `src/Services/` (see `TreeFilter.cs`, `OutlineBuilder.cs`) so it can be unit-tested. WebView2 behavior, FileSystemWatcher, drag-drop, keyboard shortcuts are verified manually, not mocked.

- **Project migration:** Analyzed and restructured old `old/` directory:
  - Flattened app files from `old/outputs/` to project root
  - Merged design handoff into `design-handoff/`
  - Consolidated plan/status tracking into root `*.md` files
  - Kept originals in `old/` per user request

- **File visibility:** "Show all files" now hides `.md` and `.log` but keeps `.jsonl` visible (parsed specially for transcripts)

## Open items / deferred scope

All user-requested fixes and known bugs are resolved. Remaining items in `todo.md` are future scope:
- PDF viewing (read PDF.js integration feasibility)
- HTML export
- Additional transcript parsing

## Recent activity

**2026-05-29:** Cleanup and final verification complete. All 14 requested fixes + bug resolutions done, tested, and committed. Test suite at 146 passing. Ready for next phase or user direction.
