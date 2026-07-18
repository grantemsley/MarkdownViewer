# Velopack installer + automatic updates

**Type:** plan
**Status:** ⬜ Not started · Last updated 2026-07-18

| Status | Phase | Notes |
|---|---|---|
| ⬜ Not started | P1: Compatibility spikes | single-file vs vpk; stable exe path; anonymous GitHub update checks |
| ⬜ Not started | P2: App integration | explicit Main + Velopack hook; background update service |
| ⬜ Not started | P3: Release pipeline | vpk pack/upload in release.yml; keep the portable exe |
| ⬜ Not started | P4: End-to-end verification | local two-version update cycle, then a real tagged release |
| ⬜ Not started | P5: Docs + close-out | README install section; decisions; graduate loose ends |

## Goal

Make installing and updating MarkdownViewer zero-touch: a one-time
`MarkdownViewerSetup.exe` download, after which the app keeps itself current
from GitHub Releases automatically (silent background check + download, new
version on next launch). No code signing, no store, no paid or registered
service. Velopack (MIT, successor to Squirrel.Windows / Clowd.Squirrel)
provides the installer, delta updates, and the in-app update API in one
package. The portable single exe stays available for people who want no
installer.

Decided up front (from the 2026-07-18 conversation):

- **Velopack over winget / MSIX / classic installer.** winget needs a
  manifest-repo submission (rejected: no registering/submitting anywhere);
  MSIX requires a signature; a classic installer alone does not update
  itself. Graduates to `decisions/` at close-out.
