# MarkdownViewer — TODO

Out-of-scope or not-yet-verified work. Tick items off as they get done.
Per-project file (not per plan); content from the various plans'
checklists is consolidated here.

---

## Resolved — 2026-05-29 (fix session, see git log for per-item commits)

Batch-2 requests:
- [x] Dark-mode inactive-selected tree row readable (aliased inactive
      selection brushes to the active highlight).
- [x] Outline H1/H2 level badges removed.
- [x] App icon added (M down-arrow accent tile; installer/Generate-Icon.ps1).
- [x] .md/.jsonl file association scripts (installer/Install-FileAssociations.ps1
      + Uninstall; per-user, Open-With, no default hijack).
- [x] WebView right-click trimmed (Back/Forward/Save as/More tools removed).

Bugs from the verification pass:
- [x] Mermaid in-app (structuredClone polyfill + CDN fallback + inline error).
- [x] Sidebar row width (buffer 30→56px; wrap + ellipsis no longer clip).
- [x] Tree "Open with…" (switched to the shell "openas" verb).
- [x] Export/browser HTML scroll (html/body overflow:auto in standalone).
- [x] Find-in-page UI (moved find bar into a Popup so it shows over WebView2).
- [x] Open popup width + path overflow (widened 420–760; NoWrap+ellipsis guard).
- [x] Hidden files honor the Windows hidden attribute.
- [x] GitHub body-style background box (canvas-colour scroll + padded body).
- [x] Auto-switch-to-outline: OBSOLETE — sidebar is now a split pane (folder +
      outline both always visible), no tabs to switch. No code change.
- [x] Transcript E2E tests: fixture path updated to .claude/transcripts;
      suite green (145 passed).

NOTE: these fixes are built but most are visual/interaction — re-verify in a
running build (esp. mermaid, which may still surface an inline error if the
WebView2 runtime is the root cause; the message will say what failed).

---

## Verification pass — 2026-05-29 (driven manually, app run from source)

**Confirmed FIXED (known bugs):**
- ✅ Dark-mode tree + outline text now legible (light on dark).
- ✅ File deletion preserves folder-tree expansion.
- ✅ Deleting the open file → "no longer exists" empty state; file removed from tree.

**Confirmed WORKING:** dark theme (chrome + document), file icons (generic
only on extensions with no registered app — correct), markdown render,
non-md viewer margins, tree context menu (Open / Open in Explorer / Make
this the root / Open parent folder), WebView right-click (Open with default
app / Open rendered in browser / Export HTML dialog), HTML viewer, PDF
viewer (~2s first open), find-in-page (functional), ⚙ tooltip, live
new-file + new-folder watch, full JSONL transcript renderer (structured
chat, sticky filters, dark legibility, working filters, 4.8 MB perf OK),
Body Style switch + GitHub light + code highlight under both styles,
Open-popup section order.

**NEW BUGS found this pass (need fixing):**
- [ ] **Mermaid doesn't render in-app** — shows raw code text. Renders
      fine in the browser export (CDN), so the markdown→div transform is
      OK; mermaid.js isn't initializing inside the in-app WebView2.
- [ ] **Sidebar row width ~1.5 chars too wide** — wrap mode overflows the
      border slightly (last char tucked under edge); ellipsis mode only
      shows 1–3 of the dots. Same root cause: per-row MaxWidth binding
      isn't subtracting enough (scrollbar width / right padding).
- [ ] **Tree context menu "Open with…" does nothing/errors** (the WebView
      "Open with default app" works fine — this is the tree file menu).
- [ ] **Exported / "open in browser" HTML can't scroll** — reader CSS sets
      `html, body { overflow: hidden }`, fine in-app but kills scrolling
      standalone. Need overflow auto in the export/standalone path.
- [ ] **Find-in-page has no visible UI** — Ctrl+F highlights/jumps but no
      find bar is shown (native WebView2 find UI suppressed).
- [ ] **Open popup too narrow + path end-overflow** — start-trim works but
      text still overflows/cuts at the end; popup should be wider and the
      path text properly width-contained.
