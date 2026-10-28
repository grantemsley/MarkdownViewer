# Transcript record JSON loads on demand, not embedded

**Date:** 2026-10-28
**Status:** accepted

## Context

Grant asked for every transcript record type to be viewable, with each
block expandable to its source JSON. A survey of ~1,000 real transcripts
on this box found 80+ record types and subtypes; most are one-liners
(agent-name, mode, atis-latch), but `api-request` records average 23 KB
each and dominate large files.

The first version embedded each record's pretty-printed JSON in the page.
Measured on real transcripts (ToMarkdown + Markdig, HTML chars):

| Transcript | Before | Embedded JSON | On demand |
|---|---|---|---|
| median (1.4 MB) | 0.2M | 2.0M | 0.3M |
| p90 (6.5 MB) | 0.5M | 14.0M | 0.7M |
| largest (61 MB) | 20.1M | 51.8M | 21.4M |

On the p90 file, API request records were 9.1M of the 11.5M markdown.

## Decision

JSON blocks carry `data-lines` (their source records' 0-based line numbers
in the file) and no body. Opening one makes bridge.js post
`requestTranscriptJson`; the host re-reads the file, formats those lines
(`TranscriptService.RecordJson`) and answers with `transcriptJson`.
Transcripts are append-only, so the line numbers stay valid without
caching anything from the render.

Exports (`HtmlExporter`) have no host to ask, so they pass
`embedJson: true` and keep the JSON inline.

Highlighting also became lazy: bridge.js skips code inside a closed
`<details>` and highlights it on toggle, since a transcript is mostly
collapsed blocks.

## Consequences

- Ctrl+F does not find text inside a JSON block until it has been opened.
- Opening a block re-reads the file; on the 61 MB transcript that is a
  noticeable but sub-second pause. A per-file line cache would fix it if
  it ever matters.
- Base64 payloads are cut to a length note in the JSON view; the image
  itself already renders in its block.
