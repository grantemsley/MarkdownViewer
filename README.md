# MarkdownViewer

A minimal Windows markdown viewer. Open a folder, read its `.md` files.

## Build

Requires .NET 10 SDK and the WebView2 Runtime (preinstalled on Windows 11).

```powershell
dotnet build src\MarkdownViewer.csproj -c Release
```

## Test

xUnit unit tests covering markdown rendering, file routing, encoding
detection, vault tree building, sidebar filtering, and outline collapse.

```powershell
.\test.ps1
```

## Publish

Framework-dependent single-file (small exe; needs .NET 10 Desktop Runtime on
the target machine):

```powershell
dotnet publish src\MarkdownViewer.csproj -c Release -r win-x64 `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --self-contained false `
    -o publish
```

Self-contained (bundles the runtime; ~100MB exe):

```powershell
dotnet publish src\MarkdownViewer.csproj -c Release -r win-x64 `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --self-contained true `
    -o publish
```

## Run

```powershell
.\MarkdownViewer.exe                    # reopens last folder + file
.\MarkdownViewer.exe C:\Notes           # open folder
.\MarkdownViewer.exe C:\Notes\foo.md    # open folder, select file
```

## Explorer right-click menu

```powershell
.\installer\Install-ContextMenu.ps1 -ExePath '<full path to MarkdownViewer.exe>'
# Optional: also offer MarkdownViewer in the Open With list for .md files
.\installer\Install-ContextMenu.ps1 -ExePath '...' -AssociateMd
```

To remove:

```powershell
.\installer\Uninstall-ContextMenu.ps1
```

## Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+O` | Open folder |
| `Ctrl+F` | Find in page |
| `Ctrl+,` | Preferences |
| `Ctrl+B` | Toggle sidebar |
| `Ctrl+1` / `Ctrl+2` | Switch sidebar tab |
| `Ctrl+R` / `F5` | Reload current file |
| `Esc` | Close find / popups |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` | Font size |
