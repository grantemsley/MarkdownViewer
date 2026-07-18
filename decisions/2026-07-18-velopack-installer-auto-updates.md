# Velopack for the installer and update apply; detection stays notify-only

*Decided 2026-07-18. Extends (does not replace) `2026-06-14-update-mechanism-notify-only.md`.*

**Decision.** Ship a Velopack-built installer (`MarkdownViewer-win-Setup.exe`)
alongside the portable single-file exe. For an installed copy, the existing
update banner's Download button now applies the update in place via Velopack
(check, download, apply, restart) instead of opening the release page; any
failure in that path falls back to opening the release page. The portable exe
keeps the notify-only behavior in full. Detection stays on the existing
`UpdateService` (GitHub Releases API, 24h throttle, per-version dismiss);
Velopack is only the apply mechanism (`src/Services/VelopackUpdater.cs`),
consulted when the user clicks.

**Why Velopack over the alternatives.**

- **winget**: requires submitting and maintaining a manifest in a registry;
  rejected outright (no registering or submitting anywhere).
- **MSIX**: requires a code signature; rejected (no signing).
- **Classic installer (Inno/NSIS/MSI)**: installs but does not update
  itself; the update problem remains unsolved.
- **Velopack** (MIT; successor to Squirrel.Windows / Clowd.Squirrel): one
  package covers installer, file-level delta updates, and the in-app apply
  API; no service to register with, GitHub Releases is the feed. Free, no
  account, works unsigned.

**Why prompt-driven, not silent.** Grant, 2026-07-18: "I don't want silent
updates. Still want it to prompt like it does now. Just handle it
automatically when clicked instead of taking you to github page." So the
banner and its detection are unchanged; only the click's action changed, and
only for installed copies.

**Accepted costs / trust model.**

- Unsigned Setup.exe means a one-time SmartScreen "unknown publisher" prompt
  (documented in the README). Self-signing would not help: SmartScreen
  treats self-signed the same as unsigned. In-app updates bypass SmartScreen.
- Unsigned + auto-apply means trust rests on GitHub over HTTPS and the
  GitHub account's integrity (Velopack verifies package checksums against
  the release index, but the index comes from the same place). Same trust
  model as the manual download it replaces, minus the human in the loop.
- Velopack installs to `%LocalAppData%\MarkdownViewer\`, the same folder the
  app already used for `WebView2Cache\`; the installer wipes that folder, so
  a portable user's first install clears those caches (they regenerate;
  observed harmless in the 2026-07-18 verification).
- Release tags must now be 3-part semver (vX.Y.Z): the version doubles as
  the Velopack package version.

**Mechanics locked in by the 2026-07-18 spikes** (details in
`plans/finished/velopack-updates.md`): installed flavor is a plain
multi-file framework-dependent publish (single-file stays portable-only);
`vpk pack --framework net10.0-x64-desktop,webview2` makes Setup bootstrap
missing runtimes; anonymous `GithubSource` (null token) serves the apply
path; the feed is overridable via the `MARKDOWNVIEWER_UPDATE_FEED` env var
for local testing against a vpk output directory.
