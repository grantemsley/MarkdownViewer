# Place marker: quote+position anchoring, stored C#-side by file path

**Date:** 2026-07-16
**Status:** Adopted (built with `plans/js-tests-and-place-marker.md`)

## Decision

One deliberate "I stopped here" marker per file, set and cleared by clicking
the gutter left of the text. The anchor descriptor is
`{ blockIndex, textPrefix, headingId }`: the unit's index among the page's
**markable units**, the first 60 chars of its whitespace-normalized text
(with injected UI chrome like the copy button stripped), and the id of the
nearest preceding heading.

Markable units are the rendered page's top-level blocks, except lists, which
explode into their `<li>`s (nested ones included, deepest item wins the hit-
test) - so one step in step-by-step instructions carries its own mark rather
than the whole list. Granularity is purely a renderer decision: the host
stores `blockIndex` without interpreting it, so this changed (2026-07-16,
after first interactive use) with no C# or contract change.

Resolution on every render, in order: the index (only if the text still
matches there), then a scan for the text, then the heading as a coarse
fallback, then drop the mark entirely. Dropping beats guessing: a mark on the
wrong paragraph is worse than none.

Storage is a C#-side `Dictionary<string, MarkAnchor>` in `MainWindow`, keyed
by file path (case-insensitive), process-lifetime only. The anchor rides out
to bridge.js on every markdown/text/transcript `setDoc` as an optional `mark`
field, exactly as `scrollTop` already does; bridge.js posts `markSet` /
`markCleared` back, gated by `BridgeGates.MarkApplies` (tab token + path,
mirroring `ScrollApplies`).

## Why

- **Quote + position, not either alone.** This is a scaled-down
  `TextQuoteSelector` + `TextPositionSelector` from the W3C Web Annotation
  model (what Hypothes.is uses). Index alone drifts when an edit above the
  mark adds or removes a block; text alone can't disambiguate repeated
  blocks. Index-validated-by-text plus a text scan survives the common case
  (edit above the mark) without ever landing on the wrong paragraph.
- **C#-side, not a JS `Map`.** The JS context does outlive a markdown reload
  (the page is never navigated away from), but a RawBrowser (.pdf/.html) file
  changing on disk calls `CoreWebView2.Reload()`, which discards the whole
  page and every tab's JS state. C# storage is immune and matches how
  `ScrollTop` is already handled.
- **Keyed by file path, not tab id.** Tab ids are process-lifetime tokens and
  deliberately not persisted (`decisions/2026-07-11-tab-identity-model.md`);
  path keying also gives the same file in two tabs the same mark for free.
- **In-memory only, deliberately.** No `AppSettings` / `SettingsSchema`
  involvement; the mark does not survive a restart. Persistence can be added
  later without changing the anchor model if it ever earns its keep.
- **Riding `setDoc` instead of a separate host->bridge mark message** removes
  any question of whether the doc is in the DOM when the mark arrives.
  (The plan originally listed `mark`/`markCleared` dispatch arms in
  bridge.js; they were dropped as inconsistent with this - nothing would
  ever send them.)

## Constraints this creates

- Marks apply to the kinds that render into `#page` and post `docRendered`
  (markdown, text - and transcripts, which are markdown-kind docs). Image,
  binary, and raw-browser kinds have no gutter and carry no mark.
- A mark set in a transcript survives re-renders (the transcript render path
  sends `mark` too), but transcript filter toggles re-render the doc, so the
  anchor's text must still exist in the filtered view or it falls back /
  drops per the resolution order.
- The bar is drawn with `::after`, not `::before` as first planned: the
  line-number gutter already owns `::before` on the same top-level blocks,
  and both pseudo-elements on one block would collide per-property.
- The bar must sit in the page's left margin at the same x for every unit,
  but `::after` positions relative to the marked element - so bridge.js
  stores the element's indent in a `--mark-indent` custom property when
  applying a mark or hover ghost, and the CSS cancels it
  (`left: calc(-12px - var(--mark-indent, 0px))`). Without this, an
  indented list item's bar lands beside its number.

## Notes

Anchor logic is tested in `tests/js/mark.test.js` through the real
`render.html` + `bridge.js` (see the jsdom-harness decision of the same
date); the C# contract (gate, parse arms, `mark` serialization) in
`tests/MarkdownViewer.Tests/BridgeMessagesTests.cs`.
