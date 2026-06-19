# Update mechanism: notify-only check, not a self-updater

*Decided 2026-06-14.*

**Decision.** Update support is a notify-only check (`UpdateService`): once at startup it
asks the GitHub Releases API for the latest release, compares the tag to the running assembly
version, and - if newer - shows a dismissible banner whose "Download" button opens the release
page. It never downloads or installs. Opt-out via Preferences -> "Check for updates on
startup" (default on). The check is throttled to at most once per 24h
(`UpdatePrefs.LastCheckUtc` + `UpdateService.CheckInterval`); the timer is stamped only when
GitHub is actually reached, so an offline launch retries next time rather than burning the
day's slot. All failures are swallowed as "up to date."

**Why.** The app ships as a single, framework-dependent, portable `.exe` attached to a GitHub
Release. The polished auto-updaters all trade that away:

- **Velopack** (the modern Squirrel successor) and **MSIX + App Installer** both move the app
  to an install-based model. MSIX additionally needs code signing. A running `.exe` can't
  cleanly overwrite itself, so seamless in-place update essentially requires that model.
- We chose to keep the portable single-exe identity. Notify-only closes the real gap (users
  not knowing an update exists) without giving up portability or taking on an install
  framework.

Rejected: Velopack (revisit if/when we accept becoming an installed app), MSIX (only worth
it alongside Store / first-class context-menu integration), self-replacing downloader (the
swap-running-exe dance is fragile for little gain over "open the release page").

**Consequences / caveats.**
- The exe is unsigned; a future download-based flow would meet SmartScreen/AV friction until
  signed. Notify-only sidesteps this because the browser does the download.
- If/when we accept the installed-app model, Velopack is the established path and would give
  silent delta updates.