- **Unsigned stays acceptable.** SmartScreen treats self-signed the same as
  unsigned (per Microsoft's SmartScreen reputation doc, 2026-05), so there
  is no free signing win to chase. The one-time SmartScreen click-through on
  `Setup.exe` is the accepted cost; in-app updates bypass SmartScreen
  entirely afterwards.
- **Update UX: fully silent.** Check + download in the background on
  startup, apply on next launch. No prompt, no restart-now nag. Rationale:
  Grant's stated goal is "preferably fully automatic"; a viewer app is
  short-lived so next-launch pickup is prompt in practice.
- **Update integrity note.** Unsigned + auto-update means trust rests
  entirely on GitHub over HTTPS and on the GitHub account not being
  compromised (Velopack verifies package checksums against the release
  index, but the index itself comes from the same place). Accepted: same
  trust model as the current manual download, minus the human in the loop.

## ⬜ P1: Compatibility spikes

Small questions to settle before touching the app. Each is an hour-ish spike,
not a design effort:

1. **Single-file publish vs Velopack.** Velopack packs a publish
   *directory*; its docs show a plain multi-file publish and never a
   `PublishSingleFile` one (verified 2026-07-18: no explicit warning either
   way, but one big exe would defeat file-level delta updates regardless).
   Figure out whether the installed build should drop
   `-p:PublishSingleFile=true` (likely yes - the installer hides the
   multi-file layout from users anyway; WebAssets embedding still works
   either way since it is a csproj/publish concern). The portable artifact
   keeps single-file regardless.
2. **Runtime bootstrapping via Setup.exe.** `vpk pack --framework` can make
   the installer detect and install missing runtime dependencies (e.g.
   `net8.0-x64-desktop`, `webview2` in current docs). Confirm the exact id
   for the .NET 10 Desktop Runtime and add it (plus `webview2`, harmless on
   Win11 where it is inbox) so `Setup.exe` works on machines without the
   runtime - today's README makes the user install it themselves.
3. **Stable exe path.** Velopack installs to
   `%LocalAppData%\MarkdownViewer\current\MarkdownViewer.exe`. Verify the
   `current` path is stable across updates (it should be in Velopack v4+),
   because "Open with" / file-association registry entries must survive an
   update. Also confirm Velopack's shortcut + uninstall entries look sane.
4. **Anonymous update checks.** `GithubSource` with a null token against a
   public repo uses the anonymous GitHub API (60 req/hr/IP). Confirm one
   check per app-start fits comfortably and that failures are non-fatal
   (they must degrade to "no update this run", never an error dialog).
5. **Local update feed for testing.** Confirm the cleanest way to run an
   install -> update cycle without touching GitHub (vpk output directory as a
   file:// / local-path source, or Velopack's test source). Feeds P4.

## ⬜ P2: App integration

- Add the `Velopack` NuGet package to `src/MarkdownViewer.csproj`.
- WPF generates `Main` from `App.xaml`, but Velopack must run first-thing in
  `Main` (it handles install/update/uninstall hook invocations that must
  exit before any UI). Add an explicit entry point:
  - `src/Program.cs`: `[STAThread] Main` that calls
    `VelopackApp.Build().Run()` then constructs and runs `App`
    (`app.InitializeComponent(); app.Run();`).
  - `<StartupObject>MarkdownViewer.Program</StartupObject>` in the csproj so
    the compiler picks it over the generated Main.
  - Verify the existing `Application_Startup` flow (crash logger,
    single-instance mutex/pipe in `src/App.xaml.cs`) is unchanged by the
    explicit entry point.
- `src/Services/UpdateService.cs`: after the main window shows, on a
  background task: if `UpdateManager.IsInstalled` (false for the portable
  exe - service no-ops there), `CheckForUpdatesAsync`; if an update exists,
  `DownloadUpdatesAsync` then the apply-on-exit call
  (`WaitExitThenApplyUpdates` in current Velopack; the docs example shows
  `ApplyUpdatesAndRestart` for the restart-now variant - confirm the
  exit-deferred name when integrating) so the swap happens
  after the process exits. All exceptions swallowed to the existing crash
  log path, never surfaced as UI.
- Single-instance interplay: the update applies at process exit, so the
  mutex/pipe teardown in `OnExit` runs first; no ordering change expected,
  but verify a second-instance hand-off during a pending update does not
  wedge (P4 checklist item).
- Tests: the service is deliberately thin glue over Velopack's API; add
  what is unit-testable without faking Velopack (e.g. the "not installed ->
  no-op" gate if it is extractable), and lean on P4 for the rest.

## ⬜ P3: Release pipeline

Rework `release.yml` (tag-triggered, version already derived from the tag):

- Publish twice:
  - installed flavor: per P1's single-file decision, to `publish/`
  - portable flavor: today's single-file publish, kept byte-compatible with
    what v1.x users already download
- Pack and upload:

  ```
  dotnet tool install -g vpk
  vpk download github --repoUrl https://github.com/grantemsley/MarkdownViewer
  vpk pack -u MarkdownViewer -v <version> -p publish -e MarkdownViewer.exe --packTitle "Markdown Viewer" --framework <net10-desktop-id>,webview2
  vpk upload github --repoUrl https://github.com/grantemsley/MarkdownViewer --publish --merge --releaseName v<version> --tag v<version> --token <GITHUB_TOKEN>
  ```

  (Command shapes verified against Velopack's GitHub Actions doc,
  2026-07-18; `--framework` id comes from spike P1.2.)
  `vpk download` pulls the previous release so the pack step can emit a
  delta package; first release after this plan simply has no delta.
- Release-creation hand-off: `vpk upload github` creates the Release, and
  its `--merge` flag merges into an existing one. Order the steps as
  `softprops/action-gh-release` first (creates the release with
  `generate_release_notes` + attaches the portable exe), then
  `vpk upload github --merge` adds the Velopack assets to it. If `--merge`
  misbehaves against a published release, fall back to vpk-first and
  softprops appending (it can update an existing release).
- `permissions: contents: write` already present; the default
  `GITHUB_TOKEN` suffices - no new secrets.
- CI (`ci.yml`) is untouched.

## ⬜ P4: End-to-end verification

- Local cycle (no GitHub): pack v0.0.1-test and v0.0.2-test per P1.5, run
  `Setup.exe`, confirm install + shortcut + first launch, point the app at
  the local feed, confirm silent download and that the next launch runs
  v0.0.2-test. Confirm uninstall from Settings > Apps is clean.
- Single-instance checks from P2: hand-off to a running instance still
  works when installed; pending-update-at-exit does not break the pipe
  owner or the mutex.
- Portable exe still behaves exactly as today (no update attempt, no new
  files dropped next to it).
- Real release: tag the next version, watch the Release workflow, install
  from the published `Setup.exe` on a clean machine/VM, then tag a
  follow-up patch and confirm the installed copy picks it up unattended.
  (An interactive-verification pass on this box hits the known WebView2/CDP
  limitation only if we automate UI; this checklist is manual/human-driven,
  so that does not block.)

## ⬜ P5: Docs + close-out

- README: rewrite the install section - `MarkdownViewerSetup.exe` as the
  recommended path with a note about the one-time SmartScreen prompt and
  automatic updates; portable exe as the alternative; existing v1.x users
  switch by running Setup once (old exe keeps working but no longer sees
  updates... it never did - just phrase it as "the portable exe does not
  auto-update").
- Release notes for the first Velopack version call out the new installer.
- Graduate loose ends per plan-format: the Velopack-over-alternatives
  decision to `decisions/`; anything deferred (e.g. an update-channel or
  "check for updates now" menu item if wanted later) to `todo.md`
  `## Proposed`.
- Move this plan to `plans/finished/`.
