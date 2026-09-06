[CmdletBinding()]
param(
    [switch]$Require
)

$ErrorActionPreference = 'Stop'

$vswherePath = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudioPath = $null
$cppCompiler = $null
if (Test-Path -LiteralPath $vswherePath) {
    # Do not use "-products *" here. SQL Server Management Studio also registers
    # as a Visual Studio instance and can otherwise win the "latest" query even
    # though it does not contain the C++ workload required for a driver build.
    $visualStudioPath = & $vswherePath -latest -products Microsoft.VisualStudio.Product.Community -property installationPath
    if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
        $visualStudioPath = $null
    }
    elseif (Test-Path -LiteralPath (Join-Path $visualStudioPath 'VC\Tools\MSVC')) {
        $cppCompiler = Get-ChildItem -LiteralPath (Join-Path $visualStudioPath 'VC\Tools\MSVC') `
            -Filter cl.exe -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\bin\\Hostx64\\x64\\cl\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

$kitRoot = 'C:\Program Files (x86)\Windows Kits\10'
$sdkVersions = @()
$includeRoot = Join-Path $kitRoot 'Include'
if (Test-Path -LiteralPath $includeRoot) {
    $sdkVersions = @(Get-ChildItem -LiteralPath $includeRoot -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' } |
        Sort-Object Name |
        ForEach-Object Name)
}

$wdfHeader = Get-ChildItem -LiteralPath $kitRoot -Filter Wdf.h -File -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
$inf2Cat = Get-ChildItem -LiteralPath $kitRoot -Filter Inf2Cat.exe -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$signTool = Get-ChildItem -LiteralPath $kitRoot -Filter SignTool.exe -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$wdkProps = Get-ChildItem -LiteralPath $kitRoot -Filter Microsoft.Cpp.WindowsDriverMode.props -File -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$nugetWdkRoots = @(
    (Join-Path $repositoryRoot 'driver\packages'),
    (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.wdk.x64')
)
$nugetWdkProps = $nugetWdkRoots |
    Where-Object { Test-Path -LiteralPath $_ } |
    ForEach-Object {
        Get-ChildItem -LiteralPath $_ -Filter Microsoft.Windows.WDK.x64.props -File -Recurse -ErrorAction SilentlyContinue
    } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

$traditionalWdkReady = [bool]($wdfHeader -and $inf2Cat -and $wdkProps)
$nugetWdkReady = [bool]$nugetWdkProps
$nugetWdfHeader = $null
$nugetInf2Cat = $null
if ($nugetWdkReady) {
    $nugetNativeDirectory = Split-Path -Parent $nugetWdkProps
    $nugetBuildDirectory = Split-Path -Parent $nugetNativeDirectory
    $nugetPackageDirectory = Split-Path -Parent $nugetBuildDirectory
    $nugetWdfHeader = Get-ChildItem -LiteralPath (Join-Path $nugetPackageDirectory 'c\Include\wdf\kmdf') `
        -Filter Wdf.h -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    $nugetInf2Cat = Get-ChildItem -LiteralPath (Join-Path $nugetPackageDirectory 'c\bin') `
        -Filter Inf2Cat.exe -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$effectiveWdfHeader = if ($wdfHeader) { $wdfHeader } else { $nugetWdfHeader }
$effectiveInf2Cat = if ($inf2Cat) { $inf2Cat } else { $nugetInf2Cat }
$effectiveWdkProps = if ($wdkProps) { $wdkProps } else { $nugetWdkProps }

$result = [pscustomobject]@{
    VisualStudioPath = $visualStudioPath
    CppCompiler = $cppCompiler
    SdkVersions = $sdkVersions -join '; '
    WdfHeader = $effectiveWdfHeader
    Inf2Cat = $effectiveInf2Cat
    SignTool = $signTool
    WdkBuildIntegration = $effectiveWdkProps
    NuGetWdkBuildIntegration = $nugetWdkProps
    TraditionalWdkReady = $traditionalWdkReady
    NuGetWdkReady = $nugetWdkReady
    Ready = [bool]($visualStudioPath -and $cppCompiler -and $sdkVersions.Count -gt 0 -and $signTool -and ($traditionalWdkReady -or $nugetWdkReady))
}

$result

if ($Require -and -not $result.Ready) {
    throw 'The Tappy driver toolchain is incomplete. Install Desktop development with C++ and either a matching traditional WDK or restore the pinned Microsoft.Windows.WDK.x64 package for the driver project.'
}
