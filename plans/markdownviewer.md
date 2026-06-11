# MarkdownViewer

**Status:** ✅ Done · Last updated 2026-05-25 · v0.6.0

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Install .NET 10 SDK | 10.0.300 installed via winget |
| ✅ Done | Phase 1 — Skeleton | WPF project, App.xaml, MainWindow with sidebar + WebView2, virtual host mappings (app.local, vault.local) |
| ✅ Done | Phase 2 — Vault and tree | VaultService walks folder + debounced FileSystemWatcher; TreeView bound; open-folder popup with pinned/recent |
| ✅ Done | Phase 3 — Markdown render | Markdig pipeline (advanced + frontmatter + math + line-numbers), bridge sends HTML, Mermaid + highlight.js bundled, link interception in JS |
| ✅ Done | Phase 4 — Other viewers | ContentRouter by ext; raw browser for HTML/PDF; image viewer; text viewer with highlight.js; UTF-8+BOM+1252 fallback encoding; binary detection |
| ✅ Done | Phase 5 — Preferences | SettingsService %APPDATA% JSON; PreferencesWindow with all toggles, typeface, font size, margins, outline collapse |
| ✅ Done | Phase 6 — Outline + line numbers | Outline tree built from heading list; line-number gutter via PreciseSourceLocation + md-block data-line attrs + conditional gutter CSS |
| ✅ Done | Phase 7 — Entry points | CLI arg (folder or file), drag-drop (first item wins), HKCU Install-ContextMenu.ps1 |
| ✅ Done | Phase 8 — Polish | All keyboard shortcuts, find-in-page via CoreWebView2.Find, breadcrumb, file-watcher reload (focus-independent), empty states, system theme detection, window state persistence |
| ✅ Done | Phase 9 — Compile exe | publish/MarkdownViewer.exe (1.8MB framework-dependent single-file); launches with sample folder, persists settings, no crash log |


A small, fast Windows markdown reader for folders of `.md` files. Renders
GFM markdown including Mermaid diagrams, fenced code with syntax
highlighting, tables, footnotes, and math. YAML frontmatter is parsed
out and hidden. Non-markdown files open in a viewer appropriate to
their type (HTML and PDF in the embedded browser, source/text files in
a plain text viewer, images inline).

## Goals

- Open a folder; browse its `.md` files in a sidebar tree.
- Render markdown using full GitHub-flavored syntax + Mermaid + math.
- Open HTML and PDF files in an embedded browser component (same
  WebView2 the markdown view uses).
- Open source / script files (`.ps1`, `.sh`, `.py`, etc.) in a plain
  text viewer with syntax highlighting.
- Open images inline.
- Light enough that it's reasonable to leave running.

Out of v1 scope (deliberately): polished Win11 chrome / Mica / Fluent
controls. We're starting with stock WPF native styling (standard window
chrome, default `CheckBox` / `TabControl` / etc.) and will revisit
visual polish after the functionality is in place.

## Non-goals (v1)

- Editing markdown. View only.
- Full-text search across the folder.
- Cloud sync, plugins, or multi-vault management.
- Mac / Linux support.

## Stack

- **Language / runtime:** C# / .NET 10 (current LTS, supported through
  Nov 2028). .NET 8 is also LTS but goes EOL Nov 2026.
- **UI shell:** WPF, **stock styling** for v1. Default window chrome,
  default controls. Polished Win11 look is a later iteration.
- **Content view:** Microsoft Edge **WebView2** filling the right pane.
  Hosts the rendered markdown, HTML files, PDF files, and images.
