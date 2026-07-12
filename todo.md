# MarkdownViewer — TODO

## Open
| | Item | Pri | Added |
|--|------|-----|-------|
| ⬜ | Verify Mermaid renders under both body styles (Win11 + GitHub), dark variant via the Mermaid theme · `plans/theming.md` | P3 | 2026-05-29 |
| ⬜ | Proper highlight.js theme pair per body style (`github`/`github-dark`; `vs`/`vs2015`) · `plans/theming.md` | P3 | 2026-05-29 |
| ⬜ | OS accent colour through links + the "reloaded" flash (chrome → CSS var via the bridge) · `plans/theming.md` | P3 | 2026-05-29 |
| ⬜ | Update README screenshots; bump app version + changelog | P3 | 2026-05-29 |
| ⬜ | External link inside a raw HTML file opens in the OS browser (unverified) | P3 | 2026-05-29 |
| ⬜ | Relative `<img>`/`<link>` refs inside a user HTML file resolve (unverified) | P3 | 2026-05-29 |
| ⬜ | Instant HTML ↔ markdown ↔ PDF switching (no `render.html` reload) | P3 | 2026-05-29 |
| ⬜ | Wiki-link `[[Note]]` support (custom Markdig `IMarkdownExtension`) | P3 | 2026-05-29 |
| ⬜ | Print (`WebView2.ShowPrintUI()`) | P3 | 2026-05-29 |
| ⬜ | Transcript renderer: pretty-print JSON in tool *outputs* · `plans/transcripts.md` | P3 | 2026-05-29 |
| ⬜ | Transcript renderer: click-through from `Read`/`Edit` tool paths into the vault · `plans/transcripts.md` | P3 | 2026-05-29 |
| ⬜ | Transcript renderer: image content blocks (render `[image]` placeholder) · `plans/transcripts.md` | P3 | 2026-05-29 |
| ⬜ | Transcript renderer: toggle-all / preset filter modes ("system noise on/off") · `plans/transcripts.md` | P3 | 2026-05-29 |
| ⬜ | Transcript renderer: custom per-user category labels or colours · `plans/transcripts.md` | P3 | 2026-05-29 |
| ⬜ | Raw-browser perf: two-iframe approach (instant switch back to a recent PDF) | P3 | 2026-05-29 |
| ⬜ | Raw-browser perf: PDF.js for faster first-PDF open (bundle ~500 KB; canvas render; nav/zoom/find) | P3 | 2026-05-29 |

## Proposed — Claude's; promote or clear
_Pulled from now-finished plans during the lifecycle cleanup — triage, promote, or clear._
| | Item | Added |
|--|------|-------|
| 💡 | Full-text search phase 2: regex mode; case-sensitive / whole-word toggles; subtree-scoped search ("search in this folder"); per-tab search persistence; a persistent index for repeat searches over slow SMB · `plans/finished/full-text-search.md` | 2026-07-11 |
| 💡 | Add `.jsonl` to the search content allowlist (currently name-match only; transcripts render specially so a source line wouldn't map to the rendered view - would need a transcript-aware jump) · `plans/finished/full-text-search.md` | 2026-07-11 |
| 💡 | Verify the exported-HTML CSP didn't break rendering: export a doc with a code block + a mermaid diagram, open it in a browser, confirm highlight.js + mermaid still run under `script-src cdnjs 'nonce' 'unsafe-eval'` (if mermaid breaks it needs another directive) · shipped bc1b374 · `plans/finished/post-audit-remediation.md` | 2026-07-11 |
| 💡 | Single-instance pipe ACL: add a `PipeSecurity` DACL scoped to the current user (needs the `System.IO.Pipes.AccessControl` package); LOW severity, bounded impact (path only reaches the viewer) · `plans/finished/post-audit-remediation.md` | 2026-07-11 |
| 💡 | MSIX / installer packaging — first-class Win11 context-menu placement (vs "Show more options"); needs packaging + code signing · `plans/finished/ci.md` | 2026-06-11 |
| 💡 | NuGet caching in CI (`setup-dotnet` / `actions/cache`) — minor build speedup; add if build minutes start to matter · `plans/finished/ci.md` | 2026-06-11 |
| 💡 | UI smoke test (Appium-WinAppDriver) / JS unit tests (jsdom) — only if the manual UI checklist starts missing real bugs · `plans/finished/testing.md` | 2026-06-11 |
| 💡 | Tab loading overlay (#4) — spinner over the blank WebView during cold start; needs a Popup/hide-until-paint approach (WebView2 airspace) · `plans/finished/tabs-and-startup.md` | 2026-06-14 |
| 💡 | Restored folder-only tab opens that folder's last file instead of staying file-less (lazy-open reuses the folder-open path) · `plans/finished/tabs-and-startup.md` | 2026-06-14 |

## Done — auto-swept after 14 days
| | Item | Done |
|--|------|------|
