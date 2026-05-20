#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs and (optionally) pushes Zakira.Conduit NuGet packages to nuget.org.

.DESCRIPTION
    Builds and packs every packable project in Zakira.Conduit.slnx in the
    requested configuration (default: Release), placing the resulting
    .nupkg / .snupkg files into the output directory (default: ./artifacts).

    When -Push is specified, every produced .nupkg is uploaded to
    https://api.nuget.org/v3/index.json. The matching .snupkg (symbol package)
    is auto-pushed by `dotnet nuget push` when present alongside the .nupkg.

    The API key is taken from -ApiKey first, and from the NUGET_API_KEY
    environment variable when -ApiKey is omitted. The key is never printed.

.PARAMETER ApiKey
    NuGet.org API key. Falls back to $env:NUGET_API_KEY when not provided.
    Only required when -Push is set.

.PARAMETER Push
    Push every produced package to nuget.org after a successful pack.

.PARAMETER Configuration
    MSBuild configuration. Default: Release.

.PARAMETER Output
    Output directory for the produced packages, relative to the script root.
    Default: artifacts.

.PARAMETER Source
    NuGet source URL to push to. Default: https://api.nuget.org/v3/index.json.

.PARAMETER SkipDuplicate
    Pass --skip-duplicate to `dotnet nuget push`. Default: $true (re-runs do
    not fail when the version is already on nuget.org).

.EXAMPLE
    ./pack.ps1
    Packs the solution into ./artifacts. Does not push.

.EXAMPLE
    ./pack.ps1 -Push -ApiKey oy2abc...
    Packs and pushes to nuget.org.

.EXAMPLE
    $env:NUGET_API_KEY = 'oy2abc...'
    ./pack.ps1 -Push
    Same as above, but takes the key from the environment.
#>
[CmdletBinding()]
param(
    [string]$ApiKey,
    [switch]$Push,
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts',
    [string]$Source = 'https://api.nuget.org/v3/index.json',
    [bool]$SkipDuplicate = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# -----------------------------------------------------------------------------
# 0) Resolve paths and (when pushing) the API key.
# -----------------------------------------------------------------------------
$Solution  = Join-Path $PSScriptRoot 'Zakira.Conduit.slnx'
$OutputDir = Join-Path $PSScriptRoot $Output

if (-not (Test-Path -LiteralPath $Solution)) {
    throw "Solution file not found: '$Solution'."
}

if ($Push) {
    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        $ApiKey = $env:NUGET_API_KEY
    }
    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        throw "Cannot push: provide -ApiKey or set the NUGET_API_KEY environment variable."
    }
}

# -----------------------------------------------------------------------------
# 1) Clean the output directory so we ship exactly what we just packed.
# -----------------------------------------------------------------------------
if (Test-Path -LiteralPath $OutputDir) {
    Write-Host "Cleaning $OutputDir"
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# -----------------------------------------------------------------------------
# 2) Pack.
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host ("Packing '{0}' ({1}) -> '{2}'" -f $Solution, $Configuration, $OutputDir)
& dotnet pack $Solution -c $Configuration -o $OutputDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed (exit $LASTEXITCODE)."
}

$packages = @(Get-ChildItem -LiteralPath $OutputDir -Filter '*.nupkg' -File | Sort-Object Name)
if ($packages.Count -eq 0) {
    throw "No .nupkg files were produced in '$OutputDir'."
}

Write-Host ""
Write-Host "Produced packages:"
foreach ($pkg in $packages) {
    Write-Host ("  - {0,-50}  {1,10:N0} bytes" -f $pkg.Name, $pkg.Length)
}

# -----------------------------------------------------------------------------
# 3) Push (when requested).
# -----------------------------------------------------------------------------
if (-not $Push) {
    Write-Host ""
    Write-Host "Skipping push (re-run with -Push to upload to nuget.org)."
    return
}

Write-Host ""
Write-Host ("Pushing {0} package(s) to {1}" -f $packages.Count, $Source)

foreach ($pkg in $packages) {
    Write-Host ""
    Write-Host ("  -> {0}" -f $pkg.Name)

    $pushArgs = @(
        'nuget', 'push', $pkg.FullName,
        '--source', $Source,
        '--api-key', $ApiKey
    )
    if ($SkipDuplicate) { $pushArgs += '--skip-duplicate' }

    & dotnet @pushArgs
    if ($LASTEXITCODE -ne 0) {
        throw ("dotnet nuget push failed for {0} (exit {1})." -f $pkg.Name, $LASTEXITCODE)
    }
}

Write-Host ""
Write-Host "Done."
