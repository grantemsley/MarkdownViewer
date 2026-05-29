# Sidebar layout options

Four mockups for showing the folder tree and outline at the same time.
Open each `.svg` in MarkdownViewer to compare.

| File | Layout |
|---|---|
| `00-current-tabs.svg` | Baseline — one tab at a time |
| `01-vertical-split.svg` | Folder on top, outline below, draggable splitter |
| `02-right-outline-column.svg` | Three-column: folder left, content middle, outline right |
| `03-accordion.svg` | Two sections in one panel, click headers to collapse |
| `04-tabs-plus-toggle.svg` | Keep tabs; outline can be pinned as a floating overlay |

## Quick pros / cons

### Option 1 — Vertical split (folder over outline)
- **Pros:** familiar from VS Code's Explorer + Outline; no new UI primitives;
  each pane has its own scroll; sidebar width unchanged so existing
  layouts still work; the split position is just one more value to persist.
- **Cons:** outline space is cramped on long docs (have to drag splitter
  or scroll); two tiny scrollbars in the same column can look busy.

### Option 2 — Outline as a right column
- **Pros:** outline gets its own dedicated width; reads like a "table of
  contents" gutter familiar from web docs; both sidebars resizable
  independently.
- **Cons:** content area gets squeezed on narrower windows; an extra
  GridSplitter to manage; only useful when there's an outline (markdown +
  transcripts), so we'd need to hide it for HTML/PDF/text views.

### Option 3 — Accordion
- **Pros:** the most flexible — user can collapse either section to give
  the other all the room; no splitter chrome; sections can stack in any
  order.
- **Cons:** less precise sizing than a splitter; collapsing/expanding
  causes the other pane to suddenly grow/shrink which can be jarring;
  WPF's `Expander` styling under WPF-UI may need tweaks.

### Option 4 — Tabs + pinnable outline overlay
- **Pros:** zero regression — tabs still work as today; the overlay
  shows up only when the user wants it (toggle button or keybinding);
  doesn't permanently steal content width.
- **Cons:** the overlay floats over the content, hiding 200px of text
  underneath; positioning + drag-to-move adds code; "is it pinned?" /
  "is it open?" state is a new pref.

## Recommendation if asked

Option 1 (vertical split) is the lowest-risk and most predictable. It's
what most IDEs do; users won't need to learn anything new. The
existing `SidebarTabs` `TabControl` becomes a `Grid` with two
row-definitions and a horizontal `GridSplitter`, plus a couple of
preferences for the split position and whether outline is visible.

Option 4 (tabs + overlay) is the only one that doesn't change anything
about the current default behaviour — the outline overlay is purely
additive.
