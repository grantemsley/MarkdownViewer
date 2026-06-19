# Same-origin vault: serve all local resources from app.local/__vault/

*Decided 2026-05-31.*

**Decision.** Images, PDFs, and markdown-embedded resources are served from
`app.local/__vault/` (same-origin). The earlier `vault.local` cross-origin domain was
retired.

**Why.** Cross-origin serving (`vault.local`) meant the WebView2 content and the local vault
were on different origins, which breaks relative resource resolution and the fetch API.
Same-origin (`app.local/__vault/`) eliminates the cross-origin friction and lets relative
`<img>` and `<link>` refs resolve naturally within the rendered document.

**Consequences / caveats.**
- External links inside raw HTML files and relative `<img>`/`<link>` refs inside user HTML
  files were not fully verified at the time of the decision - both are filed as open items
  in `todo.md`.
