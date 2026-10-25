<#
.SYNOPSIS
    Copies a folder into a time-stamped zip archive.
#>
param(
    [Parameter(Mandatory)] [string] $Source,
    [string] $Destination = "$HOME\Backups"
)

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$name = Split-Path $Source -Leaf
Compress-Archive -Path $Source -DestinationPath (Join-Path $Destination "$name-$stamp.zip")
