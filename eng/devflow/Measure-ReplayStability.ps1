#Requires -Version 7.3
# Measures replay stability: N consecutive runs of a known-green flow.
# Reports first-attempt pass rate with a Wilson 95% interval.
param(
    [int] $Runs = 5,
    [string] $Flow = 'samples\DevFlow.Sample\maui-tests\verified-add-todo.md',
    [string] $Device = 'emulator-5554',
    # Overridable so a test can measure the reporting logic against a stub instead of a device.
    [string] $CliPath,
    [string] $AdbPath
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Set-Location $repo

$maui = if ([string]::IsNullOrWhiteSpace($CliPath)) {
    Join-Path $repo 'artifacts\bin\Microsoft.Maui.Cli\Debug\net10.0\maui.exe'
}
else {
    $CliPath
}
$adb = if ([string]::IsNullOrWhiteSpace($AdbPath)) {
    Join-Path $env:USERPROFILE '.maui\android-sdk\platform-tools\adb.exe'
}
else {
    $AdbPath
}
$app = 'samples\DevFlow.Sample\DevFlow.Sample.csproj'
$pkg = 'com.companyname.mauitodo'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
# Two measurements started in the same second must not share, or delete, each other's evidence.
$root = "artifacts\devflow\stability-$stamp-$PID"

# A missing CLI would make every run fail identically and be reported as a 0/N stability result,
# which reads as a catastrophically flaky replay loop rather than as a build that was never made.
if (-not (Test-Path -LiteralPath $maui -PathType Leaf)) {
    Write-Error "replay-stability: The CLI was not found at '$maui'. Build it before measuring: dotnet build src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj"
    exit 3
}
# adb is what puts each run back to the same starting state. Without it every run after the first
# measures an app that the previous run already installed and left data behind in, which is not the
# first-attempt replay this number is quoted as. A missing adb also writes an error per run and
# keeps going, so the measurement would look complete while never having reset anything.
if (-not (Test-Path -LiteralPath $adb -PathType Leaf) -and
    $null -eq (Get-Command -Name $adb -CommandType Application, ExternalScript -ErrorAction SilentlyContinue)) {
    Write-Error "replay-stability: adb was not found at '$adb'. Install the Android platform-tools or pass -AdbPath, because every run must start from an uninstalled app."
    exit 3
}
if ($Runs -lt 1) {
    Write-Error 'replay-stability: -Runs must be at least 1.'
    exit 2
}

# The summary is written into this directory at the end, so it has to exist even when the CLI
# never creates a per-run output folder of its own.
[void] (New-Item -ItemType Directory -Force -Path $root)

# A replay either passed or it did not, and the run report's primary outcome says which: only
# outcome.status decides a pass. outcome.verified is a separate axis - whether an independent
# business oracle agreed - and a passing run with no oracle is still a pass, reported unverified.
# Host-owned failures that happened around the run, such as owned cleanup, are a third fact:
# folding them into the replay verdict understates stability and hides the cleanup problem, while
# reading a displaced verdict would overstate it.
function Get-RunVerdict {
    param(
        [AllowNull()] $Report,
        [AllowNull()] $CliJson
    )

    $verdict = [ordered]@{
        status = $null
        verified = $null
        passed = $false
        source = 'none'
        secondaryFailures = 0
        cleanupFailed = $false
    }

    if ($null -ne $Report) {
        $outcome = $Report.PSObject.Properties['outcome']
        if ($null -ne $outcome -and $null -ne $outcome.Value) {
            $status = $outcome.Value.PSObject.Properties['status']
            if ($null -ne $status -and -not [string]::IsNullOrWhiteSpace([string] $status.Value)) {
                $verdict.status = [string] $status.Value
                $verdict.source = 'outcome.status'
            }
            $verified = $outcome.Value.PSObject.Properties['verified']
            if ($null -ne $verified -and $verified.Value -is [bool]) {
                $verdict.verified = [bool] $verified.Value
            }
        }

        $secondary = $Report.PSObject.Properties['secondaryFailures']
        if ($null -ne $secondary -and $null -ne $secondary.Value) {
            $entries = @($secondary.Value)
            $verdict.secondaryFailures = $entries.Count
            $verdict.cleanupFailed = @($entries | Where-Object {
                    $null -ne $_ -and
                    $null -ne $_.PSObject.Properties['phase'] -and
                    ([string] $_.phase) -like '*cleanup*'
                }).Count -gt 0
        }
    }

    if ($null -ne $verdict.status) {
        $verdict.passed = $verdict.status -ceq 'passed'
        return [pscustomobject] $verdict
    }

    # Compatibility with artifacts written before the run outcome was structured: for those runs
    # the CLI envelope is the only verdict that was ever recorded.
    if ($null -ne $CliJson) {
        $exitCategory = $CliJson.PSObject.Properties['exitCategory']
        if ($null -ne $exitCategory -and -not [string]::IsNullOrWhiteSpace([string] $exitCategory.Value)) {
            $verdict.status = [string] $exitCategory.Value
            $verdict.source = 'legacy:exitCategory'
            $verdict.passed = $verdict.status -ceq 'pass'
            return [pscustomobject] $verdict
        }

        $ok = $CliJson.PSObject.Properties['ok']
        if ($null -ne $ok -and $ok.Value -is [bool]) {
            $verdict.status = if ([bool] $ok.Value) { 'pass' } else { 'fail' }
            $verdict.source = 'legacy:ok'
            $verdict.passed = [bool] $ok.Value
        }
    }

    return [pscustomobject] $verdict
}

# Stated from what the runs actually reported, not from the rule this script prefers. A run whose
# artifact predates the structured outcome is decided by the legacy CLI envelope, and a measurement
# that claimed 'outcome.status' for it would misdescribe its own evidence.
function Get-PassSource {
    param([AllowNull()] $Results)

    $sources = @(@($Results) |
            Where-Object { $null -ne $_ } |
            ForEach-Object { [string] $_.VerdictSource } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique)
    if ($sources.Count -eq 0) {
        return 'none'
    }

    return ($sources -join '+')
}

$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    Write-Host ''
    Write-Host "--- run $i of $Runs ---" -ForegroundColor Cyan
    & $adb uninstall $pkg 2>&1 | Out-Null
    $out = Join-Path $root "run$i"
    $raw = & $maui devflow flow run $Flow --project $app --platform android `
        --device $Device --output $out --json 2>&1 | Out-String
    $cliJson = $null; $cat = $null
    try {
        $cliJson = $raw | ConvertFrom-Json
        $cat = $cliJson.exitCategory
    } catch { }
    $report = Join-Path $out 'flow-run.json'
    $reportJson = $null
    if (Test-Path $report) {
        try { $reportJson = Get-Content $report -Raw | ConvertFrom-Json } catch { }
    }
    $verdict = Get-RunVerdict -Report $reportJson -CliJson $cliJson
    Write-Host ("  status={0} verified={1} passed={2} secondaryFailures={3} exitCategory={4}" -f `
            $verdict.status, $verdict.verified, $verdict.passed, $verdict.secondaryFailures, $cat)
    $results += [pscustomobject]@{
        Run = $i
        Status = $verdict.status
        Passed = $verdict.passed
        Verified = $verdict.verified
        VerdictSource = $verdict.source
        SecondaryFailures = $verdict.secondaryFailures
        CleanupFailed = $verdict.cleanupFailed
        ExitCategory = $cat
    }
}

