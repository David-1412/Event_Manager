# Makes the locked NuGet graph available locally, so the container build never
# has to depend on a reachable package registry.
#
#   powershell -File scripts/restore-cache.ps1
#
# It does two things:
#   1. `dotnet restore` on the host, which fills the host global-packages folder
#      (nuget.org, or whatever source the machine's NuGet config points at).
#   2. Mirrors that folder into a FLAT NuGet v2 local source — `id.version.nupkg`
#      next to `id.version.nupkg.sha512` — at .nuget-sources (override with
#      $env:NUGET_SOURCES_PATH). docker-compose.yml passes that folder to
#      backend/Dockerfile as the `offline` build context.
#
# The mirror is flat rather than the global-packages layout for two reasons. It is
# what a local *source* has to look like for `--source <folder>` to resolve against
# it (global-packages is a folder of extracted packages plus the nupkg, which NuGet
# reads as a fallback folder, not a source), and it is a fraction of the size: only
# the .nupkg files cross the wire to the build daemon instead of every extracted
# package tree. Files are hard-linked, so a populated mirror costs no extra disk.
#
# Run this once per machine, and again after editing any PackageReference (the
# lock files pin exact versions, so a new dependency means a new .nupkg).

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$hostCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
$sources = if ($env:NUGET_SOURCES_PATH) { $env:NUGET_SOURCES_PATH } else { Join-Path (Get-Location) ".nuget-sources" }

Write-Host "Restoring on the host into '$hostCache'..."
dotnet restore backend/src/SportMeet.Api/SportMeet.Api.csproj
if ($LASTEXITCODE -ne 0) {
    Write-Error "Host restore failed. The offline source cannot be built without it; fix the host restore (check 'dotnet nuget list source') and re-run."
}
dotnet restore backend/prober/prober.csproj | Out-Null

if (-not (Test-Path $hostCache)) {
    Write-Error "Host restore reported success but '$hostCache' does not exist."
}

# The container build restores in locked mode, so the mirror has to hold every
# package the lock files name. Anything missing means this machine's cache was
# built against a different graph (a redirected NUGET_PACKAGES, or a partial
# restore), and a mirror with holes is worse than no mirror: the build would fail
# with NU1101 naming a package instead of falling back to the registry cleanly.
# In a lock file the package id is the property name and `resolved` is its version;
# this script keys entries as "id/version", the form a cache path is built from.
$required = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($lock in Get-ChildItem backend -Recurse -Filter packages.lock.json) {
    $graph = Get-Content $lock.FullName -Raw | ConvertFrom-Json
    foreach ($framework in $graph.dependencies.PSObject.Properties) {
        foreach ($entry in $framework.Value.PSObject.Properties) {
            # An entry without `resolved` is this solution's own project reference,
            # which has no .nupkg and needs no mirror.
            if ($entry.Value.resolved) {
                [void]$required.Add("$($entry.Name)/$($entry.Value.resolved)")
            }
        }
    }
}

New-Item -ItemType Directory -Path $sources -Force | Out-Null

# The flat name "<id>.<version>.nupkg" cannot be parsed back reliably: ids and
# versions are both dotted, so Serilog.AspNetCore.8.0.0 could be id "Serilog" at
# version "AspNetCore.8.0.0", and NuGet normalises dots in the version to '-' on
# the way in. Rather than guess, record what was placed. The manifest is what makes
# the prune below exact, and it is ignored by NuGet, which only reads *.nupkg here.
# Read back as a list. PowerShell member-enumeration and ConvertTo-Json both
# flatten a one-element array, and wrapping the parsed result in @() leaves a
# multi-element JSON array as a single nested object, so the shape is normalised
# explicitly here. Any entry that is not exactly one spec and one file name is
# dropped: a half-populated entry would resolve its path to the source folder
# itself, and the prune below deletes paths.
function Get-ManifestField {
    param($Value)
    $items = @($Value)
    if ($items.Count -ne 1) { return $null }
    $text = [string]$items[0]
    if (-not $text) { return $null }
    return $text
}

