# MarkdownViewer — Design decisions

The durable "why it's built this way" record: load-bearing decisions, the
alternatives rejected, and known limitations. See `.claude/rules/doc-conventions.md`
for what belongs here vs. `todo.md` / `plans/`.

## Updates: notify-only check, not a full auto-updater

**Decision.** Update support is a *notify-only* check (`UpdateService`): once at
startup it asks the GitHub Releases API for the latest release, compares the tag
to the running assembly version, and — if newer — shows a dismissible banner whose
"Download" button opens the release page. It never downloads or installs.

**Why.** The app ships as a single, framework-dependent, portable `.exe` attached
to a GitHub Release. The polished auto-updaters all trade that away:

- **Velopack** (the modern Squirrel successor; the "proper" answer) and **MSIX +
  App Installer** both move the app to an *install-based* model (lives in
  `%LocalAppData%` / packaged, managed install lifecycle). MSIX additionally needs
  code signing. A running `.exe` can't cleanly overwrite itself, so seamless
  in-place update essentially requires that model.
- We chose to keep the portable single-exe identity. Notify-only closes the real
  gap (users not knowing an update exists) without giving up portability or taking
  on an install framework + signing.

**Rejected for now:** Velopack (revisit if/when we accept becoming an installed
app — it's the well-trodden path and would give silent delta updates), MSIX
(only worth it alongside Store / first-class context-menu integration), and a
self-replacing downloader (the swap-the-running-exe dance is fragile for little
gain over "open the release page").

**Known limitations.**
- The exe is unsigned, so a future download-based flow would meet SmartScreen/AV
  friction until signed. Notify-only sidesteps this (the browser does the
  download).
- Opt-out via Preferences → "Check for updates on startup" (default on). The
  check is one unauthenticated GitHub API GET, throttled to at most once per 24h
  (`UpdatePrefs.LastCheckUtc` + `UpdateService.CheckInterval`). The daily timer is
  stamped only when GitHub is actually reached, so an offline launch retries next
  time rather than burning the day's slot. All failures are swallowed as "up to date".
