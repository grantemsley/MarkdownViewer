# Handoff: Markdown Reader (Windows 11 / Fluent)

## Overview

A small, OS-native markdown reader for browsing a folder ("vault") of `.md`
notes. Two-pane layout: a left sidebar that toggles between a **Folder** tree
and an **Outline** of the current document's headings, plus a content area that
renders parsed markdown with optional line numbers.

The mockup targets a Windows 11 / Fluent look (dark by default, with a light
mode). Earlier versions of this prototype also rendered macOS and GNOME
artboards; those were trimmed but the theme tables for them are still in the
source — see `THEMES` in `reader.jsx` if cross-platform parity becomes a goal.

## About the Design Files

The files in this bundle are **design references created in HTML/React** —
prototypes showing intended look and behavior, **not production code to copy
directly**. The task is to recreate these designs inside your target codebase
using its established patterns (component library, routing, state management,
file-system APIs, etc.). If the target codebase has no UI environment yet,
pick the framework that best matches the platform (e.g. Electron + React for a
cross-platform desktop reader, Tauri + your framework of choice, native
WinUI/SwiftUI/Adwaita).

The bundled markdown parser (`markdown.jsx`) is a deliberate toy — it covers
headings, paragraphs, fenced code with a tiny JS highlighter, tables, lists,
and inline `**bold**`/`*em*`/`` `code` ``/`[link](url)`. In production replace
it with `markdown-it`, `remark`, `marked`, or whatever your codebase already
uses. Behavior the renderer must preserve is documented under **Markdown
rendering** below.

## Fidelity

**High-fidelity.** Colors, typography, spacing, line-number gutter math,
preferences modal layout, and the Open-folder popover are intended to be
recreated pixel-accurately. The Win11 chrome (titlebar, traffic-light-style
caption buttons, close hover state) is also hi-fi.

The folder tree and outline tree are functional in the mockup but rely on a
mock vault (`vault.js`). In production they should be driven by the real file
system (folder picker via the File System Access API on the web, or native
file APIs in a desktop wrapper).

## Screens / Views

There is **one** screen — the reader window. All state changes (theme,
preferences, open folder, sidebar tab) happen in-place.

### Window chrome (Win11 / Fluent)

- **Titlebar**: 32px tall, background `--rd-chrome-bg`, bottom border
  `--rd-chrome-border`. Title text 12px regular, padding `0 12px`, color
  `--rd-text`, ellipsized.
- **Caption buttons**: 46px wide each, full titlebar height. Minimize
  (single horizontal line), Maximize (square outline), Close (×). Hover bg
  `rgba(127,127,127,0.15)`. Close hover bg `#e81123`, fg `#fff`. SVG strokes
  10×10 at 1px.
- **Outer border-radius**: 8px on the window.

### Sidebar

- **Width**: default 240px, resizable by dragging the right edge (6px hit
  zone, accent-tinted on hover with 0.15s delay). Clamped to `[160, 420]`.
- **Top**: 8/8/6 padding holding a 2-option segmented control (`Folder` /
  `Outline`). Segmented control uses `--rd-seg-*` tokens.
- **Body**: scroll region, padding `4px 0 6px`. Tree rows are 22px min-height,
  font-size 12px, padding `3px 8px 3px 0` with progressive left-padding per
  depth (`8 + depth*12 px`). Folder rows show a chevron + folder icon and
  toggle on click. File rows show a file/file-image/file-code icon and the
  filename. `.md` files are full-strength text; other files are muted and
  un-clickable.
- **Foot**: 6/8 padding, top border, two compact buttons separated by a
  flex spacer:
  - **Open** (left): folder-open icon + label + caret. Opens the
    Open-folder popover (see below).
  - **Preferences** (right): gear icon + label.
- **Wrap-text mode** (preference): rows allow line-wrapping. Important:
  single-line rows must keep the same vertical metrics as non-wrap mode —
  do not increase row padding wholesale; only the wrapped lines should add
  height.

### Open-folder popover

Anchored above the **Open** button, drops up with a 6px gap. Width
`240–300px`. Closes on outside click or Esc. Sections:

1. **Pinned** — shown only if non-empty. Each row: folder icon, name, pin
   button on the right (filled accent-colored when pinned).
2. **Currently open** — single row with folder name, a small
   uppercase "current" tag in `--rd-icode-bg`, and pin toggle.
3. **Recent** — up to 3 entries, excluding the current folder and any
   pinned folders. Each row pinnable.
4. **Footer** — `Open folder…` button (full-width, folder-open icon). On
   the web, call `window.showDirectoryPicker()`; in a desktop shell call
   the native folder-picker; fall back to a prompt if neither exists.

Row interaction: clicking a non-current row "opens" that folder (in the
mock this just rotates the current/recent state). Clicking the pin button
toggles pinned state without opening the folder.