- [ ] **"Show hidden files" ignores Windows hidden attribute** — only
      checks for dot-prefixed names; a Windows-hidden file is always shown.
- [ ] **GitHub body style background hugs the text** — sets bg on the
      content element only, no surrounding padding, so the colored box
      touches the left edge of the text. Looks bad, worse in dark.
- [ ] **"Auto-switch to outline" setting not found** in Preferences —
      verify whether it's actually exposed / correctly labeled.

**Separate (not a code regression):**
- [ ] TranscriptEndToEndTests (3) fail with "No data found" — fixture path
      `Projects/MarkdownViewer/notes/transcripts/` is stale after the move
      to HomeProjects/; transcripts now live in `.claude/transcripts/`.
      118/118 real logic tests pass.

---

## New requests — 2026-05-29 (batch 2)

- [ ] **Dark mode: inactive-selected tree row unreadable.** Clicking a
      file highlights it blue (active); when the tree loses focus the
      selection goes light grey, leaving the white text unreadable.
      Style the inactive-selected TreeViewItem for dark theme.
- [ ] **Outline: drop H1/H2/… level prefixes.** The tree's natural
      structure conveys heading level; remove the explicit "H1"/"H2"
      labels.
- [ ] **App icon.** App currently has no icon; add one (window + exe).
- [ ] **File association for .md and .jsonl.** Be able to register
      MarkdownViewer as a handler for these extensions (build on the
      existing Install-ContextMenu.ps1 approach).
- [ ] **Trim WebView right-click menu.** Remove 'More tools…', 'Back',
      'Forward', and 'Save as' from the content-area context menu.

---

## Verify the most recent batch of fixes

- [ ] **Dark mode tree text.** Folder + outline tree text reads cleanly
      (white) on the dark background. Selected row keeps its highlight.
- [ ] **File deletion preserves expansion.** Delete any file externally
      (currently-open or not) and confirm the rest of the tree stays
      expanded as it was.
- [ ] **Wrap mode wraps at the sidebar edge.** With "Wrap long names"
      on, long filenames break onto a new line before hitting the
      sidebar's right border (not after).
- [ ] **Ellipsis mode shows the `…`.** With wrap off, long filenames
      truncate visibly with a trailing ellipsis and full name on hover.
- [ ] **Folder context menu — Open in Explorer.** Right-click any
      node, choose Open in Explorer: file opens Explorer with the file
      selected; folder opens the folder.
- [ ] **Folder context menu — Make this the root.** Right-click a
      non-root folder; "Make this the root" re-opens the tree pointed
      at that folder.
- [ ] **Folder context menu — Open parent folder.** Right-click the
      vault root; "Open parent folder" jumps up one directory.
- [ ] **Non-markdown viewer margins.** Open a `.ps1` or `.sh` —
      content fills more of the window, less wasted padding.
- [ ] **File icons in the tree.** `.ps1` shows the PowerShell icon,
      `.pdf` the PDF reader's icon, `.md` whatever's registered for
      markdown, folders show the standard folder icon. Should look
      like Explorer's tree pane.
- [ ] **File context menu — Open.** Right-click a file, choose Open —
      launches in the OS-registered default app.
- [ ] **File context menu — Open with…** Right-click a file, choose
      "Open with…" — opens the Windows shell "Open with" dialog.
- [ ] **WebView right-click — Open with default app.** Right-click in
      the rendered document area; "Open with default app" appears
      below the standard Copy/Find/etc. menu and launches the source
      file in its registered handler.
- [ ] **WebView right-click — Open rendered in default browser.**
      For markdown and JSONL transcripts, the rendered HTML opens in
      the user's default web browser (queried from the http UserChoice
      registry key). Diagrams + syntax highlighting load from CDN.
- [ ] **WebView right-click — Export rendered HTML…** Save-as dialog
      writes a standalone .html file with reader.css inlined and
      highlight.js + mermaid linked from CDN.

Things that have been wired up but the user has not yet confirmed work
end to end. Most of these were copied from `plans/markdownviewer.md`'s
testing section and the various changelogs.

