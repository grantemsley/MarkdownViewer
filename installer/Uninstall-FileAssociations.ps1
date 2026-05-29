# Uninstall-FileAssociations.ps1
# Reverses Install-FileAssociations.ps1: removes the MarkdownViewer ProgId and
# drops it from the per-user Open-With list for the given extensions. HKCU
# only, no admin. Does not touch any default-handler UserChoice you may have
# set yourself (Windows manages those).
#
# Usage:
#   .\Uninstall-FileAssociations.ps1
#   .\Uninstall-FileAssociations.ps1 -Extensions .md,.markdown,.jsonl

[CmdletBinding()]
param(
    [string[]] $Extensions = @('.md', '.jsonl')
)

$ErrorActionPreference = 'Stop'
$progId = 'MarkdownViewer.Document'

# Remove the ProgId tree.
$progRoot = "HKCU:\Software\Classes\$progId"
if (Test-Path -LiteralPath $progRoot) {
    Remove-Item -Path $progRoot -Recurse -Force
    Write-Output "Removed ProgId '$progId'."
}

# Drop the OpenWithProgids reference under each extension.
foreach ($ext in $Extensions) {
    if (-not $ext.StartsWith('.')) { $ext = '.' + $ext }
    $owp = "HKCU:\Software\Classes\$ext\OpenWithProgids"
    if (Test-Path -LiteralPath $owp) {
        Remove-ItemProperty -Path $owp -Name $progId -ErrorAction SilentlyContinue
        Write-Output "Removed $progId from $ext Open-With list."
    }
}

Add-Type -Namespace Mdv -Name ShellU -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr a, System.IntPtr b);
'@
[Mdv.ShellU]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Output "Done."
