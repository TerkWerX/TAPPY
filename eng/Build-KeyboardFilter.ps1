[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipRestore) {
    & (Join-Path $PSScriptRoot 'Restore-DriverDependencies.ps1')
}

$toolchain = & (Join-Path $PSScriptRoot 'Test-DriverToolchain.ps1') -Require
$msbuildPath = Join-Path $toolchain.VisualStudioPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuildPath)) {
    throw "MSBuild was not found at $msbuildPath."
}

$projectPath = Join-Path $repositoryRoot 'driver\Tappy.KeyboardFilter\Tappy.KeyboardFilter.vcxproj'
& $msbuildPath $projectPath `
    /m `
    /v:minimal `
    /p:Configuration=$Configuration `
    /p:Platform=x64 `
    /p:SignMode=Off

if ($LASTEXITCODE -ne 0) {
    throw "Tappy keyboard filter build failed with exit code $LASTEXITCODE."
}

$outputDirectory = Join-Path $repositoryRoot "artifacts\driver\$Configuration\x64"
Get-ChildItem -LiteralPath $outputDirectory -File |
    Select-Object Name, Length, LastWriteTimeUtc
