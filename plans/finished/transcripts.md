# JSONL transcript renderer

**Status:** ✅ Done · Last updated 2026-05-25 · v0.2.0

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Phase 1 — Routing and transform | ViewerKind.JsonlTranscript; TranscriptService.ToMarkdown; RenderTranscript wired in MainWindow |
| ✅ Done | Phase 2 — Filter widget fix | Moved widget inside .t-doc so :has() resolves; regression test added |
| ✅ Done | Phase 3 — Persistence | TranscriptPrefs on AppSettings; bridge.js delegated change listener; ScheduleSave on each toggle |
| ✅ Done | Phase 4 — Session header + outline headings | Metadata bullet block above conversation; H3 per turn with first-line preview |


Open Claude Code transcript `.jsonl` files in MarkdownViewer and read
them as a structured chat instead of one-line-per-record JSON.

## Goal

- Routing: `.jsonl` files in any vault become a new `ViewerKind`
  rendered through the same pipeline as markdown.
- Output: a categorized chat view. Conversation and tool calls visible
  by default; thinking blocks, hook attachments, skill listings, MCP
  instruction deltas, deferred-tool deltas, queue ops, and meta are
  available but hidden behind toggleable filter chips.
- The transform is pure logic in a service so it stays unit-testable;
  rendering still goes through `MarkdownService.Render()` so the
  viewer's CSS, outline, line numbers, and reload behavior come for
  free.

## Architecture

| Surface | What it does |
|---|---|
| `Services/ContentRouter.cs` | New `ViewerKind.JsonlTranscript`; `.jsonl` routes to it before the text fallback. |
| `Services/TranscriptService.cs` | Parses JSONL line-by-line, categorizes each record, emits markdown with raw-HTML blocks (filter widget, `<details>` for tool calls and attachments, `<div class="t-block t-X">` for visible content). |
| `MainWindow.RenderTranscript()` | Reads the file via `ContentRouter.ReadTextFile`, calls the service, runs the result through `MarkdownService.Render`, ships it to the WebView as `kind = "markdown"`. |
| `WebAssets/bridge.js` | Delegated `change` listener on `#page` posts `{type: "transcriptFilter", category, checked}` for each `#tf-*` toggle. |
| `Models/AppSettings.cs` | `TranscriptPrefs.VisibleCategories: Dictionary<string,bool>` — persisted in `settings.json`, fed back to the service on each open. |

### Categories

| Key | Source | Default visible |
|---|---|---|
| `conversation` | `type=user` text content, `type=assistant` text blocks | yes |
| `tool` | `tool_use` + its matched `tool_result` | yes |
| `thinking` | non-empty assistant `thinking` blocks | no |
| `hook` | `attachment.type=hook_*` | no |
| `skill` | `attachment.type=skill_listing` | no |
| `mcp` | `attachment.type=mcp_instructions_delta` | no |
| `toolsdelta` | `attachment.type=deferred_tools_delta` | no |
| `queue` | `type=queue-operation` | no |
| `meta` | `type=last-prompt` and unknown types | no |

Tool pairing: pre-scan all records to build a `tool_use_id → output`
map, then walk the records linearly emitting one `<details>` per
`tool_use`, embedding the matched result. `tool_result` records whose
id was consumed are skipped; unmatched results render as orphan blocks
under the `tool` category.

### Filter widget

Lives inside `<div class="t-doc">` so the CSS
`.t-doc:has(#tf-X:checked) .t-X { display: block; }` selector resolves.
No JavaScript needed for the toggle itself — pure CSS `:has()` drives
visibility. WebView2 ships modern Chromium so `:has()` (Chrome 105+)
is available.

Inline `<script>` and `onchange` attributes don't execute when
inserted via `innerHTML`, so the change listener that posts state back
to WPF lives in `bridge.js` (which loads from the page itself, not the
markdown body) and uses event delegation on `#page`.

### Session header

If the parsed records include any of `sessionId`, `timestamp`,
`gitBranch`, `version`, `cwd`, or assistant `model`, an always-visible
`<div class="t-session-header">` renders those as a bullet list above
the conversation. Not gated by any filter — it's context, not chat.

### Outline integration

Each conversation block opens with `### 🧑 User: <preview>` or
`### 🤖 Assistant: <preview>`. The preview is the first non-blank
line, leading markdown markers stripped, truncated at 60 chars with
`…`. Markdig's existing heading extractor populates the outline panel
with one entry per turn.

Known wart: when a category is filtered off, its outline entries stay
listed but click-to-scroll lands on a hidden target. Acceptable —
nobody hides conversation and then navigates it.

### Persistence

`TranscriptPrefs.VisibleCategories` is additive — it slots into
`AppSettings` with sensible defaults, no schema bump. `RenderTranscript`
passes the dict to `TranscriptService.ToMarkdown`; checkbox toggles
update the dict and call `ScheduleSave()` (the existing 500ms-debounced
write the rest of the app uses). Keys missing from the saved dict fall
through to the renderer's static defaults — adding a new category
later doesn't require a settings migration.

### Safety

- Single text values longer than 200 000 chars get truncated with a
  marker (`[... truncated, N more chars ...]`) so pathological tool
  outputs don't break WebView2.
- Fenced code block delimiters auto-grow past any backtick run in the
  content, so embedded backticks never break out.
- Malformed JSON lines are skipped silently — neighbors still render.
- All text going into `<summary>` or other HTML contexts is
  HTML-escaped. Markdown content inside `<div class="t-block">` is
  intentionally rendered as markdown (users want code blocks, lists,
  links to format).

