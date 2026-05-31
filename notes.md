# MarkdownViewer Project Notes

## Current state

**Version:** v0.9.0.0 (released)

**Status:** Public on GitHub (`grantemsley/MarkdownViewer`) with CI/release pipeline live.

**Test suite:** 208 tests passing (all green).

**CI/Release Pipeline:**
- GitHub Actions workflow for CI (checkout v6, setup-dotnet v5)
- Automated release tag with `action-gh-release v1`
- All tests passing; downloadable build available

**In-progress work:**
- **Phase 1 (planned):** Lazy-tree loading to fix folder-scan freeze on large structures. See `lazy-tree.md` for full plan.

## Recent releases

**v0.9.0.0 (2026-05-31):**
- **Frontmatter + tag visibility feature** — YAML frontmatter now renders in a collapsed expandable section at top (not hidden). Custom XML-like tags (e.g. `<example>`) render similar blocks, also collapsed by default.
- **Tag escaping fix** — backtick-escaped tags (e.g. `` `<example>` ``) now properly escape in code blocks and don't create spurious nesting.
- **Tests updated** for both features.

**v0.7.0 (2026-05-30):**
- Same-origin vault image/PDF/markdown rewrite (`WebAssetProvider`, `VaultPaths`)
- 14 user-requested fixes (dark mode, filtering, icons, context menu, find bar, hidden files)
- 8 hardening fixes (CSP, iframe sandbox, scheme validation, atomic saves)
- Security audit and README rewrite

## Decisions

- **Testing convention:** Logic in pure static helpers (`src/Services/`) for unit testing; WebView2 behavior, file watching, drag-drop verified manually.
- **File visibility:** "Show all files" shows `.jsonl` transcripts; label reflects this.
- **Find bar:** Floating `Popup` with auto-close on outside-click.
- **Same-origin vault:** Image, PDF, and markdown-embedded resources all load from `app.local/__vault/`. Cross-origin `vault.local` fully retired.
- **Public release:** README emphasizes "vibe coded" nature; honest caveats about audience included.

## Open questions

- **Lazy-tree implementation:** Phase 1 plan written. Awaiting user approval before implementation starts.

## Activity

**2026-05-30–5/31:** Frontmatter + tag visibility (v0.9.0.0 released). Large-folder freeze issue identified; lazy-tree plan written.
