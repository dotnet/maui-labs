param(
    [Parameter(Mandatory = $true)][string] $TestAssembly,
    [string] $DotNet = "dotnet",
    [string] $ResultsDirectory = "artifacts\TestResults\WpfFonts"
)

$ErrorActionPreference = "Stop"
$assembly = (Resolve-Path $TestAssembly).Path
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$dotnetPath = (Get-Command $DotNet -ErrorAction Stop).Source
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
