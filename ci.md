# GitHub Actions CI/CD

**Status:** ⬜ Not started · Last updated 2026-05-30 · plan only

| Status | Phase | Notes |
|---|---|---|
| ⬜ Not started | Phase 1 — Pin the SDK | add `global.json` so CI matches local 10.0.300 |
| ⬜ Not started | Phase 2 — CI workflow | build + test on push/PR (`ci.yml`) |
| ⬜ Not started | Phase 3 — Release workflow | tag → publish single-file exe → attach to Release (`release.yml`) |
| ⬜ Not started | Phase 4 — Decisions | FD vs self-contained; signing; versioning from tag |

## Goal

Stand up GitHub Actions so every push/PR is built and tested on a hosted
**`windows-latest`** runner, and pushing a `vX.Y.Z` tag produces a GitHub
Release with the single-file `MarkdownViewer.exe` attached. No self-hosted
runner required.

## Feasibility — confirmed against this repo

| Requirement | On GitHub hosted runner |
|---|---|
| WPF / `net10.0-windows` build | ✅ needs Windows → `windows-latest` |
| .NET 10 SDK | ✅ `actions/setup-dotnet` (+ `global.json` pin) |
| WebView2 **SDK** (NuGet) | ✅ restored normally, no extra setup |
| WebView2 **Runtime** | ✅ **not needed** to build or to run the unit tests |
| 186 unit tests | ✅ pure logic — grep of test source shows no WPF/registry/STA use → headless-safe |
| Single-file publish | ✅ already works; embeds WebAssets → one exe artifact |

Repo facts checked: local SDK `10.0.300`; no `global.json` yet; no
`.github/` yet; `MarkdownViewer.sln` contains `src` + `tests`; test TFM
`net10.0-windows`; `test.ps1` just runs `dotnet test` on the sln.

Cost: **public repo → free unlimited** Actions minutes on standard
runners. Private repo bills Windows minutes at a **2× multiplier** against
the included quota.

## ⬜ Phase 1 — Pin the SDK (`global.json`)

Add at repo root so the runner installs the same SDK family as local:

```json
{
  "sdk": {
    "version": "10.0.300",
    "rollForward": "latestFeature"
  }
}
```

`latestFeature` lets the runner use a newer 10.0.3xx if the exact patch
isn't on the image, without silently jumping a major/minor.
`actions/setup-dotnet` reads this file automatically.

## ⬜ Phase 2 — CI workflow (`.github/workflows/ci.yml`)

Fast feedback on every push and PR: build the solution, run the tests.

```yaml
name: CI
on:
  push:
    branches: [ master ]
  pull_request:
jobs:
  build-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4      # reads global.json
      - run: dotnet restore MarkdownViewer.sln
      - run: dotnet build MarkdownViewer.sln -c Release --no-restore
      - run: dotnet test tests/MarkdownViewer.Tests/MarkdownViewer.Tests.csproj -c Release --no-build
```

Notes:
- Builds the `.sln` (both projects); tests reuse the build (`--no-build`).
- CI calls `dotnet test` directly rather than `test.ps1` so it doesn't
  depend on the local wrapper script.
- **Future risk:** if a test ever instantiates a WPF control it needs an
  STA thread (xUnit needs `[STAThread]`/an STA collection). None do today;
  add that only when a UI test is introduced — or keep UI checks in the
  manual `smoke.ps1` lane, which CI does not run (it launches the GUI).

## ⬜ Phase 3 — Release workflow (`.github/workflows/release.yml`)

Trigger on a version tag; publish the single-file exe and attach it to a
GitHub Release.

```yaml
name: Release
on:
  push:
    tags: [ 'v*' ]
permissions:
  contents: write                 # required to create the Release
jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
      - run: dotnet test tests/MarkdownViewer.Tests/MarkdownViewer.Tests.csproj -c Release

      # Derive version from the tag (v1.2.3 -> 1.2.3) for assembly metadata.
      - id: ver
        shell: pwsh
        run: echo "num=$($env:GITHUB_REF_NAME.TrimStart('v'))" >> $env:GITHUB_OUTPUT

      - run: >
          dotnet publish src/MarkdownViewer.csproj -c Release -r win-x64
          -p:PublishSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true
          --self-contained false
          -p:Version=${{ steps.ver.outputs.num }}
          -o publish

      - uses: softprops/action-gh-release@v2
        with:
          files: publish/MarkdownViewer.exe
          generate_release_notes: true
```

Result: tag `v1.2.3` → a Release with `MarkdownViewer.exe` (~11.5 MB),
auto-generated notes, and the exe stamped `1.2.3`.

Optional: add a second publish step with `--self-contained true` and upload
both artifacts (see decisions below).

## ⬜ Phase 4 — Decisions to make before implementing

1. **Framework-dependent vs self-contained.**
   - *FD* (~11.5 MB): needs the **.NET 10 Desktop Runtime** on the user's
     machine. Tiny download.
   - *Self-contained* (~80–100 MB): runs with **no .NET installed**.
   - Either way the user still needs the **WebView2 Runtime** (preinstalled
     on Win11). Could ship **both** and let people choose.

2. **Code signing.** The exe is unsigned → **SmartScreen** shows a
   "Windows protected your PC" warning on first download/run, and the exe
   reads as "unknown publisher." Removing that needs an Authenticode
   (ideally EV/OV) cert — out of scope unless you have one. If/when you do,
   add a `signtool` step before the upload. Until then, **say in the README
   that the build is unsigned.**

3. **Versioning source of truth.** Today the exe reports `1.0.0+<hash>`.
   Phase 3 stamps the version from the git tag. Alternative: a `<Version>`
   in the csproj. Pick one so they don't drift.

4. **Release trigger style.** Tag-push (above) vs manual
   `workflow_dispatch` vs "on GitHub Release published." Tag-push is the
   simplest and most common.

## Out of scope (call out, revisit)

- **Code signing / notarization** — needs a cert; see decision 2.
- **MSIX / installer** — would enable first-class Win11 context-menu
  placement (vs "Show more options"), but needs packaging + signing.
- **Auto-update** — no updater; users re-download from Releases.
- **Matrix / ARM64 builds** — single `win-x64` target for now.
- **NuGet caching** (`setup-dotnet` cache or `actions/cache`) — minor
  speedup; add only if build minutes start to matter.
- **`smoke.ps1` in CI** — it launches the GUI, so it stays a manual check;
  CI runs only the headless unit tests.