- **Markdown engine:** [Markdig](https://github.com/xoofx/markdig) with
  advanced extensions enabled + YAML frontmatter (stripped from output)
  + Mermaid passthrough.
- **Code highlighting (inside the WebView):**
  [highlight.js](https://highlightjs.org/) bundled locally. No CDN
  dependency.
- **Diagrams:** [Mermaid](https://mermaid.js.org/) bundled locally,
  initialized on document load against `<pre class="mermaid">` blocks.
- **No frontend framework.** The content document is plain HTML + CSS
  + a small vanilla JS shim for Mermaid init, scroll-to-anchor, and the
  WebView2 ↔ WPF message bridge.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│ WPF Window (custom chrome)                              │
│ ┌──────────────────┬──────────────────────────────────┐ │
│ │ Native sidebar   │ WebView2 (content area)          │ │
│ │  - Folder tab    │   - markdown HTML                │ │
│ │  - Outline tab   │   - HTML/PDF/image files         │ │
│ │  - Open / Prefs  │   - text viewer (HTML page too)  │ │
│ └──────────────────┴──────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### Why native sidebar + WebView2 content (not both in WebView2)

- WPF `TreeView` with custom templates handles the folder/outline trees
  natively with good keyboard nav and virtualization.
- File watcher events update the tree directly — no IPC round-trip.
- The content area genuinely needs a browser engine (PDF, Mermaid, HTML
  files); the sidebar doesn't.
- The Win11 title bar / caption buttons / Mica are best done in WPF
  custom chrome.

### WebView2 setup

- One `WebView2` control. Reused for every content type.
- The Edge WebView2 Runtime ships preinstalled on Windows 11 and
  auto-updates, so no runtime bootstrap step is needed for Win11
  targets.
- `CoreWebView2.SetVirtualHostNameToFolderMapping("vault.local",
  <currentFolderPath>, CoreWebView2HostResourceAccessKind.Allow)` so
  relative images and links work from a stable origin without
  `file://` quirks. When the open folder changes, call
  `ClearVirtualHostNameToFolderMapping("vault.local")` first, then
  re-map to the new path.
- `CoreWebView2.SetVirtualHostNameToFolderMapping("app.local",
  <appAssetsPath>, ...)` for the renderer HTML shell, CSS, highlight.js,
  Mermaid. Set once at startup.
- WebView2 navigates to `https://app.local/render.html` for the
  markdown shell (file content arrives via `setDoc` message, not URL
  params, so we don't have to reload the page per file), or to
  `https://vault.local/<relative>` for HTML/PDF files, or to
  `https://app.local/viewer.html` for text/image viewers (content
  again via message).

### Bridge protocol

JSON messages. **WPF → JS** uses
`CoreWebView2.PostWebMessageAsJson(json)` (received in JS via
`chrome.webview.addEventListener("message", e => ...)`).
**JS → WPF** uses `window.chrome.webview.postMessage(obj)` (received in
WPF via `CoreWebView2.WebMessageReceived`).

| From → To | Type | Payload |
|---|---|---|
| WPF → JS | `setDoc` | `{kind, html, headings, path}` for markdown; `{kind:"raw", url}` for HTML/PDF/image (renderer just navigates); `{kind:"text", lang, body, path}` for text |
| WPF → JS | `setPrefs` | `{theme, typeface, fontSize, marginPct, showLineNumbers}` |
| WPF → JS | `scrollToHeading` | `{id}` |
| JS → WPF | `openLink` | `{href}` — for in-app `[[wikilink]]` or click-through to another vault file |
| JS → WPF | `headings` | `[{level, text, id}]` — populated after render so outline can fill |
| JS → WPF | `requestExternal` | `{url}` — for http/https links; WPF opens in OS default browser |

## Repo layout

```
outputs/
└── MarkdownViewer/
    ├── MarkdownViewer.sln
    ├── src/
    │   ├── MarkdownViewer.csproj
    │   ├── App.xaml + App.xaml.cs
    │   ├── MainWindow.xaml + .cs        // custom-chrome window
    │   ├── ViewModels/
    │   │   ├── MainViewModel.cs
    │   │   ├── PrefsViewModel.cs
    │   │   └── TreeNodeViewModel.cs
    │   ├── Views/
    │   │   ├── Sidebar.xaml              // tabs + tree + footer
    │   │   ├── PreferencesWindow.xaml    // modal-style window
    │   │   └── OpenFolderPopup.xaml
    │   ├── Services/
    │   │   ├── VaultService.cs           // open folder, build tree, watch
    │   │   ├── MarkdownService.cs        // Markdig pipeline + render (frontmatter stripped)
    │   │   ├── ContentRouter.cs          // pick viewer by extension
    │   │   ├── SettingsService.cs        // load/save JSON in %APPDATA%
    │   │   └── WebViewBridge.cs          // WebView2 ↔ WPF messages
    │   ├── Themes/
    │   │   ├── Win11Light.xaml
    │   │   ├── Win11Dark.xaml
    │   │   └── Brushes.xaml
    │   └── WebAssets/                    // copied to output as content
    │       ├── render.html
    │       ├── viewer.html
    │       ├── reader.css                // ported from design handoff
    │       ├── bridge.js
    │       ├── lib/
    │       │   ├── mermaid.min.js
    │       │   └── highlight.min.js + styles/
    │       └── tokens.css                // CSS vars set from prefs
    └── installer/
        ├── Install-ContextMenu.ps1       // registry edits for Explorer
        └── Uninstall-ContextMenu.ps1
```

App settings: `%APPDATA%\MarkdownViewer\settings.json`.

## Markdig pipeline

```csharp
// UseAdvancedExtensions already includes: tables (pipe + grid),
// footnotes, autolinks, task lists, definition lists, emphasis extras,
// figures, footers, citations, custom containers, abbreviations, media
// links, auto-identifiers, generic attributes, and crucially
// UseDiagrams (which wraps fenced lang=mermaid in <pre class="mermaid">).
// Math is NOT in advanced — add it explicitly.
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .UseYamlFrontMatter()
    .UseMathematics()           // $...$ / $$...$$
    .Build();
```

The pipeline omits `UseSoftlineBreakAsHardlineBreak`: GitHub renders a
single newline as a space in README files (`<br>` only inside issue
comments), and the GFM-rendered-on-GitHub feel is what users will
expect.

### Link click handling

Standard markdown links (`[text](other-note.md)`) inside a rendered
document need interception — otherwise clicking one would just have
WebView2 navigate to `vault.local/other-note.md` and download/show
the raw file. The renderer's `NavigationStarting` event handles all
clicks:

- `https://app.local/...` (our own shell) — never navigated to via
  link, so reject if seen.
- `http://` / `https://` (external) — cancel navigation, post
  `requestExternal` to WPF, which opens the URL in the OS default
  browser.
- `https://vault.local/<relative>` — cancel navigation, resolve the
  relative path to an absolute file path under the open folder, then
  route through the same content router that handles tree clicks
  (`.md` → re-render in this WebView; `.pdf`/`.html` → navigate; etc.).
  Reject anything that resolves outside the vault.
- Anchors (`#heading-id`) — let through; the WebView2 scrolls
  natively.

### Frontmatter handling

- `UseYamlFrontMatter()` recognizes a leading `--- … ---` block and
  treats it as metadata rather than rendering it. We don't surface it
  anywhere in v1 — it's simply stripped.
- We can drop `YamlDotNet` from the dependency list since we no longer
  parse the YAML.

### Heading IDs and outline

- Markdig's `UseAutoIdentifiers` slugifies headings.
- After render, the JS posts the heading list back: `[{level, text, id}]`.
- The native sidebar outline tree builds from that list.
- Clicking outline → `scrollToHeading` message → WebView2 scrolls.

## Empty states

The content area shows a centered, muted message in these cases:

| Situation | Message |
|---|---|
| No folder open yet | "Open a folder to get started." Includes a button that opens the folder picker. |
| Folder open, no file selected | "Pick a file from the sidebar." |
| Open file deleted / renamed externally | "This file no longer exists." |
| Startup, saved last folder no longer exists | App opens with no folder (no crash); shows the first message above. The stale entry stays in `recents` but is shown with a muted icon and refuses to open when clicked. |

## Content router

| Extension | Viewer | Notes |
|---|---|---|
| `.md`, `.markdown`, `.mdown`, `.mkd` | Markdown | The main one. |
| `.html`, `.htm`, `.xhtml` | Raw browser | Navigate WebView2 to the file via `vault.local` virtual host. |
| `.pdf` | Raw browser | WebView2 has a built-in PDF viewer. |
| `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.svg`, `.bmp`, `.ico` | Image | `viewer.html` displays centered with a checker background. SVGs render via `<img src="...">` (not inline DOM) to avoid executing scripts embedded in untrusted SVGs. |
| `.txt`, `.log`, `.csv`, `.tsv` | Text (no highlight) | Plain `<pre>` with `white-space: pre-wrap` so long lines soft-wrap. (Per-document wrap toggle deferred.) |
| `.ps1`, `.psm1`, `.sh`, `.bash`, `.zsh`, `.py`, `.rb`, `.js`, `.ts`, `.jsx`, `.tsx`, `.json`, `.yaml`, `.yml`, `.toml`, `.ini`, `.cfg`, `.xml`, `.cs`, `.cpp`, `.c`, `.h`, `.go`, `.rs`, `.java`, `.kt`, `.swift`, `.sql`, `.css`, `.scss`, `.less` | Text (highlight.js) | Language picked by extension. |
| Anything else | Text (no highlight) | Best-effort; binary detection (NUL byte in first 8KB) shows a placeholder card instead. |

**Text encoding.** Read the file as bytes. If it starts with UTF-8,
UTF-16 LE/BE, or UTF-32 BOM, use that. Otherwise try UTF-8 with the
strict decoder; if that throws, fall back to Windows-1252 (covers
legacy logs from older Windows apps without garbling modern files).

**Task lists** (`- [x]` / `- [ ]`) render as static checkboxes —
read-only, not togglable. The viewer never writes to disk.

**Soft wrap** is on by default for all text viewers (`pre-wrap`); a
per-document toggle is a later iteration.

## Sidebar (native WPF, stock styling)

- `TabControl` with two tabs: "Folder" and "Outline". Stock look.
- Folder pane: WPF `TreeView` of folders/files. Folders expand on click;
  files open on click. Minimal `HierarchicalDataTemplate` to get
  chevron + icon + label. Icon by extension (folder, file, file-image,
  file-code) — small inline SVG paths or Segoe MDL2 / Fluent Icons glyphs.
- Outline pane: another `TreeView`, items from the latest heading list.
  Each row shows an `H<n>` text prefix + heading text. Click scrolls
  the WebView.
- Filter rules from prefs: `showExtensions`, `showNonMarkdown`,
  `showHidden`, `wrapSidebar`, `autoOutline`.
- Footer: "Open" `Button` (drops a `Popup` with Pinned / Currently open
  / Recent / "Open folder…") and "Preferences" `Button`.
- Resizable: WPF `GridSplitter` between sidebar and content; width
  clamped 160–420 and persisted.

## Window chrome

- Stock WPF window: default `WindowStyle="SingleBorderWindow"`,
  standard Win11 title bar and caption buttons. No custom chrome, no
  Mica, no transparency tricks.
- Revisit later once the rest works and we know what's worth styling.
  See "Decisions deferred".

## Preferences

Same structure as the design: appearance, files, reading, outline.
Stock WPF controls (`CheckBox`, `ComboBox`, numeric up-down via a
`TextBox` + buttons, `Slider`). All persisted to `settings.json`.

```json
{
  "theme": "win11-dark",
  "files": {
    "showExtensions": true,
    "showNonMarkdown": false,
    "showHidden": false,
    "wrapSidebar": false,
    "autoOutline": false
  },
  "reading": {
    "typeface": "system",
    "fontSize": 14,
    "marginPct": 85,
    "showLineNumbers": false
  },
  "outline": {
    "collapseBelow": 7,
    "collapseContaining": ""
  },
  "window": {
    "x": 100, "y": 100, "width": 1200, "height": 800,
    "sidebarWidth": 240
  },
  "vaults": {
    "current": "C:\\Notes",
    "pinned": [],
    "recents": ["C:\\Notes", "C:\\Projects"],
    "lastFile": { "C:\\Notes": "intro.md" }
  }
}
```

Preferences UI: a secondary `Window` with `Owner` set to the main
window and `ShowDialog()` for modality. Stock chrome.

## Entry points

**Multi-window app.** Every launch (CLI, drag-drop, context menu,
shortcut) opens a new top-level `Window`. No IPC, no single-instance
mutex. Two folders open side-by-side is just "launch twice." Each
window has its own state and settings are written by whichever closes
last (last-write-wins is acceptable for v1).

- **In-app folder picker:** `Microsoft.Win32.OpenFolderDialog`,
  shipped in .NET 8+ for WPF. Wraps the modern Win11 shell picker;
  prefer over the legacy WinForms `FolderBrowserDialog` or manual
  `IFileOpenDialog` COM interop.
- **Command-line:**
  - `MarkdownViewer.exe` → reopen last vault + last file.
  - `MarkdownViewer.exe C:\Notes` → open that folder.
  - `MarkdownViewer.exe C:\Notes\foo.md` → open the containing folder
    `C:\Notes\` and select `foo.md`.
- **Drag-and-drop:** handle `DragEnter` / `Drop` on the main window.
  Dropping a folder opens it; dropping a file opens its containing
  folder and selects the file. If multiple items are dropped, just
  take the first.
- **Explorer context menu:** `installer/Install-ContextMenu.ps1` adds
  per-user verbs under
  `HKCU\Software\Classes\Directory\shell\Open in MarkdownViewer` and
  `HKCU\Software\Classes\Directory\Background\shell\Open in MarkdownViewer`,
  command `"<exe>" "%V"`. HKCU avoids needing admin and is the modern
  recommendation for sideloaded tools. **Caveat:** on Win11 the new
  context menu hides classic verbs behind "Show more options"
  (Shift+F10 or the legacy menu still surfaces them directly). First-
  class placement in the new menu requires an `IExplorerCommand` shell
  extension with package identity (Sparse Package or full MSIX), which
  needs code-signing — out of scope for v1.
  - Optional file-association entries for `.md` (only if user runs the
    script with `-AssociateMd`).

## File watching

- `FileSystemWatcher` rooted at the open folder, `IncludeSubdirectories
  = true`.
- Debounce events 250 ms; reload the tree on any add/remove/rename;
  reload the currently open document on Modify **regardless of window
  focus** (so editing a note in another app updates the view live).
- If the currently open file is deleted or renamed away, switch to the
  "this file no longer exists" empty state.
- Show a tiny unobtrusive "reloaded" hint in the breadcrumb area when
  the active file refreshes.

## Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+O` | Open folder |
| `Ctrl+F` | Find in page |
| `Ctrl+,` | Open Preferences |
| `Ctrl+B` | Toggle sidebar |
| `Ctrl+1` / `Ctrl+2` | Switch sidebar tab (Folder / Outline) |
| `Ctrl+R` or `F5` | Reload current file |
| `Esc` | Close popups / preferences / find bar |
| `Alt+Left` / `Alt+Right` | Back / forward in file history |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` | Increase / decrease / reset font size |

## Find in page

- `Ctrl+F` opens a small find bar pinned to the top of the content
  area (native WPF overlay, not inside the WebView).
- Backed by the WebView2 first-party `CoreWebView2.Find` API: call
  `StartAsync(query, options)` to highlight matches, then `FindNext` /
  `FindPrevious` (`Enter` / `Shift+Enter`). The control reports total
  match count and current index — surface both in the bar.
- `Esc` closes the bar and clears highlights (`Stop`).
- Works for every viewer that's rendered as HTML inside the WebView2
  (markdown, text, HTML files). PDFs use WebView2's own built-in PDF
  find UI (also Ctrl+F when the PDF has focus).

## Phases

### ✅ Phase 1 — Skeleton

- New WPF project targeting `net10.0-windows`. NuGet (latest stable as
  of May 2026): `Microsoft.Web.WebView2` 1.0.3967.48, `Markdig` 1.2.0.
- Stock-chrome `MainWindow` — no custom title bar, no Mica.
- Two-column `Grid`: sidebar placeholder + WebView2 placeholder, with
  a `GridSplitter` between them.
- WebView2 initialized with `app.local` and `vault.local` virtual host
  mappings; navigates to `https://app.local/render.html` and waits for
  a `setDoc` message.

### ✅ Phase 2 — Vault and tree

- `VaultService.OpenFolder(path)`: walk directory, build a node tree,
  start `FileSystemWatcher`.
- Sidebar `TreeView` bound to the tree.
- Filtering by prefs (showHidden / showNonMarkdown / showExtensions).
- Open-folder popup with current/pinned/recent (persistence wired).
- Folder picker (`IFileOpenDialog`).

### ✅ Phase 3 — Markdown render

- `MarkdownService.Render(file)`: read file → Markdig → HTML.
  Frontmatter is stripped by `UseYamlFrontMatter()` and not surfaced.
- Mermaid passthrough + highlight.js in `render.html`.
- WebView2 bridge: receive `{type:"setDoc"}` and inject into the page;
  return heading list; outline pane populates.
- `NavigationStarting` link interception: external URLs → OS browser;
  in-vault `.md` → re-render; in-vault other files → route through
  content router; anchors pass through.

### ✅ Phase 4 — Other viewers

- ContentRouter picks viewer by extension.
- HTML/PDF: WebView2 navigates to `vault.local/<rel>`.
- Image / text viewer: `viewer.html` with the file's content streamed
  in via the bridge.
- Binary detection short-circuits text viewer to a "binary file"
  placeholder.

### ✅ Phase 5 — Preferences

- `SettingsService` load/save JSON.
- Preferences window UI (toggles, select, stepper, slider).
- Live apply: theme toggles re-skin native sidebar, `setPrefs` message
  re-skins the WebView page.
- Outline collapse rules apply on render.

### ✅ Phase 6 — Outline + line numbers

- Outline tree built from heading list, with collapse-below + collapse-
  containing rules per design.
- Line-number gutter:
  - Markdig pipeline configured with `PreciseSourceLocation = true` so
    every block in the AST carries its source span.
  - Custom `HtmlRenderer` wrapper emits each block as `<div
    class="md-block md-block-<type>" data-line="<1-indexed start>">`.
    Multi-line blocks collapse to their starting line so the gutter
    stays sparse.
  - CSS gutter math per the design — gutter floats into the existing
    side margin when it's wide enough; otherwise the scroll area's
    `padding-left` is grown by exactly the shortfall. Do NOT
    unconditionally `padding-left: 3em`.

### ✅ Phase 7 — Entry points

- Command-line arg parsing (folder vs. file).
- Drag-and-drop folder/file onto the window.
- `Install-ContextMenu.ps1` for the Explorer right-click entry.

### ✅ Phase 8 — Polish

- Keyboard shortcuts.
- Find-in-page bar (Ctrl+F, `CoreWebView2.Find`).
- Breadcrumb path display above the content.
- File-watcher debounced reload (focus-independent).
- Empty states wired (no folder / no file selected / file deleted /
  stale last-folder).
- Detect Windows light/dark theme on first run; match the WebView2
  content background accordingly. (Full theming still deferred.)
- Window state persistence.
- README and packaging notes.

### ✅ Phase 9 — Packaging

- `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true --self-contained false`
  → small `MarkdownViewer.exe` that depends on the .NET 10 **Desktop**
  Runtime (`Microsoft.WindowsDesktop.App`) plus the WebView2 Runtime
  (preinstalled on Win11).
- Optional `--self-contained true` build for portability (~80–100MB
  exe; bundles the .NET runtime).

## Testing checklist

Things that have been wired up but the user has not yet confirmed work end
to end. Tick off as they get verified.

### Re-verify after the latest round of fixes

- [ ] **File deletion → empty state.** Delete or rename the currently-open
      .md file from another tool. Content area should switch to
      "This file no longer exists." (previously kept showing a stale view).
- [ ] **Auto-switch to outline.** With AutoOutline on, clicking a different
      .md while on the Folder tab should switch the sidebar to Outline
      (previously failed on manual click; now deferred via Dispatcher.BeginInvoke).
- [ ] **Wrap long names — wrap mode.** "Wrap long names in sidebar" on:
      long filenames break across multiple lines.
- [ ] **Wrap long names — ellipsis mode.** Same toggle off: long filenames
      ellipsize with `…`; the full name appears in a tooltip on hover.
- [ ] **Empty folders.** Empty subfolders show in the tree (previously
      hidden because they had no visible children).
- [ ] **Open popup full paths.** Pinned / Currently open / Recent rows in
      the Open popup show the full folder path (with ellipsis when too long;
      tooltip with the full path).
- [ ] **Scroll position on file-watcher reload.** Scroll into the middle of
      a long document, then create / delete a sibling file from another
      tool. The view must stay at the same position (was resetting to top).

### Carry-over (also worth re-confirming nothing broke)

- [ ] **File watcher: new files appear** without restart.
- [ ] **File watcher: new folders appear** without restart.
- [ ] **File watcher: open file edits live-reload** (and the breadcrumb
      flashes "reloaded").
- [ ] **Theme dropdown** — System / Light / Dark applied to both sidebar
      and content (sidebar text legibility is a known follow-up tied to
      the future theming pass).
- [ ] **Outline auto-collapse below** — set to H2, deeper headings start
      collapsed.
- [ ] **Outline always collapse containing** — substring match starts those
      headings collapsed.
- [ ] **Show .md extension** toggle.
- [ ] **Show all files** toggle.
- [ ] **Show hidden files** toggle.

## Decisions deferred (call them out and revisit)

- **Wiki-link support `[[Note]]`.** Dropped from v1. If your notes use
  this syntax later, add a custom Markdig `IMarkdownExtension` (~80
  lines) that emits `<a class="wikilink" data-target="...">`; the link
  resolver and scope-check are already there for ordinary `.md` links.
- **Polished Win11 / Fluent styling.** Custom window chrome, Mica
  backdrop, Fluent control templates (toggle switches, segmented
  control), themed scrollbars, modal animations. The design handoff
  remains the reference if/when we go this direction. WPF-UI
  (https://github.com/lepoco/wpfui) would do most of this with one
  NuGet dependency.
- **Light/dark theme.** Stock WPF follows the system colors weakly;
  proper light/dark theming arrives with the styling pass above. For
  v1, just match the WebView2 content area to the OS theme.
- **Showing frontmatter at all.** v1 hides it entirely. If/when we
  want to surface it (as a card, sidebar panel, or hover preview),
  bring `YamlDotNet` back and design the visual.
- **Tabbed file viewing.** v1 is single-document per window (and
  multi-window is the answer to wanting two things side-by-side).
- **Print.** WebView2 has `ShowPrintUI()` — easy to add in Phase 8 if
  wanted.
- **Export to HTML / PDF.** Trivial via WebView2's print-to-PDF; not in
  v1.

## Changelog


## v0.6.0 — 2026-05-25

Pre-implementation sanity pass against the design doc. Added:
- **Empty states** section: no folder / no file / file deleted / stale
  last-folder all have explicit handling.
- **Find in page** (Ctrl+F) via WebView2's first-party
  `CoreWebView2.Find` API — small native overlay bar.
- **File watching always reloads** the open document regardless of
  window focus.
- **Line-number gutter implementation** spelled out: Markdig's
  `PreciseSourceLocation`, custom HtmlRenderer wrapper emitting
  `md-block` divs with `data-line`, conditional gutter CSS.
- **Text encoding**: UTF-8 with BOM detection, fall back to
  Windows-1252 for legacy logs.
- **Task lists are static**, read-only.
- **Soft wrap** is the default for text viewers.
- **Multi-file drag-drop** takes the first item.
- **System theme detection** on first run added to Phase 8.

User confirmed keeping (was offered for trimming): line-number gutter,
outline collapse prefs (collapse-below + collapse-containing),
`wrapSidebar` pref, `autoOutline` pref, pinned folders.

## v0.5.0 — 2026-05-25

- Wiki-link support `[[Note]]` dropped from v1. Custom Markdig
  extension and the wiki-specific click handler are gone. Standard
  markdown links between files still work via `NavigationStarting`
  interception (which we needed anyway, and is now documented as the
  single mechanism for all link handling). Wiki-link added as a
  deferred decision in case future notes need it.

## v0.4.0 — 2026-05-25

- Frontmatter dropped from v1: `UseYamlFrontMatter()` still strips
  the leading `--- … ---` block so it doesn't render as ugly text, but
  we surface no frontmatter UI. `YamlDotNet` dependency removed.
  Showing it some other way is a deferred decision.
- .NET 10 target confirmed.

## v0.3.0 — 2026-05-25

User decisions after plan-review questions:
- v1 uses stock WPF native styling. Dropped custom chrome, Mica,
  Fluent control templates, light/dark theme palette work — all moved
  to "Decisions deferred." TabControl + standard CheckBox/ComboBox/etc
  replace the bespoke design controls. Visual polish revisits after
  the app works.
- Multi-window confirmed: every launch opens a new top-level window;
  no IPC, no single-instance mutex.
- Context menu stays as HKCU verb (lands under "Show more options" on
  Win11). User has classic context menu enabled anyway.
- Frontmatter card: ship a minimal "key: value" stack for v1, iterate
  later. Logged as a deferred decision.

## v0.2.0 — 2026-05-25

Plan-review pass. Clear-cut fixes applied:
- Bumped target framework to .NET 10 (current LTS through Nov 2028);
  .NET 8 would EOL six months from now.
- Replaced `IFileOpenDialog` with `Microsoft.Win32.OpenFolderDialog`
  (shipped in .NET 8+ for WPF).
- Moved Explorer context-menu registry from `HKCR` to per-user
  `HKCU\Software\Classes\...` so it installs without admin; noted the
  Win11 "Show more options" limitation honestly.
- Removed Markdig pipeline duplicates (`UseAdvancedExtensions` already
  includes `UseDiagrams`, auto-identifiers, generic attributes);
  flagged that `UseSoftlineBreakAsHardlineBreak` is intentionally off.
- Confirmed Markdig has no built-in wiki-link extension (open issue
  #714) — custom extension is unavoidable. Specified the idiomatic
  registration pattern.
- Mica details: explicit warning against `AllowsTransparency=true`,
  `Background="Transparent"` requirement, and pairing with
  `DWMWA_USE_IMMERSIVE_DARK_MODE` for the dark-mode title bar.
- WebView2 bridge clarified: `PostWebMessageAsJson` is WPF→JS,
  `chrome.webview.postMessage` is JS→WPF. Noted that `vault.local`
  must be re-mapped (clear then set) on folder change.
- Pinned current package versions (Markdig 1.2.0,
  Microsoft.Web.WebView2 1.0.3967.48).
- Security note: render SVGs via `<img>` not inline DOM.
- Wiki-link resolution refuses paths outside the current vault.

## v0.1.0 — 2026-05-25

Initial plan. WPF + WebView2 + Markdig stack. Native sidebar, WebView2
content area. Markdown / HTML / PDF / image / text viewers. Wiki-links,
frontmatter card, Mermaid, syntax highlighting. Multi entry-points
(folder picker, command line, drag-drop, Explorer context menu). Phased
implementation, packaging via single-file `dotnet publish`.
