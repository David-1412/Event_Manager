# Builds the API and web images, pointing the API build at the host NuGet folder
# so a blocked or flaky package registry costs as little as possible.
#
#   powershell -File scripts/build.ps1              # both images
#   powershell -File scripts/build.ps1 api          # one service
#
# Compose alone (`docker compose build`) also works: backend/Dockerfile restores
# through a BuildKit package cache that survives between builds. This script adds
# two things on top:
#   1. NUGET_PACKAGES_PATH, which docker-compose.yml passes to backend/Dockerfile
#      as the `offline` build context, giving restore the host's own packages as
#      an extra source before it goes to the network.
#   2. A refreshed corp-ca.pem when a TLS-intercepting proxy root is in the
#      certificate store, which is what prevents NU1301 during the restore that
#      has to reach api.nuget.org.
param(
    [Parameter(Position = 0)]
    [string] $Service = ""
)

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
if (-not (Test-Path $cache)) {
    # Not fatal: the BuildKit package cache and the registry can still carry the
    # build. Warn rather than fail so a machine with a clean host cache and a
    # reachable registry still builds.
    Write-Warning "No NuGet cache at '$cache'; the build will depend entirely on the registry."
    $env:NUGET_PACKAGES_PATH = ""
} else {
    $env:NUGET_PACKAGES_PATH = $cache
}

# The offline source is what lets a build finish with the registry unreachable,
# and it can only be created while the registry is reachable. Build it from the
# host cache now, while that is still possible, so the next cold build has it:
# a machine that has restored once never needs nuget.org again.
$sources = if ($env:NUGET_SOURCES_PATH) { $env:NUGET_SOURCES_PATH } else { Join-Path (Get-Location) ".nuget-sources" }
if (-not (Test-Path (Join-Path $sources "*.nupkg"))) {
    if (Test-Path $cache) {
        Write-Host "No offline NuGet source yet at '$sources'; creating it from the host cache."
        & (Join-Path $PSScriptRoot "restore-cache.ps1")
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
if (Test-Path (Join-Path $sources "*.nupkg")) {
    # Preferred over the raw host cache: a flat source folder resolves reliably,
    # whereas global-packages is a fallback folder rather than a source.
    $env:NUGET_PACKAGES_PATH = $sources
}

# A stale corp CA silently reintroduces NU1301, so refresh it when a proxy root
# is present in the store.
$proxy = Get-ChildItem Cert:\LocalMachine\Root -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -like "*Zscaler*" -or $_.Subject -like "*Websense*" -or $_.Subject -like "*Forcepoint*" }
if ($proxy) {
    & (Join-Path $PSScriptRoot "export-corp-ca.ps1") | Out-Null
    Write-Host "Refreshed corp-ca.pem from $($proxy.Count) store certificate(s)."
}

if ($Service) {
    docker compose build $Service
} else {
    docker compose build
}
exit $LASTEXITCODE
