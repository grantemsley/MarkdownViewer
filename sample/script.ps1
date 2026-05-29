# A sample PowerShell script — used to test the text viewer + syntax highlight.

param(
    [string] $Path = ".",
    [switch] $Recurse
)

function Get-MarkdownFiles {
    param([string] $Root, [bool] $Recurse)
    if ($Recurse) {
        Get-ChildItem -Path $Root -Filter *.md -Recurse -File
    } else {
        Get-ChildItem -Path $Root -Filter *.md -File
    }
}

Get-MarkdownFiles -Root $Path -Recurse:$Recurse |
    ForEach-Object {
        [PSCustomObject]@{
            Name  = $_.Name
            Size  = $_.Length
            LastWrite = $_.LastWriteTime
        }
    } |
    Sort-Object LastWrite -Descending |
    Format-Table -AutoSize
