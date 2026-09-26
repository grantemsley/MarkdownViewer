# PDF.js vs the built-in WebView2 PDF viewer

**Date:** 2026-09-26 · **Task:** [MV-17](https://tasks.emsley.ca/tasks/80)

## Question

PDFs open in WebView2's built-in (Edge) PDF viewer, and opening one feels slow.
Would rendering with PDF.js be fast enough to justify bundling it and giving up
the native viewer's toolbar?

## Measurements

All on this box, Debug build, WebView2 1.0.3485.44, driven over a temporary CDP
hook. Timings run from the click on an in-page link.

| Case | Built-in viewer | PDF.js 6.3.289 |
|---|---|---|
| 1 KB one-page sample, cold (first PDF after launch) | ~4.5 s to first text | 154 ms to page 1 on a canvas |
| same, warm (second PDF, new frame) | ~4.4 s | 34 ms |
| 3 MB 40-page manual, cold | ~4.5 s | 177 ms |
| same file, again | (not re-measured) | 83 ms |
| switch back to a pooled, already loaded PDF ([MV-16](https://tasks.emsley.ca/tasks/79)) | instant | n/a |

How it was measured:

- **Built-in viewer:** window captured with `PrintWindow` every ~190 ms and
  timed to the first dark (text) pixels in the content area, so figures are
  +/-200 ms. The CDP `frameNavigated` for the PDF frame lands ~2.0 s after the
  click on every open, cold or warm, so about half the delay happens before the
  frame even commits.
- **Serving is not the bottleneck:** `fetch()` of the same PDF from
  `app.local/__vault/` returns in 3-4 ms.
- **PDF.js:** `pdf.min.mjs` imported from cdnjs into `render.html` (allowed by
  the page CSP), then `getDocument` + `getPage(1)` + `render` into a canvas at
  1.5x. The import cost 22-130 ms from the network; bundled locally it would be
  parse time only. The worker loaded from cdnjs, so PDF.js may have run its
  in-thread fallback; a bundled same-origin worker should be no slower.

The finding that overturns the task's premise: the slowness is not a
first-open cost. **Every** open through the built-in viewer takes ~4.5 s here;
the first one is simply the one you notice.

## Costs of switching

- **Size:** PDF.js 6.3.289 is `pdf.min.mjs` 459 KB + `pdf.worker.min.mjs`
  1.27 MB, about 1.7 MB, not the ~500 KB the task assumed. The full viewer app
  (`web/viewer.html`, toolbar, find bar, thumbnails) adds more on top.
- **UI:** the built-in toolbar (zoom, page nav, find, print, rotate, annotations,
  save) goes away. Either ship PDF.js's own viewer app (closest parity, largest)
  or build a minimal one (page scroll, zoom, Ctrl+F find via the text layer).
- **Security model:** PDF.js runs in our page instead of Edge's sandboxed viewer.
  It is widely used this way, but it becomes our dependency to keep patched
  (PDF.js has had CVEs, e.g. CVE-2024-4367, fixed in 4.2.67).

## Recommendation

1. **First, spend an hour on the ~2 s pre-commit delay.** Serving takes 4 ms,
   yet the frame commits 2 s later on every open. If that is something in how
   this app hosts the frame (e.g. how the navigation is intercepted), fixing it
   may halve the wait for free, and the whole case for PDF.js weakens.
2. **If the built-in viewer stays at seconds per open, adopt PDF.js**, bundled
   locally (no CDN), in a same-origin frame. Start with PDF.js's stock viewer
   app for feature parity, and only hand-roll a lighter UI if its size or look
   is a problem. The speedup (~25x to first page) is large enough to justify
   ~1.7 MB+ in an exe that already bundles Mermaid (3.3 MB).

Both steps are build work beyond this evaluation, filed as a separate proposed
task.
