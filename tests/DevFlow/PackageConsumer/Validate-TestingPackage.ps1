[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [string] $WorkingDirectory,

    [string[]] $RequireTargetFrameworks = @(),

    [switch] $SkipCliBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepositoryRoot {
    $directory = [System.IO.DirectoryInfo]$PSScriptRoot
    while ($null -ne $directory) {
        if ((Test-Path (Join-Path $directory.FullName 'MauiLabs.slnx')) -and
            (Test-Path (Join-Path $directory.FullName 'NuGet.config'))) {
            return $directory.FullName
        }

        $directory = $directory.Parent
    }

    throw 'Could not find the repository root from the package-consumer script.'
}

function Get-Package {
    param(
        [Parameter(Mandatory)]
        [string] $Directory,

        [Parameter(Mandatory)]
        [string] $PackageId
    )

    $escapedId = [regex]::Escape($PackageId)
    $packages = @(
        Get-ChildItem -Path $Directory -File -Filter '*.nupkg' |
            Where-Object {
                $_.Name -notlike '*.symbols.nupkg' -and
                $_.Name -match "^$escapedId\.(?<version>.+)\.nupkg$"
            } |
            Sort-Object Name
    )

    if ($packages.Count -ne 1) {
        throw "Expected exactly one $PackageId .nupkg in '$Directory', found $($packages.Count)."
    }

    $match = [regex]::Match($packages[0].Name, "^$escapedId\.(?<version>.+)\.nupkg$")
    return [pscustomobject]@{
        Path = $packages[0].FullName
        Version = $match.Groups['version'].Value
    }
}

function Get-ArchiveEntries {
    param([Parameter(Mandatory)][string] $Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        return @($archive.Entries | ForEach-Object FullName)
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ArchiveText {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "Archive '$Path' does not contain '$EntryName'."
        }

        $reader = [System.IO.StreamReader]::new($entry.Open())
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-Package {
    param(
        [Parameter(Mandatory)][string] $PackagePath,
        [Parameter(Mandatory)][string] $ExpectedVersion
    )

    $packageId = 'Microsoft.Maui.DevFlow.Testing'
    $entries = Get-ArchiveEntries -Path $PackagePath
    $nuspecNames = @($entries | Where-Object { $_ -match '\.nuspec$' })
    if ($nuspecNames.Count -ne 1) {
        throw "Expected exactly one nuspec in the Testing package, found $($nuspecNames.Count)."
    }
    $nuspecName = $nuspecNames[0]

    foreach ($expectedEntry in @(
        'README.md',
        'lib/net9.0/Microsoft.Maui.DevFlow.Testing.dll'
    )) {
        if ($entries -notcontains $expectedEntry) {
            throw "The Testing package is missing required entry '$expectedEntry'."
        }
    }

    $libAssemblies = @($entries | Where-Object { $_ -match '^lib/[^/]+/[^/]+\.dll$' })
    if ($libAssemblies.Count -ne 1 -or $libAssemblies[0] -ne 'lib/net9.0/Microsoft.Maui.DevFlow.Testing.dll') {
        throw "Unexpected managed assembly inventory: $($libAssemblies -join ', ')"
    }

    $prohibitedPayload = @(
        $entries | Where-Object { $_ -match '(?i)(Microsoft\.Maui\.Cli|broker|provider|ModelContextProtocol|Appium)' }
    )
    if ($prohibitedPayload.Count -gt 0) {
        throw "The framework-neutral package contains prohibited CLI/broker/provider payload: $($prohibitedPayload -join ', ')"
    }

    [xml] $nuspec = Get-ArchiveText -Path $PackagePath -EntryName $nuspecName
    $metadata = $nuspec.package.metadata
    if ($metadata.id -ne $packageId) {
        throw "Unexpected package ID '$($metadata.id)'."
    }

    if ($metadata.version -ne $ExpectedVersion) {
        throw "Package version '$($metadata.version)' does not match archive version '$ExpectedVersion'."
    }

    if ($metadata.description -notmatch '(?i)experimental preview') {
        throw 'The package description must identify this package as an experimental preview.'
    }

    foreach ($tag in @('maui', 'devflow', 'testing', 'preview', 'experimental')) {
        if ($metadata.tags -notmatch "(?i)(^|\s)$tag(\s|$)") {
            throw "Package tags do not include '$tag'."
        }
    }

    if ($metadata.readme -ne 'README.md') {
        throw "Unexpected package README metadata '$($metadata.readme)'."
    }

    $repository = $nuspec.SelectSingleNode("//*[local-name()='repository']")
    if ($null -eq $repository -or
        $repository.GetAttribute('type') -ne 'git' -or
        $repository.GetAttribute('url') -ne 'https://github.com/dotnet/maui-labs') {
        throw 'The package must inherit the dotnet/maui-labs git repository metadata.'
    }

    $dependencyIds = @(
        $nuspec.SelectNodes("//*[local-name()='dependency']") |
            ForEach-Object { $_.GetAttribute('id') }
    )
    if ($dependencyIds -notcontains 'Microsoft.Maui.DevFlow.Driver') {
        throw 'The Testing package must declare its Microsoft.Maui.DevFlow.Driver dependency.'
    }

    $expectedDependencies = @(
        'Interop.UIAutomationClient',
        'Microsoft.Maui.DevFlow.Driver',
        'SkiaSharp',
        'System.Text.Json'
    )
    $unexpectedDependencies = @(
        $dependencyIds | Where-Object { $_ -notin $expectedDependencies }
    )
    if ($unexpectedDependencies.Count -gt 0) {
        throw "The Testing package has unexpected dependencies: $($unexpectedDependencies -join ', ')"
    }

    $prohibitedDependencies = @(
        $dependencyIds | Where-Object {
            $_ -match '(?i)(^Microsoft\.Maui\.Cli$|broker|provider|modelcontextprotocol|appium)'
        }
    )
    if ($prohibitedDependencies.Count -gt 0) {
        throw "The Testing package has prohibited CLI/broker/provider dependencies: $($prohibitedDependencies -join ', ')"
    }

    $readme = Get-ArchiveText -Path $PackagePath -EntryName 'README.md'
    foreach ($requiredText in @(
        'Experimental preview',
        'framework-neutral',
        'xUnit',
        'NUnit',
        'MSTest',
        'compatibility'
    )) {
        if ($readme -notmatch [regex]::Escape($requiredText)) {
            throw "The packed README is missing '$requiredText'."
        }
    }

    Write-Host "Validated $packageId $ExpectedVersion package inventory, metadata, dependency boundary, and README."
}

function Test-Workload {
    param(
        [Parameter(Mandatory)][string] $WorkloadId,
        [Parameter(Mandatory)][string] $WorkloadList
    )

    return $WorkloadList -match "(?m)^\s*$([regex]::Escape($WorkloadId))\s"
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    Write-Host "+ dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = Get-RepositoryRoot
$packageDirectory = (Resolve-Path $PackageDirectory).Path
if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $WorkingDirectory = Join-Path $repositoryRoot 'artifacts/package-consumer'
}

$workingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$artifactsPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $workingDirectory.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "WorkingDirectory must be below the repository artifacts directory: $artifactsRoot"
}

$testingPackage = Get-Package -Directory $packageDirectory -PackageId 'Microsoft.Maui.DevFlow.Testing'
$driverPackage = Get-Package -Directory $packageDirectory -PackageId 'Microsoft.Maui.DevFlow.Driver'
Assert-Package -PackagePath $testingPackage.Path -ExpectedVersion $testingPackage.Version

if (Test-Path $workingDirectory) {
    Remove-Item -Recurse -Force $workingDirectory
}

$localFeed = Join-Path $workingDirectory 'local-feed'
$nugetPackages = Join-Path $workingDirectory 'nuget-packages'
$localConfig = Join-Path $workingDirectory 'NuGet.local.config'
New-Item -ItemType Directory -Force -Path $localFeed, $nugetPackages | Out-Null
Copy-Item $testingPackage.Path, $driverPackage.Path -Destination $localFeed
Copy-Item (Join-Path $repositoryRoot 'NuGet.config') $localConfig

Invoke-DotNet -Arguments @(
    'nuget', 'add', 'source', $localFeed,
    '--name', 'devflow-testing-local',
    '--configfile', $localConfig
)

$previousNugetPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = $nugetPackages
try {
    $consumerProject = Join-Path $PSScriptRoot 'Microsoft.Maui.DevFlow.Testing.PackageConsumer.csproj'
    $workloadList = (& dotnet workload list 2>&1 | Out-String)
    $runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
    $runningOnMacOS = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::OSX)

    $matrix = @(
        [pscustomobject]@{ TargetFramework = 'net9.0'; Status = 'pending'; Reason = 'Framework-neutral consumer.' }
        [pscustomobject]@{ TargetFramework = 'net10.0'; Status = 'pending'; Reason = 'Framework-neutral consumer.' }
        [pscustomobject]@{ TargetFramework = 'net10.0-android'; Status = 'skipped'; Reason = 'Requires a Windows host with the MAUI workload.' }
        [pscustomobject]@{ TargetFramework = 'net10.0-ios'; Status = 'skipped'; Reason = 'Requires a macOS host with the iOS workload.' }
        [pscustomobject]@{ TargetFramework = 'net10.0-maccatalyst'; Status = 'skipped'; Reason = 'Requires a macOS host with the Mac Catalyst workload.' }
        [pscustomobject]@{ TargetFramework = 'net10.0-windows10.0.19041.0'; Status = 'skipped'; Reason = 'Requires a Windows host.' }
        [pscustomobject]@{ TargetFramework = 'net10.0-macos'; Status = 'skipped'; Reason = 'Experimental AppKit compile only; requires macOS and the macOS workload.' }
    )

    $availableTargets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    [void] $availableTargets.Add('net9.0')
    [void] $availableTargets.Add('net10.0')

    if ($runningOnWindows -and (Test-Workload -WorkloadId 'maui' -WorkloadList $workloadList)) {
        [void] $availableTargets.Add('net10.0-android')
    }

    if ($runningOnWindows) {
        [void] $availableTargets.Add('net10.0-windows10.0.19041.0')
    }

    if ($runningOnMacOS) {
        if (Test-Workload -WorkloadId 'ios' -WorkloadList $workloadList) {
            [void] $availableTargets.Add('net10.0-ios')
        }

        if (Test-Workload -WorkloadId 'maccatalyst' -WorkloadList $workloadList) {
            [void] $availableTargets.Add('net10.0-maccatalyst')
        }

        if (Test-Workload -WorkloadId 'macos' -WorkloadList $workloadList) {
            [void] $availableTargets.Add('net10.0-macos')
        }
    }

    foreach ($requiredTarget in $RequireTargetFrameworks) {
        if (-not $availableTargets.Contains($requiredTarget)) {
            throw "Required package-consumer target '$requiredTarget' is unavailable on this host. $workloadList"
        }
    }

    foreach ($entry in $matrix) {
        if (-not $availableTargets.Contains($entry.TargetFramework)) {
            continue
        }

        Invoke-DotNet -Arguments @(
            'restore', $consumerProject,
            ('-p:TargetFrameworks=' + $entry.TargetFramework),
            ('-p:DevFlowTestingPackageVersion=' + $testingPackage.Version),
            '--configfile', $localConfig,
            '--packages', $nugetPackages,
            '--no-cache'
        )
        Invoke-DotNet -Arguments @(
            'build', $consumerProject,
            '--no-restore',
            '-c', 'Release',
            '-f', $entry.TargetFramework,
            ('-p:DevFlowTestingPackageVersion=' + $testingPackage.Version)
        )

        $entry.Status = 'compiled'
        $entry.Reason = 'Package-only local-feed restore and compile succeeded; no app or device runtime was started.'
    }

    $packageMetadataPath = Join-Path $nugetPackages "microsoft.maui.devflow.testing/$($testingPackage.Version)/.nupkg.metadata"
    if (-not (Test-Path $packageMetadataPath)) {
        throw 'The package-only restore did not produce NuGet package provenance metadata.'
    }

    $packageMetadata = Get-Content -Raw $packageMetadataPath | ConvertFrom-Json
    if ([string]$packageMetadata.source -notlike "*$localFeed*") {
        throw "Microsoft.Maui.DevFlow.Testing was not restored from the artifact-local feed: $($packageMetadata.source)"
    }

    if (-not $SkipCliBuild) {
        if ($null -eq $previousNugetPackages) {
            Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
        }
        else {
            $env:NUGET_PACKAGES = $previousNugetPackages
        }

        $cliProject = Join-Path $repositoryRoot 'src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj'
        Invoke-DotNet -Arguments @(
            'restore', $cliProject,
            '--configfile', (Join-Path $repositoryRoot 'NuGet.config')
        )

        foreach ($targetFramework in @('net9.0', 'net10.0')) {
            Invoke-DotNet -Arguments @(
                'build', $cliProject,
                '--no-restore',
                '-c', 'Release',
                '-f', $targetFramework
            )
        }
    }

    $matrixPath = Join-Path $workingDirectory 'consumer-matrix.json'
    $matrix | ConvertTo-Json -Depth 4 | Set-Content -NoNewline -Encoding utf8 $matrixPath
    $matrix | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Wrote package consumer matrix to $matrixPath"
}
finally {
    if ($null -eq $previousNugetPackages) {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
    else {
        $env:NUGET_PACKAGES = $previousNugetPackages
    }
}