function Get-ManifestEntries {
    param([string]$Path)
    $parsed = @(Get-Content $Path -Raw | ConvertFrom-Json)
    # @() keeps one JSON object as one element but wraps N objects as a nested
    # array; index into it so both shapes become a flat sequence.
    if ($parsed.Count -eq 1) { $parsed = @($parsed[0]) }
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($item in $parsed) {
        $spec = Get-ManifestField $item.spec
        $file = Get-ManifestField $item.file
        if (-not $spec -or -not $file) { continue }
        $entries.Add([pscustomobject]@{ spec = $spec; file = $file })
    }
    return $entries
}

$manifestPath = Join-Path $sources "sportmeet-lock-manifest.json"
$manifest = if (Test-Path $manifestPath) { Get-ManifestEntries $manifestPath } else { [System.Collections.Generic.List[object]]::new() }

# Anything this script placed that the lock files no longer name gets removed.
# Only manifest entries are candidates: the mirror holds hard links into the host
# cache, so a blind wipe could reach the cache itself if a run ever points
# $sources at it. Files added by hand stay exactly where they are.
$stale = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $manifest) {
    if ($required.Contains($entry.spec)) { continue }
    # Belt and braces: never hand Remove-Item anything but a plain file inside the
    # mirror, whatever the manifest claims.
    if ($entry.file -ne (Split-Path -Path $entry.file -Leaf)) { continue }
    $path = Join-Path $sources $entry.file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path -LiteralPath $path)) { $stale.Add($entry.spec) }
}
$keptManifest = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $manifest) { if ($required.Contains($entry.spec)) { $keptManifest.Add($entry) } }

$manifestDirty = $stale.Count -gt 0
$unresolved = [System.Collections.Generic.List[string]]::new()
$copied = 0
foreach ($spec in ($required | Sort-Object)) {
    # Lock entries are "id/version"; the cache folder layout is "<id>/<id>.<version>".
    $parts = $spec.Split('/')
    $id = $parts[0]
    $version = $parts[1]
    $nupkg = Join-Path (Join-Path (Join-Path $hostCache $id) $version) "$id.$version.nupkg"
    if (-not (Test-Path $nupkg)) { $unresolved.Add($spec); continue }
    $file = Split-Path $nupkg -Leaf
    $target = Join-Path $sources $file
    if (-not (Test-Path $target)) {
        # Hard link: no extra disk, and the mirror stays byte-identical to the cache.
        # Falls back to a copy across volumes, where linking is refused.
        try {
            New-Item -ItemType HardLink -Path $target -Target $nupkg -ErrorAction Stop | Out-Null
        } catch {
            Copy-Item $nupkg $target -Force
        }
        $copied++
    }
    if (-not ($keptManifest.spec -contains $spec)) {
        $keptManifest.Add([pscustomobject]@{ spec = $spec; file = $file })
        $manifestDirty = $true
    }
}
if ($manifestDirty) {
    # ConvertTo-Json unwinds a single-element array into a bare object, which the
    # reader above would have to guess at; wrap it in an explicit array so the file
    # on disk is always a JSON array.
    $json = ConvertTo-Json @($keptManifest | Sort-Object spec) -Depth 3
    if ($json.TrimStart().StartsWith('{')) { $json = "[$json]" }
    Set-Content -Path $manifestPath -Value $json -Encoding ascii
}

if ($unresolved.Count) {
    Write-Warning ("{0} locked package(s) have no .nupkg under '{1}', so the mirror cannot cover them:" -f $unresolved.Count, $hostCache)
    $unresolved | ForEach-Object { Write-Warning "  $_" }
    Write-Warning "The container build will fetch these from the registry. Point NUGET_PACKAGES at a complete cache, or keep the registry reachable."
}

$available = @($required | Where-Object {
    $p = $_.Split('/')
    Test-Path -LiteralPath (Join-Path $sources "$($p[0]).$($p[1]).nupkg")
})
$size = (Get-ChildItem $sources -Filter *.nupkg | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Offline NuGet source at '{0}': {1} of {2} locked package(s) available ({3} added, {4} pruned), {5:N1} MB." -f `
    $sources, $available.Count, $required.Count, $copied, $stale.Count, $size)
if ($unresolved.Count) {
    Write-Warning "The mirror is incomplete; those packages will come from the registry during the container build."
} else {
    Write-Host "Locked graph fully covered: docker compose build now restores without contacting a registry."
}
