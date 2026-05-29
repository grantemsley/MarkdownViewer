# Uninstall-ContextMenu.ps1
# Removes per-user Explorer verbs and file-association entries created by
# Install-ContextMenu.ps1.

[CmdletBinding()]
param()

$verb = 'Open in MarkdownViewer'
$paths = @(
    "HKCU:\Software\Classes\Directory\shell\$verb",
    "HKCU:\Software\Classes\Directory\Background\shell\$verb",
    "HKCU:\Software\Classes\MarkdownViewer.Document"
)
foreach ($p in $paths) {
    if (Test-Path -LiteralPath $p) {
        Remove-Item -LiteralPath $p -Recurse -Force
        Write-Output "Removed: $p"
    }
}

# Drop our progId from .md's OpenWithProgids list.
$extKey = 'HKCU:\Software\Classes\.md\OpenWithProgids'
if (Test-Path -LiteralPath $extKey) {
    Remove-ItemProperty -LiteralPath $extKey -Name 'MarkdownViewer.Document' -ErrorAction SilentlyContinue
    Write-Output "Removed .md OpenWith association."
}
Write-Output "Done."
