# Velopack installer + automatic updates

**Type:** plan
**Status:** ⏳ In progress · Last updated 2026-07-18

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | P1: Compatibility spikes | all five settled; answers recorded in P1 body |
| ✅ Done | P2: App integration | Program.cs + StartupObject; VelopackUpdater; banner wired with fallback; 6 tests added |
| ✅ Done | P3: Release pipeline | two publishes + vpk download/pack/upload --merge; local equivalents verified |
| ✅ Done | P4: End-to-end verification | local cycle passed end to end incl. UIA-driven banner clicks; real-release round-trip is the user's manual checklist |
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
- **Update UX: keep the existing prompt, automate the action** (Grant,
  2026-07-18: "I don't want silent updates. Still want it to prompt like it
  does now. Just handle it automatically when clicked instead of taking you
  to github page."). The app already has a notify-only checker
  (`src/Services/UpdateService.cs` + the `UpdateBanner` in `MainWindow`,
  settings-gated with a 24h throttle and per-version dismiss). All of that
  stays. The only behavior change: for an *installed* copy, the banner's
  Download button runs the Velopack download + apply + restart flow instead
  of opening the release page; the portable exe keeps today's
  open-the-release-page behavior.
- **Update integrity note.** Unsigned + auto-update means trust rests
  entirely on GitHub over HTTPS and on the GitHub account not being
  compromised (Velopack verifies package checksums against the release
  index, but the index itself comes from the same place). Accepted: same
  trust model as the current manual download, minus the human in the loop.

## ✅ P1: Compatibility spikes

All five settled 2026-07-18 against the Velopack 1.2.0 docs
(docs.velopack.io) plus local experiments with vpk 1.2.0 (installed via
`dotnet tool install -g vpk`; NuGet package Velopack 1.2.0 matches).

1. **Single-file publish vs Velopack: drop single-file for the installed
   flavor.** The docs never show a `PublishSingleFile` publish; `vpk pack
   --packDir` is documented as "the folder containing your compiled
   application" and every example is a plain multi-file `dotnet publish`.
   Verified locally: a plain framework-dependent
   `dotnet publish -r win-x64 --self-contained false` directory packs
   cleanly. The installed flavor drops `-p:PublishSingleFile=true` and
   `-p:IncludeNativeLibrariesForSelfExtract=true` (keeps ReadyToRun and the
   embedded WebAssets - both are csproj/publish concerns, unaffected). The
   portable artifact keeps today's single-file publish exactly.
2. **Framework ids confirmed: `net10.0-x64-desktop,webview2`.** Doc pattern
   is `net{major.minor}-{arch}-{type}`, "every version of dotnet >= 5.0";
   verified empirically - vpk 1.2.0 accepts the pair and logs the runtime
   dependencies (normalizes to `net10-x64-desktop`). Docs caveat honored:
   `--framework` for dotnet is only for framework-dependent publishes,
   which ours is. `webview2` is a valid id (inbox on Win11, harmless).
3. **Stable exe path confirmed (docs; re-verified locally in P4).** Install
   root is `%LocalAppData%\MarkdownViewer\` containing `current\` (contents
   replaced wholesale each update, path itself stable), `Update.exe`, and a
   root-level `MarkdownViewer.exe` execution stub documented as the stable
   path for shortcuts/launchers that survives updates. Default shortcuts:
   Desktop + Start Menu root. Uninstall: standard Apps entry that removes
   the whole install folder and its shortcuts.
4. **Anonymous GithubSource confirmed by docs.** Constructor is
   `GithubSource(string repoUrl, string? accessToken, bool prerelease, ...)`
   in the core Velopack package; docs state an empty token uses the
   unauthenticated rate limit (60 requests/hour/IP) and public download
   URLs - fine for a click-triggered apply path. Failure fallback to the
   release page is P2 wiring, exercised in P4.
5. **Local update feed: pass a directory path to UpdateManager.**
   `new UpdateManager(@"C:\path")` resolves to `SimpleFileSource`, which
   reads `releases.{channel}.json` straight from a vpk output directory -
   so the P4 cycle is: vpk pack v1 and v2 into two feed dirs, install v1's
   Setup.exe, point the app at the v2 feed dir, click Download.
   `VelopackUpdater` therefore takes its feed from an env var override
   (`MARKDOWNVIEWER_UPDATE_FEED`) falling back to the GitHub repo URL.

Also locally verified while packing: vpk 1.2.0 output naming on the default
`win` channel is `MarkdownViewer-win-Setup.exe` and
`MarkdownViewer-win-Portable.zip` (CLI reality; some doc pages say
`{packId}-Setup.exe` without the channel). We ship our own single-file exe
as the portable download, so CI passes `--noPortable` to skip Velopack's
zip. Default channel `win` is fine - one flavor only, no `--channel`
needed.

## ✅ P2: App integration

Landed 2026-07-18. As specified below, plus: the feed comes from
`VelopackUpdater.ResolveFeed` (env var `MARKDOWNVIEWER_UPDATE_FEED`
overrides the GitHub repo URL - a local vpk output dir for P4 testing; a
GitHub URL routes through anonymous `GithubSource`, anything else through
`UpdateManager`'s local-path handling). Banner buttons got names
(`UpdateDownloadButton`/`UpdateDismissButton`) so the click handler can
show a downloading state with percent and disable both during the attempt;
on failure they re-enable and the click falls through to today's
open-the-release-page path. Startup semantics verified: the new explicit
Main launched and handed off to a running instance through the existing
mutex/pipe path (observed live; full installed-launch check is P4). Tests:
505 -> 511 (ResolveFeed x4, IsInstalled false when not installed,
UpdateAndRestartAsync returns false instead of throwing on a dead feed).

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
- **Detection stays as-is.** The existing notify-only
  `src/Services/UpdateService.cs` (GitHub API check, 24h throttle,
  version-compare helpers, unit-tested) remains the thing that decides
  whether the banner shows. Velopack is NOT used for detection - only for
  applying. This keeps the banner behavior identical for portable users.
- **New apply path, new file** (do not touch `UpdateService`'s name or
  contract): `src/Services/VelopackUpdater.cs`, thin glue exposing
  `IsInstalled` (Velopack `UpdateManager.IsInstalled`) and an
  `UpdateAndRestartAsync()` that runs `CheckForUpdatesAsync` ->
  `DownloadUpdatesAsync` -> `ApplyUpdatesAndRestart` (user just clicked, so
  restart-now is the right variant - the doc-verified API).
- **Banner wiring** in `MainWindow.UpdateDownload_Click`: if
  `VelopackUpdater.IsInstalled`, swap the banner text to a downloading state
  and call `UpdateAndRestartAsync()`; on any failure, fall back to today's
  `TryOpenExternal(_pendingUpdateUrl)` so the user always has a path
  forward. If not installed (portable exe), behavior is exactly today's:
  open the release page.
- Single-instance interplay: `ApplyUpdatesAndRestart` exits the process, so
  the mutex/pipe teardown in `OnExit` runs first; no ordering change
  expected, but verify a second-instance hand-off during apply-restart does
  not wedge (P4 checklist item).
- Tests: `UpdateService` tests are untouched. `VelopackUpdater` is
  deliberately thin glue over Velopack's API; add what is unit-testable
  without faking Velopack (e.g. the fallback-on-failure branch if
  extractable), and lean on P4 for the rest.

## ✅ P3: Release pipeline

Landed 2026-07-18 as specified below, with these deltas: the delta-download
step gets `continue-on-error: true` (the first Velopack release has no
prior Velopack assets - pre-Velopack releases carry only the portable exe -
and that must not fail the build), `--noPortable` skips Velopack's
redundant portable zip, the token flows via the `VPK_TOKEN` env var, and a
comment notes tags must now be 3-part semver (vX.Y.Z) since the version
doubles as the Velopack package version. Verified locally: YAML parses;
both publish commands and the exact `vpk pack` command (icon, authors,
framework ids, `--noPortable`, VelopackApp-hook check active) run clean.
The upload/merge step is reasoning-verified only (no pushes from this box);
first real exercise is the next tagged release.

Original phase spec:

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

## ✅ P4: End-to-end verification

Local cycle executed and passed 2026-07-18, driven for real (UI Automation
InvokePattern works on this box even though input injection does not - the
clicks below were actual button invocations on the running app, with
PrintWindow screenshots as evidence):

- Packed 0.0.1 and 0.0.2 locally with the exact CI flags; packing 0.0.2
  into a feed already containing 0.0.1 produced the delta package, same as
  CI's download-then-pack flow will.
- `MarkdownViewer-win-Setup.exe --silent` installed to
  `%LocalAppData%\MarkdownViewer` (current\, packages\, Update.exe, root
  stub exe), created the Desktop + Start Menu shortcuts and the Apps
  uninstall entry (Update.exe --uninstall). Caveat found: the installer
  wiped the pre-existing `WebView2Cache\` and `startup.log` that the app
  keeps under that same folder (both are regenerating caches - harmless,
  but it means a portable user's first install clears them).
- Installed 0.0.1 launched (root stub execs current\MarkdownViewer.exe),
  the banner appeared from the real GitHub check, Download was clicked via
  UIA with `MARKDOWNVIEWER_UPDATE_FEED` pointed at the local 0.0.2 feed:
  banner switched to "Downloading ... 70%" with both buttons disabled
  (screenshot), the process exited, and the app relaunched by itself as
  0.0.2 (new pid, current\MarkdownViewer.exe FileVersion 0.0.2.0), fully
  functional with tabs restored.
- Single-instance: a second launch of the installed exe with a file
  argument exited and the file opened as a tab in the running updated
  instance - the mutex/pipe owner survived the apply-restart.
- Fallback: with the feed pointed at a nonexistent directory, Download
  left the app running and unchanged (same pid, still 0.0.2), opened the
  release page in the browser, and dismissed the banner.
- Dismiss: the banner ✕ stamped dismissedVersion, no download, no
  restart. (First attempt found all tab-close buttons are also named "✕"
  in UIA - targeting by position next to Download resolved it.)
- Portable exe: single-file publish launched from a bare folder; banner
  appeared, Download opened the release page in the browser (no update
  attempt: the process is not Velopack-installed), and afterwards the
  folder still contained exactly one file and `%LocalAppData%` had no
  Velopack artifacts.
- Uninstall via `Update.exe --uninstall --silent`: install root, both
  shortcuts, and the registry entry all gone.

Remaining (user-manual, cannot be done from this box): the SmartScreen
first-run prompt, a clean-VM install, and the first real tagged release
round-trip - see the checklist in the session hand-off.

- Local cycle (no GitHub): pack v0.0.1-test and v0.0.2-test per P1.5, run
  `Setup.exe`, confirm install + shortcut + first launch, point the app at
  the local feed, trigger the update banner, click Download, and confirm
  the app downloads, restarts, and comes back as v0.0.2-test. Confirm
  Dismiss still works (no download, banner stays gone for that version)
  and that a Velopack failure falls back to opening the release page.
  Confirm uninstall from Settings > Apps is clean.
- Single-instance checks from P2: hand-off to a running instance still
  works when installed; the apply-restart does not break the pipe owner or
  the mutex.
- Portable exe still behaves exactly as today (banner shows, Download opens
  the release page, no update attempt, no new files dropped next to it).
- Real release: tag the next version, watch the Release workflow, install
  from the published `Setup.exe` on a clean machine/VM, then tag a
  follow-up patch and confirm the banner appears and one click updates.
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