### Main plan — re-verify after the recent rounds of fixes

- [x] **File deletion → empty state.** Verified: shows "This file no
      longer exists." Regression noted — see "Known bugs" below.
- [ ] **Auto-switch to outline.** With AutoOutline on, clicking a different
      .md while on the Folder tab should switch the sidebar to Outline
      (previously failed on manual click; now deferred via
      `Dispatcher.BeginInvoke`).
- [~] **Wrap long names — wrap mode.** Partial. Wraps, but text overflows
      past the sidebar border before wrapping — wrap boundary is wrong.
      See "Known bugs" below.
- [ ] **Wrap long names — ellipsis mode.** Couldn't test — the ellipsized
      text was hidden behind the edge of the sidebar.
- [x] **Empty folders.** Verified.
- [ ] **Open popup full paths.** Pinned / Currently-open / Recent rows in
      the Open popup show the full folder path with tooltip.
- [x] **Scroll position on file-watcher reload.** Verified.

### Main plan — carry-over from earlier phases

- [x] **File watcher: new files removed correctly.** Deleting a non-open
      file removes it from the tree without affecting reading position
      or which folders are expanded.
- [ ] **File watcher: new files appear** without restart.
- [ ] **File watcher: new folders appear** without restart.
- [x] **File watcher: open file edits live-reload** — verified, reading
      position survives the reload.
- [~] **Theme dropdown** — chrome + WebView2 markdown body track the
      theme correctly. Folder tree + outline text stay black on dark
      background — see "Known bugs" below.
- [x] **Outline auto-collapse below.**
- [x] **Outline always-collapse-containing.**
- [x] **Show .md extension** toggle.
- [x] **Show all files** toggle.
- [ ] **Show hidden files** toggle.

### JSONL transcript renderer

- [x] Category filter checkboxes at the top of a transcript toggle the
      visibility of each block category.
- [x] Filter selection persists across opens of the same file AND across
      app restarts.
- [x] Session header at the top of a transcript shows the recorded
      metadata.
- [x] Outline panel populates with one entry per conversation turn.
- [x] Large transcripts (the 4.8 MB one in `notes/transcripts/`) open
      at acceptable speed.
- [ ] Sticky filter row stays sticky during scroll.
- [ ] Dark mode: filter row, session header, `<details>` all read
      legibly.

### Theming T4 — GitHub body style

- [x] Body Style dropdown in Preferences switches Win11 ↔ GitHub.
- [ ] OS accent color shows through on links and the "reloaded" flash
      (chrome → CSS var via the bridge).
- [x] GitHub body looks right in light AND dark.
- [ ] Mermaid diagrams still render under GitHub body style.
- [ ] highlight.js theme reads well under each body style.

### Recent iframe / raw-browser work

- [ ] Open `sample/report.html` — renders correctly, no white-flash,
      iframe fills the content area.
- [ ] Open `sample/report.pdf` — PDF viewer works, navigation/zoom
      intact. First-open is ~2s (WebView2 PDF viewer init); subsequent
      switches are fast.
- [ ] HTML → markdown → HTML — instant switching (no `render.html`
      reload).
- [ ] PDF → markdown → PDF — instant switching back; PDF re-render
      itself still pays the viewer init cost.
- [ ] External link inside `report.html` opens in the OS browser
      (intercepted via `Frame_NavigationStarting`).
