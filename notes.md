# MarkdownViewer Project Notes

## Current state

**Version:** v0.9.6.1 (released)

**Status:** Public on GitHub (`grantemsley/MarkdownViewer`) with CI/release pipeline live.

**Test suite:** 208+ tests passing (all green). TreeSorterTests covers the sort feature.

**CI/Release Pipeline:**
- GitHub Actions workflow for CI (checkout v6, setup-dotnet v5)
- Automated release tag with `action-gh-release v1`
- All tests passing; downloadable builds available

**In-progress work:**
- **Phase 1 (planned):** Lazy-tree loading to fix folder-scan freeze on large structures. See `lazy-tree.md` for full plan.

## Recent releases

**v0.9.6.1 (2026-06-01):**
- **Preferences text clipping fix** — "Descending" word wrap in direction dropdown and "MarkdownViewer" text wrapping in file-association row. Regression in v0.9.6.0 fixed immediately.

**v0.9.6.0 (2026-06-01):**
- **Sort feature** — Folder and file sort preferences with Name, Created date, Modified date, File extension options. Ascending/descending order toggle. Feature integrated into PreferencesWindow and VaultService.

**v0.9.0.0 (2026-05-31):**
- **Frontmatter + tag visibility feature** — YAML frontmatter renders in collapsed expandable section. Custom XML-like tags (e.g. `<example>`) render similar blocks, collapsed by default.
- **Tag escaping fix** — backtick-escaped tags properly escape in code blocks.

**v0.7.0 (2026-05-30):**
- Same-origin vault image/PDF/markdown rewrite (`WebAssetProvider`, `VaultPaths`)
- 14 user-requested fixes (dark mode, filtering, icons, context menu, find bar, hidden files)
- 8 hardening fixes (CSP, iframe sandbox, scheme validation, atomic saves)

## Decisions

- **Testing convention:** Logic in pure static helpers (`src/Services/`) for unit testing; WebView2 behavior, file watching, drag-drop verified manually.
- **File visibility:** "Show all files" shows `.jsonl` transcripts; label reflects this.
- **Find bar:** Floating `Popup` with auto-close on outside-click.
- **Same-origin vault:** Image, PDF, and markdown-embedded resources load from `app.local/__vault/`. Cross-origin `vault.local` retired.
- **Public release:** README emphasizes "vibe coded" nature with honest caveats about audience.

## Open questions

- **Lazy-tree implementation:** Phase 1 plan written. Awaiting user approval before implementation starts.

## Activity

**2026-06-01:** Sort feature released as v0.9.6.0; text clipping in Preferences discovered and fixed same day (v0.9.6.1).

**2026-05-31:** Sort feature implemented (folder/file sort by Name/Created/Modified/Extension with ascending/descending). Code complete; unit tests pass; visual verification confirmed.

**2026-05-30–5/31:** Frontmatter + tag visibility (v0.9.0.0 released). Large-folder freeze issue identified; lazy-tree plan written.
