# Theming

**Status:** ⏳ In progress · Last updated 2026-05-25 · v0.3.0

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Phase T1 — WPF-UI in, theme tracking OS | WPF-UI 4.3.0 added; FluentWindow + Mica + TitleBar; ThemesDictionary + ControlsDictionary merged; brushes rewired to WPF-UI keys; SystemThemeWatcher hooked when pref is "system". |
| ✅ Done | Phase T2 — Control swap | Footer Buttons, find TextBox, popup folder rows, and PreferencesWindow controls swapped to ui: equivalents (Button, TextBox, ToggleSwitch, NumberBox). Sidebar tab switcher kept as stock TabControl (Option A). |
| ✅ Done | Phase T3 — Schema reset + Appearance pref | SettingsSchema.Current = 2; SettingsService.Load wipes (renames .bak-*) on parse failure or version mismatch; Theme default is now "system"; Preferences ComboBox shows system/light/dark. |
| ✅ Done | Phase T4 — GitHub body style | github-markdown-light/dark.css under WebAssets/lib/github-markdown/; Reading.BodyStyle pref + Preferences ComboBox; bridge.js toggles #gh-style href and wraps content in <article class="markdown-body"> when active; accent now pushed across the bridge as a CSS var |
| ⬜ Not started | Phase T5 — Polish + cleanup | Pending after a full visual regression pass and Mermaid theme verification in both body styles. |

**Outcome so far:** T1–T3 land the WPF-UI chrome and Win11/system theme tracking. Native shell now uses Fluent brushes; PreferencesWindow is a FluentWindow with Mica. WebView2 body styling is unchanged (still the existing tokens). Tab switcher visual not yet checked against the handoff — leaving Option A in place pending user feedback. Note: bridge.js' mermaid selector fixed from `pre.mermaid` to `.mermaid` (Markdig's UseDiagrams emits `<div>`, not `<pre>`); regression locked in `MarkdownServiceTests.Render_FencedMermaid_BecomesDivMermaid`.


