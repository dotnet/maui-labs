<#
.SYNOPSIS
    Renders a bounded, structured-fields-only summary of the flow-run.json reports produced by a
    DevFlow flow lane into $GITHUB_STEP_SUMMARY.

.DESCRIPTION
    The renderer is a reporting surface, not a gate. It never changes a lane verdict: it exits 0
    even when reports are missing or malformed, and instead states that fact in the summary so a
    silent lane is visibly distinguishable from a green lane.

    Only enum-shaped and numeric report fields are rendered. Free-text fields (outcome.summary,
    failure.message) are deliberately never emitted because they can carry build-machine paths.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultsRoot,

    [Parameter(Mandatory)]
    [ValidateSet(
        'android-flow-pilot',
        'ios-flow-qa',
        'maccatalyst-flow-qa',
        'macos-appkit-flow-qa',
        'windows-flow-qa')]
    [string] $Lane,

    [string] $SummaryPath = $env:GITHUB_STEP_SUMMARY,

    [ValidateRange(1, 1000)]
    [Int32] $MaximumReports = 200,

    [ValidateRange(1024, 8388608)]
    [Int32] $MaximumReportBytes = 1048576
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$maximumFieldLength = 80
$safeFieldPattern = '[^A-Za-z0-9 ._:@/#+-]'

function ConvertTo-SafeField {
    param([object] $Value)

    if ($null -eq $Value) { return '-' }

    $text = [string] $Value
    if ([string]::IsNullOrWhiteSpace($text)) { return '-' }

    $text = $text.Trim() -replace $safeFieldPattern, '?'
    if ($text.Length -gt $maximumFieldLength) {
        $text = $text.Substring(0, $maximumFieldLength) + '...'
    }

    return $text
}

