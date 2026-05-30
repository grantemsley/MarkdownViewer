@echo off
REM ---------------------------------------------------------------------------
REM Build MarkdownViewer as a single, framework-dependent .exe.
REM WebAssets are embedded and the .pdb is suppressed, so the output is exactly
REM one file: publish\MarkdownViewer.exe (needs the .NET 10 Desktop Runtime +
REM WebView2 Runtime on the target; both ship with Windows 11).
REM Just run:  build
REM ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

echo Building MarkdownViewer (Release, single file)...
echo.

dotnet publish src\MarkdownViewer.csproj -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false ^
  --self-contained false ^
  -o publish

if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  exit /b 1
)

echo.
echo Done -^> "%~dp0publish\MarkdownViewer.exe"
endlocal
