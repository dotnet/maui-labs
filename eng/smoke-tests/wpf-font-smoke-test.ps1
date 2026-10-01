[CmdletBinding(DefaultParameterSetName = 'HandlerTests')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'HandlerTests')][string] $TestAssembly,
    [Parameter(Mandatory = $true, ParameterSetName = 'Sample')][string] $SampleAssembly,
    [Parameter(Mandatory = $true, ParameterSetName = 'Sample')][string] $ControlFont,
    [string] $DotNet = "dotnet",
    [string] $ResultsDirectory = "artifacts\TestResults\WpfFonts"
)

$ErrorActionPreference = "Stop"
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$dotnetPath = (Get-Command $DotNet -ErrorAction Stop).Source

if ($PSCmdlet.ParameterSetName -eq 'Sample') {
    $assembly = (Resolve-Path $SampleAssembly).Path
    $output = Split-Path $assembly
    $control = (Resolve-Path $ControlFont).Path
    $isolated = New-Item -ItemType Directory -Force (Join-Path $results 'IsolatedCwd')
    $alternate = New-Item -ItemType Directory -Force (Join-Path $results 'AlternateCwd')
    $directories = [ordered]@{
        Output = $output
        Isolated = $isolated.FullName
        Alternate = $alternate.FullName
    }
    foreach ($directory in $directories.Values) {
        $prefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($directory)) + [IO.Path]::DirectorySeparatorChar
        if ($control.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Fixture failure: the control font must be outside the app base and every tested working directory.'
        }
    }
    $expectedCases = @{
        'trusted-native-control' = 'fixture'
        'native-font-identity' = 'rendering'
        'native-family-and-cmap' = 'rendering'
        'label-glyph-runs' = 'rendering'
        'label-native-pixels' = 'rendering'
        'font-image-native-metrics' = 'rendering'
        'font-image-native-pixels' = 'rendering'
        'portable-font-origin' = 'portability'
        'route-diagnostics' = 'diagnostic'
    }
    $renderingFailures = @()
    $unprovenPortability = @()
    foreach ($entry in $directories.GetEnumerator()) {
        $destination = New-Item -ItemType Directory -Force (Join-Path $results $entry.Key)
        $start = [Diagnostics.ProcessStartInfo]::new($dotnetPath)
        $start.WorkingDirectory = $entry.Value
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.Environment['MAUI_WPF_CONTROL_FONT'] = $control
        foreach ($argument in @($assembly, '--test-scenario', 'registered-fonts')) {
            $start.ArgumentList.Add($argument)
        }
        $process = [Diagnostics.Process]::Start($start)
        try {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (!$process.WaitForExit(30000)) {
                $process.Kill($true)
                throw "Native font scenario timed out from $($entry.Value)."
            }
            $text = $stdout.GetAwaiter().GetResult()
            $text | Set-Content (Join-Path $destination.FullName 'stdout.log')
            $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $destination.FullName 'stderr.log')
            $summaries = @($text -split '\r?\n' | Where-Object { $_.StartsWith('FONT_SCENARIO_RESULT:') })
            if ($summaries.Count -ne 1) {
                throw "Expected one actual native scenario result from $($entry.Value)."
            }
            $cases = @($summaries[0].Substring('FONT_SCENARIO_RESULT:'.Length) | ConvertFrom-Json)
            $cases | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination.FullName 'cases.json')
            if ($cases.Count -ne $expectedCases.Count -or
                (Compare-Object ($expectedCases.Keys | Sort-Object) ($cases.Name | Sort-Object))) {
                throw "Fixture failure: native font scenario did not execute the required nine cases from $($entry.Value)."
            }
            foreach ($case in $cases) {
                if ($case.Category -ne $expectedCases[$case.Name] -or $case.Passed -isnot [bool]) {
                    throw 'Fixture failure: unexpected native case category or result type.'
                }
            }
            $failed = @($cases | Where-Object { !$_.Passed })
            if (@($failed | Where-Object Category -in @('fixture', 'diagnostic')).Count) {
                throw "Fixture failure: control or diagnostics failed from $($entry.Value); not baseline regression evidence."
            }
            $expectedExit = 0
            if (@($failed | Where-Object Category -eq 'rendering').Count) {
                $expectedExit = 1
                $renderingFailures += $entry.Key
            } elseif (@($failed | Where-Object Category -eq 'portability').Count) {
                $expectedExit = 4
                $unprovenPortability += $entry.Key
            }
            if ($process.ExitCode -ne $expectedExit) {
                throw "Fixture failure: child exit $($process.ExitCode) disagrees with its classified cases (expected $expectedExit)."
            }
            Write-Host "$($entry.Key): $($cases.Count) cases; $($failed.Count) failed; classified exit=$expectedExit."
        } finally {
            $process.Dispose()
        }
    }
    if ($renderingFailures.Count -gt 0) {
        throw "RENDERING_FAILURE with passing native control: $($renderingFailures -join ', '); see $results."
    }
    if ($unprovenPortability.Count -gt 0) {
        throw "PORTABILITY_UNPROVEN (not a rendering failure): $($unprovenPortability -join ', '); inspect $results."
    }
    return
}

