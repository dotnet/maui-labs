[CmdletBinding()]
param([string] $ArtifactsDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $ArtifactsDirectory) {
    $ArtifactsDirectory = Join-Path ([IO.Path]::GetTempPath()) "devflow-analyzer-smoke-$([Guid]::NewGuid().ToString('N'))"
}
if (Test-Path $ArtifactsDirectory) {
    throw "Use a new artifacts directory to avoid stale packages or generated files: $ArtifactsDirectory"
}
$work = (New-Item -ItemType Directory -Path $ArtifactsDirectory).FullName
$version = "0.0.0-analyzer-smoke.$([Guid]::NewGuid().ToString('N'))"
$devflow = Join-Path $repo 'src/DevFlow'
$core = Join-Path $devflow 'Microsoft.Maui.DevFlow.Agent.Core/Microsoft.Maui.DevFlow.Agent.Core.csproj'
$dependencies = Join-Path $work 'dependencies'

function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

foreach ($name in @('Microsoft.Maui.DevFlow.Logging', 'Microsoft.Maui.DevFlow.Agent.Abstractions')) {
    Invoke-DotNet @('pack', (Join-Path $devflow "$name/$name.csproj"), '-c', 'Release',
        "-p:PackageVersion=$version", '-o', $dependencies, '--nologo', '-v', 'minimal')
}

foreach ($mode in @('normal', 'no-build')) {
    $packages = (New-Item -ItemType Directory -Path (Join-Path $work $mode)).FullName
    Copy-Item (Join-Path $dependencies '*.nupkg') $packages
    $packArguments = @('pack', $core, '-c', 'Release', "-p:PackageVersion=$version",
        '-o', $packages, '--nologo', '-v', 'minimal')
    if ($mode -eq 'no-build') {
        Invoke-DotNet @('build', $core, '-c', 'Release', "-p:PackageVersion=$version", '--nologo', '-v', 'minimal')
        $packArguments += '--no-build'
    }
    Invoke-DotNet $packArguments

    $package = Join-Path $packages "Microsoft.Maui.DevFlow.Agent.Core.$version.nupkg"
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $analyzer = $archive.GetEntry('analyzers/dotnet/cs/Microsoft.Maui.DevFlow.Analyzers.dll')
        if (-not $analyzer -or $analyzer.Length -eq 0) {
            throw "$mode pack is missing the DevFlow analyzer DLL."
        }
    } finally {
        $archive.Dispose()
    }

    Write-Host "PASS: $mode package contains the analyzer."
}

$consumerScenario = (New-Item -ItemType Directory -Path (Join-Path $work 'consumer')).FullName
$packages = Join-Path $work 'normal'
[xml] $config = Get-Content (Join-Path $repo 'NuGet.config') -Raw
$source = $config.CreateElement('add')
$source.SetAttribute('key', 'devflow-smoke')
$source.SetAttribute('value', $packages)
[void] $config.configuration.packageSources.AppendChild($source)
$config.Save((Join-Path $consumerScenario 'NuGet.config'))

$consumer = (New-Item -ItemType Directory -Path (Join-Path $consumerScenario 'app')).FullName
Set-Content (Join-Path $consumer 'Consumer.csproj') @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>obj/generated</CompilerGeneratedFilesOutputPath>
    <WarningsAsErrors>CS8032;CS8784;CS8785</WarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.DevFlow.Agent.Core" Version="$version" />
    <MauiXaml Include="MainPage.xaml" />
  </ItemGroup>
</Project>
"@
    Set-Content (Join-Path $consumer 'MainPage.xaml') @'
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml" x:Class="PackagingProbe.MainPage">
  <Label Text="Packaging probe" />
</ContentPage>
'@
    Set-Content (Join-Path $consumer 'Program.cs') @'
using System;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

var map = XamlSourceMapRegistry.Instance.GetMap("PackagingProbe.MainPage");
if (map is null || !map.File.EndsWith("MainPage.xaml", StringComparison.Ordinal)
    || map.Count != 2 || !map.TryGet("0", out var label)
    || label.TypeName != "Label" || label.Text != "Packaging probe" || label.Line != 3)
    throw new InvalidOperationException("The packaged generator did not register the expected XAML source map.");
Console.WriteLine("Packaged generator loaded and registered the Label source map.");
'@
$project = Join-Path $consumer 'Consumer.csproj'
Invoke-DotNet @('restore', $project, '--packages', (Join-Path $consumerScenario 'nuget'), '--nologo', '-v', 'minimal')
Invoke-DotNet @('build', $project, '-c', 'Debug', '--no-restore', '--nologo', '-v', 'minimal')
$generated = @(Get-ChildItem (Join-Path $consumer 'obj/generated') -Recurse -Filter 'DevFlowXamlSourceMaps.g.cs')
if ($generated.Count -ne 1) {
    throw "The clean consumer did not emit exactly one DevFlow XAML source-map file."
}
Invoke-DotNet @((Join-Path $consumer 'bin/Debug/net11.0/Consumer.dll'))
Write-Host 'PASS: clean .NET 11 consumer loaded the generator and registered the Label source map.'