The polished-styling pass deferred from the main plan (see
`markdownviewer.md` → "Decisions deferred → Polished Win11 / Fluent
styling"). Brings the app from stock WPF chrome to a Fluent / Win11
look, and adds a second markdown body style ("GitHub") alongside the
existing handoff-tokens style.

## Goal

- Replace stock WPF chrome with **WPF-UI**'s `FluentWindow` so the app
  gets Mica/Acrylic backdrop, Win11 caption buttons, themed scrollbars,
  and the rest of the Fluent control look — natively, with one NuGet.
- Light/dark theme tracks the OS automatically and re-applies on
  change.
- Accent color follows the user's Windows personalization color.
- WebView2 content area gains a second body style ("GitHub") that
  the user can pick alongside the existing "Win11" (handoff tokens)
  style. The two body styles are independent of the chrome theme.

Constraint: the app already ships and works. Theming is purely
visual — no feature changes, no regressions in content rendering.
Settings shape does change (new `bodyStyle`, new `theme` values); no
migration is written — see "Settings: reset on schema mismatch".

## Two surfaces, two stacks

| Surface | What | Stack |
|---|---|---|
| Native shell | `MainWindow`, sidebar, popovers, preferences window | WPF-UI 4.3.0 (`Wpf.Ui` NuGet) |
| WebView2 content | Rendered markdown, text viewer, image viewer | CSS tokens (existing) + optional github-markdown-css preset |

These are intentionally decoupled. The native shell's theme and the
web body style swap independently. The only coupling is that the
native theme pushes its accent color and light/dark mode across the
bridge so the WebView matches by default — but the user can override
the body style separately.

## Stack additions

### NuGet

```xml
<PackageReference Include="WPF-UI" Version="4.3.0" />
```

- 4.3.0 is the current stable (released 2026-05-04). Supports
  `net10.0-windows7.0` so it drops into the existing csproj.
- Earlier 4.x versions (4.0–4.2) are marked deprecated on NuGet for
  critical bugs — pin to **≥4.3.0**.
- Adds a single managed DLL plus its themes. Re-measure the published
  exe after T1 lands; if it ends up materially larger than the current
  1.8 MB framework-dependent build, decide whether to call it out in
  the README.

### Optional asset

`WebAssets/lib/github-markdown.css` — drop in the prebuilt CSS from
[sindresorhus/github-markdown-css](https://github.com/sindresorhus/github-markdown-css).
Single file (~25 KB), no build step, regenerated from GitHub's real
stylesheet by the upstream tooling. Pick the `github-markdown.css`
file (auto light/dark) so it follows the chrome theme automatically.

## Surface 1 — WPF-UI integration (full)

User chose "full" scope: switch `MainWindow` to `FluentWindow` and
replace stock controls with WPF-UI equivalents. The current
`App.xaml` hand-rolled brushes (`SidebarBg`, `SidebarFg`, etc.) and
the runtime `ApplyTheme()` swap get deleted — WPF-UI's
`ApplicationThemeManager` owns the brushes after this.

### App.xaml

Merge WPF-UI's theme + control dictionaries:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ui:ThemesDictionary Theme="Dark" />
      <ui:ControlsDictionary />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

`Theme="Dark"` is just the initial value — overwritten at startup.
The hand-rolled `SidebarBg` / `SidebarFg` / `SidebarBorder` /
`SidebarHover` / `PopupBg` / `PopupBorder` / `MutedFg` brushes go
away. References to them in `MainWindow.xaml` rewire to WPF-UI's
named resources:

| Old | New (WPF-UI brush key) |
|---|---|
| `SidebarBg` | `LayerOnAcrylicFillColorDefaultBrush` (sidebar pane) |
| `SidebarFg` | `TextFillColorPrimaryBrush` |
| `SidebarBorder` | `ControlStrokeColorDefaultBrush` |
| `SidebarHover` | `SubtleFillColorSecondaryBrush` |
| `PopupBg` | `SolidBackgroundFillColorBaseBrush` |
| `PopupBorder` | `SurfaceStrokeColorFlyoutBrush` |
| `MutedFg` | `TextFillColorSecondaryBrush` |

All resolved via `DynamicResource` so a theme change at runtime
refreshes them automatically.

The brush key names above follow WinUI 3 conventions; WPF-UI's exact
key set is what determines the final names. Verify each key resolves
during T1 by checking against `Wpf.Ui`'s theme resource dictionaries
(e.g. via the WPF-UI Gallery app or the source). Swap to whichever
neighboring key reads correctly if a name has drifted.

### MainWindow

Change the root from `<Window>` to `<ui:FluentWindow>` and set
`WindowBackdropType` and `ExtendsContentIntoTitleBar`:

```xml
<ui:FluentWindow x:Class="MarkdownViewer.MainWindow"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        WindowBackdropType="Mica"
        ExtendsContentIntoTitleBar="True"
        ...>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <ui:TitleBar Grid.Row="0" Title="MarkdownViewer"/>

    <Grid Grid.Row="1">
      <!-- existing sidebar + WebView grid -->
    </Grid>
  </Grid>
</ui:FluentWindow>
```

The existing 3-column layout becomes the inner grid. `ui:TitleBar`
hosts only the window title text and the caption buttons (min /
max / close) with Win11 SnapLayout support. The breadcrumb stays
where it is — sticky-positioned inside the WebView2 HTML — because
it changes per file and lives in the rendered document, not the
chrome.

`ExtendsContentIntoTitleBar="True"` is mandatory when
`WindowBackdropType` is anything other than `None` — Mica needs the
window content extended into the chrome region to render. Without it
you get a stock title bar and no backdrop.

### Controls swap

| Stock control | WPF-UI replacement |
|---|---|
| `Button` (footer Open / Prefs) | `ui:Button` with `Appearance="Secondary"` |
| `TabControl` (Folder / Outline) | **See "Sidebar tab switcher" below** — needs a decision |
| `TreeView` (folder + outline) | Keep stock `TreeView`; WPF-UI restyles it via `ControlsDictionary` |
| `TextBox` (find bar) | `ui:TextBox` (placeholder + clear-button support) |
| `Popup` content (open-folder) | Restyle inside as `ui:Card` with `ui:Button` rows |
| Preferences modal | `ui:FluentWindow` itself (separate window). WPF-UI-specific controls: `ui:ToggleSwitch` (for toggles), `ui:NumberBox` (for font size). Stock controls (`Slider`, `ComboBox`, `TextBox`) get Fluent styling automatically from `ControlsDictionary` — WPF-UI doesn't ship `ui:`-prefixed versions of those. |

#### Sidebar tab switcher

The design handoff shows a 2-option segmented control, not classic
tabs. WPF-UI doesn't ship a segmented control primitive, and its
`NavigationView` (despite the `Top` PaneDisplayMode) is structurally a
top-level page navigator with a back stack — wrong semantics for
"toggle between two views of the same window." Realistic options:

- **A. Keep stock `TabControl`.** WPF-UI's `ControlsDictionary`
  restyles it Fluent-ish. Least code change. May not look exactly
  like the handoff's segmented pills.
- **B. Two stock `ToggleButton`s in a horizontal `StackPanel`,**
  acting as a mutually-exclusive segmented control (group via `Tag`
  or a small attached behavior). Matches the handoff visual.
  Restyled by `ControlsDictionary`.
- **C. Stock `RadioButton`s with a segmented-style template.** Same
  visual as B; uses built-in mutex behavior. More XAML.

Defer the choice to T2 implementation — pick B or C if A looks wrong
when the rest is themed.

### Theme + accent wiring (startup)

In `App.xaml.cs` (or `MainWindow` constructor, but `App` is cleaner —
needs to run after the main window is constructed since
`SystemThemeWatcher.Watch` takes a window instance):

```csharp
// Read OS theme + accent at startup.
ApplicationThemeManager.ApplySystemTheme();
ApplicationAccentColorManager.ApplySystemAccent();

// Re-apply automatically when the OS personalization changes.
// The 3-arg overload Watch(window, backdrop, updateAccents) is what
// the WPF-UI docs show; the parameter is `updateAccents` (plural).
SystemThemeWatcher.Watch(
    mainWindow,
    WindowBackdropType.Mica,
    updateAccents: true);
```

`SystemThemeWatcher.Watch` hooks `WM_SETTINGCHANGE` /
`WM_WININICHANGE` on the window's WndProc; flipping Windows from
light → dark (or changing the accent swatch) re-applies the theme
without the user touching the Preferences screen. Per the WPF-UI
docs, the watcher works **app-wide** — you only need to call `Watch`
once with any one window instance and every WPF-UI window picks up
the change.

Notes:
- `ApplicationThemeManager.ApplySystemTheme()` takes **no
  parameters**. Accent is handled by `ApplicationAccentColorManager`
  separately at startup, and by `SystemThemeWatcher`'s
  `updateAccents` flag thereafter.
- `ApplicationAccentColorManager.SystemAccent` returns a `Color`.
  To hand it across the bridge as a hex string, format with
  `$"#{c.R:X2}{c.G:X2}{c.B:X2}"`.
- The "accent or theme changed" event to subscribe to is
  `ApplicationThemeManager.Changed` (fires for both Apply() and
  watcher-driven updates).

### Removed code

- `MainWindow.ApplyTheme()` and its hand-rolled brush-swap logic.
- The `SidebarBg` / `SidebarFg` / etc. brushes in `App.xaml`.
- Whatever Phase 8 "detect Windows light/dark theme on first run" does
  by reading the registry — `ApplySystemTheme()` covers that case.

The settings file's `"theme"` key becomes a tri-state: `"system"`
(new default) | `"light"` | `"dark"`. See "Settings: reset on schema
mismatch" — old values are not migrated.

## Surface 2 — WebView2 body styles

Two preset body styles, picked in Preferences:

| Body style | Where the CSS comes from |
|---|---|
| **Win11** (default) | Existing `reader.css` with its `--bg` / `--fg` / `--accent` tokens. Already implemented; light/dark already work via `body.theme-dark`. |
| **GitHub** | `lib/github-markdown.css` (drop-in). Wrap the rendered markdown in `<article class="markdown-body">` per the package's instructions. |

Both styles respect the active light/dark mode and the active accent
color, which the bridge already pushes via `setPrefs`. New prefs key:

```json
"reading": {
  "bodyStyle": "win11" | "github",   // NEW, default "win11"
  // ...existing reading keys
}
```

In `render.html`, conditionally toggle `<body class="md-style-github">`
and conditionally `<link>` the GitHub stylesheet based on the value
pushed via `setPrefs`. The existing tokens still apply for the
breadcrumb and find bar and line-number gutter — only the
`.markdown-body` subtree picks up the GitHub styling.

The accent color comes across the bridge as a hex string. Read
`ApplicationAccentColorManager.SystemAccent` (returns a `Color`),
format as `#RRGGBB`, include in the `setPrefs` payload. Re-push
whenever `ApplicationThemeManager.Changed` fires (covers both
user-initiated theme changes and OS-driven ones via
`SystemThemeWatcher`).

## Settings: reset on schema mismatch

No migration code. Add a top-level `"schemaVersion"` integer to the
settings file; bump it when the shape changes. On load:

- File missing → write defaults.
- File present and `schemaVersion` matches → load normally.
- File present and `schemaVersion` is missing, older, newer, or the
  JSON fails to parse → **delete the file** (or rename to
  `settings.json.bak-<timestamp>` for one boot, then never touch the
  backup again) and write fresh defaults.

The user loses theme, font size, margins, pinned/recent folders, and
window position on the version that introduces this work. That is the
cost the user signed off on by choosing "wipe, don't migrate." The
app does not warn on launch; defaults are sensible and re-acquiring
preferences takes <30 seconds.

```json
{
  "schemaVersion": 2,                 // NEW; bump per shape change
  "theme": "system",                  // tri-state
  "reading": {
    "bodyStyle": "win11",             // NEW
    "typeface": "system",
    "fontSize": 14,
    "marginPct": 85,
    "showLineNumbers": false
  }
  // ...rest unchanged
}
```

Implementation: `SettingsService.Load()` reads the file, tries to
parse, checks `schemaVersion`. On any mismatch or parse failure,
`File.Delete(path)` (or rename) and `return new AppSettings()`. Save
on next change writes the new shape with the current version.

This pattern continues going forward — any future schema change just
bumps `schemaVersion` and the wipe handles every previous shape
uniformly. No accumulating migration ladder.

## Preferences UI changes

Appearance section gains a 3-option select:

- Theme: **System** | Light | Dark (was "Dark mode" toggle)

Reading section gains a 2-option select:

- Body style: **Win11** | GitHub (new)

Everything else stays. Implement with stock `ComboBox` — it picks up
Fluent styling from `ControlsDictionary` automatically; WPF-UI does
not ship a `ui:ComboBox`.

## Phases

## ✅ Phase T1 — WPF-UI in, theme tracking OS

- Add `WPF-UI 4.3.0` NuGet to `MarkdownViewer.csproj`.
- Merge `ThemesDictionary` + `ControlsDictionary` in `App.xaml`.
- Delete hand-rolled brushes in `App.xaml` and their references in
  `MainWindow.xaml`; rewire to WPF-UI brush keys via `DynamicResource`.
- Sweep `MainWindow.xaml` for hard-coded literal colors that bypass
  the brush system and will not re-theme: e.g. `Foreground="#888"` on
  the "Pinned" / "Currently open" / "Recent" / "(current)" /
  `FindCount` labels, `Background="White"` and
  `BorderBrush="#33000000"` on the `FindBar`, `Background="#10000000"`
  on the `GridSplitter`. Replace each with the corresponding
  WPF-UI brush via `DynamicResource`.
- Convert `MainWindow` to `ui:FluentWindow` with `WindowBackdropType="Mica"`,
  `ui:TitleBar`, and the existing grid as its content.
- Wire `ApplicationThemeManager.ApplySystemTheme()` +
  `ApplicationAccentColorManager.ApplySystemAccent()` at startup, and
  `SystemThemeWatcher.Watch(mainWindow, WindowBackdropType.Mica,
  updateAccents: true)` for live updates.
- Delete `MainWindow.ApplyTheme()` and the Phase 8 first-run theme
  detection.
- Test: launch in OS light, then OS dark; verify sidebar, popovers, find
  bar all re-skin without a restart. Verify Mica visible on Win11
  desktop with a wallpaper behind.

## ✅ Phase T2 — Control swap

- Replace footer `Button`s with `ui:Button`.
- Sidebar tab switcher: start with stock `TabControl` (Option A
  above) and check the visual against the design handoff. If it's too
  far off, switch to two `ui:ToggleButton`s with a mutex behavior
  (Option B). Re-bind `SelectionChanged` accordingly.
- Replace find-bar `TextBox` with `ui:TextBox`.
- Update open-folder popup contents to `ui:Card` + `ui:Button` rows.
- Convert `PreferencesWindow` toggles to `ui:ToggleSwitch` and font
  size to `ui:NumberBox`. `Slider` (margins) and `ComboBox`
  (typeface, theme, outline-collapse) stay stock — `ControlsDictionary`
  restyles them. Window itself becomes `ui:FluentWindow` with its own
  `ui:TitleBar`.
- Test: every interaction still works (open folder, pin, prefs save,
  find next/prev, sidebar resize).

## ✅ Phase T3 — Schema reset + Appearance pref

- Add top-level `schemaVersion` field to `AppSettings`. Set the
  current version constant (e.g. `2`) in `SettingsService`.
- In `SettingsService.Load()`: on parse failure or version mismatch,
  delete the file and return defaults. No migration.
- Bump `theme` to `"system" | "light" | "dark"` in the model.
- Replace "Dark mode" toggle with a 3-option `ComboBox`. When set to
  "System", call `SystemThemeWatcher.Watch` and
  `ApplicationThemeManager.ApplySystemTheme()`; when set to
  Light/Dark, call `ApplicationThemeManager.Apply(ApplicationTheme.X,
  WindowBackdropType.Mica)` and unhook the watcher with
  `SystemThemeWatcher.UnWatch`.
- Test: settings persist across restart; corrupting the file by hand
  (or downgrading the version field) wipes cleanly to defaults
  without crashing.

## ✅ Phase T4 — GitHub body style

- Drop `github-markdown.css` (auto variant) into `WebAssets/lib/`.
  Include in the `Content Include="WebAssets\**\*.*"` glob — already
  done.
- Add `bodyStyle` to `reading` prefs (default `"win11"`).
- Add Body Style `ui:ComboBox` to Preferences → Reading.
- Push `bodyStyle` across the bridge in the existing `setPrefs`
  message. In `render.html`, conditionally toggle a `<link>` to the
  GitHub stylesheet and wrap the rendered HTML in
  `<article class="markdown-body">` when active.
- Push accent color across the bridge as `accent: "#rrggbb"`. On
  `ApplicationThemeManager.Changed`, re-emit `setPrefs`.
- Test: switch between Win11/GitHub in prefs and verify code blocks,
  tables, headings, blockquotes, task list checkboxes all look right
  in both modes and in both light and dark.

## ⬜ Phase T5 — Polish + cleanup

- Verify Mermaid diagrams render against both body styles (dark
  background variant via Mermaid theme).
- Verify highlight.js theme switches with light/dark (use one
  theme pair per body style: e.g. `github.css` / `github-dark.css`
  for GitHub mode; `vs.css` / `vs2015.css` for Win11 mode).
- Re-test all 9 phases of the main plan in dark and light — sidebar
  drag, file watcher reload, drag-drop, context-menu launch, every
  shortcut.
- Update `README.md` screenshots.
- Bump app version in `markdownviewer.md` and `markdownviewer-changelog.md`.

## Out of scope (still)

- Custom accent color picker. User chose "follow OS" — adding a
  swatch picker in Prefs is deferred.
- Themes beyond Win11 + GitHub. Token system supports more, but no
  current need.
- Acrylic instead of Mica (different backdrop). Mica is the
  Win11-correct choice for stable app surfaces; Acrylic is for
  transient flyouts.
- Re-theming the WebView's PDF viewer chrome — that's
  Edge-controlled, no API surface to skin.
- Touching the Markdig pipeline, content router, file watcher, or
  any non-visual code path.

## Risks and unknowns

- **WPF-UI's `NavigationView` may not exactly match the design
  handoff segmented control.** If close-enough doesn't satisfy, fall
  back to a styled `ui:Button` pair acting as a segmented group; the
  visual is two pills with a selected state.
- **Mica requires Win11 Build 22621+.** On Win10 / older Win11 builds
  it silently falls back to a solid color — acceptable. The app
  is Win11-only per the main plan anyway.
- **WPF-UI restyling of stock `TreeView`** may interact with the
  existing `HierarchicalDataTemplate` and `ItemContainerStyle`. If
  the template breaks, the fix is to inherit from WPF-UI's TreeView
  style via `BasedOn`.
- **`github-markdown.css` ships GitHub's own font stack.** That may
  clash with the user's typeface preference. Either override
  `font-family` inside `.markdown-body` to honor `--font`, or
  document that GitHub style overrides the typeface pref.
- **Settings reset is user-visible.** First launch of the T3 build
  silently discards the existing `settings.json` and rebuilds
  defaults. The user loses pinned/recent folders, last open file,
  window position, and all prefs. Mention this in the release notes
  for that build.
- **Phase ordering means T1 ignores the saved `theme` pref.** Between
  T1 (always tracks OS) and T3 (respects user pref), a user with
  `theme: "win11-light"` running the app on a dark-OS would see dark
  until T3 lands. Acceptable for an internal phased rollout; do not
  ship a release between T1 and T3.
- **Accent in Light/Dark explicit modes.** User asked for "accent
  follows OS." When the user picks an explicit Light or Dark theme,
  the watcher's `updateAccents: true` should keep working —
  effectively the user is overriding only the light/dark axis, not
  the accent. Verify in T3 that calling `ApplicationThemeManager.Apply`
  doesn't clobber the accent set by `SystemAccent`. If it does,
  re-apply `ApplicationAccentColorManager.ApplySystemAccent()`
  immediately after.

## Decisions confirmed up-front

- Themes shipped: Win11 light/dark (chrome) + GitHub (body style).
  Four practical combinations.
- WPF-UI scope: full (FluentWindow + control swap).
- Accent color: read from OS via `SystemThemeWatcher` /
  `ApplicationAccentColorManager.SystemAccent`. No user picker.

## Changelog


## v0.3.0 — 2026-05-25

User decision: drop settings migration entirely.

- "Settings migration" section replaced with "Settings: reset on
  schema mismatch." Adds top-level `schemaVersion` integer. On load,
  parse failure or version mismatch → delete the file and rebuild
  defaults. No migration code, now or later — same pattern handles
  every future schema change.
- Phase T3 renamed "Schema reset + Appearance pref"; migration step
  replaced with the wipe-on-mismatch logic and a corruption test.
- Risk note rewritten: settings reset is user-visible (loses pinned
  folders, recents, window state, last file); call out in release
  notes for the T3 build.
- Top-of-plan constraint reworded — "no settings format breakage"
  was no longer true; replaced with "shape changes; no migration."

## v0.2.0 — 2026-05-25

Plan-review pass against live WPF-UI 4.3.0 docs. Clear-cut fixes:

- **API signatures corrected.**
  `ApplicationThemeManager.ApplySystemTheme()` takes no parameters
  (was claimed to take `updateAccent`). Accent is applied separately
  via `ApplicationAccentColorManager.ApplySystemAccent()` at startup
  and via `SystemThemeWatcher.Watch(window, backdrop, updateAccents:
  true)` (parameter is plural `updateAccents`) for live updates.
- **NavigationView dropped as the tab-control replacement.** It's a
  top-level page navigator with a back stack — wrong semantics for
  toggling between two views of the same window. Replaced with a
  "pick at T2" choice between stock `TabControl` (auto-restyled by
  `ControlsDictionary`) or two stock `ToggleButton`s as a segmented
  control.
- **`ui:ComboBox` / `ui:Slider` / `ui:ToggleButton` don't exist.**
  WPF-UI re-styles the stock controls via `ControlsDictionary`;
  only `ui:`-prefixed controls that add features (`ui:ToggleSwitch`,
  `ui:NumberBox`, `ui:Button`, `ui:TextBox`, `ui:Card`,
  `ui:TitleBar`, `ui:FluentWindow`) get the prefix.
- **TitleBar contents corrected.** The original draft said `ui:TitleBar`
  would host the breadcrumb; the breadcrumb is HTML inside the
  WebView2, so the TitleBar only carries the window title + caption
  buttons. Also called out that `ExtendsContentIntoTitleBar="True"`
  is mandatory for backdrop effects.
- **Brush-key names flagged as unverified.** The WinUI-style names
  (`LayerOnAcrylicFillColorDefaultBrush`, etc.) need verification
  against `Wpf.Ui`'s actual resource dictionaries during T1.
- **Hard-coded color sweep added to T1.** `MainWindow.xaml` has
  literal `Foreground="#888"` / `Background="White"` /
  `BorderBrush="#33000000"` on the find bar, popup section headers,
  and grid splitter that bypass the brush system. They have to
  rewire to `DynamicResource` brushes or the dark theme won't apply
  to them.
- **Risk added: phase ordering.** Between T1 and T3 the app
  temporarily ignores the saved `theme` pref. Acceptable for an
  internal phased rollout; called out so it doesn't ship.
- **Risk added: accent in explicit Light/Dark.** Need to verify that
  `ApplicationThemeManager.Apply()` doesn't clobber the system
  accent; if it does, re-apply `ApplySystemAccent()` after.
- **Unverified footprint claim removed.** Original said WPF-UI adds
  "~3 MB to the published exe" — number was invented. Replaced with
  "re-measure after T1."

Sources verified: [WPF-UI 4.3.0 on NuGet](https://www.nuget.org/packages/WPF-UI/),
[Themes docs](https://wpfui.lepo.co/documentation/themes.html),
[SystemThemeWatcher docs](https://wpfui.lepo.co/documentation/system-theme-watcher.html),
[Accent docs](https://wpfui.lepo.co/documentation/accent.html),
[Wpf.Ui.Controls namespace API](https://wpfui.lepo.co/api/Wpf.Ui.Controls.html).

## v0.1.0 — 2026-05-25

Initial theming plan. Covers the styling pass deferred from the main
plan (`markdownviewer.md` → "Decisions deferred → Polished Win11 /
Fluent styling").

Stack:
- **Native shell:** WPF-UI 4.3.0 (`Wpf.Ui` NuGet), full integration
  — `FluentWindow` + Mica + control swap (`ui:NavigationView`,
  `ui:Button`, `ui:ToggleSwitch`, etc.).
- **WebView body:** keep existing token-based CSS; add
  github-markdown-css as a second selectable body style.

Behavior:
- Light/dark follows OS via `ApplicationThemeManager.ApplySystemTheme`
  + `SystemThemeWatcher.Watch`.
- Accent follows OS via `ApplicationAccentColorManager.SystemAccent`,
  pushed to the WebView through `setPrefs`.
- Settings `theme` becomes tri-state: `system` (default) | `light` |
  `dark`. One-shot migration for legacy `win11-light` / `win11-dark`.
- New `reading.bodyStyle`: `win11` (default) | `github`.

Phased: T1 WPF-UI in + theme tracking, T2 control swap, T3 settings
migration + Appearance pref, T4 GitHub body style, T5 polish.

Out of scope: custom accent picker, themes beyond Win11 + GitHub,
Acrylic, PDF chrome theming, non-visual code changes.
