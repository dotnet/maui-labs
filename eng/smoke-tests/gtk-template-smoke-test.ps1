param(
    [switch]$BuildApps,
    [switch]$KeepArtifacts,
    [string]$DotNetSdkPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
# Keep restored package paths below Windows' assembly resolver path-length limit.
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) "gtk-$([Guid]::NewGuid().ToString('N'))"
$templateProject = Join-Path $repoRoot 'platforms\Linux.Gtk4\templates\Linux.Gtk4.Templates.csproj'
$packagePrefix = 'Microsoft.Maui.Platforms.Linux.Gtk4'
$dotnetPrefix = @()
if ($DotNetSdkPath) {
    $dotnetPrefix = @(Join-Path $DotNetSdkPath 'dotnet.dll')
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @dotnetPrefix @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Read-PackageEntry {
    param([string]$Package, [string]$Entry)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $matches = @($archive.Entries | Where-Object FullName -Like $Entry)
        if ($matches.Count -ne 1) {
            throw "Expected exactly one '$Entry' in $Package; found $($matches.Count)"
        }
        $reader = [System.IO.StreamReader]::new($matches[0].Open())
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Assert-TemplateProject {
    param([string]$Text, [string]$Version, [bool]$Essentials, [bool]$Blazor)
    [xml]$project = $Text
    $references = @($project.Project.ItemGroup.PackageReference)
    $expected = @($packagePrefix)
    if ($Essentials) { $expected += "$packagePrefix.Essentials" }
    if ($Blazor) { $expected += "$packagePrefix.BlazorWebView" }
    if ($references.Count -ne $expected.Count) { throw 'Unexpected package reference count' }
    foreach ($id in $expected) {
        $reference = @($references | Where-Object Include -EQ $id)
        if ($reference.Count -ne 1 -or $reference[0].Version -cne $Version) {
            throw "Expected exactly one reference to $id version $Version"
        }
    }
    if ($Text.Contains('__GtkPackageVersion__')) { throw 'Unexpanded version token' }
}

New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
try {
    # Generated apps must not inherit repository-only Arcade or CPM settings.
    '<Project />' | Set-Content (Join-Path $workRoot 'Directory.Build.props')
    '<Project />' | Set-Content (Join-Path $workRoot 'Directory.Build.targets')
    '<Project />' | Set-Content (Join-Path $workRoot 'Directory.Packages.props')
    $sourceProject = Join-Path (Split-Path $templateProject) 'maui-linux-gtk4-app\MauiLinuxApp.csproj'
    $originalSource = [System.IO.File]::ReadAllText($sourceProject)

    foreach ($mode in @('default', 'override')) {
        $modeRoot = Join-Path $workRoot $mode
        $packages = Join-Path $modeRoot 'packages'
        $packArgs = @('pack', $templateProject, '-c', 'Release', '-o', $packages, '-v', 'quiet')
        if ($mode -eq 'override') { $packArgs += '-p:PackageVersion=9.8.7-preview.template-smoke' }
        Invoke-DotNet $packArgs
        $templatePackage = @(Get-ChildItem $packages -Filter "$packagePrefix.Templates.*.nupkg")
        if ($templatePackage.Count -ne 1) { throw 'Expected exactly one template package' }
        [xml]$nuspec = Read-PackageEntry $templatePackage[0].FullName '*.nuspec'
        $version = [string]$nuspec.package.metadata.version
        if ($mode -eq 'override' -and $version -cne '9.8.7-preview.template-smoke') {
            throw 'PackageVersion override was not applied'
        }
        $packedProject = Read-PackageEntry $templatePackage[0].FullName 'content/maui-linux-gtk4-app/MauiLinuxApp.csproj'
        Assert-TemplateProject $packedProject $version $true $true
        if ([System.IO.File]::ReadAllText($sourceProject) -cne $originalSource) {
            throw 'Packing modified template source'
        }

        if ($BuildApps -and $mode -eq 'default') {
            foreach ($project in @('Linux.Gtk4', 'Linux.Gtk4.Essentials', 'Linux.Gtk4.BlazorWebView')) {
                $path = Join-Path $repoRoot "platforms\Linux.Gtk4\src\$project\$project.csproj"
                Invoke-DotNet @('pack', $path, '-c', 'Release', '-o', $packages, '-v', 'quiet')
                $id = "Microsoft.Maui.Platforms.$project"
                $package = Join-Path $packages "$id.$version.nupkg"
                [xml]$backendNuspec = Read-PackageEntry $package '*.nuspec'
                if ($backendNuspec.package.metadata.version -cne $version) {
                    throw "$id does not match the template package version"
                }
            }
            [xml]$config = Get-Content (Join-Path $repoRoot 'NuGet.config') -Raw
            $source = $config.CreateElement('add')
            $source.SetAttribute('key', 'gtk-template-smoke')
            $source.SetAttribute('value', $packages)
            $config.configuration.packageSources.AppendChild($source) | Out-Null
            $config.Save((Join-Path $modeRoot 'NuGet.config'))
        }

        $hive = Join-Path $modeRoot 'hive'
        Invoke-DotNet @('new', 'install', $templatePackage[0].FullName, '--debug:custom-hive', $hive)
        $variants = @(
            @{ Name = 'Default'; Essentials = $true; Blazor = $false; Options = @() },
            @{ Name = 'Neither'; Essentials = $false; Blazor = $false; Options = @('--essentials', 'false', '--blazor', 'false') },
            @{ Name = 'Essentials'; Essentials = $true; Blazor = $false; Options = @('--essentials', 'true', '--blazor', 'false') },
            @{ Name = 'Blazor'; Essentials = $false; Blazor = $true; Options = @('--essentials', 'false', '--blazor', 'true') },
            @{ Name = 'Both'; Essentials = $true; Blazor = $true; Options = @('--essentials', 'true', '--blazor', 'true') }
        )
        foreach ($variant in $variants) {
            $app = Join-Path $modeRoot $variant.Name
            Invoke-DotNet (@('new', 'maui-linux-gtk4', '-n', $variant.Name, '-o', $app, '--debug:custom-hive', $hive) + $variant.Options)
            $project = Join-Path $app "$($variant.Name).csproj"
            Assert-TemplateProject (Get-Content $project -Raw) $version $variant.Essentials $variant.Blazor
            $program = Get-Content (Join-Path $app 'MauiProgram.cs') -Raw
            $hasCall = $program.Contains('builder.AddLinuxGtk4Essentials();')
            $hasImport = $program.Contains('using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;')
            if ($hasCall -ne $variant.Essentials -or $hasImport -ne $variant.Essentials -or $program.Contains('#if')) {
                throw "Incorrect Essentials registration in $($variant.Name)"
            }
            if ($hasCall -and $program.IndexOf('builder.AddLinuxGtk4Essentials();') -gt $program.IndexOf('return builder.Build();')) {
                throw 'Essentials registration must run before Build'
            }
            if ($BuildApps -and $mode -eq 'default') {
                Invoke-DotNet @('restore', $project, '--configfile', (Join-Path $modeRoot 'NuGet.config'),
                    '--packages', (Join-Path $modeRoot 'restore'), '-v', 'quiet')
                Invoke-DotNet @('build', $project, '--no-restore', '-c', 'Release', '-v', 'quiet')
            }
            Write-Host "PASS $mode/$($variant.Name): package references and Essentials registration"
        }

        if ($BuildApps -and $mode -eq 'default') {
            $probe = Join-Path $modeRoot 'EssentialsProbe'
            New-Item -ItemType Directory -Path $probe | Out-Null
            @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><PackageReference Include="$packagePrefix.Essentials" Version="$version" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $probe 'EssentialsProbe.csproj')
            @'
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Accessibility;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;

var builder = MauiApp.CreateBuilder().AddLinuxGtk4Essentials();
using var app = builder.Build();
var reader = app.Services.GetRequiredService<ISemanticScreenReader>();
if (reader is not LinuxSemanticScreenReader ||
    !object.ReferenceEquals(reader, SemanticScreenReader.Default))
    throw new System.Exception("SemanticScreenReader must share the Linux implementation between DI and static APIs after Build.");
System.Console.WriteLine("PASS SemanticScreenReader DI and static default (no native GTK calls)");
'@ | Set-Content (Join-Path $probe 'Program.cs')
            Invoke-DotNet @('restore', $probe, '--configfile', (Join-Path $modeRoot 'NuGet.config'),
                '--packages', (Join-Path $modeRoot 'restore'), '-v', 'quiet')
            Invoke-DotNet @('run', '--project', $probe, '--no-restore', '-c', 'Release', '-v', 'quiet')
        }
    }
}
finally {
    if ($KeepArtifacts) { Write-Host "Artifacts: $workRoot" }
    else { Remove-Item -LiteralPath $workRoot -Recurse -Force }
}
