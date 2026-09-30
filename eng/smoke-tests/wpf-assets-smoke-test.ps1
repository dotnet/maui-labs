[CmdletBinding()]
param(
    [string] $DotNet = 'dotnet',
    [string] $ArtifactsDirectory,
    [switch] $SkipMauiWorkload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repo 'platforms\Windows.WPF\tests\PackagedAssetsApp\PackagedAssetsApp.csproj'
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

$properties = @('-m:1', '-nr:false', '-p:Configuration=Release', "-p:OutputPath=$build\")
if ($SkipMauiWorkload) {
    $properties += '-p:UseMaui=false'
}

Invoke-DotNet (@('build', $project, '--nologo', '-v:minimal') + $properties)
Invoke-DotNet @((Join-Path $build 'PackagedAssetsApp.dll'))

# A no-build publish must reconstruct the content items without relying on the Build target.
Invoke-DotNet (@('publish', $project, '--no-build', '--no-restore', '--nologo', '-v:minimal',
    "-p:PublishDir=$publish\") + $properties)
Invoke-DotNet @((Join-Path $publish 'PackagedAssetsApp.dll'))
Write-Host "PASS: built and published assets opened through IFileSystem. Artifacts: $work"
