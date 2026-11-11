[CmdletBinding()]
param(
    [string] $ArtifactsDirectory,
    [string] $DotNet = 'dotnet',
    [ValidateSet('all', 'gtk', 'wpf', 'appkit', 'switch')]
    [string] $Backend = 'gtk',
    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$fixture = Join-Path $repo 'platforms\Shared\ResourceAcceptance'
$version = '9.8.7-resource-smoke'
$mauiVersion = '11.0.0-rc.2.26505.5'
if (-not $ArtifactsDirectory) {
    $ArtifactsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "community-resources-$([Guid]::NewGuid().ToString('N'))"
}
if (Test-Path -LiteralPath $ArtifactsDirectory) {
    throw "Use a new artifacts directory to avoid stale resource results: $ArtifactsDirectory"
}
$work = (New-Item -ItemType Directory -Path $ArtifactsDirectory).FullName
$packages = New-Item -ItemType Directory -Path (Join-Path $work 'packages')
$restore = New-Item -ItemType Directory -Path (Join-Path $work 'restore')
$env:DOTNET_CLI_HOME = (New-Item -ItemType Directory -Path (Join-Path $work 'dotnet-home')).FullName
$env:NUGET_HTTP_CACHE_PATH = (New-Item -ItemType Directory -Path (Join-Path $work 'http-cache')).FullName

function Invoke-DotNet {
    param(
        [string[]] $Arguments,
        [switch] $ExpectFailure
    )

    & $DotNet @Arguments
    $exitCode = $LASTEXITCODE
    if ($ExpectFailure) {
        if ($exitCode -eq 0) {
            throw "dotnet $($Arguments -join ' ') unexpectedly succeeded"
        }
        return
    }
    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode"
    }
}

function Add-PackageSource {
    param([string] $Destination)

    [xml] $config = Get-Content (Join-Path $repo 'NuGet.config') -Raw
    $source = $config.CreateElement('add')
    $source.SetAttribute('key', 'community-resource-acceptance')
    $source.SetAttribute('value', $packages.FullName)
    $config.configuration.packageSources.AppendChild($source) | Out-Null
    $config.Save($Destination)
}

function Set-Tokens {
    param(
        [string] $Path,
        [hashtable] $Tokens
    )

    $text = [System.IO.File]::ReadAllText($Path)
    foreach ($entry in $Tokens.GetEnumerator()) {
        $text = $text.Replace($entry.Key, $entry.Value)
    }
    [System.IO.File]::WriteAllText($Path, $text)
}

function Assert-SingleFile {
    param(
        [string] $Root,
        [string] $RelativePath,
        [byte[]] $ExpectedBytes
    )

    $normalized = $RelativePath.Replace('\', [System.IO.Path]::DirectorySeparatorChar).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $matches = @(Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object {
        $_.FullName.EndsWith($normalized, [StringComparison]::Ordinal)
    })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$RelativePath' below '$Root'; found $($matches.Count)."
    }
    $actual = [System.IO.File]::ReadAllBytes($matches[0].FullName)
    if (-not [System.Linq.Enumerable]::SequenceEqual[byte]($actual, $ExpectedBytes)) {
        throw "Unexpected bytes in '$($matches[0].FullName)'."
    }
    return $matches[0].FullName
}

function Assert-NoFile {
    param(
        [string] $Root,
        [string] $RelativePath
    )

    $normalized = $RelativePath.Replace('\', [System.IO.Path]::DirectorySeparatorChar).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $matches = @(Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object {
        $_.FullName.EndsWith($normalized, [StringComparison]::Ordinal)
    })
    if ($matches.Count -ne 0) {
        throw "Expected '$RelativePath' to be absent below '$Root'; found $($matches.Count)."
    }
}

function Assert-BackendIdentity {
    param(
        [string] $Root,
        [string] $Expected
    )

    $matches = @(Get-ChildItem -LiteralPath $Root -File -Recurse -Filter 'resource-acceptance-backend.txt')
    if ($matches.Count -ne 1 -or ([System.IO.File]::ReadAllText($matches[0].FullName).Trim() -cne $Expected)) {
        throw "Expected exactly one backend identity '$Expected' below '$Root'."
    }
}

