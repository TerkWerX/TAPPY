#requires -Version 7.2

<#
.SYNOPSIS
Builds and audits a local Tappy portable package. It never publishes a release.

.DESCRIPTION
The application readiness contract is intentionally strict. Tappy.exe must accept:

  --readiness-smoke --result <absolute-json-path>

The process must use TAPPY_SMOKE_DATA_ROOT for isolated temporary state and write a
JSON object with schemaVersion=1, product="Tappy", the requested semantic version,
ready=true, injectedInputCount=0, and passing checks named controller-registry,
profile-round-trip, rehearsal-no-output, and tappy-doctor. Readiness mode must not
register input, inject output, open a normal UI, or write inside the payload.
#>

[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $OutputDirectory,

    [switch] $KeepStaging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repoRoot 'Tappy.slnx'
$appProjectPath = Join-Path $repoRoot 'src\Tappy.App\Tappy.App.csproj'
$identityPath = Join-Path $repoRoot 'eng\product-identity.json'
$buildPropsPath = Join-Path $repoRoot 'Directory.Build.props'
$stagingParent = Join-Path $repoRoot 'artifacts\staging'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\packages'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$requiredPayload = @(
    'ControllerPacks/controller_registry.json',
    'ControllerPacks/trusted-publishers.json',
    'Tappy.exe'
)
$requiredSmokeChecks = @(
    'controller-registry',
    'profile-round-trip',
    'rehearsal-no-output',
    'tappy-doctor'
)

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Get-RelativePayloadPath {
    param([string] $Root, [string] $Path)
    return [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function Assert-PathUnderRoot {
    param([string] $Path, [string] $Root, [string] $Purpose)
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose path is outside its allowed root: $fullPath"
    }
}

function Test-TextForSensitiveContent {
    param([string] $Text, [string] $Description)
    $patterns = [ordered]@{
        'PAD IMAGES reference' = '(?i)PAD[\\/ ]+IMAGES'
        'raw HID or USB device path' = '(?i)\\\\\?\\(?:hid|usb)#'
        'absolute Windows user-profile path' = '(?i)\b[A-Z]:\\Users\\[^\\\s]+'
        'private key material' = '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
        'GitHub access token' = '(?i)\bgh[pousr]_[A-Za-z0-9]{20,}\b'
        'AWS access-key identifier' = '\bAKIA[0-9A-Z]{16}\b'
        'probable client secret' = '(?i)\bclient[_-]?secret\s*[:=]'
    }
    foreach ($entry in $patterns.GetEnumerator()) {
        if ($Text -match $entry.Value) {
            throw "$Description contains a forbidden $($entry.Key)."
        }
    }
}

function Assert-ControllerRegistry {
    param([string] $Path)
    $raw = [IO.File]::ReadAllText($Path)
    Test-TextForSensitiveContent -Text $raw -Description 'Controller registry'
    if ($raw -match '(?i)\.(?:png|jpe?g|webp|gif|svg|ico)\b') {
        throw 'The bootstrap controller registry must be code-rendered and cannot reference artwork files.'
    }
    $registry = $raw | ConvertFrom-Json
    if ($registry.schema_version -ne 1 -or $registry.product -ne 'Tappy') {
        throw 'Controller registry identity/schema is invalid.'
    }
    if ($registry.rendering.mode -ne 'code' -or $registry.rendering.external_artwork -ne $false) {
        throw 'Controller registry must declare code rendering with external_artwork=false.'
    }
    if (@($registry.controllers).Count -ne 0) {
        throw 'The bootstrap registry must not claim reviewed hardware-specific controllers.'
    }
    if ($registry.fallback_layout.kind -ne 'generated-grid' -or
        $registry.fallback_layout.control_source -ne 'observed-physical-controls') {
        throw 'Controller registry fallback must generate a grid from observed physical controls.'
    }
}

function Assert-TrustStore {
    param([string] $Path)
    $raw = [IO.File]::ReadAllText($Path)
    Test-TextForSensitiveContent -Text $raw -Description 'Trusted-publisher store'
    $trust = $raw | ConvertFrom-Json
    if ($trust.schema_version -ne 1 -or $trust.product -ne 'Tappy') {
        throw 'Trusted-publisher store identity/schema is invalid.'
    }
    if (@($trust.publishers).Count -ne 0) {
        throw 'The bootstrap Tappy trust store must remain empty until publisher keys are separately approved.'
    }
}

function Assert-PeIdentity {
    param([string] $Path, [string] $ExpectedVersion)
    $stream = [IO.File]::OpenRead($Path)
    try {
        if ($stream.ReadByte() -ne 0x4D -or $stream.ReadByte() -ne 0x5A) {
            throw 'Tappy.exe is not a Windows PE file.'
        }
    }
    finally {
        $stream.Dispose()
    }
    $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($versionInfo.ProductName -ne 'Tappy' -or $versionInfo.CompanyName -ne 'TerkWerX') {
        throw "Tappy.exe product metadata is wrong (Product='$($versionInfo.ProductName)', Company='$($versionInfo.CompanyName)')."
    }
    if (-not $versionInfo.FileVersion.StartsWith("$ExpectedVersion.", [StringComparison]::Ordinal)) {
        throw "Tappy.exe file version '$($versionInfo.FileVersion)' does not match $ExpectedVersion."
    }
}

function Assert-Payload {
    param([string] $Root, [string] $ExpectedVersion)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Payload directory does not exist: $Root"
    }

    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File)
    $linkedDirectories = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -Directory |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($linkedDirectories.Count -gt 0) {
        throw "Payload contains a symbolic-link or reparse-point directory: $($linkedDirectories[0].FullName)"
    }
    $actual = @($files | ForEach-Object { Get-RelativePayloadPath -Root $Root -Path $_.FullName } | Sort-Object)
    $expected = @($requiredPayload | Sort-Object)
    $missing = @($expected | Where-Object { $_ -notin $actual })
    $unexpected = @($actual | Where-Object { $_ -notin $expected })
    if ($missing.Count -gt 0) {
        throw "Published payload is missing required files: $($missing -join ', ')"
    }
    if ($unexpected.Count -gt 0) {
        throw "Published payload contains undeclared files: $($unexpected -join ', ')"
    }

    foreach ($file in $files) {
        $relative = Get-RelativePayloadPath -Root $Root -Path $file.FullName
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Payload contains a symbolic link or reparse point: $relative"
        }
        if ($relative -match '(?i)(^|/)(?:PAD IMAGES|SupportReports|Logs|Backups|Quarantine)(/|$)' -or
            $relative -match '(?i)(?:\.log|\.pfx|\.p12|\.pem|\.key|\.secrets\.json|\.tappy(?:-[a-z]+)?\.json)$') {
            throw "Payload contains a forbidden profile, report, log, artwork, or secret file: $relative"
        }
        if ($file.Extension -in @('.json', '.txt', '.csv', '.xml', '.config')) {
            Test-TextForSensitiveContent -Text ([IO.File]::ReadAllText($file.FullName)) -Description "Payload file '$relative'"
        }
    }

    Assert-ControllerRegistry -Path (Join-Path $Root 'ControllerPacks\controller_registry.json')
    Assert-TrustStore -Path (Join-Path $Root 'ControllerPacks\trusted-publishers.json')
    Assert-PeIdentity -Path (Join-Path $Root 'Tappy.exe') -ExpectedVersion $ExpectedVersion
}

