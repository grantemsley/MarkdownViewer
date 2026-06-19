# MarkdownViewer - Reference

Stable facts about the project. Volatile state (unpushed commits, current version target)
lives in `.claude/notes.md` and the active plan, not here.

## Repository

- **GitHub:** `grantemsley/MarkdownViewer` (public)
- **CI:** GitHub Actions (`checkout v6`, `setup-dotnet v5`, `action-gh-release v3`).
  Tag push triggers build and release. All tests run on every push.
- **Release artifact:** Single framework-dependent portable `.exe` attached to the GitHub
  Release. No installer. Framework-dependent (requires .NET runtime on the machine).

## Architecture

| Layer | Component | Notes |
|---|---|---|
| Native shell | `MainWindow` (WPF, `ui:FluentWindow`) | Thin view; delegates tab decisions to `TabManager` |
| Tab logic | `TabManager`, `TabState`, `TabSession` | Pure, UI-agnostic; unit-tested (16 tests) |
| Per-tab runtime | `TabRuntime`, `VaultService` | One `VaultService` per tab; events gated to active tab only |
| Content rendering | Shared `WebView2` | Single instance shared across all tabs |
| Vault serving | `app.local/__vault/` | Same-origin; serves images, PDFs, embedded resources |
| Update check | `UpdateService` | Notify-only; GitHub Releases API; throttled to once per 24h |
| Settings | `SettingsService`, `AppSettings` | Schema-versioned; wipe-and-default on mismatch (no migration) |
| Single-instance | `SingleInstanceServer` | Named mutex + named pipe; second launch routes to owner |

## Test suite

302 tests (as of 2026-06-14). Run with `dotnet test`. Tests cover tab-manager logic,
markdown rendering, `MarkdownService`, session round-trips. WPF/WebView2 behavior verified
manually.

## Key settings

- `TabsPrefs.Enabled` - tabs on/off (default on; requires restart)
- `UpdatePrefs.LastCheckUtc` - timestamp of last successful GitHub check
- `UpdateService.CheckInterval` - 24h throttle
- `reading.bodyStyle` - `"win11"` (default) or `"github"`
- `schemaVersion` - bumped on any breaking shape change; triggers wipe-to-default on load

## Theming stack (as of plans/theming.md T4)

| Surface | Stack |
|---|---|
| Native shell | WPF-UI 4.3.0 (`Wpf.Ui` NuGet); `FluentWindow` + Mica; `SystemThemeWatcher` |
| WebView2 content | `reader.css` tokens (Win11 style) or `lib/github-markdown-light/dark.css` (GitHub style) |
