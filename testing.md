# Testing

**Status:** ✅ Done · Last updated 2026-05-25 · v0.1.0

| Status | Phase | Notes |
|---|---|---|
| ✅ Done | Extract pure logic from MainWindow | Services/TreeFilter.cs and Services/OutlineBuilder.cs; MainWindow now delegates |
| ✅ Done | Create test project | tests/MarkdownViewer.Tests/, xUnit 2.9, net10.0-windows + UseWPF, added to .sln |
| ✅ Done | Test runner | test.ps1 at project root, anchored to $PSScriptRoot |
| ✅ Done | Write tests | 67 tests across 6 files (Markdown, ContentRouter, VaultService, TreeFilter, OutlineBuilder, VaultNode) |
| ✅ Done | Green run | dotnet test passes 67/67 in under 1 second |
| ✅ Done | README pointer | "Run tests: .\test.ps1" added |


## Context

The app has shipped through Phase 9 (compiled, runnable) but has zero automated
tests. The plan's "Testing checklist" lists 16 manual scenarios that need
re-verification after every round of fixes — exactly the kind of regression
surface where Claude Code sessions reintroduce bugs because the cost of
verifying everything is too high. We want:

1. A fast unit-test pass Claude runs after any non-trivial change.
2. Coverage on the logic that has historically broken: markdown render,
   sidebar filtering, outline collapse, encoding detection, file routing.

WebView2 / WPF UI interactions stay manual — automating them costs more than
they save for a tool this small.

## Scope

In scope (unit-testable, runs in seconds):

- `MarkdownService.Render` — frontmatter strip, heading extraction + slugs,
  Mermaid passthrough, math, line-number annotation.
- `ContentRouter.Route` — extension → ViewerKind mapping + highlight lang.
- `ContentRouter.ReadTextFile` — BOM detection, UTF-8 strict, Windows-1252
  fallback.
- `VaultService.ResolveInput` and `BuildNode` (via `Open` on a temp dir) —
  folder/file disambiguation; empty folders still appear in the tree.
- Tree filter logic (`ShowHidden`, `ShowNonMarkdown`) — extracted to a new
  pure static class so tests can call it without instantiating MainWindow.
- Outline collapse logic (`CollapseBelow`, `CollapseContaining`) — same
  extraction.
- `VaultNode.DisplayName` — respects `UiPrefs.ShowExtensions`.

Out of scope (would require UI automation or live FileSystemWatcher timing):

- WebView2 bridge, link interception, theme application.
- Drag-drop, keyboard shortcuts, find-in-page.
- FileSystemWatcher live reload (scroll-preservation, breadcrumb flash).
- JS in `WebAssets/bridge.js` (small, mostly defensive; revisit if it grows).

## Approach

### 1. Refactor two private statics out of MainWindow

`MainWindow.xaml.cs:276 ApplyFilterRec` and `:615 ApplyOutlineCollapse` are
already pure static methods, but living in a partial WPF class means tests
can't see them. Move each to its own file under `src/Services/`:

- `Services/TreeFilter.cs` — `public static class TreeFilter { public static bool Apply(VaultNode, FilePrefs) }`. Keep the `bool` return that `ApplyFilterRec` had (unused by MainWindow today, but lets tests assert recursion outcome directly).
- `Services/OutlineBuilder.cs` — `public static class OutlineBuilder { public static void ApplyCollapse(IEnumerable<HeadingViewModel>, int threshold, string needle) }`

Update the two call sites in MainWindow. No behavior change.

### 2. New test project

```
outputs/MarkdownViewer/
├── MarkdownViewer.sln           # add the test project
├── src/MarkdownViewer.csproj
└── tests/
    └── MarkdownViewer.Tests/
        ├── MarkdownViewer.Tests.csproj
        ├── MarkdownServiceTests.cs
        ├── ContentRouterTests.cs
        ├── VaultServiceTests.cs
        ├── TreeFilterTests.cs
        ├── OutlineBuilderTests.cs
        └── VaultNodeTests.cs
```

- `net10.0-windows`, `UseWPF=true` (so it can reference the WPF-typed source
  assembly — `UiPrefs` and `VaultNode` pull in `System.Windows` types).
