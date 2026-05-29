# MarkdownViewer — TODO

Out-of-scope or not-yet-done work. Per-project file (not per plan);
consolidates the leftover checklists from the various plans. Git history is
the changelog — this file tracks what's *left*.

---

## Resolved — 2026-05-29 fix session (see git log for per-item commits)

**Batch-2 requests**
- [x] Dark-mode inactive-selected tree row readable (aliased inactive-
      selection brushes to the active highlight).
- [x] Outline H1/H2 level badges removed.
- [x] App icon (M down-arrow accent tile; `installer/Generate-Icon.ps1` →
      `src/app.ico`; wired to exe + window + title bar).
- [x] `.md`/`.jsonl` file-association scripts (`installer/Install-
      FileAssociations.ps1` + Uninstall; per-user, Open-With, no default
      hijack).
- [x] WebView right-click trimmed (Back/Forward/Save as/More tools removed).

**Bugs fixed**
- [x] Mermaid renders in-app (structuredClone polyfill + CDN fallback +
      inline error surfacing).
- [x] Sidebar row width — mode-aware buffer; wrap reaches the border,
      ellipsis dots fully visible.
- [x] Tree "Open with…" via `SHOpenWithDialog` (works for any file, incl.
      ones with a registered default).
- [x] Export / open-in-browser HTML scrolls (overflow:auto in standalone).
- [x] Find-in-page bar floats over content (Popup; StaysOpen=False + user32
      SetFocus so it's typeable over the WebView2 airspace; square buttons).
- [x] Open popup widened (420–760) + path end-trim guard.
- [x] Hidden files honor the Windows hidden attribute.
- [x] GitHub body style — canvas-matched scroll background + padded body.
- [x] Auto-switch-to-outline — obsolete (sidebar is a split pane now).
- [x] Transcript E2E tests point at `.claude/transcripts`; suite green (146).

**Earlier known bugs (re-verified this session)**
- [x] Dark-mode tree/outline text legible; file-deletion preserves tree
      expansion; deleted-open-file shows empty state.

**Confirmed working** — file icons, non-md viewer margins, all tree + WebView
context-menu items, HTML/PDF viewers, full transcript renderer (sticky
filters + dark legibility + 4.8 MB perf), live new-file/new-folder watch,
⚙ tooltip, Open-popup ordering + full-path tooltips.

**Code cleanup**
- [x] Removed `AreDevToolsEnabled` dev flag.
- [x] Deleted leftover `sample/asdfasdfasdfasdf/` test folder.
- [x] Untracked `bin/obj/publish`; added to `.gitignore`.

---

## Remaining — backlog (deferred, not blocking)

### Theming polish (Phase T5, from `theming.md`)
- [ ] Verify Mermaid renders under BOTH body styles (Win11 + GitHub), dark
      variant via the Mermaid theme.
- [ ] Proper highlight.js theme pair per body style (`github`/`github-dark`
      for GitHub mode; `vs`/`vs2015` for Win11 mode).
- [ ] OS accent colour through links + the "reloaded" flash (chrome → CSS
      var via the bridge).
- [ ] Update `README.md` screenshots.
- [ ] Bump app version in `markdownviewer.md` + changelog.

### UI niceties
- [ ] Folder containing the currently-open file auto-expands on open
      (incl. the cold-start path that restores the last-opened file);
      manual collapse stays collapsed until the next file inside it opens.

### Unverified, low priority (raw-browser)
- [ ] External link inside a raw HTML file opens in the OS browser.
- [ ] Relative `<img>`/`<link>` refs inside a user HTML file resolve.
- [ ] Instant HTML ↔ markdown ↔ PDF switching (no `render.html` reload).

### Deferred features (explicitly not-for-v1)
- [ ] Wiki-link `[[Note]]` support (custom Markdig `IMarkdownExtension`).
- [ ] Frontmatter display (bring back `YamlDotNet` + a visual).
- [ ] Tabbed / multi-doc viewing.
- [ ] Print (`WebView2.ShowPrintUI()`). Export-to-HTML already exists via the
      WebView right-click menu.

### Transcript renderer — out of scope (`transcripts.md`)
- [ ] Pretty-print JSON in tool *outputs* (inputs already indented).
- [ ] Click-through from `Read`/`Edit` tool paths into the vault.
- [ ] Image content blocks (render as `[image]` placeholder).
- [ ] Toggle-all / preset filter modes ("system noise on/off").
- [ ] Custom per-user category labels or colours.

### Raw-browser viewer — future perf
- [ ] Two-iframe approach (instant switch back to a recently-viewed PDF).
- [ ] PDF.js to speed first-PDF open (bundle ~500 KB; canvas render; nav /
      zoom / find).

---

> `notes.md` is empty/stale — run `/summarize-notes` to regenerate it.