$assembly = (Resolve-Path $TestAssembly).Path
$sdkVersion = & $dotnetPath --version
if ($LASTEXITCODE -ne 0) { throw "Cannot determine the .NET SDK version." }
$sdkList = & $dotnetPath --list-sdks
if ($LASTEXITCODE -ne 0) { throw "Cannot locate the .NET SDK." }
$sdkLine = $sdkList | Where-Object { $_.StartsWith("$sdkVersion ") } | Select-Object -First 1
if (!$sdkLine -or $sdkLine -notmatch '\[(.+)\]') { throw "SDK $sdkVersion was not found." }
$vstest = Join-Path $Matches[1] "$sdkVersion\vstest.console.dll"
$output = Split-Path $assembly
$directories = [ordered]@{
    Output = $output
    DriveRoot = [IO.Path]::GetPathRoot($output)
    Fonts = Join-Path $output "Resources\Fonts"
}
$expectedTests = @{
    CompositeFont_TextMatchesNativeAndGlyphImagesRejectIt = 2
    GlyphBitmap_PreservesNativeAdvanceAndLineBox = 1
    FontManagerOverride_PreservesPublicServiceAndNativeResolution = 1
    FontFormatFailure_LogsAndDoesNotRenderFallbackGlyph = 1
    PasswordEntry_CreatingNativeControl_AppliesRegisteredFont = 1
    GraphicsCanvas_RegisteredFont_MeasurementMatchesNativeDrawing = 1
    GlyphSize_ExceedsLimit_ReturnsNoImageAndLogs = 1
    GlyphBitmapBounds_AreLimitedBeforeAllocation = 1
    MissingFontCache_RemainsFailureForGlyphsAndLogsOnce = 1
    EmbeddedFont_TransientExtractionFailure_CanRetry = 1
    EmbeddedFont_AmbiguousShortName_RequiresFullResourceName = 1
    NativeToolbar_WithoutMauiContext_PreservesTextFallback = 1
    TextControlHandlers_ResolveAliases = 1
    RegisteredAbsolutePath_WithSpacesAndHash_UsesDeclaredFamily = 1
    SystemFontAndDefault_AreStillNativeFonts = 1
    IconFont_RendersRegisteredGlyphInsteadOfFallbackBox = 1
    FontImageSources_UseSameNativeRendererAcrossSurfaces = 1
    UnavailableFontOrGlyph_ReturnsNoImageAndLogs = 2
    MissingRegisteredFont_LogsAndUsesExplicitDefaultForText = 1
    RegisteredFont_LoadsDeclaredNativeFamily = 1
    EmbeddedFont_LoadsFromSpecifiedAssembly = 1
    Label_ConfigureFontsAlias_UsesNativeFont = 1
}
$expectedCount = ($expectedTests.Values | Measure-Object -Sum).Sum

foreach ($entry in $directories.GetEnumerator()) {
    $destination = Join-Path $results $entry.Key
    New-Item -ItemType Directory -Force $destination | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new($dotnetPath)
    $start.WorkingDirectory = $entry.Value
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment["MAUI_WPF_FONT_EVIDENCE_DIR"] = $destination
    foreach ($argument in @($vstest, $assembly, "/TestCaseFilter:FullyQualifiedName~HandlerTests.FontTests",
        "/Logger:trx;LogFileName=fonts.trx", "/ResultsDirectory:$destination")) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(120000)) {
        $process.Kill($true)
        throw "Font tests timed out from $($entry.Value)."
    }
    $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $destination "stdout.log")
    $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $destination "stderr.log")
    if ($process.ExitCode -ne 0) { throw "Font tests failed from $($entry.Value); see $destination." }
    [xml]$trx = Get-Content (Join-Path $destination "fonts.trx")
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.executed -ne $expectedCount -or [int]$counters.failed -ne 0 -or $counters.executed -ne $counters.passed) {
        throw "Font tests did not all execute successfully from $($entry.Value)."
    }
    foreach ($test in $expectedTests.GetEnumerator()) {
        $name = "HandlerTests.FontTests.$($test.Key)"
        $passedCases = @($trx.TestRun.Results.UnitTestResult | Where-Object {
            ($_.testName -eq $name -or $_.testName.StartsWith("$name(")) -and $_.outcome -eq "Passed"
        })
        if ($passedCases.Count -ne $test.Value) { throw "Expected $($test.Value) passing case(s) of $name from $($entry.Value)." }
    }
    Write-Host "$($entry.Key): $($counters.passed) native font tests passed."
}

foreach ($image in @("registered-label.png", "registered-icon.png")) {
    $hashes = @($directories.Keys | ForEach-Object {
        (Get-FileHash (Join-Path $results "$_\$image")).Hash
    } | Select-Object -Unique)
    if ($hashes.Count -ne 1) { throw "Rendered $image changed with the working directory." }
    Write-Host "$image is identical across working directories: $($hashes[0])"
}
