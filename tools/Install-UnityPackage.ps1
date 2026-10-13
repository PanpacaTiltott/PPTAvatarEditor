param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'
$packageName = 'net.panpacatiltott.pptavatareditor'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $repository ('Packages\' + $packageName)
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt'))) {
    throw 'The target must be a Unity project.'
}
$destination = Join-Path $project ('Packages\' + $packageName)
if ([IO.Path]::GetFullPath($source) -eq [IO.Path]::GetFullPath($destination)) {
    throw 'Choose the avatar project, not the source repository.'
}
if (Test-Path -LiteralPath $destination) {
    $installed = Get-Content -LiteralPath (Join-Path $destination 'package.json') -Raw | ConvertFrom-Json
    if ($installed.name -ne $packageName) { throw 'The installed package identity does not match.' }
}

$backup = $null
if (Test-Path -LiteralPath $destination) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $backup = Join-Path (Split-Path $project -Parent) ('PPAE-backups\' + (Split-Path $project -Leaf) + '\' + $stamp)
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup -Recurse
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
}

[PSCustomObject]@{ source = $source; destination = $destination; backup = $backup } | ConvertTo-Json -Compress
