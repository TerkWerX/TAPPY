[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolDirectory = Join-Path $repositoryRoot '.tmp\tools'
$nugetPath = Join-Path $toolDirectory 'nuget.exe'

if (-not (Test-Path -LiteralPath $nugetPath)) {
    New-Item -ItemType Directory -Force -Path $toolDirectory | Out-Null
    Invoke-WebRequest -UseBasicParsing `
        'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' `
        -OutFile $nugetPath
}

$signature = Get-AuthenticodeSignature -LiteralPath $nugetPath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw "Refusing to run NuGet CLI because its Microsoft Authenticode signature is not valid: $($signature.Status)"
}

& $nugetPath restore (Join-Path $repositoryRoot 'driver\packages.config') `
    -PackagesDirectory (Join-Path $repositoryRoot 'driver\packages') `
    -NonInteractive `
    -DirectDownload `
    -Source 'https://api.nuget.org/v3/index.json'

if ($LASTEXITCODE -ne 0) {
    throw "NuGet driver dependency restore failed with exit code $LASTEXITCODE."
}

$expectedHashes = @{}
Get-Content -LiteralPath (Join-Path $repositoryRoot 'driver\dependencies.lock') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object {
        if ($_ -notmatch '^([A-F0-9]{64})\s{2}(.+\.nupkg)$') {
            throw "Invalid driver dependency hash record: $_"
        }
        $expectedHashes[$Matches[2]] = $Matches[1]
    }

$restoredPackages = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'driver\packages') `
    -Filter '*.nupkg' -File -Recurse)
if ($restoredPackages.Count -ne $expectedHashes.Count) {
    throw "Expected $($expectedHashes.Count) pinned driver packages, found $($restoredPackages.Count)."
}

foreach ($package in $restoredPackages) {
    $expectedHash = $expectedHashes[$package.Name]
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash
    if (-not $expectedHash -or $actualHash -ne $expectedHash) {
        throw "Driver dependency hash mismatch: $($package.Name)"
    }

    & $nugetPath verify $package.FullName -Signatures -Verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet signature verification failed for $($package.Name)."
    }
}

& (Join-Path $PSScriptRoot 'Test-DriverToolchain.ps1') -Require
