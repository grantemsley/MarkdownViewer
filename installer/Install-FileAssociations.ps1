# Install-FileAssociations.ps1
# Registers MarkdownViewer as a per-user (HKCU) handler for .md and .jsonl
# files. No admin required. This ADDS MarkdownViewer to the "Open with" list
# and registers a ProgId with the app icon; it does NOT seize the current
# default handler (Win10/11 guard that with a UserChoice hash). After running,
# pick MarkdownViewer via right-click -> Open with -> Choose another app, and
# tick "Always" if you want it as the default.
#
# Usage:
#   .\Install-FileAssociations.ps1                       # auto-detect exe
#   .\Install-FileAssociations.ps1 -ExePath 'C:\...\MarkdownViewer.exe'
#   .\Install-FileAssociations.ps1 -Extensions .md,.markdown,.jsonl

[CmdletBinding()]
param(
    [string]   $ExePath,
    [string[]] $Extensions = @('.md', '.jsonl')
)

$ErrorActionPreference = 'Stop'

# Resolve the exe: explicit param, else common build/publish locations.
if (-not $ExePath) {
    $candidates = @(
        (Join-Path $PSScriptRoot '..\publish\MarkdownViewer.exe'),
        (Join-Path $PSScriptRoot '..\src\bin\Release\net10.0-windows\MarkdownViewer.exe'),
        (Join-Path $PSScriptRoot '..\src\bin\Debug\net10.0-windows\MarkdownViewer.exe')
    )
    $ExePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath)) {
    Write-Error "MarkdownViewer.exe not found. Pass -ExePath 'C:\path\to\MarkdownViewer.exe'."
    exit 1
}
$exe = (Resolve-Path -LiteralPath $ExePath).Path

$progId = 'MarkdownViewer.Document'
$friendly = 'Markdown / transcript document'

# ProgId: friendly name, icon, and the open command.
$progRoot = "HKCU:\Software\Classes\$progId"
New-Item -Path $progRoot -Force | Out-Null
Set-ItemProperty -Path $progRoot -Name '(default)'         -Value $friendly
Set-ItemProperty -Path $progRoot -Name 'FriendlyTypeName'  -Value $friendly

$iconKey = Join-Path $progRoot 'DefaultIcon'
New-Item -Path $iconKey -Force | Out-Null
Set-ItemProperty -Path $iconKey -Name '(default)' -Value ('"{0}",0' -f $exe)

$cmdKey = Join-Path $progRoot 'shell\open\command'
New-Item -Path $cmdKey -Force | Out-Null
Set-ItemProperty -Path $cmdKey -Name '(default)' -Value ('"{0}" "%1"' -f $exe)

# Offer the ProgId under each extension's Open-With list (no default hijack).
foreach ($ext in $Extensions) {
    if (-not $ext.StartsWith('.')) { $ext = '.' + $ext }
    $owp = "HKCU:\Software\Classes\$ext\OpenWithProgids"
    New-Item -Path $owp -Force | Out-Null
    Set-ItemProperty -Path $owp -Name $progId -Value '' -Type String
}

# Tell Explorer associations changed so the icons/menus refresh now.
Add-Type -Namespace Mdv -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr a, System.IntPtr b);
'@
[Mdv.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)  # SHCNE_ASSOCCHANGED

Write-Output "Registered ProgId '$progId' -> $exe"
Write-Output ("Added to Open With for: {0}" -f ($Extensions -join ', '))
Write-Output "Set as default via: right-click a file -> Open with -> Choose another app -> Always."