function Get-ReportProperty {
    param(
        [object] $Object,
        [string] $Name)

    if ($null -eq $Object) { return $null }
    if ($Object -isnot [psobject]) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-DurationSeconds {
    param(
        [object] $StartedAt,
        [object] $EndedAt)

    $start = [datetimeoffset]::MinValue
    $end = [datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse([string] $StartedAt, [ref] $start)) { return $null }
    if (-not [datetimeoffset]::TryParse([string] $EndedAt, [ref] $end)) { return $null }
    if ($end -lt $start) { return $null }

    return [math]::Round(($end - $start).TotalSeconds, 1)
}

$rows = @()
$reportCount = 0
$passedCount = 0
$failedCount = 0
$verifiedCount = 0
$cleanupFailedCount = 0
$unreadableCount = 0
$truncated = $false
$status = 'summarized'

try {
    $reportFiles = @()
    if (Test-Path -LiteralPath $ResultsRoot) {
        $reportFiles = @(
            Get-ChildItem -LiteralPath $ResultsRoot -Filter 'flow-run.json' -File -Recurse -ErrorAction SilentlyContinue |
                Sort-Object -Property FullName)
    }

    if ($reportFiles.Count -gt $MaximumReports) {
        $truncated = $true
        $reportFiles = $reportFiles[0..($MaximumReports - 1)]
    }

    $rootFullName = ''
    if (Test-Path -LiteralPath $ResultsRoot) {
        $rootFullName = (Resolve-Path -LiteralPath $ResultsRoot).ProviderPath
    }

    foreach ($file in $reportFiles) {
        $reportCount++
        $relative = $file.FullName
        if ($rootFullName -and $relative.StartsWith($rootFullName, [StringComparison]::Ordinal)) {
            $relative = $relative.Substring($rootFullName.Length).TrimStart('\', '/')
        }
        $location = ConvertTo-SafeField ($relative -replace '\\', '/')

        if ($file.Length -gt $MaximumReportBytes) {
            $unreadableCount++
            $rows += , @($location, '-', 'oversized', '-', '-', '-', '-', '-', '-', '-')
            continue
        }

        $report = $null
        try {
            $report = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
        }
        catch {
            $report = $null
        }

        # ConvertFrom-Json happily returns a bare string, number, or array for a JSON scalar or
        # array document, and every one of those still answers -is [psobject] in PowerShell, so the
        # base object has to be checked explicitly. Only a JSON object with at least one property
        # can be a flow run report.
        $isReportObject = $false
        if ($null -ne $report -and $report.PSObject.BaseObject -is [System.Management.Automation.PSCustomObject]) {
            $isReportObject = @($report.PSObject.Properties).Count -gt 0
        }

        if (-not $isReportObject) {
            $unreadableCount++
            $rows += , @($location, '-', 'unreadable', '-', '-', '-', '-', '-', '-', '-')
            continue
        }

        $outcome = Get-ReportProperty $report 'outcome'
        $failure = Get-ReportProperty $report 'failure'
        $target = Get-ReportProperty $report 'target'
        $steps = Get-ReportProperty $report 'steps'
        $exitCategory = ConvertTo-SafeField (Get-ReportProperty $report 'exitCategory')

        # A run carries two independent verdicts: what it concluded about the app, and whether the
        # host managed to tear itself down afterwards. `outcome.status` only answers the first, so
        # counting it alone reported a run that passed and then failed cleanup as a green pass.
        $secondaryFailures = Get-ReportProperty $report 'secondaryFailures'
        $cleanupFailureCount = 0
        if ($null -ne $secondaryFailures) {
            if ($secondaryFailures -is [string]) {
                # Not a shape this contract produces. It is still not "no cleanup failure".
                $cleanupFailureCount = 1
            }
            else {
                # A one-element JSON array is unrolled to a bare object on output, so it must be
                # re-wrapped before it is counted or a single cleanup failure reads as none.
                $cleanupFailureCount = @($secondaryFailures).Count
            }
        }
        $cleanupFailed = $cleanupFailureCount -gt 0 -or $exitCategory -eq 'infrastructure-failure'
        if ($cleanupFailureCount -gt 0) { $cleanupFailedCount++ }

        $outcomeStatus = ConvertTo-SafeField (Get-ReportProperty $outcome 'status')
        # `verification.verified` is the canonical answer; `outcome.verified` mirrors it and is the
        # fallback for a producer that only writes one of the two.
        $verification = Get-ReportProperty $report 'verification'
        $verified = Get-ReportProperty $verification 'verified'
        if ($verified -isnot [bool]) { $verified = Get-ReportProperty $outcome 'verified' }
        if ($verified -is [bool] -and $verified) { $verifiedCount++ }

        # A report that parses but carries no outcome status is suspicious, not neutral: count it
        # with the failures so a truncated or garbled report can never read as "nothing to see".
        # A passing flow whose owned cleanup failed is counted with the failures for the same
        # reason: the lane exited non-zero and the artifact says so.
        if ($outcomeStatus -eq 'passed' -and -not $cleanupFailed) { $passedCount++ }
        else { $failedCount++ }

        $cleanupText = 'ok'
        if ($cleanupFailureCount -gt 0) { $cleanupText = "failed ($cleanupFailureCount)" }
        elseif ($exitCategory -eq 'infrastructure-failure') { $cleanupText = 'legacy' }

        $stepCount = 0
        if ($null -ne $steps -and $steps -isnot [string]) {
            $stepCount = @($steps).Count
        }

        $duration = Get-DurationSeconds (Get-ReportProperty $report 'startedAt') (Get-ReportProperty $report 'endedAt')
        $durationText = if ($null -eq $duration) { '-' } else { "$duration s" }

        $flowName = Get-ReportProperty $report 'legacyFlowIdentity'
        if ($null -eq $flowName -or [string]::IsNullOrWhiteSpace([string] $flowName)) {
            $flowName = $file.Directory.Name
        }

        $verifiedText = '-'
        if ($verified -is [bool]) {
            $verifiedText = if ($verified) { 'yes' } else { 'no' }
        }

        $rows += , @(
            (ConvertTo-SafeField $flowName),
            (ConvertTo-SafeField (Get-ReportProperty $target 'platform')),
            $outcomeStatus,
            $verifiedText,
            $cleanupText,
            $exitCategory,
            (ConvertTo-SafeField (Get-ReportProperty $failure 'code')),
            (ConvertTo-SafeField (Get-ReportProperty $failure 'phase')),
            "$stepCount",
            $durationText)
    }
}
catch {
    $status = 'error'
    Write-Warning "DevFlow flow-run summary renderer failed: $($_.Exception.GetType().Name)"
}

$lines = @()
$lines += "## DevFlow flow run — $Lane"
$lines += ''

if ($status -eq 'error') {
    $lines += '> The summary renderer failed. The lane verdict is unchanged; see the step log.'
    $lines += ''
}

if ($reportCount -eq 0) {
    $lines += 'No `flow-run.json` report was produced by this lane. A lane that publishes no report is not a passing lane.'
}
else {
    $lines += "Reports: **$reportCount** · replay passed: **$passedCount** · not passed: **$failedCount** · independently verified: **$verifiedCount** · owned cleanup failed: **$cleanupFailedCount** · unreadable: **$unreadableCount**"
    $lines += ''
    $lines += '| Flow | Platform | Outcome | Verified | Cleanup | Exit category | Failure code | Phase | Steps | Duration |'
    $lines += '| --- | --- | --- | --- | --- | --- | --- | --- | ---: | ---: |'
    foreach ($row in $rows) {
        $lines += '| ' + ($row -join ' | ') + ' |'
    }

    if ($truncated) {
        $lines += ''
        $lines += "_Only the first $MaximumReports reports are listed._"
    }

    $lines += ''
    $lines += '_Structured fields only. Free-text outcome and failure messages are omitted because they can carry build-machine paths._'
    $lines += ''
    $lines += '_"Replay passed" is the flow''s own result. A replay that passed without independent business-oracle evidence still exits non-zero as `unverified`, and so does a run whose owned cleanup failed — read the Verified, Cleanup, and Exit category columns, not the count alone._'
}

$lines += ''

$summaryWritten = $false
if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
    try {
        $directory = Split-Path -Parent $SummaryPath
        if ($directory -and -not (Test-Path -LiteralPath $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        Add-Content -LiteralPath $SummaryPath -Value ($lines -join [Environment]::NewLine) -Encoding utf8
        $summaryWritten = $true
    }
    catch {
        Write-Warning "Could not write the step summary: $($_.Exception.GetType().Name)"
    }
}
else {
    $lines | ForEach-Object { Write-Host $_ }
}

[ordered] @{
    status          = $status
    lane            = $Lane
    reports         = $reportCount
    passed          = $passedCount
    notPassed       = $failedCount
    verified        = $verifiedCount
    cleanupFailed   = $cleanupFailedCount
    unreadable      = $unreadableCount
    truncated       = $truncated
    summaryWritten  = $summaryWritten
} | ConvertTo-Json -Depth 3 -Compress | Write-Output

exit 0
