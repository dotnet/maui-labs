[CmdletBinding()]
param(
    [string] $ArtifactsDirectory,
    [string] $PackageVersion = '0.1.0-preview.518.1',
    [string] $SdkDirectory,
    [switch] $FontScenarios,
    [ValidateSet('win-x64', 'win-arm64')][string] $RuntimeIdentifier = 'win-x64'
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
$packArguments = @('-restore', '-t:Pack', '-p:Configuration=Release', '-m:1', '-nr:false',
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

if ($FontScenarios) {
    $backendPackage = Join-Path $packages "Microsoft.Maui.Platforms.Windows.WPF.$PackageVersion.nupkg"
    $mainTargetEntry = 'build/Microsoft.Maui.Platforms.Windows.WPF.targets'
    $fontTargetEntry = 'build/Fonts/Microsoft.Maui.Platforms.Windows.WPF.Fonts.targets'
    $transitiveTargetEntry = 'buildTransitive/Microsoft.Maui.Platforms.Windows.WPF.targets'
    $fontImport = '$(MSBuildThisFileDirectory)..\build\Fonts\Microsoft.Maui.Platforms.Windows.WPF.Fonts.targets'
    $packedTargets = @{}
    $archive = [IO.Compression.ZipFile]::OpenRead($backendPackage)
    try {
        foreach ($entryPath in @($mainTargetEntry, $fontTargetEntry, $transitiveTargetEntry)) {
            $entry = $archive.GetEntry($entryPath)
            if (!$entry) { throw "The actual backend package is missing $entryPath." }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $packedTargets[$entryPath] = $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
    } finally {
        $archive.Dispose()
    }
    [xml] $mainTargets = $packedTargets[$mainTargetEntry]
    [xml] $fontTargets = $packedTargets[$fontTargetEntry]
    [xml] $transitiveTargets = $packedTargets[$transitiveTargetEntry]
    if (@($mainTargets.Project.Import).Count -ne 1 -or
        $mainTargets.Project.Import.Project -cne '$(MSBuildThisFileDirectory)..\buildTransitive\Microsoft.Maui.Platforms.Windows.WPF.targets' -or
        $mainTargets.Project.Import.Condition -cne '''$(_MicrosoftMauiPlatformsWindowsWPFBuildTargetsImported)'' != ''true''') {
        throw 'The public build entry point must import the transitive resource pipeline exactly once.'
    }
    $fontImports = @($transitiveTargets.Project.Import | Where-Object Project -CEQ $fontImport)
    $importCondition = '''$(_WpfUseLegacyResourcePipeline)'' == ''true'' and ''$(_MicrosoftMauiPlatformsWindowsWPFFontsTargetsImported)'' != ''true'''
    if ($fontImports.Count -ne 1 -or $fontImports[0].Condition -cne $importCondition -or
        $fontTargets.Project.PropertyGroup._MicrosoftMauiPlatformsWindowsWPFFontsTargetsImported -cne 'true') {
        throw 'The legacy font import must remain packaged, version-gated, and protected against duplicate loading.'
    }
    foreach ($name in @('_WpfCollectProcessedMauiResources', '_WpfLegacyProcessMauiImage', '_WpfLegacyProcessMauiAsset',
        '_WpfProcessMauiIcon', '_WpfProcessMauiSplashScreen')) {
        if (@($transitiveTargets.SelectNodes("//*[local-name()='Target' and @Name='$name']")).Count -ne 1) {
            throw "The packed transitive pipeline must preserve target $name exactly once."
        }
    }
    $fontTarget = @($fontTargets.SelectNodes('//*[local-name()="Target"]'))
    if ($fontTarget.Count -ne 1 -or $fontTarget[0].Name -cne '_WpfProcessMauiFont' -or
        $fontTarget[0].BeforeTargets -cne 'AssignTargetPaths' -or $fontTarget[0].Condition -cne "@(MauiFont) != ''") {
        throw 'The packed font-only target changed its name, scheduling, or item condition.'
    }
    $content = $fontTarget[0].ItemGroup.ContentWithTargetPath
    if ($content.Include -cne '@(MauiFont)' -or $content.TargetPath -cne 'Resources\Fonts\%(Filename)%(Extension)' -or
        $content.CopyToOutputDirectory -cne 'PreserveNewest' -or $content.CopyToPublishDirectory -cne 'PreserveNewest') {
        throw 'The packed font target changed its build/publish content contract.'
    }
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
$mauiVersion = (Invoke-MSBuild @($templateProject, '-getProperty:MauiVersion', '-verbosity:quiet', '-nologo') -join "`n").Trim()
if ($references['Microsoft.Maui.Controls'] -ne $mauiVersion) {
    throw "Microsoft.Maui.Controls must explicitly match the backend's MauiVersion ($mauiVersion)."
}
$windowsSdkVersion = (Invoke-MSBuild @($templateProject, '-getProperty:MicrosoftWindowsSdkNetRefVersion', '-verbosity:quiet', '-nologo') -join "`n").Trim()
if ($projectXml.SelectSingleNode('/Project/PropertyGroup/WindowsSdkPackageVersion').InnerText -cne $windowsSdkVersion) {
    throw 'The generated consumer must use the same Windows SDK projection version as Essentials.'
}
if ((Get-Content $appProject -Raw) -match '__\w+_VERSION__') {
    throw 'The packed template contains unresolved version tokens.'
}

if ($FontScenarios) {
    # Reuse the sample's router, real font-registration callback, and native assertions.
    # Only fixture SOURCE files are copied: deployment must come from the installed NuGet target.
    $sample = Join-Path $platform 'samples\Windows.WPF.Sample'
    $fixture = New-Item -ItemType Directory -Path (Join-Path $appDirectory 'Resources\Fonts') -Force
    Copy-Item (Join-Path $sample 'Resources\Fonts\OpenSans-Regular.ttf') $fixture.FullName
    $items = $projectXml.CreateElement('ItemGroup')
    foreach ($relative in @('SampleFontRegistration.cs', 'TestScenarios\TestScenarioRunner.cs', 'TestScenarios\RegisteredFontsScenario.cs')) {
        $compile = $projectXml.CreateElement('Compile')
        $compile.SetAttribute('Include', (Join-Path $sample $relative))
        [void] $items.AppendChild($compile)
    }
    $font = $projectXml.CreateElement('MauiFont')
    $font.SetAttribute('Include', 'Resources\Fonts\OpenSans-Regular.ttf')
    [void] $items.AppendChild($font)
    [void] $projectXml.Project.AppendChild($items)
    $properties = $projectXml.CreateElement('PropertyGroup')
    $constants = $projectXml.CreateElement('DefineConstants')
    $constants.InnerText = '$(DefineConstants);WPF_TEST_SCENARIOS;WPF_FONT_TEST_SCENARIO'
    [void] $properties.AppendChild($constants)
    [void] $projectXml.Project.AppendChild($properties)
    $projectXml.Save($appProject)
    if ($projectXml.SelectNodes('//ProjectReference|//Import').Count -ne 0) {
        throw 'The packed font consumer must not import repository targets or use project references.'
    }

    $programPath = Join-Path $appDirectory 'Program.cs'
    $program = Get-Content $programPath -Raw
    $signature = 'public static void Main\(\)\s*\{'
    if ([regex]::Matches($program, $signature).Count -ne 1) {
        throw 'The generated template startup changed; update the shared scenario integration explicitly.'
    }
    $route = @'
public static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--test-scenario") >= 0)
        {
            Environment.Exit(Microsoft.Maui.Platforms.Windows.WPF.Sample.TestScenarios.TestScenarioRunner.Run(args));
            return;
        }
'@
    [regex]::Replace($program, $signature, $route) | Set-Content $programPath
}

$consumerArguments = @("-p:RuntimeIdentifier=$RuntimeIdentifier", '-p:SelfContained=false',
    '-p:PublishReadyToRun=false', '-m:1', '-nr:false')
Invoke-MSBuild -Arguments (@($appProject, '-restore', '-t:Build', '-p:Configuration=Release',
    "-p:RestoreConfigFile=$configPath", "-p:RestorePackagesPath=$(Join-Path $work 'cache')",
    '-warnaserror:NU1605', '-verbosity:minimal', '-nologo') + $consumerArguments)

$assets = Get-Content (Join-Path $appDirectory 'obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($id in $references.Keys) {
    $expected = "$id/$($references[$id])"
    if ($expected -notin $assets.libraries.PSObject.Properties.Name) {
        throw "Restore did not resolve $expected."
    }
}

$targetFramework = $projectXml.SelectSingleNode('/Project/PropertyGroup/TargetFramework').InnerText
$output = Join-Path $appDirectory "bin\Release\$targetFramework\$RuntimeIdentifier"
if ($FontScenarios) {
    $installedPackage = Join-Path $work "cache\microsoft.maui.platforms.windows.wpf\$($PackageVersion.ToLowerInvariant())"
    foreach ($entryPath in @($mainTargetEntry, $fontTargetEntry, $transitiveTargetEntry)) {
        $installed = Join-Path $installedPackage ($entryPath.Replace('/', '\'))
        if (!(Test-Path -LiteralPath $installed) -or (Get-Content $installed -Raw) -cne $packedTargets[$entryPath]) {
            throw "The consumer did not restore the tested package target: $entryPath."
        }
    }
    $installedFontTarget = Join-Path $installedPackage ($transitiveTargetEntry.Replace('/', '\'))
    $preprocessed = Join-Path $work 'font-consumer.xml'
    Invoke-MSBuild -Arguments (@($appProject, '-p:Configuration=Release', "-preprocess:$preprocessed",
        '-verbosity:quiet', '-nologo') + $consumerArguments)
    $consumerImportText = Get-Content $preprocessed -Raw
    [xml] $consumerImports = $consumerImportText
    if ($consumerImportText.IndexOf($installedFontTarget, [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
        @($consumerImports.SelectNodes('//*[local-name()="Target" and @Name="_WpfCollectProcessedMauiResources"]')).Count -ne 1 -or
        @($consumerImports.SelectNodes('//*[local-name()="Target" and @Name="_WpfProcessMauiFont"]')).Count -ne 0) {
        throw 'The .NET 11 consumer must import the processed-resource target exactly once, without the legacy font target.'
    }
    $fontItemJson = Invoke-MSBuild -Arguments (@($appProject, '-p:Configuration=Release',
        '-target:_WpfCollectProcessedMauiResources', '-getProperty:UseMaui', '-getItem:ContentWithTargetPath',
        '-verbosity:quiet', '-nologo') + $consumerArguments)
    $fontItemJson | Set-Content (Join-Path $work 'font-items.json')
    $fontItems = ($fontItemJson -join "`n") | ConvertFrom-Json
    $consumerFont = Join-Path $appDirectory 'Resources\Fonts\OpenSans-Regular.ttf'
    $copiedFonts = @($fontItems.Items.ContentWithTargetPath | Where-Object {
        $_.TargetPath -ceq 'Resources\Fonts\OpenSans-Regular.ttf'
    })
    if ($fontItems.Properties.UseMaui -ne 'true' -or $copiedFonts.Count -ne 1 -or
        $copiedFonts[0].CopyToOutputDirectory -cne 'PreserveNewest' -or
        $copiedFonts[0].CopyToPublishDirectory -cne 'PreserveNewest' -or
        (Get-FileHash -LiteralPath $copiedFonts[0].FullPath).Hash -cne
            (Get-FileHash -LiteralPath $consumerFont).Hash) {
        throw 'The real PackageReference consumer did not execute the installed font content contract.'
    }
    [ordered]@{
        PackageVersion = $PackageVersion
        PackageSha256 = (Get-FileHash $backendPackage).Hash
        MainTargetEntry = $mainTargetEntry
        FontTargetEntry = $transitiveTargetEntry
        InstalledFontTarget = $installedFontTarget
        FontTargetSha256 = (Get-FileHash $installedFontTarget).Hash
        UseMaui = $fontItems.Properties.UseMaui
        RuntimeIdentifier = $RuntimeIdentifier
        SelfContained = $false
        RestoredReferences = $references
        Content = $copiedFonts
    } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $work 'font-package-contract.json')

    $fontSmoke = Join-Path $PSScriptRoot 'wpf-font-smoke-test.ps1'
    & $fontSmoke -SampleAssembly (Join-Path $output 'TemplateSmoke.WPF.dll') -DotNet $dotnet `
        -ControlFont (Join-Path $sample 'Resources\Fonts\OpenSans-Regular.ttf') `
        -ResultsDirectory (Join-Path $work 'FontBuild')
    $publish = Join-Path $work 'publish'
    Invoke-DotNet -Arguments (@('publish', $appProject, '-c', 'Release', '--no-build', '--no-restore',
        '-o', $publish) + $consumerArguments)
    & $fontSmoke -SampleAssembly (Join-Path $publish 'TemplateSmoke.WPF.dll') -DotNet $dotnet `
        -ControlFont (Join-Path $sample 'Resources\Fonts\OpenSans-Regular.ttf') `
        -ResultsDirectory (Join-Path $work 'FontPublish')
}

$executable = Join-Path $output 'TemplateSmoke.WPF.exe'
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
