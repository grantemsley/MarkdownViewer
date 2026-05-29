$ErrorActionPreference = "Stop"
$sln = Join-Path $PSScriptRoot "MarkdownViewer.sln"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

& $dotnet test $sln --nologo @args
