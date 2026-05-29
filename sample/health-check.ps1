# Sample PowerShell script — health check for a small fleet of servers.
# Demonstrates the text viewer with PowerShell syntax highlighting on a
# meatier example than script.ps1.

[CmdletBinding()]
param(
    [string[]] $Hosts = @('dc01', 'dc02', 'fileserver', 'web01'),
    [int]      $TimeoutSec = 5,
    [switch]   $IncludeDisk
)

function Test-HostUp {
    param([string] $HostName, [int] $Timeout)

    $ping = Test-Connection -ComputerName $HostName -Count 1 -Quiet `
        -ErrorAction SilentlyContinue
    if (-not $ping) { return [pscustomobject]@{ HostName = $HostName; Up = $false } }

    $svc = $null
    try {
        $svc = Invoke-Command -ComputerName $HostName -ScriptBlock {
            Get-Service | Where-Object Status -eq 'Stopped' |
                Where-Object StartType -eq 'Automatic' |
                Select-Object -ExpandProperty Name
        } -ErrorAction Stop
    } catch {
        return [pscustomobject]@{
            HostName  = $HostName
            Up        = $true
            Error     = $_.Exception.Message
        }
    }

    [pscustomobject]@{
        HostName       = $HostName
        Up             = $true
        StoppedAutoSvc = $svc.Count
        Services       = ($svc -join ', ')
    }
}

function Get-DiskFree {
    param([string] $HostName)
    Invoke-Command -ComputerName $HostName -ScriptBlock {
        Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' |
            ForEach-Object {
                [pscustomobject]@{
                    Drive   = $_.DeviceID
                    FreeGB  = [math]::Round($_.FreeSpace / 1GB, 1)
                    SizeGB  = [math]::Round($_.Size / 1GB, 1)
                    PctFree = [math]::Round($_.FreeSpace / $_.Size * 100, 1)
                }
            }
    } -ErrorAction SilentlyContinue
}

$results = foreach ($h in $Hosts) {
    Write-Progress -Activity 'Health check' -Status $h
    $status = Test-HostUp -HostName $h -Timeout $TimeoutSec
    if ($IncludeDisk -and $status.Up) {
        $status | Add-Member -NotePropertyName Disks `
            -NotePropertyValue (Get-DiskFree -HostName $h)
    }
    $status
}

$results | Format-Table -AutoSize

# Exit non-zero if anything was down — useful in scheduled-task runs.
$down = @($results | Where-Object { -not $_.Up })
if ($down.Count -gt 0) { exit 1 } else { exit 0 }
