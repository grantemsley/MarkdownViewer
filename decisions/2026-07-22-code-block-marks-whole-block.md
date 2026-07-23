# Code-block place marks are whole-block, drawn as an overlay bar

**Date:** 2026-07-22

## Decision

A gutter click beside a code block (a fenced block, or the text viewer's
whole-file `<pre>`) marks the **entire block**, same semantics as any other
markable unit. The bar is an absolutely-positioned overlay element in
`#scroll` (`.code-mark-bar`), not the `::after` pseudo-element the other
blocks use, and the block gets no `.md-mark` tint: bar only, in the margin.

## Why

- Marking a code block previously appeared to do nothing at all:
  `.page pre { overflow-x: auto }` clips a `::after` drawn outside the box,
  and the tint loses specificity to the code background. The overlay fixes
  visibility; that part is load-bearing regardless of granularity.
- Grant explicitly wanted block-level marks ("I don't want it to choose each
  line... mark the entire code block") with no coloring of the code, just
  the line in the margin.

## Rejected alternative: per-line marks

A full per-line implementation was built and verified first (anchor grew
`lineIndex`/`lineText`, Range-based line measurement with wrap handling,
line-aware Ctrl+G). Walked back the same day at Grant's direction.
If line-level marking is ever wanted again (e.g. the text viewer, where the
whole file is one block and a mark is now all-or-nothing), the complete
working implementation is commit `f01126a`; the walk-back is `97c395b`.