- xUnit 2.x + `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`.
- Project reference to `../../src/MarkdownViewer.csproj`. The src project is
  a `WinExe` but referencing it from a test library is supported in .NET 10
  (the test runner provides its own entry point; the referenced project's
  `Main` is unused). If a benign warning appears, leave it — don't paper over
  it with a wrong-property suppress. The alternative — splitting Services /
  Models into a separate `MarkdownViewer.Core` library — is more churn than
  it's worth for v1.
- **Parallelization:** `VaultNodeTests` mutates the `UiPrefs.Instance`
  singleton. xUnit runs different test classes in parallel by default, so
  add `xunit.runner.json` with `"parallelizeTestCollections": false`.
  (Tests within a class already run serially.)
- **CodePagesEncodingProvider registration:** `ContentRouter.ReadTextFile`
  uses `Encoding.GetEncoding(1252)`, which on .NET Core requires the provider
  to be registered (the main app does this at startup; tests don't run that
  code path). Add a `[ModuleInitializer]` in a `TestInit.cs` that calls
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` so it runs
  once on test-assembly load.

### 3. Test runner

A wrapper at `outputs/MarkdownViewer/test.ps1` that anchors paths to its own
location so it works regardless of cwd:

```powershell
$ErrorActionPreference = "Stop"
$sln = Join-Path $PSScriptRoot "MarkdownViewer.sln"
& "C:\Program Files\dotnet\dotnet.exe" test $sln --nologo @args
```

Future Claude sessions invoke `outputs/MarkdownViewer/test.ps1` (or
`dotnet test`) after changes. The README under `outputs/MarkdownViewer/`
gets a one-line "Run tests: `./test.ps1`" pointer.

## Critical files

| File | Change |
|---|---|
| `outputs/MarkdownViewer/src/MainWindow.xaml.cs` | Replace `ApplyFilterRec` body with `TreeFilter.Apply`; replace `ApplyOutlineCollapse` with `OutlineBuilder.ApplyCollapse`. |
| `outputs/MarkdownViewer/src/Services/TreeFilter.cs` | New. Extracted logic. |
| `outputs/MarkdownViewer/src/Services/OutlineBuilder.cs` | New. Extracted logic. |
| `outputs/MarkdownViewer/MarkdownViewer.sln` | Add test project. |
| `outputs/MarkdownViewer/tests/MarkdownViewer.Tests/*` | New project + test files. |
| `outputs/MarkdownViewer/test.ps1` | New runner. |
| `outputs/MarkdownViewer/README.md` | Mention how to run tests. |

## Tests to write (concrete)

**MarkdownServiceTests**
- `Render_StripsYamlFrontmatter` — `--- title: x ---\n# H` → no `title` in HTML.
- `Render_ExtractsHeadings_WithSlug` — three headings of different levels, ids match `# Hello World` → `hello-world`, etc.
- `Render_MathDoubleDollar_ProducesMathBlock` — `$$x^2$$` → renders with math class (Markdig math extension).
- `Render_FencedMermaid_BecomesPreMermaid` — ```` ```mermaid```` → `<pre class="mermaid">`.
- `Render_WithLineNumbers_AnnotatesTopLevelBlocks` — paragraph + heading + code → each top-level wrapper has `data-line="N"` and `class` includes `md-block`.
- `Render_Tables_Render` — pipe table → `<table>`.
- `Render_TaskLists_Render` — `- [x] done` → `<input` with `checkbox`.

**ContentRouterTests**
- `Route_MdExtension_Markdown`, `Route_HtmlExtension_RawBrowser`,
  `Route_PdfExtension_RawBrowser`, `Route_PngExtension_Image`,
  `Route_Ps1Extension_TextWithPowerShellHighlight`,
  `Route_TxtExtension_TextNoHighlight`,
  `Route_UnknownExtensionBinaryContent_Binary` (write file with null byte),
  `Route_UnknownExtensionTextContent_Text`,
  `Route_NonexistentFile_None`.
- `ReadTextFile_Utf8Bom_StripsBom`,
  `ReadTextFile_Utf16Le_Decodes`,
  `ReadTextFile_Utf8NoBom_Decodes`,
  `ReadTextFile_Latin1Bytes_FallsBackToCp1252` (write `0xE9` byte, expect "é").
- Register CodePagesEncodingProvider in a static ctor / fixture so cp1252 works on .NET Core.

**VaultServiceTests**
- `ResolveInput_FolderPath_ReturnsFolder`,
  `ResolveInput_FilePath_ReturnsParentAndFile`,
  `ResolveInput_NonexistentPath_ReturnsEmpty`,
  `ResolveInput_NullOrWhitespace_ReturnsEmpty`.
- `Open_BuildsRecursiveTree` — temp dir w/ subfolders + files → node tree
  matches.
- `Open_EmptyFolder_StillProducesNode` — confirms the "empty folders appear"
  regression from the checklist.
- `Open_NonexistentFolder_DoesNotThrow`.

> Every `VaultService` test must `using var vault = new VaultService();`
> so `Dispose` shuts the FileSystemWatcher down. `Open` is synchronous
> for the tree build; watcher events would queue onto a never-pumped
> `Dispatcher.CurrentDispatcher` and are harmless as long as tests don't
> wait for them.

**TreeFilterTests**
- `Apply_HidesDotFiles_WhenShowHiddenFalse`.
- `Apply_ShowsDotFiles_WhenShowHiddenTrue`.
- `Apply_HidesNonMarkdownFiles_WhenShowNonMarkdownFalse`.
- `Apply_ShowsAllFiles_WhenShowNonMarkdownTrue`.
- `Apply_HiddenMarkdownFile_HiddenWhenShowHiddenFalse` — `.private.md`
  should be filtered out even though `IsMarkdown == true`; the dotfile
  rule comes before the markdown filter.
- `Apply_EmptyFolder_IsStillVisible`.
- `Apply_FolderWithOnlyFilteredChildren_StillVisible`.
- `Apply_DotFolder_HiddenWhenShowHiddenFalse`.

**OutlineBuilderTests**
- `ApplyCollapse_LevelBelowThreshold_IsExpanded`.
- `ApplyCollapse_LevelAtOrAboveThreshold_IsCollapsed`.
- `ApplyCollapse_Threshold7_NeverCollapsesByLevel`.
- `ApplyCollapse_ContainingNeedle_IsCollapsed_CaseInsensitive`.
- `ApplyCollapse_EmptyNeedle_DoesNotFilter`.
- `ApplyCollapse_RecursesIntoChildren`.

**VaultNodeTests**
- `DisplayName_MarkdownFile_DropsExtension_WhenShowExtensionsFalse`.
- `DisplayName_MarkdownFile_KeepsExtension_WhenShowExtensionsTrue`.
- `DisplayName_NonMarkdownFile_AlwaysKeepsExtension`.
- `DisplayName_Folder_AlwaysKeepsName`.
- Each test must reset `UiPrefs.Instance.ShowExtensions` in a finally block —
  it's a singleton.

## Verification

```powershell
.\test.ps1
```

Expected: all tests pass in under ~5 seconds. Build the main app too
(`dotnet build src\MarkdownViewer.csproj`) to confirm the MainWindow
refactor compiles. Optionally launch the exe and verify the sample folder
still loads (smoke test).

## Conventions

When adding logic to MainWindow that could live in a pure static helper,
extract it to `src/Services/` (see `TreeFilter.cs` and `OutlineBuilder.cs`
for the pattern) so it can be tested. Then add or extend the matching
`*Tests.cs` file under `tests/MarkdownViewer.Tests/`.

WebView2, FileSystemWatcher live behavior, drag-drop, and keyboard
shortcuts are not unit-tested — verify those manually via the checklist
in `markdownviewer.md`.

## Future

If we add more JS to `bridge.js` (search, folding, etc.), revisit JS unit
tests with Node + `jsdom`. If the manual UI checklist starts catching real
bugs again, consider Appium-WinAppDriver for a small "open file, render,
take screenshot" smoke test — but only if cost/benefit shifts.
