# MarkdownViewer startup smoke test.
# Launches the built exe pointing at the sample folder, gives it a few
# seconds to render the WebView2, then kills it. Fails if the process
# exits on its own before the timeout (early crash) or if a new entry
# appears in %APPDATA%\MarkdownViewer\crash.log.

$ErrorActionPreference = "Stop"

$exe    = Join-Path $PSScriptRoot "src\bin\Debug\net10.0-windows\MarkdownViewer.exe"
$sample = Join-Path $PSScriptRoot "sample"
$crash  = Join-Path $env:APPDATA "MarkdownViewer\crash.log"

if (-not (Test-Path $exe)) {
    Write-Output "FAIL: exe not found at $exe (run a Debug build first)"
    exit 1
}

# Capture pre-state of the crash log so we can detect new failures rather
# than a stale one from a previous run.
$crashSnap = if (Test-Path $crash) { (Get-Item $crash).Length } else { -1 }

$p = Start-Process -FilePath $exe -ArgumentList "`"$sample`"" -PassThru
Start-Sleep -Seconds 5

if ($p.HasExited) {
    Write-Output "FAIL: process exited on its own with code $($p.ExitCode) before timeout"
    if (Test-Path $crash) {
        Write-Output "--- tail of crash.log ---"
        Get-Content $crash -Tail 30
    }
    exit 1
}

$p | Stop-Process -Force
Start-Sleep -Milliseconds 500

$crashSnap2 = if (Test-Path $crash) { (Get-Item $crash).Length } else { -1 }
if ($crashSnap2 -ne $crashSnap) {
    Write-Output "FAIL: new entry in $crash"
    Get-Content $crash -Tail 30
    exit 1
}

Write-Output "PASS: process started and stayed running for 5s, no new crash log entries"
exit 0
