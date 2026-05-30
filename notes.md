# MarkdownViewer Project Notes

## Current state

**Version:** v0.7.0, fully compiled and tested

**Status:** Ready for public release. All user-requested fixes completed, code review findings applied, security scan passed.

**Test suite:** 149 tests passing (all green). Stale `TranscriptEndToEndTests` fixture path was fixed.

**Completed in recent sessions:**
- **Session 1 (5/29):** Old directory migration — recovered testing convention from OLDCLAUDE.md.
- **Session 2 (5/29):** `.jsonl` always-visible fix; updated label to "not just .md and .jsonl"; 10 TreeFilter tests green.
- **Session 3-5 (5/29):** Large batch — 14 user-requested fixes (dark mode highlight colors, outline filtering, icon generation, file associations, context menu cleanup, find bar reworked as floating Popup, sidebar/overflow improvements, hidden-files attribute, dev-flag removal, stale test fixture). All committed incrementally.
- **Session 6 (5/29):** Code review of `summarize-notes/SKILL.md` update identified one Medium finding (Tool rules / step 8 mismatch); fix awaited user decision. _(Note: fix has NOT been applied per review scope.)_
- **Session 7 (5/29–5/30):** Smoke test + 8 additional fixes (CSP hardening, iframe sandbox, scheme validation, watcher reload, settings atomic-save, preferences clamping, FS-watcher resync on overflow, exit-code parsing); manual verification of code highlighting, Mermaid, GitHub styling, remote/data images, HTML, PDF, external links. All passing.
- **Session 8 (5/30):** Security review for public release — scanned history, `.gitignore` audit, sensitive data check (all clear). README.md rewritten with user-centric tone ("vibe coded" transparency, feature highlights, usage caveats). Transcript backslash-escape fix applied (Windows paths no longer render doubled backslashes in code-spans).
- **Session 9 (5/30):** README screenshots replaced with user-captured full-window renders (1920×1020) showing sidebar, outline, Mermaid, code, and PDF viewer.

## Decisions

- **Testing convention:** Logic in pure static helpers extracted to `src/Services/` (TreeFilter.cs, OutlineBuilder.cs, etc.) for unit testing. WebView2 behavior, FileSystemWatcher, drag-drop, keyboard shortcuts verified manually.
- **File visibility:** "Show all files" hides docs but always shows `.jsonl` (transcript files parsed specially). Label reflects this.
- **Find bar:** Floating `Popup` (StaysOpen=False) positioned top-right; auto-closes and clears highlights on outside-click.
- **Project structure:** Old directory restructured — app files flattened to root, design handoff to `design-handoff/`, originals kept in `old/` per user request.
- **Public release:** README emphasizes "vibe coded" nature and lack of assembly review; included honest caveats about audience.

## Open items

- **`summarize-notes/SKILL.md` Tool rules fix** — Medium severity finding from code review (step 8 lock-file deletion mismatch). Awaiting user decision on fix application.

Future scope (non-blocking, in `todo.md`):
- PDF.js integration for faster first-PDF open
- HTML export
- Additional transcript parsing formats

## Recent activity

**2026-05-29:** Directory restructure, 14 user-requested bug fixes, first code review of SKILL.md.

**2026-05-29–5/30:** Smoke testing, 8 additional hardening fixes, security audit, README rewrite with real screenshots.

**2026-05-30:** Publication-ready; all tests green, history clean, security scan passed.