function Invoke-ReadinessSmoke {
    param([string] $PayloadRoot, [string] $ResultPath, [string] $DataRoot, [string] $ExpectedVersion)
    New-Item -ItemType Directory -Path $DataRoot | Out-Null
    $processInfo = [Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = Join-Path $PayloadRoot 'Tappy.exe'
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    $processInfo.ArgumentList.Add('--readiness-smoke')
    $processInfo.ArgumentList.Add('--result')
    $processInfo.ArgumentList.Add($ResultPath)
    $processInfo.Environment['TAPPY_SMOKE_DATA_ROOT'] = $DataRoot
    $processInfo.Environment['TAPPY_READINESS_SMOKE'] = '1'

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $processInfo
    try {
        if (-not $process.Start()) {
            throw 'Tappy readiness-smoke process did not start.'
        }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) {
            try { $process.Kill($true) } catch { }
            try { [void]$process.WaitForExit(5000) } catch { }
            throw 'Tappy readiness smoke exceeded 60 seconds.'
        }
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        Test-TextForSensitiveContent -Text $standardOutput -Description 'Readiness-smoke standard output'
        Test-TextForSensitiveContent -Text $standardError -Description 'Readiness-smoke standard error'
        if ($process.ExitCode -ne 0) {
            throw "Tappy readiness smoke exited $($process.ExitCode). stdout='$standardOutput' stderr='$standardError'"
        }
    }
    finally {
        $process.Dispose()
    }

    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        throw 'Tappy readiness smoke did not write its requested result file.'
    }
    $raw = [IO.File]::ReadAllText($ResultPath)
    Test-TextForSensitiveContent -Text $raw -Description 'Readiness-smoke result'
    $result = $raw | ConvertFrom-Json
    if ($result.schemaVersion -ne 1 -or $result.product -ne 'Tappy' -or $result.version -ne $ExpectedVersion -or
        $result.ready -ne $true -or [int64]$result.injectedInputCount -ne 0) {
        throw 'Readiness-smoke result has the wrong schema/product/version or reports unsafe output.'
    }
    $checks = @($result.checks)
    foreach ($name in $requiredSmokeChecks) {
        $matching = @($checks | Where-Object { $_.name -eq $name })
        if ($matching.Count -ne 1 -or $matching[0].passed -ne $true) {
            throw "Readiness-smoke check '$name' is missing, duplicated, or failed."
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $DataRoot -Recurse -Force -File) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Readiness smoke created a symbolic link or reparse point in its isolated data root.'
        }
        if ($file.Length -le 4MB -and $file.Extension -in @('.json', '.txt', '.csv', '.xml', '.config', '.log')) {
            Test-TextForSensitiveContent -Text ([IO.File]::ReadAllText($file.FullName)) `
                -Description "Readiness-smoke data file '$($file.Name)'"
        }
    }
}

function Get-PayloadManifestEntries {
    param([string] $Root)
    return @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = Get-RelativePayloadPath -Root $Root -Path $_.FullName
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
}

function Assert-ZipEntries {
    param([string] $ZipPath)
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $actual = @()
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $name = $entry.FullName.Replace('\', '/')
            $unsafeSegments = @($name.Split('/') | Where-Object { $_ -in @('', '.', '..') })
            if ($name.StartsWith('/') -or $name.Contains(':') -or $unsafeSegments.Count -gt 0) {
                throw "ZIP contains an unsafe path: $name"
            }
            if (-not $seen.Add($name)) {
                throw "ZIP contains a duplicate path: $name"
            }
            $actual += $name
        }
        $missing = @($requiredPayload | Where-Object { $_ -notin $actual })
        $unexpected = @($actual | Where-Object { $_ -notin $requiredPayload })
        if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
            throw "ZIP payload differs from its allowlist. Missing='$($missing -join ', ')' Unexpected='$($unexpected -join ', ')'"
        }
    }
    finally {
        $archive.Dispose()
    }
}

foreach ($path in @($solutionPath, $appProjectPath, $identityPath, $buildPropsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required build input is missing: $path"
    }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found on PATH.'
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProps = [IO.File]::ReadAllText($buildPropsPath)
    $versionNode = $buildProps.SelectSingleNode('//VersionPrefix')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw 'Directory.Build.props does not define VersionPrefix.'
    }
    $Version = $versionNode.InnerText.Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use numeric major.minor.patch form.'
}

$identity = [IO.File]::ReadAllText($identityPath) | ConvertFrom-Json
if ($identity.product -ne 'Tappy' -or $identity.executable -ne 'Tappy.exe' -or
    $identity.installerAppId -ne '{B42E5FBB-E4AB-458A-908E-838C8BD101BB}' -or
    $identity.mutex -ne 'Local\TerkWerX.Tappy.HandController.0_1' -or
    $identity.appUserModelId -ne 'TerkWerX.Tappy' -or
    $identity.updateEndpoint -ne 'https://api.github.com/repos/TerkWerX/TAPPY/releases/latest' -or
    $identity.version -ne $Version) {
    throw 'eng/product-identity.json does not match the frozen Tappy packaging identity/version.'
}

New-Item -ItemType Directory -Path $stagingParent -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stagingRoot = Join-Path $stagingParent "tappy-portable-$([Guid]::NewGuid().ToString('N'))"
Assert-PathUnderRoot -Path $stagingRoot -Root $stagingParent -Purpose 'Staging'
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
$publishRoot = Join-Path $stagingRoot 'publish'
$extractRoot = Join-Path $stagingRoot 'extracted'
$zipPath = Join-Path $OutputDirectory "Tappy-$Version-Portable-x64.zip"
$manifestPath = Join-Path $OutputDirectory "Tappy-$Version-Portable-x64.manifest.json"
$stagedZipPath = Join-Path $stagingRoot ([IO.Path]::GetFileName($zipPath))
$stagedManifestPath = Join-Path $stagingRoot ([IO.Path]::GetFileName($manifestPath))
$completed = $false
$zipCreatedByThisRun = $false
$manifestCreatedByThisRun = $false

try {
    foreach ($output in @($zipPath, $manifestPath)) {
        if (Test-Path -LiteralPath $output) {
            throw "Refusing to overwrite an existing package artifact: $output"
        }
    }

    Invoke-Checked -FilePath 'dotnet' -WorkingDirectory $repoRoot -Arguments @('restore', $solutionPath, '--locked-mode')
    Invoke-Checked -FilePath 'dotnet' -WorkingDirectory $repoRoot -Arguments @(
        'build', $solutionPath, '--configuration', 'Release', '--no-restore', "-p:Version=$Version"
    )
    Invoke-Checked -FilePath 'dotnet' -WorkingDirectory $repoRoot -Arguments @(
        'test', $solutionPath, '--configuration', 'Release', '--no-build', '--no-restore',
        '--logger', 'trx;LogFileName=tappy-tests.trx', "-p:Version=$Version"
    )
    Invoke-Checked -FilePath 'dotnet' -WorkingDirectory $repoRoot -Arguments @(
        'publish', $appProjectPath, '--configuration', 'Release', '--runtime', 'win-x64',
        '--self-contained', 'true', '--no-restore', '--output', $publishRoot, "-p:Version=$Version"
    )

    Assert-Payload -Root $publishRoot -ExpectedVersion $Version
    Invoke-ReadinessSmoke -PayloadRoot $publishRoot `
        -ResultPath (Join-Path $stagingRoot 'published-readiness.json') `
        -DataRoot (Join-Path $stagingRoot 'published-smoke-data') `
        -ExpectedVersion $Version
    Assert-Payload -Root $publishRoot -ExpectedVersion $Version

    $payloadEntries = Get-PayloadManifestEntries -Root $publishRoot
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $publishRoot,
        $stagedZipPath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
    Assert-ZipEntries -ZipPath $stagedZipPath
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($stagedZipPath, $extractRoot)
    Assert-Payload -Root $extractRoot -ExpectedVersion $Version

    $extractedEntries = Get-PayloadManifestEntries -Root $extractRoot
    if (($payloadEntries | ConvertTo-Json -Depth 5 -Compress) -ne ($extractedEntries | ConvertTo-Json -Depth 5 -Compress)) {
        throw 'Freshly extracted ZIP hashes or sizes differ from the audited publish directory.'
    }
    Invoke-ReadinessSmoke -PayloadRoot $extractRoot `
        -ResultPath (Join-Path $stagingRoot 'archive-readiness.json') `
        -DataRoot (Join-Path $stagingRoot 'archive-smoke-data') `
        -ExpectedVersion $Version
    Assert-Payload -Root $extractRoot -ExpectedVersion $Version

    $dotnetSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not record the .NET SDK version.' }
    $commit = (& git -C $repoRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) { $commit = 'uncommitted-bootstrap' }
    $dirty = @(& git -C $repoRoot status --porcelain 2>$null).Count -gt 0
    $inputFiles = @($identityPath, $buildPropsPath, (Join-Path $repoRoot 'Directory.Packages.props'), (Join-Path $repoRoot 'global.json'))
    $inputHashes = @($inputFiles | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
        [ordered]@{
            path = Get-RelativePayloadPath -Root $repoRoot -Path $_
            sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
        }
    })
    $lockHashes = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter 'packages.lock.json' |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|artifacts)[\\/]' } |
        Sort-Object FullName | ForEach-Object {
            [ordered]@{
                path = Get-RelativePayloadPath -Root $repoRoot -Path $_.FullName
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        })
    $peFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File |
        Where-Object { $_.Extension -in @('.exe', '.dll') } | Sort-Object FullName)
    $peInventory = @($peFiles | ForEach-Object {
        $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
        if ($signature.Status -notin @([System.Management.Automation.SignatureStatus]::Valid, [System.Management.Automation.SignatureStatus]::NotSigned)) {
            throw "PE signature state is unsafe for '$($_.Name)': $($signature.Status)"
        }
        [ordered]@{
            path = Get-RelativePayloadPath -Root $publishRoot -Path $_.FullName
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            authenticode = $signature.Status.ToString()
        }
    })
    $signingStatus = if (@($peInventory | Where-Object { $_.authenticode -eq 'Valid' }).Count -eq $peInventory.Count) {
        'all-pe-signatures-valid'
    }
    else {
        'unsigned-local-build'
    }
    $packageManifest = [ordered]@{
        schemaVersion = 1
        product = 'Tappy'
        version = $Version
        runtimeIdentifier = 'win-x64'
        selfContained = $true
        singleFile = $true
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        source = [ordered]@{
            commit = $commit.Trim()
            dirty = $dirty
        }
        toolchain = [ordered]@{
            dotnetSdk = $dotnetSdk
            powerShell = $PSVersionTable.PSVersion.ToString()
            operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
            innoSetup = 'not used by Build-Portable.ps1; installer compiler must be recorded by an authorized installer build'
        }
        buildInputs = $inputHashes
        packageLocks = $lockHashes
        readinessContract = [ordered]@{
            schemaVersion = 1
            checks = $requiredSmokeChecks
            injectedInputCount = 0
            auditedPublishedDirectory = $true
            auditedFreshArchiveExtraction = $true
        }
        payload = $payloadEntries
        peInventory = $peInventory
        archive = [ordered]@{
            name = [IO.Path]::GetFileName($zipPath)
            bytes = (Get-Item -LiteralPath $stagedZipPath).Length
            sha256 = (Get-FileHash -LiteralPath $stagedZipPath -Algorithm SHA256).Hash
        }
        signing = [ordered]@{
            status = $signingStatus
            note = 'This local portable builder does not access code-signing credentials or publish releases.'
        }
    }
    $manifestJson = $packageManifest | ConvertTo-Json -Depth 12
    Test-TextForSensitiveContent -Text $manifestJson -Description 'Package manifest'
    [IO.File]::WriteAllText($stagedManifestPath, $manifestJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

    [IO.File]::Move($stagedZipPath, $zipPath, $false)
    $zipCreatedByThisRun = $true
    [IO.File]::Move($stagedManifestPath, $manifestPath, $false)
    $manifestCreatedByThisRun = $true
    $completed = $true

    Write-Host "Tappy portable package passed publish-directory and fresh-ZIP readiness audits."
    Write-Host "ZIP:      $zipPath"
    Write-Host "Manifest: $manifestPath"
    if ($KeepStaging) {
        Write-Host "Audited staging retained at: $stagingRoot"
    }
}
finally {
    if (-not $completed) {
        if ($zipCreatedByThisRun -and (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        if ($manifestCreatedByThisRun -and (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            Remove-Item -LiteralPath $manifestPath -Force
        }
    }
    if (-not $KeepStaging -and (Test-Path -LiteralPath $stagingRoot)) {
        Assert-PathUnderRoot -Path $stagingRoot -Root $stagingParent -Purpose 'Cleanup'
        if (-not ([IO.Path]::GetFileName($stagingRoot).StartsWith('tappy-portable-', [StringComparison]::Ordinal))) {
            throw "Refusing to clean an unexpected staging directory: $stagingRoot"
        }
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
