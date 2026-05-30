# MarkdownViewer Project Notes

## Current state

**Version:** v0.7.0, fully compiled and tested

**Status:** Ready for public release. Image bug fixed, same-origin vault complete, smoke testing passed.

**Test suite:** 208 tests passing (all green). Added `VaultPathsTests` and enhanced existing test coverage for path resolution and cross-origin scenarios.

**Recent major work (5/30):**
- **Image bug fix — multi-phase same-origin vault implementation** (commits `b09660a` through `9d7c06a`):
  - Phase 1: Security-critical path resolver (`VaultPaths.cs`) — validates and resolves vault-relative paths to prevent traversal attacks
  - Phase 2: Rewrite image/PDF/markdown URLs at render time via `WebAssetProvider.cs` (serves from `app.local/__vault/` instead of `vault.local`)
  - Phase 3: JavaScript bridge updates to request rewriting + error handling
  - Phase 4: Updated HTML templates and CSS for same-origin serving
  - Phase 5: Unit test coverage for path validation, relative paths, and edge cases
- **Test fixtures generated** (`sample/logo.svg`, `sample/relative-image.md`, `sample/image tests/nested-note.md`, etc.) for repeatable smoke testing
- **Build convenience script** (`build.bat`) — single exe to `publish\MarkdownViewer.exe`
- **Smoke test complete:** All manual tests passed — image files (PNG, SVG), relative-image.md, nested spaced folder, links.md, PDF, HTML, external resources

**Completed in prior sessions:**
- **Session 1 (5/29):** Old directory migration
- **Session 2-5 (5/29):** 14 user-requested fixes (dark mode, filtering, icons, context menu, find bar as floating popup, hidden files, dev flags, test fixtures)
- **Session 6 (5/29):** Code review of SKILL.md (one Medium finding pending)
- **Session 7 (5/29–5/30):** 8 hardening fixes (CSP, iframe sandbox, scheme validation, atomic saves, etc.)
- **Session 8 (5/30):** Security audit, README rewrite with screenshots
- **Session 9 (5/30):** Screenshot refinements

## Decisions

- **Testing convention:** Logic in pure static helpers (`src/Services/`) for unit testing; WebView2 behavior, file watching, drag-drop verified manually.
- **File visibility:** "Show all files" shows `.jsonl` transcripts; label reflects this.
- **Find bar:** Floating `Popup` with auto-close on outside-click.
- **Same-origin vault:** Image, PDF, and markdown-embedded resources all load from `app.local/__vault/`. Cross-origin `vault.local` fully retired.
- **Public release:** README emphasizes "vibe coded" nature; honest caveats about audience included.

## Open items

- **`summarize-notes/SKILL.md` Tool rules fix** — Medium severity code review finding (step 8 lock-file deletion mismatch). Awaiting user decision.

Non-blocking future scope (in `todo.md`):
- PDF.js integration for faster first-PDF open
- HTML export
- Additional transcript parsing formats

## Recent activity

**2026-05-29:** Directory restructure, 14 user-requested bug fixes.

**2026-05-29–5/30:** Hardening fixes, security audit, README rewrite.

**2026-05-30 (latest):** Image bug fixed via same-origin vault architecture, 208 tests green, smoke testing complete, build.bat added. All changes committed locally; nothing pushed yet.