function Copy-Fixture {
    param([string] $Destination)

    New-Item -ItemType Directory -Path $Destination | Out-Null
    Copy-Item (Join-Path $fixture 'ReferencedAssets') $Destination -Recurse
    Copy-Item (Join-Path $fixture 'Consumer') $Destination -Recurse
}

function Invoke-BackendAcceptance {
    param(
        [string] $Name,
        [string] $PackageId,
        [string] $Project,
        [string] $TargetFramework,
        [string] $PlatformProperties,
        [string] $ExpectedBackendIdentity,
        [string] $RuntimeIdentifier = '',
        [string] $ConsumerMauiVersion = $mauiVersion
    )

    Write-Host "=== $Name resource acceptance ==="
    Invoke-DotNet @('pack', $Project, '-c', 'Release', '-o', $packages.FullName,
        "-p:PackageVersion=$version", '-v:minimal')

    $root = Join-Path $work $Name
    Copy-Fixture $root
    '<Project />' | Set-Content (Join-Path $root 'Directory.Build.props')
    '<Project />' | Set-Content (Join-Path $root 'Directory.Build.targets')
    '<Project />' | Set-Content (Join-Path $root 'Directory.Packages.props')
    $config = Join-Path $root 'NuGet.config'
    Add-PackageSource $config

    $tokens = @{
        '__TARGET_FRAMEWORK__' = $TargetFramework
        '__MAUI_VERSION__' = $ConsumerMauiVersion
        '__BACKEND_PACKAGE__' = $PackageId
        '__BACKEND_VERSION__' = $version
        '__PLATFORM_PROPERTIES__' = $PlatformProperties
    }
    $libraryProject = Join-Path $root 'ReferencedAssets\ReferencedAssets.csproj'
    $consumerProject = Join-Path $root 'Consumer\Consumer.csproj'
    Set-Tokens $libraryProject $tokens
    Set-Tokens $consumerProject $tokens

    $restoreArguments = @('--configfile', $config, '--packages', $restore.FullName)
    $buildArguments = @('-c', 'Release', '-m:1', '-nr:false')
    if ($RuntimeIdentifier) {
        $restoreArguments += @('-r', $RuntimeIdentifier)
        $buildArguments += @('-r', $RuntimeIdentifier)
    }
    Invoke-DotNet (@('restore', $consumerProject) + $restoreArguments)
    Invoke-DotNet (@('build', $consumerProject, '--no-restore') + $buildArguments)

    $output = Join-Path $root 'Consumer\bin\Release'
    $backendIdentity = @(Get-ChildItem -LiteralPath $output -File -Recurse -Filter 'resource-acceptance-backend.txt')
    if ($backendIdentity.Count -ne 1 -or ([System.IO.File]::ReadAllText($backendIdentity[0].FullName).Trim() -cne $ExpectedBackendIdentity)) {
        throw "Expected exactly one backend identity '$ExpectedBackendIdentity' for $Name."
    }
    $payloadV1 = [System.Text.Encoding]::UTF8.GetBytes("referenced-payload-v1`n")
    $unicode = [System.Text.Encoding]::UTF8.GetBytes("héllo from the referenced asset library`n")
    $local = [System.Text.Encoding]::UTF8.GetBytes("consumer-local-v1`n")
    Assert-SingleFile $output 'Shared/Nested/payload.bin' $payloadV1 | Out-Null
    Assert-SingleFile $output 'Shared/Unicode/café.txt' $unicode | Out-Null
    Assert-SingleFile $output 'Shared/zero.bin' ([byte[]]::new(0)) | Out-Null
    Assert-SingleFile $output 'App/local.txt' $local | Out-Null

    $payload = Join-Path $root 'ReferencedAssets\Resources\Raw\Nested\payload.bin'
    [System.IO.File]::WriteAllText($payload, "referenced-payload-v2`n")
    Invoke-DotNet (@('build', $consumerProject, '--no-restore') + $buildArguments)
    $payloadV2 = [System.Text.Encoding]::UTF8.GetBytes("referenced-payload-v2`n")
    Assert-SingleFile $output 'Shared/Nested/payload.bin' $payloadV2 | Out-Null

    $libraryText = [System.IO.File]::ReadAllText($libraryProject)
    $libraryText = $libraryText.Replace('Shared/Nested/payload.bin', 'Shared/Renamed/payload.bin')
    [System.IO.File]::WriteAllText($libraryProject, $libraryText)
    Invoke-DotNet (@('build', $consumerProject, '--no-restore') + $buildArguments)
    Assert-SingleFile $output 'Shared/Renamed/payload.bin' $payloadV2 | Out-Null
    Assert-NoFile $output 'Shared/Nested/payload.bin'

    $libraryText = [System.IO.File]::ReadAllText($libraryProject)
    $libraryText = $libraryText.Replace('    <MauiAsset Include="Resources/Raw/zero.bin" LogicalName="Shared/zero.bin" />' + [Environment]::NewLine, '')
    [System.IO.File]::WriteAllText($libraryProject, $libraryText)
    Invoke-DotNet (@('build', $consumerProject, '--no-restore') + $buildArguments)
    Assert-NoFile $output 'Shared/zero.bin'

    $publish = Join-Path $root 'publish'
    Invoke-DotNet (@('publish', $consumerProject, '--no-build', '--no-restore', "-p:PublishDir=$publish") + $buildArguments)
    Assert-SingleFile $publish 'Shared/Renamed/payload.bin' $payloadV2 | Out-Null
    Assert-SingleFile $publish 'Shared/Unicode/café.txt' $unicode | Out-Null
    Assert-SingleFile $publish 'App/local.txt' $local | Out-Null
    Assert-NoFile $publish 'Shared/Nested/payload.bin'
    Assert-NoFile $publish 'Shared/zero.bin'

    $collision = Join-Path $root 'Consumer\Resources\Raw\collision.bin'
    [System.IO.File]::WriteAllText($collision, "collision`n")
    $consumerText = [System.IO.File]::ReadAllText($consumerProject)
    $consumerText = $consumerText.Replace(
        '    <MauiAsset Include="Resources/Raw/local.txt" LogicalName="App/local.txt" />',
        "    <MauiAsset Include=`"Resources/Raw/local.txt`" LogicalName=`"App/local.txt`" />$([Environment]::NewLine)    <MauiAsset Include=`"Resources/Raw/collision.bin`" LogicalName=`"Shared/Renamed/payload.bin`" />")
    [System.IO.File]::WriteAllText($consumerProject, $consumerText)
    $collisionPublish = Join-Path $root 'collision-publish'
    Invoke-DotNet (@('publish', $consumerProject, '--no-restore', "-p:PublishDir=$collisionPublish") + $buildArguments) -ExpectFailure

    Write-Host "PASS ${Name}: referenced-library bytes/names, incremental mutation, stale removal, no-build publish, and collision failure."
}

function Invoke-BackendSwitchAcceptance {
    Write-Host '=== same-consumer backend switch acceptance ==='
    $gtkProject = Join-Path $repo 'platforms\Linux.Gtk4\src\Linux.Gtk4\Linux.Gtk4.csproj'
    $wpfProject = Join-Path $repo 'platforms\Windows.WPF\src\Windows.WPF\Windows.WPF.csproj'
    Invoke-DotNet @('pack', $gtkProject, '-c', 'Release', '-o', $packages.FullName,
        "-p:PackageVersion=$version", '-v:minimal')
    Invoke-DotNet @('pack', $wpfProject, '-c', 'Release', '-o', $packages.FullName,
        "-p:PackageVersion=$version", '-v:minimal')

    $root = Join-Path $work 'backend-switch'
    Copy-Fixture $root
    '<Project />' | Set-Content (Join-Path $root 'Directory.Build.props')
    '<Project />' | Set-Content (Join-Path $root 'Directory.Build.targets')
    '<Project />' | Set-Content (Join-Path $root 'Directory.Packages.props')
    $config = Join-Path $root 'NuGet.config'
    Add-PackageSource $config
    $libraryProject = Join-Path $root 'ReferencedAssets\ReferencedAssets.csproj'
    $consumerProject = Join-Path $root 'Consumer\Consumer.csproj'

    function Set-BackendProjects {
        param(
            [string] $TargetFramework,
            [string] $PackageId,
            [string] $PlatformProperties,
            [string] $ConsumerMauiVersion = $mauiVersion
        )

        Copy-Item (Join-Path $fixture 'ReferencedAssets\ReferencedAssets.csproj') $libraryProject -Force
        Copy-Item (Join-Path $fixture 'Consumer\Consumer.csproj') $consumerProject -Force
        $tokens = @{
            '__TARGET_FRAMEWORK__' = $TargetFramework
            '__MAUI_VERSION__' = $ConsumerMauiVersion
            '__BACKEND_PACKAGE__' = $PackageId
            '__BACKEND_VERSION__' = $version
            '__PLATFORM_PROPERTIES__' = $PlatformProperties
        }
        Set-Tokens $libraryProject $tokens
        Set-Tokens $consumerProject $tokens
    }

    Set-BackendProjects `
        -TargetFramework 'net11.0' `
        -PackageId 'Microsoft.Maui.Platforms.Linux.Gtk4' `
        -PlatformProperties ''
    Invoke-DotNet @('restore', $consumerProject, '--configfile', $config, '--packages', $restore.FullName)
    Invoke-DotNet @('build', $consumerProject, '--no-restore', '-c', 'Release', '-m:1', '-nr:false')
    $gtkOutput = Join-Path $root 'Consumer\bin\Release\net11.0'
    Assert-BackendIdentity $gtkOutput 'linux'
    if (-not (Test-Path (Join-Path $gtkOutput 'Microsoft.Maui.Platforms.Linux.Gtk4.dll'))) {
        throw 'GTK backend switch stage did not contain the GTK backend assembly.'
    }
    Assert-NoFile $gtkOutput 'Microsoft.Maui.Platforms.Windows.WPF.dll'

    $wpfPlatformProperties = '<UseWPF>true</UseWPF><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableWindowsTargeting>true</EnableWindowsTargeting><PublishReadyToRun>false</PublishReadyToRun><EnableDefaultApplicationDefinition>false</EnableDefaultApplicationDefinition><EnableDefaultPageItems>false</EnableDefaultPageItems>'
    Set-BackendProjects `
        -TargetFramework 'net11.0-windows10.0.17763.0' `
        -PackageId 'Microsoft.Maui.Platforms.Windows.WPF' `
        -PlatformProperties $wpfPlatformProperties
    Invoke-DotNet @('restore', $consumerProject, '--configfile', $config, '--packages', $restore.FullName, '-r', 'win-x64')
    Invoke-DotNet @('build', $consumerProject, '--no-restore', '-c', 'Release', '-r', 'win-x64', '-m:1', '-nr:false')
    $wpfOutput = Join-Path $root 'Consumer\bin\Release\net11.0-windows10.0.17763.0\win-x64'
    Assert-BackendIdentity $wpfOutput 'wpf'
    if (-not (Test-Path (Join-Path $wpfOutput 'Microsoft.Maui.Platforms.Windows.WPF.dll'))) {
        throw 'WPF backend switch stage did not contain the WPF backend assembly.'
    }
    Assert-NoFile $wpfOutput 'Microsoft.Maui.Platforms.Linux.Gtk4.dll'
    Assert-SingleFile $wpfOutput 'Shared/Nested/payload.bin' ([System.Text.Encoding]::UTF8.GetBytes("referenced-payload-v1`n")) | Out-Null

    Set-BackendProjects `
        -TargetFramework 'net10.0' `
        -PackageId 'Microsoft.Maui.Platforms.Linux.Gtk4' `
        -PlatformProperties '' `
        -ConsumerMauiVersion '10.0.110'
    Invoke-DotNet @('restore', $consumerProject, '--configfile', $config, '--packages', $restore.FullName)
    Invoke-DotNet @('build', $consumerProject, '--no-restore', '-c', 'Release', '-m:1', '-nr:false')
    $gtk10Output = Join-Path $root 'Consumer\bin\Release\net10.0'
    Assert-BackendIdentity $gtk10Output ''
    if (-not (Test-Path (Join-Path $gtk10Output 'Microsoft.Maui.Platforms.Linux.Gtk4.dll'))) {
        throw 'GTK .NET 10 compatibility stage did not contain the GTK backend assembly.'
    }

    Set-BackendProjects `
        -TargetFramework 'net10.0-windows10.0.17763.0' `
        -PackageId 'Microsoft.Maui.Platforms.Windows.WPF' `
        -PlatformProperties $wpfPlatformProperties `
        -ConsumerMauiVersion '10.0.110'
    Invoke-DotNet @('restore', $consumerProject, '--configfile', $config, '--packages', $restore.FullName, '-r', 'win-x64')
    Invoke-DotNet @('build', $consumerProject, '--no-restore', '-c', 'Release', '-r', 'win-x64', '-m:1', '-nr:false')
    $wpf10Output = Join-Path $root 'Consumer\bin\Release\net10.0-windows10.0.17763.0\win-x64'
    Assert-BackendIdentity $wpf10Output ''
    if (-not (Test-Path (Join-Path $wpf10Output 'Microsoft.Maui.Platforms.Windows.WPF.dll'))) {
        throw 'WPF .NET 10 compatibility stage did not contain the WPF backend assembly.'
    }
    Assert-NoFile $wpf10Output 'Microsoft.Maui.Platforms.Linux.Gtk4.dll'

    Write-Host 'PASS backend-switch: one consumer switched GTK/WPF on .NET 11 without leakage and retained .NET 10 package compatibility.'
}

try {
    if ($Backend -in @('all', 'gtk')) {
        Invoke-BackendAcceptance `
            -Name 'gtk' `
            -PackageId 'Microsoft.Maui.Platforms.Linux.Gtk4' `
            -Project (Join-Path $repo 'platforms\Linux.Gtk4\src\Linux.Gtk4\Linux.Gtk4.csproj') `
            -TargetFramework 'net11.0' `
            -PlatformProperties '' `
            -ExpectedBackendIdentity 'linux'

        Invoke-BackendAcceptance `
            -Name 'gtk-net10' `
            -PackageId 'Microsoft.Maui.Platforms.Linux.Gtk4' `
            -Project (Join-Path $repo 'platforms\Linux.Gtk4\src\Linux.Gtk4\Linux.Gtk4.csproj') `
            -TargetFramework 'net10.0' `
            -PlatformProperties '' `
            -ExpectedBackendIdentity '' `
            -ConsumerMauiVersion '10.0.110'
    }

    if ($Backend -in @('all', 'wpf')) {
        Invoke-BackendAcceptance `
            -Name 'wpf' `
            -PackageId 'Microsoft.Maui.Platforms.Windows.WPF' `
            -Project (Join-Path $repo 'platforms\Windows.WPF\src\Windows.WPF\Windows.WPF.csproj') `
            -TargetFramework 'net11.0-windows10.0.17763.0' `
            -PlatformProperties '<UseWPF>true</UseWPF><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableWindowsTargeting>true</EnableWindowsTargeting><PublishReadyToRun>false</PublishReadyToRun><EnableDefaultApplicationDefinition>false</EnableDefaultApplicationDefinition><EnableDefaultPageItems>false</EnableDefaultPageItems>' `
            -ExpectedBackendIdentity 'wpf' `
            -RuntimeIdentifier 'win-x64'
    }

    if ($Backend -in @('all', 'appkit')) {
        Invoke-BackendAcceptance `
            -Name 'appkit' `
            -PackageId 'Microsoft.Maui.Platforms.MacOS' `
            -Project (Join-Path $repo 'platforms\MacOS\src\MacOS\MacOS.csproj') `
            -TargetFramework 'net11.0-macos' `
            -PlatformProperties '<RuntimeIdentifier>osx-arm64</RuntimeIdentifier><ValidateXcodeVersion>false</ValidateXcodeVersion><ApplicationId>com.microsoft.maui.resourceacceptance</ApplicationId><ApplicationTitle>Resource Acceptance</ApplicationTitle><ApplicationVersion>1</ApplicationVersion><ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>' `
            -ExpectedBackendIdentity 'macos' `
            -RuntimeIdentifier 'osx-arm64'
    }

    if ($Backend -in @('all', 'switch')) {
        Invoke-BackendSwitchAcceptance
    }
}
finally {
    if ($KeepArtifacts) {
        Write-Host "Artifacts: $work"
    }
    else {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
