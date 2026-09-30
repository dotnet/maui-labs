param(
    [string] $PackageDirectory,
    [string] $BaselineTargets
)

$ErrorActionPreference = 'Stop'
if (!$IsMacOS) {
    throw 'Real AppKit bundle and runtime tests require macOS with Xcode and the macos/MAUI workloads.'
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$evidence = Join-Path $repo 'artifacts\BundleResourceEvidence'
New-Item -ItemType Directory -Force $evidence | Out-Null
$project = Join-Path $PSScriptRoot 'Probe\BundleResourceProbe.csproj'
$pathFile = Join-Path $evidence 'bundle-path.txt'
$expected = @(
    'Images/appicon.png', 'Fonts/OpenSans-Regular.ttf',
    'Data/sample.txt', 'Other/sample.txt', 'Custom/renamed.txt',
    'default.txt', 'Local/local.txt'
)

function Build-Probe([string] $phase, [string[]] $properties, [string] $verb = 'build') {
    $options = @()
    if ($phase -in @('before', 'after')) { $options += '-t:Rebuild' }
    & dotnet $verb $project -c Debug "-p:ResourceTestBundlePathFile=$pathFile" `
        -p:CreatePackage=false "-bl:$(Join-Path $evidence "$phase.binlog")" @properties @options
    if ($LASTEXITCODE -ne 0) { throw "$phase failed to $verb." }
    $script:bundle = (Get-Content $pathFile -Raw).Trim()
    if (!(Test-Path (Join-Path $bundle 'Contents\MacOS\BundleResourceProbe'))) {
        throw "No native app executable in $bundle."
    }
}

function Assert-Bundle {
    $resources = Join-Path $bundle 'Contents\Resources'
    foreach ($name in $expected) {
        if (!(Test-Path (Join-Path $resources $name))) { throw "Bundle is missing $name." }
    }
    if (Test-Path (Join-Path $resources 'Raw\local.txt')) {
        throw 'The default BundleResource glob duplicated the raw asset under Raw/.'
    }
    Get-ChildItem $resources -File -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($resources, $_.FullName)
    } | Set-Content (Join-Path $evidence 'bundle-files.txt')
}

function Run-Probe([string] $phase, [bool] $expectSuccess) {
    $stdout = Join-Path $evidence "$phase.stdout.txt"
    $stderr = Join-Path $evidence "$phase.stderr.txt"
    $process = Start-Process (Join-Path $bundle 'Contents\MacOS\BundleResourceProbe') `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    if (!$process.WaitForExit(60000)) {
        Stop-Process -Id $process.Id
        throw "$phase AppKit probe timed out."
    }
    if ($expectSuccess) {
        if ($process.ExitCode -ne 0) { throw "$phase AppKit probe failed: $(Get-Content $stderr -Raw)" }
        $result = Get-Content $stdout | Where-Object { $_ -match '^\{"result":' } | ConvertFrom-Json
        if ($result.result -ne 'PASS') { throw "$phase did not report runtime success." }
    } elseif ($process.ExitCode -eq 0 -or (Get-Content $stderr -Raw) -notmatch 'linked MauiImage was not loaded') {
        throw 'The baseline must fail specifically because its linked MauiImage is missing.'
    }
}

if ($BaselineTargets) {
    Build-Probe 'before' @("-p:MacOSResourceTargets=$((Resolve-Path $BaselineTargets).Path)")
    $resources = Join-Path $bundle 'Contents\Resources'
    foreach ($name in $expected) {
        if (Test-Path (Join-Path $resources $name)) { throw "Baseline unexpectedly contains $name." }
    }
    Run-Probe 'before' $false
}

$properties = @()
if ($PackageDirectory) {
    $packages = (Resolve-Path $PackageDirectory).Path
    $package = @(Get-ChildItem $packages -Recurse -Filter 'Microsoft.Maui.Platforms.MacOS.*.nupkg' |
        Where-Object { $_.Name -match '^Microsoft\.Maui\.Platforms\.MacOS\.\d' })
    if ($package.Count -ne 1) { throw 'Expected exactly one core macOS package.' }
    $version = $package[0].Name -replace '^Microsoft\.Maui\.Platforms\.MacOS\.', '' -replace '\.nupkg$', ''
    $properties += "-p:ResourceTestPackageVersion=$version"
    $properties += "-p:RestoreAdditionalProjectSources=$($package[0].DirectoryName)"
}

Build-Probe 'after' $properties
Assert-Bundle
Run-Probe 'after' $true
Build-Probe 'incremental' $properties
Assert-Bundle
Run-Probe 'incremental' $true
Build-Probe 'publish' $properties 'publish'
Assert-Bundle
Run-Probe 'publish' $true
Write-Host "Bundle, incremental, publish and native image/font/file checks passed. Evidence: $evidence"
