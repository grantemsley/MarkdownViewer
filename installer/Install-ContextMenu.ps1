# Install-ContextMenu.ps1
# Registers per-user Explorer right-click verbs to open a folder in
# MarkdownViewer. No admin required. On Windows 11 with the modern context
# menu, these entries appear under "Show more options".
#
# Usage:
#   .\Install-ContextMenu.ps1 -ExePath 'C:\Path\To\MarkdownViewer.exe'
#   .\Install-ContextMenu.ps1 -ExePath '...' -AssociateMd   # also adopt .md files

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $ExePath,
    [switch] $AssociateMd
)

if (-not (Test-Path -LiteralPath $ExePath)) {
    Write-Error "Exe not found: $ExePath"
    exit 1
}
$exe = (Resolve-Path -LiteralPath $ExePath).Path

function Set-Verb($rootPath, $label, $command, $icon) {
    if (-not (Test-Path -LiteralPath $rootPath)) {
        New-Item -Path $rootPath -Force | Out-Null
    }
    Set-ItemProperty -Path $rootPath -Name '(default)' -Value $label
    if ($icon) {
        Set-ItemProperty -Path $rootPath -Name 'Icon' -Value $icon
    }
    $cmdKey = Join-Path $rootPath 'command'
    if (-not (Test-Path -LiteralPath $cmdKey)) {
        New-Item -Path $cmdKey -Force | Out-Null
    }
    Set-ItemProperty -Path $cmdKey -Name '(default)' -Value $command
}

$verb = 'Open in MarkdownViewer'

# Folder right-click
Set-Verb -rootPath "HKCU:\Software\Classes\Directory\shell\$verb" `
         -label $verb `
         -command ('"{0}" "%V"' -f $exe) `
         -icon $exe

# Inside-folder empty-space right-click ("background")
Set-Verb -rootPath "HKCU:\Software\Classes\Directory\Background\shell\$verb" `
         -label $verb `
         -command ('"{0}" "%V"' -f $exe) `
         -icon $exe

if ($AssociateMd) {
    # File-association for .md (per-user only). Sets MarkdownViewer as a
    # candidate "Open with" entry without forcibly hijacking the default.
    $progId = 'MarkdownViewer.Document'
    Set-Verb -rootPath "HKCU:\Software\Classes\$progId\shell\open" `
             -label 'Open' `
             -command ('"{0}" "%1"' -f $exe) `
             -icon $exe

    $extKey = 'HKCU:\Software\Classes\.md\OpenWithProgids'
    if (-not (Test-Path -LiteralPath $extKey)) {
        New-Item -Path $extKey -Force | Out-Null
    }
    Set-ItemProperty -Path $extKey -Name $progId -Value '' -Type String
}

Write-Output "Context menu installed."
Write-Output "Exe: $exe"
if ($AssociateMd) { Write-Output "Also added .md to Open With options." }