### Content area

- **Breadcrumb**: padding `8px 16px`, bottom border, font 11.5px. Path
  segments separated by `/` in a faint color; last segment is muted-strong.
- **Scroll area** (`.rd-scroll`): the markdown page is centered with
  `margin: 0 auto`. The page width is `marginPct%` of the scroll area's
  content box (default 85%).
- **Page padding**: `28px 0 80px`. Font is preference-driven (System / Inter
  / Charter / mono); base size is preference-driven 11–22px.

### Line-number gutter (critical detail)

When line numbers are enabled, each block displays its source line in a
left gutter (`<div class="md-block" data-line="N">`). The gutter is
**3em** wide. It does **NOT** unconditionally carve space out of the
reading column. The behavior is:

- If the existing side-margin (half of `100% - marginPct`) is at least 3em,
  the line numbers float into that margin and the reading column keeps its
  width.
- If the side-margin is narrower than 3em, the scroll area's `padding-left`
  is increased by exactly the shortfall so the gutter fits.

Implemented in CSS (do not just `padding-left: 3em` — that's what we
explicitly fixed):

```css
.rd-scroll {
  padding-left: max(
    0px,
    calc(var(--rd-lineno-gutter, 0px) - (100% - var(--rd-page-pct, 100%)) / 2)
  );
}
```

`--rd-page-pct` is set as `${marginPct}%` on `.rd-content`. `--rd-lineno-gutter`
is set to `3em` on `.rd-content` only when line numbers are enabled. The
line-number pseudo-element is positioned `left: calc(-1 * gutter + 0.3em)`
relative to each `.md-block` (which is `position: relative`).

### Preferences modal

Centered backdrop modal, fades in over 0.12s, body scales in over 0.14s.
Width 440px (capped to viewport-32), max-height `calc(100% - 32px)`.

- **Head** (12/14 padding, chrome-bg, bottom border): title "Preferences",
  close button (22×22, ×).
- **Body**: scrollable with **always-visible** themed scrollbar (12px,
  `--rd-text-faint` thumb on `--rd-icode-bg` track, 6px radius, 3px
  transparent inner border for inset feel). The container the prototype
  lives inside (the design canvas) blanket-sets `scrollbar-width: none`;
  any production host that does the same needs an override scoped to
  `.rd-modal-body` and `.rd-scroll` / `.rd-sidebar-body` with `!important`
  on `scrollbar-width`.
- **Sections** (each section title is 10.5px / 600 / uppercase, color
  `--rd-text-faint`):
  - **Appearance**
    - Dark mode (toggle)
  - **Files**
    - Show file extensions (toggle)
    - Show non-markdown files (toggle)
    - Show hidden files (toggle, e.g. `.gitignore`)
    - Wrap text in sidebar (toggle)
    - Auto-switch to outline (toggle) — when on, opening a `.md` file
      switches the sidebar tab to Outline.
  - **Reading**
    - Show line numbers (toggle)
    - Typeface (select: System / Sans (Inter) / Serif (Charter) / Monospace)
    - Font size (stepper, 11–22, px)
    - Margins (slider, 50–100%, suffix `%`)
  - **Outline**
    - Auto-collapse below (select: H1…H6, Never) — outline tree starts
      collapsed at this level and deeper.
    - Always collapse containing (text input) — outline rows whose text
      contains this substring start collapsed.
- **Foot**: right-aligned `Done` button (primary, accent bg).
- **Pref-row layout**: 10/8 padding, 12px gap, hover bg `--rd-row-hover`.
  Rows are separated by a 1px hairline in `--rd-sidebar-border`.

### Outline pane

Built from the parsed heading list (level, text, id). Nested by level.
Each row: optional chevron twisty, a small `H<level>` badge (monospace,
`--rd-icode-bg` pill), then the heading text as an anchor to `#id`.
Initial open state per node is `!(collapseByLevel || matchesText)` — the
tree re-mounts via key whenever the two collapse prefs change so that
recomputes.

## Interactions & Behavior

- **Resize sidebar**: `mousedown` on `.rd-sidebar-resizer` starts a global
  `mousemove`/`mouseup` capture, updates width clamped to `[160, 420]`,
  sets `document.body.style.cursor = "col-resize"`.
- **Toggle tab**: Folder ↔ Outline via segmented control.
- **Open file**: click an `.md` row in the folder tree; the active row
  highlights via `--rd-row-active`. If `Auto-switch to outline` is on,
  sidebar flips to Outline.
- **Pin / unpin folder**: pin button in the Open popover. Pinned list
  shown on top; clicking pin in the Currently-open row toggles whether
  the current folder is pinned.
- **Open folder**: `window.showDirectoryPicker()` when available; on the
  desktop, route to your native folder picker.
