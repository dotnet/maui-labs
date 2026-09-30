[CmdletBinding()]
param(
    [string] $ArtifactsDirectory,
    [string] $PackageVersion = '0.1.0-preview.518.1',
    [string] $SdkDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) {
    throw 'The WPF template smoke test requires Windows and the MAUI workload.'
}

$dotnet = 'dotnet'
if ($SdkDirectory) {
    $SdkDirectory = (Resolve-Path $SdkDirectory).Path
    $dotnet = (Resolve-Path (Join-Path $SdkDirectory '..\..\dotnet.exe')).Path
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $ArtifactsDirectory) {
    $ArtifactsDirectory = Join-Path ([IO.Path]::GetTempPath()) "wpf-template-smoke-$([Guid]::NewGuid().ToString('N'))"
}
if (Test-Path $ArtifactsDirectory) {
    throw "Use a new artifacts directory to avoid stale packages or generated files: $ArtifactsDirectory"
}
$work = (New-Item -ItemType Directory -Path $ArtifactsDirectory).FullName
$packages = (New-Item -ItemType Directory -Path (Join-Path $work 'packages')).FullName
$hive = Join-Path $work 'hive'
$appDirectory = Join-Path $work 'app'
Write-Host "Smoke test artifacts: $work"

function Invoke-DotNet {
    param([string[]] $Arguments)
    if ($SdkDirectory) {
        & $dotnet (Join-Path $SdkDirectory 'dotnet.dll') @Arguments
    } else {
        & $dotnet @Arguments
    }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Invoke-MSBuild {
    param([string[]] $Arguments)
    if ($SdkDirectory) {
        & $dotnet (Join-Path $SdkDirectory 'MSBuild.dll') @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "MSBuild $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
        }
    } else {
        Invoke-DotNet (@('msbuild') + $Arguments)
    }
}

# Prevent the generated consumer from inheriting the repository's MAUI versions or build imports.
foreach ($file in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
    Set-Content (Join-Path $work $file) '<Project />'
}
[xml] $config = Get-Content (Join-Path $repo 'NuGet.config') -Raw
$source = $config.CreateElement('add')
$source.SetAttribute('key', 'wpf-smoke')
$source.SetAttribute('value', $packages)
[void] $config.configuration.packageSources.AppendChild($source)
$configPath = Join-Path $work 'NuGet.config'
$config.Save($configPath)

$platform = Join-Path $repo 'platforms\Windows.WPF'
$templateProject = Join-Path $platform 'templates\Windows.WPF.Templates.csproj'
$packArguments = @('-restore', '-t:Pack', '-p:Configuration=Release',
    "-p:PackageOutputPath=$packages", '-verbosity:minimal', '-nologo')

# Pack twice without cleaning to catch stale version substitution in incremental builds.
Invoke-MSBuild (@($templateProject, '-p:PackageVersion=0.0.0-template-smoke') + $packArguments)
foreach ($project in @(
    'src\Windows.WPF\Windows.WPF.csproj',
    'src\Windows.WPF.Essentials\Windows.WPF.Essentials.csproj',
    'templates\Windows.WPF.Templates.csproj'
)) {
    Invoke-MSBuild (@((Join-Path $platform $project), "-p:PackageVersion=$PackageVersion") + $packArguments)
}

$templatePackage = Join-Path $packages "Microsoft.Maui.Platforms.Windows.WPF.Templates.$PackageVersion.nupkg"
Invoke-DotNet -Arguments @('new', 'install', $templatePackage, '--debug:custom-hive', $hive)
Invoke-DotNet -Arguments @('new', 'maui-wpf', '-n', 'TemplateSmoke.WPF', '-o', $appDirectory, '--debug:custom-hive', $hive)
$appProject = Join-Path $appDirectory 'TemplateSmoke.WPF.csproj'
[xml] $projectXml = Get-Content $appProject -Raw
$references = @{}
foreach ($reference in $projectXml.Project.ItemGroup.PackageReference) {
    $references[$reference.Include] = $reference.Version
}
foreach ($id in @('Microsoft.Maui.Platforms.Windows.WPF', 'Microsoft.Maui.Platforms.Windows.WPF.Essentials')) {
    if ($references[$id] -ne $PackageVersion) {
        throw "$id must use the packed version $PackageVersion, not '$($references[$id])'."
    }
}
[xml] $platformProps = Get-Content (Join-Path $platform 'Directory.Build.props') -Raw
$mauiVersion = [string] ($platformProps.SelectSingleNode('//MauiVersion').InnerText)
if ($references['Microsoft.Maui.Controls'] -ne $mauiVersion) {
    throw "Microsoft.Maui.Controls must explicitly match the backend's MauiVersion ($mauiVersion)."
}
if ((Get-Content $appProject -Raw) -match '__\w+_VERSION__') {
    throw 'The packed template contains unresolved version tokens.'
}

Invoke-MSBuild -Arguments @($appProject, '-restore', '-t:Build', '-p:Configuration=Release',
    "-p:RestoreConfigFile=$configPath", "-p:RestorePackagesPath=$(Join-Path $work 'cache')",
    '-warnaserror:NU1605', '-verbosity:minimal', '-nologo')

$assets = Get-Content (Join-Path $appDirectory 'obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($id in $references.Keys) {
    $expected = "$id/$($references[$id])"
    if ($expected -notin $assets.libraries.PSObject.Properties.Name) {
        throw "Restore did not resolve $expected."
    }
}

$executable = Join-Path $appDirectory 'bin\Release\net10.0-windows\TemplateSmoke.WPF.exe'
$process = Start-Process -FilePath $executable -WorkingDirectory $appDirectory -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $process.Refresh()
        if ($process.HasExited) {
            throw "Generated app exited before creating a window (exit code $($process.ExitCode))."
        }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            Write-Host 'PASS: packed template restored exact dependencies, built, and opened a WPF window.'
            break
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw 'Generated app did not create a WPF window within 30 seconds.'
    }
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id
    }
    $process.Dispose()
}
