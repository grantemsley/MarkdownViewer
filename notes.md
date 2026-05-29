# MarkdownViewer Project Notes

## Current state

**Version:** v0.6.0, fully compiled and published

**Status:** Feature-complete; all user-requested fixes and known bugs resolved

**Test suite:** 146 tests passing (all green). Fixed stale test data in `TranscriptEndToEndTests` that was causing 3 spurious "No data found" failures.

**Completed this cycle (14 items):**
- Dark mode selection colors (consistent active/inactive)
- Outline filtering (removed H1/H2/etc.; kept natural tree structure)
- App icon (generated via `installer/Generate-Icon.ps1`)
- File associations for `.md` and `.jsonl` (via `installer/Install-FileAssociations.ps1`)
- Context menu cleanup (removed "More tools...", Back, Forward, Save as)
- Find bar reworked to float as Popup over content (not grid row); fixed keyboard capture
- Sidebar width and overflow handling improved
- Hidden-files attribute added to tree filter
- Removed `AreDevToolsEnabled = true` dev flag from `MainWindow.xaml.cs`
- Fixed `TranscriptEndToEndTests` stale fixture path
- "Show all files" label updated to "not just .md and .jsonl"; `.jsonl` always visible (parsed specially)
- Old directory restructured and migrated to project root layout
- `todo.md` cleaned and tidied; leftover `sample/asdfasdfasdfasdf/` test folder deleted

## Decisions

- **Testing convention:** Logic that could live in pure static helpers is extracted to `src/Services/` (see `TreeFilter.cs`, `OutlineBuilder.cs`) so it can be unit-tested. WebView2 behavior, FileSystemWatcher, drag-drop, keyboard shortcuts are verified manually, not mocked.

- **File visibility:** "Show all files" hides `.md`, `.log`, etc. but always keeps `.jsonl` visible (transcript files are parsed specially). Label updated to say "not just .md and .jsonl".

- **Find bar:** Implemented as a floating `Popup` (`StaysOpen="False"` to allow keyboard focus capture) positioned top-right over the WebView2 content area; clears highlights on auto-close.

- **Project structure:** Old `old/` directory analyzed and restructured — app files flattened to root, design handoff moved to `design-handoff/`, plan/status tracking consolidated into root `*.md` files. Originals kept in `old/` per user request.

## Open items

- **`summarize-notes` SKILL.md inconsistency (Medium):** The Tool rules section says "use Bash ONLY for rm and git commit" but doesn't list the step-8 lock-file deletion. The instruction and the rule are slightly mismatched. User was asked to approve a fix; no response yet.

Future scope (non-blocking, tracked in `todo.md`):
- PDF viewing (PDF.js integration)
- HTML export
- Additional transcript parsing

## Recent activity

**2026-05-29 (session 1):** Old directory migration — restructured `old/` layout, recovered testing convention from `OLDCLAUDE.md` into `testing.md`.

**2026-05-29 (session 2):** `.jsonl` filter and label fix — updated `TreeFilter.cs`, `PreferencesWindow.xaml`, and added a `TreeFilterTests` test; all 10 TreeFilter tests green.

**2026-05-29 (session 3):** Large batch — 14 fixes applied and committed incrementally. Tests at 146 passing. All housekeeping done (test folder deleted, `todo.md` tidied). Computer-use verification was attempted but desktop-control tools were unavailable; manual verification done via AskUserQuestion.

**2026-05-29 (session 4):** Code review of `summarize-notes/SKILL.md` update — found one Medium finding (Tool rules / step 8 mismatch). Fix not applied; awaiting user go-ahead.