- **Preferences**: click the gear; modal opens with backdrop. Esc or
  click outside closes. `Done` closes.
- **Dark / light**: now lives inside Preferences → Appearance.

## State Management

Top-level Reader state (see `Reader` in `reader.jsx`):

```ts
type Mode = "light" | "dark";

interface Prefs {
  // Files
  showExtensions: boolean;      // default true
  showNonMarkdown: boolean;     // default false
  showHidden: boolean;          // default false
  wrapSidebar: boolean;         // default false
  autoOutline: boolean;         // default false
  // Reading
  typeface: "system" | "sans" | "serif" | "mono"; // default "system"
  fontSize: number;             // 11..22, default 14
  marginPct: number;            // 50..100, default 85
  showLineNumbers: boolean;     // default false
  // Outline
  outlineCollapseBelow: 1|2|3|4|5|6|7; // 7 == never; default 7
  outlineCollapseContaining: string;   // default ""
}

interface State {
  tab: "folder" | "outline";
  path: string;                 // active file path inside the vault
  sidebarWidth: number;         // 160..420
  prefs: Prefs;
  prefsOpen: boolean;
  mode: Mode;                   // theme half (theme key = `win11-${mode}`)
  currentFolder: string;        // name of the vault root
  pinned: string[];             // pinned folder names
  recents: string[];            // recent folder names, newest-first
}
```

Persist `prefs`, `mode`, `pinned`, `recents`, `sidebarWidth`, and last open
file to disk (or `localStorage` for a web build).

## Design Tokens

All tokens are exposed as CSS custom properties on `.rd-app` / `.rd-win`
via the `themeVars(theme)` helper. Two themes ship: `win11-light` and
`win11-dark`. Values:

### Win11 Light

```
--rd-app-bg            #f3f3f3
--rd-chrome-bg         #f3f3f3
--rd-chrome-border     rgba(0,0,0,0.10)
--rd-sidebar-bg        #f9f9f9
--rd-sidebar-border    rgba(0,0,0,0.06)
--rd-content-bg        #ffffff
--rd-text              #1b1b1b
--rd-text-muted        #5c5c5c
--rd-text-faint        #8a8a8a
--rd-accent            #0067c0
--rd-row-hover         rgba(0,0,0,0.04)
--rd-row-active        rgba(0,103,192,0.10)
--rd-seg-bg            transparent
--rd-seg-active        #ffffff
--rd-seg-active-border rgba(0,0,0,0.10)
--rd-code-bg           #f6f8fa
--rd-code-border       rgba(0,0,0,0.06)
--rd-table-border      rgba(0,0,0,0.10)
--rd-table-header-bg   #f6f8fa
--rd-icode-bg          rgba(0,0,0,0.05)

Syntax highlight (JS):
--hl-kw    #0000ff
--hl-str   #a31515
--hl-num   #098658
--hl-com   #008000
--hl-fn    #795e26
--hl-punct #1b1b1b
```

### Win11 Dark

```
--rd-app-bg            #202020
--rd-chrome-bg         #202020
--rd-chrome-border     rgba(0,0,0,0.6)
--rd-sidebar-bg        #2b2b2b
--rd-sidebar-border    rgba(255,255,255,0.06)
--rd-content-bg        #1c1c1c
--rd-text              #e5e5e5
--rd-text-muted        #a0a0a0
--rd-text-faint        #7a7a7a
--rd-accent            #4cc2ff
--rd-row-hover         rgba(255,255,255,0.04)
--rd-row-active        rgba(76,194,255,0.12)
--rd-seg-bg            transparent
--rd-seg-active        #383838
--rd-seg-active-border rgba(255,255,255,0.08)
--rd-code-bg           #1e1e1e
--rd-code-border       rgba(255,255,255,0.06)
--rd-table-border      rgba(255,255,255,0.10)
--rd-table-header-bg   #262626
--rd-icode-bg          rgba(255,255,255,0.08)

Syntax highlight (JS):
--hl-kw    #569cd6
--hl-str   #ce9178
--hl-num   #b5cea8
--hl-com   #6a9955
--hl-fn    #dcdcaa
--hl-punct #d4d4d4
```

### Typography

```
--rd-font   "Segoe UI Variable", "Segoe UI", system-ui, sans-serif
--rd-mono   "Cascadia Mono", "Cascadia Code", Consolas, "Courier New", monospace

Body / UI: 13px / 12px in compact density
Tree rows: 12px
Section titles: 10.5px / 600 / 0.05em letter-spacing / uppercase
Markdown body: prefs.fontSize (11–22px), line-height 1.55
H1 1.92em / H2 1.41em (with top hairline + 0.95em top padding)
H3 1.11em / H4 1em / H5–H6 0.93em (muted)
Inline code: 0.88em, --rd-icode-bg, 4px radius
Code block: 0.88em / line-height 1.55 / 12/14 padding
```

