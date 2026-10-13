param(
    [Parameter(Mandatory = $true)]
    [string]$OriginalPackagePath
)

$ErrorActionPreference = 'Stop'
$package = Join-Path (Split-Path $PSScriptRoot -Parent) 'Packages\net.panpacatiltott.pptavatareditor'
$original = (Resolve-Path -LiteralPath $OriginalPackagePath).Path

function Read-AssetGuids([string]$Root) {
    Get-ChildItem -LiteralPath $Root -File -Recurse -Filter '*.meta' | ForEach-Object {
        $match = [regex]::Match([IO.File]::ReadAllText($_.FullName), '(?m)^guid: ([a-f0-9]{32})')
        if ($match.Success) { $match.Groups[1].Value }
    }
}

$ownGuids = @(Read-AssetGuids $package)
$originalGuids = @(Read-AssetGuids $original)
if (@($ownGuids | Group-Object | Where-Object Count -gt 1).Count) { throw 'Duplicate PPAE asset GUID.' }
if (@($ownGuids | Where-Object { $_ -in $originalGuids }).Count) { throw 'PPAE GUID conflicts with the original package.' }

$manifest = Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json
if ($manifest.name -ne 'net.panpacatiltott.pptavatareditor') { throw 'Incorrect PPAE package identity.' }
if (@($manifest.legacyFolders.PSObject.Properties).Count -ne 0 -or @($manifest.legacyFiles.PSObject.Properties).Count -ne 0) {
    throw 'PPAE must not claim or remove legacy files from the original tool.'
}

Get-ChildItem -LiteralPath $package -File -Recurse -Filter '*.cs' | ForEach-Object {
    $source = [IO.File]::ReadAllText($_.FullName)
    if ($source -notmatch 'namespace PPTAvatarEditor' -or $source -match '\b(using|namespace) VRCAvatarEditor\b') {
        throw "Unisolated C# namespace: $($_.FullName)"
    }
    if ($source -match 'Resources\.Load(?:<[^>]+>)?\(\$?"(?!PPAE/)') {
        throw "Unisolated Resources path: $($_.FullName)"
    }
}

$originalAssemblies = @(Get-ChildItem -LiteralPath $original -File -Recurse -Filter '*.asmdef' | ForEach-Object {
    (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).name
})
Get-ChildItem -LiteralPath $package -File -Recurse -Filter '*.asmdef' | ForEach-Object {
    $definition = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    if ($definition.name -in $originalAssemblies -or @($definition.references | Where-Object { $_ -in $originalAssemblies }).Count) {
        throw "PPAE assembly is not independent: $($_.FullName)"
    }
}

[PSCustomObject]@{ package = $manifest.name; assetGuids = $ownGuids.Count; conflicts = 0; status = 'Passed' } | ConvertTo-Json
