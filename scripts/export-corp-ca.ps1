# Exports the corporate root/intermediate CA certificates that the Windows
# certificate store trusts into ./corp-ca.pem, which docker-compose.yml passes to
# both image builds as the `corp-ca` build secret.
#
# Needed only on networks that TLS-intercept (Zscaler and similar): they present
# a certificate signed by their own CA, no base image trusts it, and `dotnet
# restore` dies with NU1301 / `npm ci` hangs. On any other network the secret is
# mounted but ignored by the Dockerfiles, and an empty corp-ca.pem is fine.
#
# The chain already in the store is what makes container builds work on THIS
# machine, so re-running this after a store refresh is the only step required.
#
#   powershell -File scripts/export-corp-ca.ps1 [-Issuer Zscaler] [-OutPath corp-ca.pem]
#
# The file contains public certificates only — no private keys — so it is safe
# to commit. It is passed as a build secret anyway so it never lands in a layer.
param(
    # Substring matched against Subject and Issuer, case-insensitive. The
    # default targets the proxy in use here; pass -Issuer to retarget.
    [string] $Issuer = "Zscaler",
    [string] $OutPath = "corp-ca.pem"
)

$ErrorActionPreference = "Stop"

$stores = @(
    "Cert:\LocalMachine\Root",
    "Cert:\LocalMachine\CA",
    "Cert:\CurrentUser\Root",
    "Cert:\CurrentUser\CA"
) | Where-Object { Test-Path $_ }

# Deduplicate by thumbprint: the same root is typically present in several
# stores, and a duplicated block in the bundle is harmless but noisy.
$seen = [System.Collections.Generic.HashSet[string]]::new()
$pems = foreach ($store in $stores) {
    Get-ChildItem $store -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -like "*$Issuer*" -or $_.Issuer -like "*$Issuer*" } |
        Where-Object { $seen.Add($_.Thumbprint) } |
        ForEach-Object {
            "-----BEGIN CERTIFICATE-----`n" +
            [System.Convert]::ToBase64String($_.RawData, [System.Base64FormattingOptions]::InsertLineBreaks) +
            "`n-----END CERTIFICATE-----"
        }
}

if (-not $pems) {
    # An empty bundle is a valid "no extra CA" answer, not an error: the build
    # mounts it and the Dockerfile conditionals skip it.
    Write-Warning "No certificates matching '$Issuer' found; writing an empty $OutPath."
}

# ASCII keeps the line endings and the PEM header bytes plain — no BOM, which
# OpenSSL's parser rejects.
$pems -join "`n" | Set-Content -Path $OutPath -Encoding ascii

Write-Host ("Wrote {0} certificate(s) to {1}" -f @($pems).Count, (Resolve-Path $OutPath))