### Spacing & radii

```
Window radius        8px (Win11)
Sidebar              240px default, 160–420 clamp, 6px resizer hit-zone
Sidebar foot         6/8 padding, top border
Tree row             padding 3px 8px 3px 0, min-height 22px, depth-step 12px
Pref row             padding 10/8, 12px gap
Modal radius         10px
Modal width          440px (max 100% - 32px)
Open popover         min 240 / max 300, 8px radius, 4px inner padding
Open popover row     padding 5px 6px 5px 8px, 5px radius, 6px gap
Pin button           22×22, 4px radius
Modal scrollbar      12px / 6px radius / 3px transparent inner border
Body scrollbar       10px / 5px radius / 2px transparent inner border
```

### Markdown rendering

Block-level parser exposes:

```ts
mdParse(source: string): Block[]
parseHeadings(source: string): { level, text, id }[]
<Markdown source={string} showLineNumbers={boolean} />
```

Each block is rendered into a `<div class="md-block md-block-<type>"
data-line="<1-indexed source line>">`. The `data-line` value is what the
gutter shows; multi-line blocks (paragraphs, fenced code, tables) collapse
to their starting line so the gutter is sparse.

Supported blocks: heading (`#…######`), fenced code (` ``` `, optional lang),
pipe tables (with separator row), `-`/`*` unordered list, `1.` ordered list,
paragraph (collapses adjacent non-blank lines).

Supported inlines: `**bold**`, `*em*`, `` `code` ``, `[text](url)`.

JS-ish highlighter token classes: `hl-kw hl-str hl-num hl-com hl-fn
hl-punct`. Triggered when fence lang is `js`, `javascript`, `jsx`, or `ts`.

Heading slug: lowercase, strip non-`\w\s-`, collapse spaces to `-`.

## Assets

- **Icons**: hand-rolled inline SVGs in the `Icon` component
  (`reader.jsx`). 16×16 viewBox, 1.4 stroke, currentColor. Names:
  `chevron-right`, `chevron-down`, `folder`, `folder-open`, `file`,
  `file-image`, `file-code`, `sun`, `moon`, `search`, `gear`, `close`.
- **Pin icon**: defined inline inside `PinButton` (`reader.jsx`).
  Replace with your icon library if you have one (Lucide / Phosphor /
  Fluent UI icons are all good fits).
- **No images / no bitmap assets** — the design is entirely vector.
- **Fonts** are system stacks; nothing to bundle.

## Files

In this handoff (the design source):

- `Markdown Reader.html` — host page; loads React 18, Babel, and the JSX
  files below.
- `reader.jsx` — themes, sidebar, content, preferences modal, open-folder
  popover, Win11 chrome.
- `markdown.jsx` — toy markdown parser + `<Markdown>` renderer + JS
  highlighter.
- `reader.css` — all styling (tokens consumed as CSS vars from
  `themeVars`).
- `vault.js` — mock vault tree and mock markdown content (`window.VAULT`,
  `window.MARKDOWN`). Replace with real file-system data.
- `app.jsx` — design-canvas host (single Win11 artboard). Drop this and
  mount `<Reader theme="win11-dark" mode="dark" onToggleMode={…} />`
  full-window in your real shell.
- `design-canvas.jsx` / `macos-window.jsx` — design-canvas + legacy chrome
  components, not needed in production.

## Implementation notes / gotchas

1. **Line-number gutter math** (above): do not regress to `padding-left:
   3em` on the markdown root. The gutter only steals room when there isn't
   enough margin to absorb it.
2. **Scrollbar visibility**: the design canvas in this prototype hides
   scrollbars globally with `.dc-card *{scrollbar-width:none}`. In your
   real shell that won't apply, but if your shell does something similar,
   ensure `.rd-scroll`, `.rd-sidebar-body`, `.md-code`, and `.rd-modal-body`
   each force their own `scrollbar-width` (`thin` for content, `auto` for
   modal).
3. **Wrap-sidebar regression**: when implementing the wrap toggle, keep
   the row's existing min-height/padding. Only allow `white-space: normal`
   + `align-items: flex-start`; do not bump padding-top/bottom or
   single-line rows will grow.
4. **Preferences modal animation**: backdrop fade 0.12s, modal scale-in
   from 0.97 + 6px translate, 0.14s. Keep these snappy.
5. **Open-folder popover**: closes on outside-click and Esc. Use
   `mousedown` (not `click`) for outside-click so it doesn't race with the
   button itself.
6. **Theme parity**: `THEMES` in `reader.jsx` still carries mac and GNOME
   palettes. If/when you want to support those platforms, the chrome
   components are also still in the source.