- [ ] Relative `<img>` / `<link>` references inside a user HTML file
      still resolve (base-tag injection at C# level).
- [ ] Find-in-page (Ctrl+F) — still works on markdown documents.
      Expected limitation: probably not in raw HTML or PDF iframes.

### UI tweaks (today)

- [ ] ⚙ Preferences button shows tooltip on hover.
- [ ] Open popup orders sections: Currently-open → Pinned → Recent →
      Open folder.
- [ ] Open popup is wider so most paths fit untrimmed.
- [ ] Long paths trim from the **start** (`…\folder\file`) with the
      full path on hover.
- [ ] Folder containing the currently-open file auto-expands on file
      open (including the cold-start path that restores last-opened
      file from settings); manual collapse stays collapsed until the
      next file inside that folder is opened.

---

## Known bugs (surfaced during the testing pass)

- [x] **File deletion collapses the folder tree.** Fixed in VaultService:
      Rescan now snapshots expanded-folder paths before rebuilding and
      restores them on the new tree.
- [x] **Wrap-long-names mode wraps past the border.** Fixed: `MaxWidth`
      on the TextBlock now goes through a multi-binding that subtracts
      `Depth * 19px` from the sidebar's row width.
- [x] **Ellipsis mode hides the ellipsis.** Same fix as wrap-mode —
      the per-row width is now correct.
- [x] **Dark mode: folder + outline tree text stays black.** Fixed:
      bound the TreeViewItem foreground and the template TextBlocks to
      `DynamicResource TextFillColorPrimaryBrush`.

---

## Theming — Phase T5 (planned but not started)

From `plans/theming.md`. Out of scope until someone gets to it.

- [ ] Verify Mermaid diagrams render against both body styles (dark
      background variant via Mermaid theme).
- [ ] highlight.js theme pair per body style:
      `github.css` / `github-dark.css` for GitHub mode;
      `vs.css` / `vs2015.css` for Win11 mode.
- [ ] Re-test all 9 phases of the main plan in dark and light — sidebar
      drag, file watcher reload, drag-drop, context-menu launch, every
      shortcut.
- [ ] Update `README.md` screenshots.
- [ ] Bump app version in `markdownviewer.md` and
      `markdownviewer-changelog.md`.

---

## Deferred / "decisions deferred" from the main plan

These were called out explicitly as not-for-v1 — track here so we can
revisit if any of them earn a real bar.

- [ ] **Wiki-link support `[[Note]]`.** Custom Markdig
      `IMarkdownExtension` (~80 lines) that emits
      `<a class="wikilink" data-target="...">`; the link resolver and
      scope-check are already there for ordinary `.md` links.
- [ ] **Showing frontmatter at all.** v1 hides it entirely. Bring
      `YamlDotNet` back and design a visual (card, sidebar panel,
      hover preview) if/when it's wanted.
- [ ] **Tabbed file viewing.** Single-doc-per-window today;
      multi-window is the answer to side-by-side.
- [ ] **Print.** WebView2 has `ShowPrintUI()` — easy add.
- [ ] **Export to HTML / PDF.** Trivial via WebView2's print-to-PDF.

---

## JSONL transcript renderer — out of scope (per `plans/transcripts.md`)

- [ ] **Pretty-print JSON in tool *outputs*.** Inputs already serialize
      indented; outputs are often plain text and we don't try to
      detect-and-reformat.
- [ ] **Click-through from `Read`/`Edit` tool paths** into the vault.
      Would need to recognize paths in tool arguments and rewrite to
      vault-local URLs.
- [ ] **Image content blocks.** Not present in current Claude Code
      transcripts; would render as `[image]` placeholder if encountered.
- [ ] **Toggle-all / preset filter modes** (e.g. "system noise on/off").
- [ ] **Custom per-user category labels or colors.**

---

## Raw-browser viewer — possible future improvements

- [ ] **Two-iframe approach** (one for HTML, one for PDF) so switching
      back to a recently-viewed PDF is instant. Doesn't help first-time
      opens. Adds a small amount of code + one iframe of memory.
- [ ] **PDF.js** replacement of WebView2's built-in PDF viewer. Much
      faster first-open for PDFs. Significant work: bundle PDF.js
      (~500 KB minified), implement canvas-based render, handle page
      nav / zoom / find-in-page. Only worth it if PDFs become central.

---

## Code cleanup

- [ ] Drop `AreDevToolsEnabled = true` from `MainWindow.xaml.cs` when
      shipping a release build (left on during dev).
- [ ] `outputs/MarkdownViewer/sample/asdfasdfasdfasdf/` — leftover test
      folder; delete when convenient.
