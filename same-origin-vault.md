# Same-origin vault serving

**Status:** ⏳ In progress · Last updated 2026-05-30

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Phase 1 — Serve vault files at `app.local/__vault/<rel>` | `WebResourceRequested` handler serves `/__vault/`; `VaultPaths.ResolveWithinRoot` guard + 19 unit tests |
| ✅ Done | Phase 2 — Migrate subresources | Image viewer + PDF now same-origin; base64→blob (image) and pdfBase64 hacks retired; `VaultFileUrl` helper |
| ✅ Done | Phase 3 — Migrate markdown base + in-vault links | Markdown/transcript base → `__vault` (fixes embedded images); `HandleInVaultLink` / both `NavigationStarting` handlers / bridge.js link handler recognize `app.local/__vault`; `TryVaultRel` helper |
| ✅ Done | Phase 4 — Retire `vault.local` + CSP cleanup | Virtual-host mapping removed; `vault.local` dropped from `default-src`/`img-src`/`media-src`/`connect-src` |
| ⏳ In progress | Phase 5 — Tests | Refresh base URLs in existing rewriter/transcript tests; manual pass pending |

## Goal
Eliminate the `app.local` ↔ `vault.local` cross-origin split that blocks image
subresources. Serve every vault file from the **same `app.local` origin** under a
reserved `/__vault/` path, so the image viewer, markdown-embedded images, PDFs,
and in-vault links all work through one mechanism — no per-type cross-origin
workarounds. Retire the base64→blob image hack (just added in `7f5f566`) and the
`pdfBase64` path. HTML keeps its `srcdoc` + sandbox isolation (that's a security
feature, not a cross-origin workaround — see below).

## Security model (load-bearing)
- The handler resolves `/__vault/<rel>` against the **current** vault root with
  `Path.GetFullPath` and refuses anything that escapes the root — mirroring the
  existing guard in `TryOpenRelative` (MainWindow.xaml.cs:1233-1238). Extract it
  to a static `VaultPaths.ResolveWithinRoot(root, rel) → string?` (null on
  escape/invalid) so it's unit-testable in isolation.
- Only regular files **under the open vault** are served; no directory listing,
  no traversal, no absolute-path or UNC escapes.
- **HTML is not served for same-origin rendering.** Navigating an iframe to
  `app.local/__vault/foo.html` would give untrusted HTML the `app.local` origin
  (DOM access to the shell, postMessage to parent). HTML therefore stays inline
  `srcdoc` in a null-origin sandbox, exactly as today. `/__vault/` serving is for
  passive resources only: images, PDF, and assets that markdown/HTML reference.
- Reserved prefix `__vault/` cannot collide with embedded assets (those are
  `render.html`, `reader.css`, `bridge.js`, `lib/…`); the handler checks the
  `__vault/` prefix first, falls through to `WebAssetProvider` otherwise.
- **Reparse points are followed, by design.** `ResolveWithinRoot` is a path
  check, not a canonicalization, so a symlink/junction inside the vault that
  targets an outside file is served. Accepted: it matches the existing
  `TryOpenRelative` link-open behavior, the vault is the user's own folder, and
  there is no egress (CSP `connect-src` self-only, no script) to exfiltrate a
  displayed file. Blocking reparse points would break legitimately symlinked
  vaults, which are common.

## ✅ Phase 1 — Serve vault files at `app.local/__vault/<rel>`
- New static helper `VaultPaths.ResolveWithinRoot(root, rel)` — the traversal
  guard, returns the absolute on-disk path or null.
- In the `WebResourceRequested` handler (MainWindow.xaml.cs:199-210): if the
  request path starts with `__vault/`, resolve it under `_vault.Root`, stream the
  file with `WebAssetProvider.ContentType` (add `.pdf → application/pdf`); 404 on
  miss/escape. Otherwise serve embedded assets as today.
- The handler reads `_vault.Root` at request time, so a vault switch needs no
  re-registration.

## ✅ Phase 2 — Migrate subresource consumers
- `ShowImage`: revert to a plain same-origin URL
  `https://app.local/__vault/<rel>`; drop the base64/blob payload.
- `bridge.js setImage`: back to `<img src=url>`; remove the blob branch.
- `NavigateRaw` (PDF): iframe `src = https://app.local/__vault/<rel>` (same-origin
  now — no ~2 s penalty, no bytes over the bridge); drop `pdfBase64`.
- `bridge.js setRaw`: remove the `pdfBase64`/blob branch; `currentBlobUrl` becomes
  unused → remove. HTML `srcdoc` branch unchanged.
- `RenderMarkdown` basePath → `https://app.local/__vault/<dir>/`.
- HTML `<base>` injection (`InjectBaseTag`) → same-origin base.

## ✅ Phase 3 — Migrate in-vault link interception
- `HandleInVaultLink`, `WebView_NavigationStarting`, `Frame_NavigationStarting`:
  recognize `https://app.local/__vault/` as the in-vault prefix instead of
  `vault.local`. Note `NavigationStarting` currently early-returns on **all**
  `app.local/` URLs (line 1272) — must special-case `app.local/__vault/*` link
  clicks (cancel + route through `TryOpenRelative`) before that return.
- bridge.js link-click handler (bridge.js:455): treat `app.local/__vault/` as
  in-vault (post to host) and only truly-external `http(s)` as external.

## ✅ Phase 4 — Retire `vault.local` + CSP cleanup
- Remove `SetVirtualHostNameToFolderMapping("vault.local", …)` and its clear call
  (FinishOpenVault).
- render.html CSP: drop `https://vault.local` from `default-src`/`img-src`/
  `media-src`/`connect-src`; keep `data:`/`blob:` (markdown data-URI images) and
  `https:` img (remote images). `frame-src` keeps `https:`/`'self'` for PDF.

## ⬜ Phase 5 — Tests
- New `VaultPathsTests`: `../` escape, absolute path, UNC, valid nested path,
  path with spaces/unicode, empty rel → root.
- `UrlRewriterTests` / `TranscriptEndToEndTests` currently assert a
  `https://vault.local/` base — update to the new base (these test the rewriter
  generically, so it's a string swap, not a behavior change).
- Manual: image viewer (png/svg/bmp), markdown with a relative `![](pic.png)`,
  PDF, an in-vault `[link](other.md)`, and a raw `.html` with a relative image.

## Out of scope
- HTML same-origin rendering (intentionally kept sandboxed).
- Remote (`https:`) and `data:` images already work and are untouched.