## Phases

### ✅ Phase 1 — Routing and transform

- `ViewerKind.JsonlTranscript` added; `.jsonl` routes to it before the
  text fallback.
- `TranscriptService.ToMarkdown(string jsonl)` returns markdown with
  the filter widget, session header, and one block per record.
- `MainWindow.RenderTranscript()` mirrors `RenderMarkdown()`.

### ✅ Phase 2 — Filter widget fix

- The widget was first emitted *outside* `<div class="t-doc">`, so
  `:has()` couldn't see the checkboxes and every block stayed hidden
  behind the base `display: none`. Moved inside. Regression test
  asserts the ordering.

### ✅ Phase 3 — Persistence

- `TranscriptPrefs.VisibleCategories: Dictionary<string,bool>` on
  `AppSettings`. No schema bump (additive property).
- `ToMarkdown` takes an optional `IDictionary<string,bool>` to drive
  initial checkbox state.
- `bridge.js` delegates `change` events on `#page` to a `tf-*`
  checkbox handler; `MainWindow.WebMessageReceived` handles
  `"transcriptFilter"` → dict update → `ScheduleSave()`.

### ✅ Phase 4 — Session header + outline headings

- Pre-scan extracts `sessionId`, `timestamp`, `gitBranch`, `version`,
  `cwd`, assistant `model`. Renders as a bullet list in an
  always-visible `.t-session-header` div above the conversation.
- Conversation blocks open with an H3 (`### 🧑 User: …` / `### 🤖
  Assistant: …`) so the outline panel populates.

## Out of scope

- **Pretty-printing JSON inside tool *outputs*.** Inputs already
  serialize indented; outputs are often plain text and we don't try
  to detect-and-reformat.
- **Click-through navigation** from `Read`/`Edit` tool paths into the
  vault. Could be a future enhancement; would need to recognize
  paths in tool arguments and rewrite them to vault-local URLs.
- **Image content blocks.** Not present in current Claude Code
  transcripts; would render as `[image]` placeholder if encountered.
- **Toggle-all / preset filter modes** (e.g. "system noise on/off").
  Per-category toggles are enough for now.
- **Custom per-user category labels or colors.** Hardcoded in the
  service.

## Testing

`TranscriptServiceTests.cs` covers:
- Each record type (user string, user array, assistant text, assistant
  thinking, tool_use+result pairing, orphan results, hooks, skills,
  MCP, tool-deltas, queue, last-prompt, unknown).
- Defaults vs override behavior of `visibleCategories`.
- Session header presence + omission.
- Heading preview shape (first-line, truncation, marker stripping,
  empty-message).
- Malformed lines, CRLF, embedded HTML, large inputs.
- The filter widget lives inside `.t-doc` (regression).

`ContentRouterTests.cs` covers `.jsonl` routing.

Total: ~26 transcript-specific tests; suite still runs sub-second.

## Notes

- WebView2's `innerHTML` assignment doesn't execute embedded
  `<script>` tags, which is why filter state changes round-trip
  through `bridge.js` instead of inline JS.
- The transcript renderer doesn't sanitize user content beyond HTML
  escaping at element boundaries. Same trust boundary as any markdown
  file in the vault: the viewer is reading the user's own files.

## Changelog


## v0.2.0 — 2026-05-25

Persistence + session header + outline headings.

- `AppSettings.Transcripts.VisibleCategories: Dictionary<string,bool>`
  added (additive — no schema bump). `RenderTranscript` passes the
  dict; `WebMessageReceived` handles a new `"transcriptFilter"`
  message to update + schedule save.
- `bridge.js` delegates `change` events on `#page` to detect `#tf-*`
  checkbox toggles and post them back. Inline `onchange` doesn't
  fire on innerHTML-inserted nodes, so delegation from a stable
  parent is the right shape.
- Session header block: pulls `sessionId`, `timestamp`,
  `gitBranch`, `version`, `cwd`, assistant `model` from the parsed
  records. Always-visible (not behind a filter); omitted when no
  metadata fields are present.
- Conversation blocks emit `### 🧑 User: <preview>` /
  `### 🤖 Assistant: <preview>` so the existing outline panel
  populates. Preview is first non-blank line, leading markers
  stripped, 60-char truncated.

## v0.1.0 — 2026-05-25

Initial JSONL transcript renderer.

- New `ViewerKind.JsonlTranscript`; `.jsonl` routes to it.
- `TranscriptService.ToMarkdown` converts a transcript into
  categorized markdown with a `:has()`-driven filter widget and
  collapsible `<details>` blocks for tool calls and attachments.
- Categories: conversation, tool, thinking, hook, skill, mcp,
  toolsdelta, queue, meta. Conversation + tool visible by default.
- Tool pairing: pre-scan all records to map `tool_use_id` →
  output text, then embed the result inside each `tool_use`'s
  `<details>`. Orphan results render under the `tool` category.
- Long single-value content (>200 KB chars) truncated with a marker.
- Fence delimiters auto-grow past inner backticks.
- Malformed lines skipped silently.
- 24 xUnit tests covering categories, pairing, truncation,
  malformed lines, CRLF, embedded HTML.

Filter scope bugfix (same day): the filter widget initially lived
outside `<div class="t-doc">`, so `:has()` couldn't see the
checkboxes and every block stayed hidden. Moved inside. Regression
test added.