$n = $results.Count
$passes = ($results | Where-Object { $_.Passed }).Count
$verifiedCount = ($results | Where-Object { $_.Verified -eq $true }).Count
$secondaryFailureRuns = ($results | Where-Object { $_.SecondaryFailures -gt 0 }).Count
$cleanupFailureRuns = ($results | Where-Object { $_.CleanupFailed }).Count

function WilsonInterval {
    param([int] $Successes, [int] $Total)
    if ($Total -eq 0) { return @(0, 0) }
    $z = 1.959963985
    $p = $Successes / $Total
    $d = 1 + ($z * $z / $Total)
    $c = $p + ($z * $z / (2 * $Total))
    $m = $z * [Math]::Sqrt(($p * (1 - $p) / $Total) + ($z * $z / (4 * $Total * $Total)))
    return @([Math]::Round(($c - $m) / $d, 4), [Math]::Round(($c + $m) / $d, 4))
}

$ci = WilsonInterval -Successes $passes -Total $n
$passSource = Get-PassSource $results
Write-Host ''
Write-Host '=========================================================' -ForegroundColor DarkCyan
Write-Host '  REPLAY STABILITY' -ForegroundColor Cyan
Write-Host '=========================================================' -ForegroundColor DarkCyan
$results | Format-Table -AutoSize | Out-String -Width 140 | Write-Host
Write-Host ("  first-attempt pass : {0}/{1}" -f $passes, $n)
Write-Host ("  verified           : {0}/{1}" -f $verifiedCount, $n)
Write-Host ("  secondary failures : {0}/{1} runs (cleanup: {2})" -f $secondaryFailureRuns, $n, $cleanupFailureRuns)
Write-Host ("  wilson 95%         : [{0}, {1}]" -f $ci[0], $ci[1])
Write-Host ("  pass source        : {0}" -f $passSource)
Write-Host ("  artifacts          : {0}" -f $root)

$summary = [pscustomobject]@{
    metric               = 'replay-stability'
    flow                 = $Flow
    platform             = 'android'
    device               = $Device
    runs                 = $n
    firstAttemptPasses   = $passes
    verifiedRuns         = $verifiedCount
    secondaryFailureRuns = $secondaryFailureRuns
    cleanupFailureRuns   = $cleanupFailureRuns
    # Stated from what the runs actually reported, not from the rule this script prefers. A run
    # whose artifact predates the structured outcome is decided by the legacy CLI envelope, and a
    # measurement that claimed 'outcome.status' for it would misdescribe its own evidence.
    passSource           = $passSource
    value                = if ($n) { [Math]::Round($passes / $n, 4) } else { $null }
    confidenceInterval   = @{ method = 'wilson-95'; lower = $ci[0]; upper = $ci[1] }
    measuredAtUtc        = (Get-Date).ToUniversalTime().ToString('o')
    runs_detail          = $results
}
$summaryPath = Join-Path $root 'replay-stability.json'
$summary | ConvertTo-Json -Depth 6 | Set-Content $summaryPath
Write-Host ("  summary            : {0}" -f $summaryPath)
