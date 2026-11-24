[CmdletBinding()]
param(
    [string] $DotNet = 'dotnet',
    [string] $ArtifactsDirectory,
    [string] $TargetsFile,
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string] $RuntimeIdentifier,
    [ValidateSet('net10.0-windows10.0.17763.0', 'net11.0-windows10.0.17763.0')]
    [string] $TargetFramework = 'net11.0-windows10.0.17763.0',
    [switch] $SkipMauiWorkload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repo 'platforms\Windows.WPF\samples\Windows.WPF.Sample\Windows.WPF.Sample.csproj'
if (-not $ArtifactsDirectory) {
    $ArtifactsDirectory = Join-Path $repo "artifacts\wpf-assets-$([Guid]::NewGuid().ToString('N'))"
}
if (Test-Path $ArtifactsDirectory) {
    throw "Use a new artifacts directory to avoid stale assets: $ArtifactsDirectory"
}
$work = (New-Item -ItemType Directory -Path $ArtifactsDirectory).FullName
$build = Join-Path $work 'build'
$publish = Join-Path $work 'publish'

function Invoke-DotNet {
    param([string[]] $Arguments)
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

# MAUI publish infers a Windows RID; use the same RID for build so Razor's manifests exist for no-build publish.
$properties = @('-m:1', '-nr:false', '-p:Configuration=Release', '-p:WpfTestScenarios=true',
    "-p:WpfSampleTargetFramework=$TargetFramework",
    "-p:RuntimeIdentifier=$RuntimeIdentifier", '-p:SelfContained=false', "-p:OutputPath=$build\")
if ($TargetsFile) {
    $properties += "-p:WpfTestTargetsPath=$((Resolve-Path -LiteralPath $TargetsFile).Path)"
}
if ($SkipMauiWorkload) {
    $properties += '-p:UseMaui=false'
}

Invoke-DotNet (@('build', $project, '--nologo', '-v:minimal') + $properties)
Invoke-DotNet @((Join-Path $build 'Windows.WPF.Sample.dll'), '--test-scenario', 'packaged-assets')
Invoke-DotNet @((Join-Path $build 'Windows.WPF.Sample.dll'), '--test-scenario', 'standard-resources')

# A no-build publish must reconstruct the content items without relying on the Build target.
Invoke-DotNet (@('publish', $project, '--no-build', '--no-restore', '--nologo', '-v:minimal',
    "-p:PublishDir=$publish\") + $properties)
Invoke-DotNet @((Join-Path $publish 'Windows.WPF.Sample.dll'), '--test-scenario', 'packaged-assets')
Invoke-DotNet @((Join-Path $publish 'Windows.WPF.Sample.dll'), '--test-scenario', 'standard-resources')
Write-Host "PASS: $TargetFramework build and publish resource contracts (processed icons require .NET 11). Artifacts: $work"
