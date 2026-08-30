#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CliArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitSuccess = 0
$ExitUsage = 2
$ExitPrerequisite = 3
$ExitFlowFailure = 4
$ExitPending = 5
$MaxArtifactRecords = 256
# The unfinalized flow-pilot manifest is preserved beside the fallback when finalization fails, and
# a manifest larger than this is not copied: an artifact this pass cannot bound is not published.
$MaxPreservedManifestBytes = 4194304
$UnfinalizedManifestName = 'manifest.unfinalized.json'
$MaxDiagnosticCharacters = 65536
$MaxDiagnosticLines = 1000
# Scanned over every produced line, not over the bounded diagnostic that is written out, so a host
# that names its infrastructure failure past the cap is still classified from what it said. Every
# marker is an anchored phrase a failing host actually prints: a bare word could not stay, because
# an ordinary line naming the emulator, or a flow assertion that timed out, reclassified a product
# defect as an infrastructure failure and excused it. Kept byte-identical to
# $INFRASTRUCTURE_DIAGNOSTIC_PATTERN in Run-DevFlowFlowQa.sh apart from the leading flag.
$script:InfrastructureDiagnosticPattern = '(?i)\b(workload.{0,64}(is|are) not installed|to install missing workloads|workload manifest .{0,64}not found|dotnet sdk .{0,40}not found|sdk .{0,40}was not found|adb(\.exe)?:? .{0,40}not found|adb: no devices|no devices?/emulators? found|device .{0,40}not found|emulator: error|emulator .{0,40}(failed to start|failed to boot|terminated|exited)|avd .{0,40}(not found|does not exist)|xcrun: error|simctl .{0,40}(error|failed)|unable to boot device|agent readiness (timed out|failed)|(emulator|simulator|avd|adb|device readiness|agent readiness)( [a-z]+){0,3} timed out|(agent|emulator|simulator|device|broker|fixture) did not become ready|fixture initialization (failed|error)|android-fixture-initialization|infrastructure-error|infrastructure-failure)\b'

function Write-Usage {
    @"
Usage:
  Run-DevFlowFlowQa.ps1 --platform android|windows|ios|maccatalyst|macos `
    --results-root <repo>/artifacts/TestResults/devflow-flow/<platform> [options]

Required:
  --platform <name>       android, windows, ios, maccatalyst, or macos
  --results-root <path>   Exact repository-local results directory for the selected platform

Options:
  --repeat <N>            Clean repetitions per invocation (default: 3; maximum: 20).
                          The cap is deliberate: gates that need 100+ clean first attempts want
                          100 independent runs, not 100 iterations of one warm process. Use
                          --accumulate to merge evidence across separate runs instead.
  --accumulate <dir>      Merge qualification metric numerators/denominators across independent
                          runs into <dir>. Requires --qualification.
  --baseline <path>       Fail when a gated qualification metric regresses below this committed
                          baseline report. Requires --qualification.
  --configuration <name>  Test configuration (default: Debug)
  --flow-filter <filter>  Additional VSTest filter appended to the platform filter
  --no-build              Pass --no-build to dotnet test
  --qualification         Run the read-only qualification evaluator after the flow host
  --experimental          Required for the experimental AppKit/macOS lane
  --physical-device       Run the separately identified physical-iOS lane
  --device-id <id>        Android serial or required physical-iOS device identifier
  --ios-runtime <version> iOS Simulator runtime selector (for ios simulator only)
  --signing-identity <id> Physical-iOS signing identity; never written to artifacts
  --provisioning-profile <id>
                           Physical-iOS provisioning profile; never written to artifacts
  --keychain <path>       Physical-iOS keychain reference; never written to artifacts
  --verbosity <level>     quiet, minimal, normal, detailed, or diagnostic
  --verbose               Alias for --verbosity detailed
  --dry-run               Validate arguments and emit the planned, non-executing command as JSON
  --help                  Show this help text

Exit codes: 0 succeeded, 2 invalid invocation, 3 prerequisite/infrastructure failure,
4 flow failure, 5 pending capability or not-qualified result.
"@
}

function Exit-Usage {
    param([Parameter(Mandatory)][string] $Message)

    [Console]::Error.WriteLine("flow-qa: $Message")
    [Console]::Error.WriteLine((Write-Usage))
    exit $ExitUsage
}

function Get-RequiredValue {
    param(
        [Parameter(Mandatory)][string] $Option,
        [Parameter(Mandatory)][int] $Index,
        [Parameter(Mandatory)][string[]] $Tokens
    )

    if ($Index + 1 -ge $Tokens.Count -or
        [string]::IsNullOrWhiteSpace($Tokens[$Index + 1]) -or
        $Tokens[$Index + 1].StartsWith('-')) {
        Exit-Usage "$Option requires a value."
    }

    $Tokens[$Index + 1]
}

function Test-OptionValue {
    param(
        [string] $Value,
        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match "[`r`n]") {
        Exit-Usage "$Name must be a single non-empty line."
    }
}

function Test-UnsafePath {
    param([Parameter(Mandatory)][string] $Path)

    if ($Path.IndexOfAny([char[]] '*?[]') -ge 0 -or
        $Path -match '(^|[\\/])\.\.($|[\\/])') {
        Exit-Usage '--results-root must not contain wildcards or parent-directory segments.'
    }
}

function Get-PathComparison {
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return [System.StringComparison]::OrdinalIgnoreCase
    }

    return [System.StringComparison]::Ordinal
}

function Get-CanonicalPath {
    param([Parameter(Mandatory)][string] $Path)

    [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]] @('\', '/'))
}

function Assert-ExistingPathIsNotReparsePoint {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Path
    )

    $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $Path)
    if ($relative.StartsWith('..', [System.StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($relative)) {
        Exit-Usage 'The output path must remain inside the repository.'
    }

    $current = $RepositoryRoot
    foreach ($segment in $relative -split '[\\/]') {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }

        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Exit-Usage 'The output path must not traverse a symbolic link or reparse point.'
            }
        }
    }
}

function Resolve-ResultsRoot {
    param(
        [Parameter(Mandatory)][string] $InputPath,
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Platform
    )

    Test-UnsafePath $InputPath
    $expected = Get-CanonicalPath (Join-Path $RepositoryRoot "artifacts/TestResults/devflow-flow/$Platform")
    $candidate = if ([System.IO.Path]::IsPathRooted($InputPath)) {
        Get-CanonicalPath $InputPath
    }
    else {
        Get-CanonicalPath (Join-Path $RepositoryRoot $InputPath)
    }

    if (-not [string]::Equals($candidate, $expected, (Get-PathComparison))) {
        Exit-Usage "--results-root must resolve exactly to '$expected' for platform '$Platform'."
    }

    Assert-ExistingPathIsNotReparsePoint -RepositoryRoot $RepositoryRoot -Path $candidate
    $candidate
}

function Resolve-ArtifactRoot {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Platform,
        [Parameter(Mandatory)][string] $RunId
    )

    $expected = Get-CanonicalPath (Join-Path $RepositoryRoot "artifacts/devflow/$RunId/$Platform")
    $configured = if ($Platform -eq 'android') {
        $env:DEVFLOW_FLOW_PILOT_ARTIFACT_ROOT
    }
    else {
        $env:DEVFLOW_FLOW_QA_ARTIFACT_ROOT
    }

    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        Test-UnsafePath $configured
        $candidate = if ([System.IO.Path]::IsPathRooted($configured)) {
            Get-CanonicalPath $configured
        }
        else {
            Get-CanonicalPath (Join-Path $RepositoryRoot $configured)
        }

        if (-not [string]::Equals($candidate, $expected, (Get-PathComparison))) {
            Exit-Usage "The configured artifact root must resolve exactly to '$expected'."
        }
    }

    Assert-ExistingPathIsNotReparsePoint -RepositoryRoot $RepositoryRoot -Path $expected
    $expected
}

function Get-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Path
    )

    ([System.IO.Path]::GetRelativePath($RepositoryRoot, $Path) -replace '\\', '/')
}

function Get-RunId {
    $configured = $env:DEVFLOW_FLOW_QA_RUN_ID
    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        if ($configured -notmatch '^[A-Za-z0-9._-]+$') {
            Exit-Usage 'DEVFLOW_FLOW_QA_RUN_ID may contain only letters, digits, dot, underscore, and hyphen.'
        }

        return $configured
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID)) {
        if ([string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ATTEMPT)) {
            return $env:GITHUB_RUN_ID
        }

        return "$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)"
    }

    "local-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))-$PID"
}

function Get-RepositoryCommit {
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    try {
        $commit = (& git -C $RepositoryRoot rev-parse HEAD 2>$null | Select-Object -First 1).Trim()
        if (-not [string]::IsNullOrWhiteSpace($commit)) {
            return $commit
        }
    }
    catch {
    }

    'unknown'
}

function Get-FileDigest {
    param([string] $Path)

    $measured = Get-FileDigestAndSize $Path
    if ($null -eq $measured) {
        return $null
    }

    return $measured.Sha256
}

# The digest and the size describe one read of one file. Taking the size from a directory entry
# captured during enumeration published the length the file had then beside a hash of the bytes it
# has now - a pair nothing on disk matches, which disqualifies the whole inventory. The size
# published is the number of bytes that were actually hashed. Kept identical in behaviour to
# Get-FileDigestAndSize in Finalize-DevFlowFlowPilotManifest.ps1.
function Get-FileDigestAndSize {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        try {
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            try {
                $hash = $sha256.ComputeHash($stream)
            }
            finally {
                $sha256.Dispose()
            }

            return [pscustomobject]@{
                Sha256 = "sha256:$([System.Convert]::ToHexString($hash).ToLowerInvariant())"
                SizeBytes = [Int64] $stream.Position
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        return $null
    }
}

function Get-FlowDigests {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Platform
    )

    $flowDirectory = if ($Platform -eq 'macos') {
        Join-Path $RepositoryRoot 'samples/DevFlow.Sample.MacOS/maui-tests'
    }
    else {
        Join-Path $RepositoryRoot 'samples/DevFlow.Sample/maui-tests'
    }
    if (-not (Test-Path -LiteralPath $flowDirectory -PathType Container)) {
        return @()
    }

    @(
        Get-ChildItem -LiteralPath $flowDirectory -Filter '*.md' -File |
            Where-Object { $_.Name -ne 'README.md' } |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    path = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path $_.FullName
                    sha256 = Get-FileDigest $_.FullName
                }
            })
}

function Get-ArtifactKind {
    param([Parameter(Mandatory)][System.IO.FileInfo] $File)

    switch ($File.Extension.ToLowerInvariant()) {
        '.trx' { 'test-results'; break }
        '.mauitrace' { 'mauitrace'; break }
        '.json' { 'json'; break }
        default { 'host-diagnostic'; break }
    }
}

function Get-ArtifactRecords {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string[]] $Roots,
        [Parameter(Mandatory)][string] $ResultsRoot,
        [Parameter(Mandatory)][string] $RunId,
        [string[]] $ExcludePaths = @()
    )

    $records = [System.Collections.Generic.List[object]]::new()
    $omissions = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $excluded = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($excludedPath in @($ExcludePaths)) {
        if (-not [string]::IsNullOrWhiteSpace($excludedPath)) {
            [void] $excluded.Add((Get-CanonicalPath $excludedPath))
        }
    }
    $omittedByLimit = 0
    $omittedOutsideRepository = 0
    $omittedUnhashable = 0
    $enumerationErrors = 0

    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        # Ordinal so the enumeration order, and therefore which files fall inside the cap, matches
        # the shell entry point's byte ordering rather than the current culture's collation. A
        # directory that could not be read is counted and stated rather than treated as empty: the
        # finalizer holds itself to the same rule, and a partial inventory presented as a complete
        # one is the failure both are guarding against.
        $rootErrors = $null
        $rootFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
        foreach ($candidate in @(Get-ChildItem -LiteralPath $root -Recurse -File `
                    -ErrorAction SilentlyContinue -ErrorVariable rootErrors)) {
            $rootFiles.Add($candidate)
        }
        $enumerationErrors += @($rootErrors).Count
        $rootFiles.Sort([System.Comparison[System.IO.FileInfo]] {
                param($left, $right)
                [string]::CompareOrdinal($left.FullName, $right.FullName)
            })

        foreach ($file in $rootFiles) {
            if ($file.Name.EndsWith('.tmp', [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            # A file this same write pass rewrites after the digests are taken cannot be listed
            # here. Its recorded hash would describe bytes that no longer exist by the time the
            # list is published, and a consumer that checks the list would refuse the whole run.
            if ($excluded.Contains((Get-CanonicalPath $file.FullName))) {
                continue
            }

            if ([string]::Equals($root, $ResultsRoot, (Get-PathComparison)) -and
                $file.Name -notlike "*$RunId*") {
                continue
            }

            $relative = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path $file.FullName
            # Nothing outside the checkout may reach artifacts[].path: a consumer resolves those
            # entries against its own clone. An excluded path is counted and stated once, not
            # dropped in silence.
            if ($relative.StartsWith('../', [System.StringComparison]::Ordinal) -or
                [System.IO.Path]::IsPathRooted($relative)) {
                $omittedOutsideRepository++
                continue
            }
            if (-not $seen.Add($relative)) {
                continue
            }

            # Every artifact past the cap is counted, not just the first one. A consumer that has
            # to decide whether the evidence is complete cannot do that from a bare "truncated".
            if ($records.Count -ge $MaxArtifactRecords) {
                $omittedByLimit++
                continue
            }

            $measured = Get-FileDigestAndSize $file.FullName
            if ($null -eq $measured) {
                $omittedUnhashable++
                $omissions.Add([ordered]@{
                    kind = 'artifact-hash'
                    reason = 'An artifact could not be hashed.'
                    path = $relative
                })
                continue
            }

            $records.Add([ordered]@{
                kind = Get-ArtifactKind $file
                path = $relative
                sha256 = $measured.Sha256
                sizeBytes = $measured.SizeBytes
                redacted = $true
            })
        }
    }

    if ($omittedOutsideRepository -gt 0) {
        $omissions.Add([ordered]@{
            kind = 'artifact-path'
            reason = 'An artifact outside the repository was excluded.'
            omittedArtifacts = $omittedOutsideRepository
        })
    }

    if ($enumerationErrors -gt 0) {
        $omissions.Add([ordered]@{
            kind = 'artifact-enumeration'
            reason = 'An artifact directory could not be fully enumerated, so this inventory may be incomplete.'
            # A floor, not a measurement: an unreadable directory may have held any number of
            # references. Counting one keeps the summary and the omissions reconcilable.
            omittedArtifacts = $enumerationErrors
            enumerationErrors = $enumerationErrors
        })
    }

    # The cap omission is stated by the caller, which is the only place that knows whether the
    # reports written in this same pass also have to be counted against the cap.
    [ordered]@{
        records = @($records)
        omissions = @($omissions)
        omittedByLimit = $omittedByLimit
        omittedOther = $omittedOutsideRepository + $omittedUnhashable + $enumerationErrors
    }
}

function New-ArtifactRecord {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $measured = Get-FileDigestAndSize $Path
    if ($null -eq $measured) {
        return $null
    }

    $file = Get-Item -LiteralPath $Path -Force
    [ordered]@{
        kind = Get-ArtifactKind $file
        path = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path $file.FullName
        sha256 = $measured.Sha256
        sizeBytes = $measured.SizeBytes
        redacted = $true
    }
}

# Values this invocation was handed are held only in memory and are never written to an artifact.
# They are registered here so a diagnostic can be redacted by exact value: a signing identity,
# provisioning profile, or keychain reference frequently appears in tool output with no key,
# scheme, or assignment around it to key a pattern off.
$script:SecretValues = [System.Collections.Generic.List[string]]::new()

function Register-SecretValue {
    param([AllowNull()][string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $trimmed = $Value.Trim()
    if ($trimmed.Length -lt 3 -or $script:SecretValues.Contains($trimmed)) {
        return
    }

    $script:SecretValues.Add($trimmed)
}

function Protect-DiagnosticText {
    param([AllowNull()][string] $Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return ''
    }

    $redacted = $Text
    foreach ($secret in @($script:SecretValues | Sort-Object -Property Length -Descending)) {
        $redacted = $redacted.Replace($secret, '[REDACTED]', [System.StringComparison]::OrdinalIgnoreCase)
    }

    # A named credential often carries an authentication scheme before its value. Consuming only
    # the first token after the separator redacted the scheme and left the credential in place, so
    # the optional scheme is consumed together with the value that follows it.
    $redacted = $redacted -replace '(?i)\b((?:proxy-)?authorization|www-authenticate|proxy-authenticate|token|password|secret|api[_-]?key)\b\s*([:=])\s*(?:(?:bearer|basic|digest|negotiate|ntlm|token|jwt|apikey)\s+)?\S+', '$1$2[REDACTED]'
    # A scheme-prefixed credential outside a header key is redacted only when what follows really
    # looks like one. "digest", "basic", and "negotiate" are ordinary words in tool output, and
    # "digest sha256:<hex>" is a diagnostic a reader needs, so the value must be a single opaque
    # credential-shaped token: drawn from the base64url/JWT alphabet and not a plain word. The
    # trailing guard is the same character class, so a credential followed by any other punctuation
    # - '&', '>', '#' - is still redacted rather than falling out of the match entirely.
    $redacted = $redacted -replace '(?i)\b(bearer|basic|digest|negotiate|ntlm|jwt)(\s+)(?![A-Za-z]+(?![A-Za-z0-9+/=_.-]))[A-Za-z0-9+/=_.-]{8,}(?![A-Za-z0-9+/=_.-])', '$1$2[REDACTED]'
    $redacted -replace '(?i)(DEVFLOW_IOS_(?:SIGNING_IDENTITY|PROVISIONING_PROFILE|KEYCHAIN)|DEVFLOW_APPLE_AGENT_SESSION_SECRET)\s*([:=])\s*\S+', '$1$2[REDACTED]'
}

function Write-AtomicJson {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $Value
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = $Value | ConvertTo-Json -Depth 32
        [System.IO.File]::WriteAllText($temporary, $json, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

function Invoke-RecordedCommand {
    param(
        [Parameter(Mandatory)][string] $FileName,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $DiagnosticPath
    )

    $output = [System.Collections.Generic.List[string]]::new()
    $characterCount = 0
    $truncated = $false
    $exitCode = 1
    # The recorded diagnostic is a bounded projection: it stops at the line and character caps so a
    # chatty host cannot fill an artifact. Classification may not read that projection, because the
    # one line that says what happened is often the last one a failing host prints. These markers
    # are scanned over every line the command produced, and only the flags survive - the unbounded
    # text itself is never held or written anywhere.
    $markers = [ordered]@{
        capabilityMissing = $false
        infrastructure = $false
    }
    try {
        # Cleared first: a stale status left by an earlier native command would otherwise be read
        # as this command's, and the "no native command ran" case could never be detected.
        $global:LASTEXITCODE = $null
        & $FileName @Arguments 2>&1 | ForEach-Object {
            $raw = [string] $_
            # Markers are read from what the command actually printed, before redaction, exactly as
            # the shell entry point greps its raw capture. Only the booleans survive this scope.
            if ($raw -match '(?i)\bcapability-missing\b') {
                $markers['capabilityMissing'] = $true
            }
            if ($raw -match $script:InfrastructureDiagnosticPattern) {
                $markers['infrastructure'] = $true
            }

            $line = Protect-DiagnosticText $raw
            if ($output.Count -lt $MaxDiagnosticLines -and $characterCount -lt $MaxDiagnosticCharacters) {
                $output.Add($line)
                $characterCount += $line.Length + [Environment]::NewLine.Length
            }
            else {
                $truncated = $true
            }
        }

        # A null exit status means no native command reported one. Reading that as success would
        # turn a test host that never launched into a passing lane, so it fails closed instead.
        $reportedExitCode = Get-Variable -Name 'LASTEXITCODE' -ValueOnly -ErrorAction SilentlyContinue
        $exitCode = if ($null -eq $reportedExitCode) { 1 } else { [int] $reportedExitCode }
    }
    catch {
        $raw = [string] $_.Exception.Message
        if ($raw -match '(?i)\bcapability-missing\b') {
            $markers['capabilityMissing'] = $true
        }
        if ($raw -match $script:InfrastructureDiagnosticPattern) {
            $markers['infrastructure'] = $true
        }
        $output.Add((Protect-DiagnosticText $raw))
        $exitCode = 1
    }

    if ($truncated) {
        $output.Add('[truncated: the recorded diagnostic reached its line or character limit]')
    }

    $text = ($output -join [Environment]::NewLine)
    if ($text.Length -gt $MaxDiagnosticCharacters) {
        $text = $text.Substring(0, $MaxDiagnosticCharacters) + [Environment]::NewLine + '[truncated]'
        $truncated = $true
    }

    $directory = Split-Path -Parent $DiagnosticPath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    [System.IO.File]::WriteAllText($DiagnosticPath, $text, [System.Text.UTF8Encoding]::new($false))
    [ordered]@{
        exitCode = $exitCode
        output = $text
        markers = $markers
        truncated = $truncated
        diagnosticPath = $DiagnosticPath
    }
}

# Structured run evidence is the only account of a failure that the platform host actually
# recorded. Free text that happens to contain "timeout" or "emulator" describes whatever the tool
# printed, which is why it may only answer when no structured evidence exists.
function Get-ClassificationFromStructuredFields {
    param(
        [AllowNull()][string] $Outcome,
        [AllowNull()][string] $FailureClass
    )

    if ($FailureClass -ceq 'capability-missing') {
        return 'capability-missing'
    }
    if ($FailureClass -cin @(
            'infrastructure',
            'transport',
            'agent-disconnected',
            'lease-conflict',
            'lease-lost',
            'reset-failed',
            'timeout',
            'secret-unavailable')) {
        return 'infrastructure-failure'
    }
    if ($Outcome -cin @(
            'infrastructure-error',
            'timed-out',
            'lease-lost',
            'orphaned',
            'unknown-completion',
            'cancelled')) {
        return 'infrastructure-failure'
    }
    if ($Outcome -ceq 'failed' -or -not [string]::IsNullOrWhiteSpace($FailureClass)) {
        return 'flow-failure'
    }

    $null
}

function Get-StructuredFailureClassification {
    param(
        [Parameter(Mandatory)][string] $ArtifactRoot,
        [Parameter(Mandatory)][string] $ScriptReportPath
    )

    if (-not (Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
        return $null
    }

    $observed = [System.Collections.Generic.List[string]]::new()
    $manifestPath = Join-Path $ArtifactRoot 'manifest.json'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
            $hostManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
            foreach ($flow in @($hostManifest['flows'])) {
                if ($null -eq $flow) {
                    continue
                }

                $attempt = $flow['firstAttempt']
                if ($null -eq $attempt) {
                    continue
                }

                $classification = Get-ClassificationFromStructuredFields `
                    -Outcome ([string] $attempt['outcome']) `
                    -FailureClass ([string] $attempt['failureClass'])
                if ($null -ne $classification) {
                    $observed.Add($classification)
                }
            }
        }
        catch {
        }
    }

    $scriptReportFull = Get-CanonicalPath $ScriptReportPath
    foreach ($report in @(Get-ChildItem -LiteralPath $ArtifactRoot -Recurse -File -Filter 'flow-run.json' -ErrorAction SilentlyContinue |
                Sort-Object -Property FullName)) {
        if ([string]::Equals((Get-CanonicalPath $report.FullName), $scriptReportFull, (Get-PathComparison))) {
            continue
        }

        try {
            $runReport = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json -AsHashtable
            if ([string] $runReport['kind'] -ceq 'devflow-flow-qa-run') {
                continue
            }

            $outcome = $runReport['outcome']
            $failure = $runReport['failure']
            $classification = Get-ClassificationFromStructuredFields `
                -Outcome $(if ($null -ne $outcome) { [string] $outcome['status'] } else { $null }) `
                -FailureClass $(if ($null -ne $failure) { [string] $failure['class'] } else { $null })
            if ($null -ne $classification) {
                $observed.Add($classification)
            }
        }
        catch {
        }
    }

    if ($observed.Contains('capability-missing')) {
        return 'capability-missing'
    }
    if ($observed.Contains('infrastructure-failure')) {
        return 'infrastructure-failure'
    }
    if ($observed.Contains('flow-failure')) {
        return 'flow-failure'
    }

    $null
}

function Get-ExecutionClassification {
    param(
        [int] $ExitCode,
        [string] $Output,
        [AllowNull()][string] $StructuredClassification,
        [AllowNull()] $Markers
    )

    if ($ExitCode -eq 0) {
        return 'passed'
    }

    if (-not [string]::IsNullOrWhiteSpace($StructuredClassification)) {
        return $StructuredClassification
    }

    # The markers were taken from every line the command produced; the recorded text is only the
    # bounded projection of it. Reading the projection here meant a host that named its missing
    # capability after the cap was classified from the lines that happened to fit.
    if ($null -ne $Markers -and $Markers['capabilityMissing']) {
        return 'capability-missing'
    }
    if ($Output -match '(?i)\bcapability-missing\b') {
        return 'capability-missing'
    }

    # Bounded fallback only. It runs when the host produced no structured failure or exit evidence
    # at all, which is the one case where the printed text is the only account that exists.
    if ($null -ne $Markers -and $Markers['infrastructure']) {
        return 'infrastructure-failure'
    }
    if ($Output -match $script:InfrastructureDiagnosticPattern) {
        return 'infrastructure-failure'
    }

    'flow-failure'
}

function Resolve-AttemptClassification {
    param(
        [Parameter(Mandatory)][int] $ExitCode,
        [Parameter(Mandatory)][AllowEmptyString()][string] $Output,
        [Parameter(Mandatory)][string] $ArtifactRoot,
        [Parameter(Mandatory)][string] $ScriptReportPath,
        [AllowNull()] $Markers
    )

    $structured = $null
    if ($ExitCode -ne 0) {
        $structured = Get-StructuredFailureClassification -ArtifactRoot $ArtifactRoot -ScriptReportPath $ScriptReportPath
    }

    [ordered]@{
        classification = Get-ExecutionClassification `
            -ExitCode $ExitCode `
            -Output $Output `
            -StructuredClassification $structured `
            -Markers $Markers
        source = if ($ExitCode -eq 0) {
            'exit-code'
        }
        elseif ($null -ne $structured) {
            'structured-evidence'
        }
        else {
            'diagnostic-text'
        }
    }
}

function Get-HostMetadata {
    param(
        [Parameter(Mandatory)][string] $Platform,
        [string] $IosRuntime,
        [bool] $PhysicalDevice,
        [string] $DeviceId
    )

    $hostOs = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    $dotnetSdk = 'unknown'
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        try {
            $dotnetSdk = (& dotnet --version 2>$null | Select-Object -First 1).Trim()
        }
        catch {
        }
    }

    $xcode = 'not-applicable'
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::OSX)) {
        $xcode = 'unavailable'
        if (Get-Command xcodebuild -ErrorAction SilentlyContinue) {
            try {
                $xcode = ((& xcodebuild -version 2>$null) -join '; ').Trim()
            }
            catch {
            }
        }
    }

    [ordered]@{
        hostOs = $hostOs
        dotnetSdk = $dotnetSdk
        workloadVersion = if ($env:DOTNET_WORKLOAD_VERSION) { $env:DOTNET_WORKLOAD_VERSION } else { 'unknown' }
        xcode = $xcode
        runtime = if ($IosRuntime) { $IosRuntime } else { 'default' }
        deviceEvidence = [ordered]@{
            kind = if ($PhysicalDevice) { 'physical-device' } elseif ($Platform -eq 'android') { 'emulator' } elseif ($Platform -eq 'ios') { 'simulator' } else { 'desktop-host' }
            realDevice = $PhysicalDevice
            deviceIdFingerprint = if ($DeviceId) { Get-StringDigest $DeviceId } else { $null }
            profile = 'not-observed'
        }
    }
}

function Get-WindowsDesktopSessionAdmission {
    $timestamp = [DateTimeOffset]::UtcNow.ToString('O')
    $result = [ordered]@{
        schema = 1
        kind = 'devflow-windows-desktop-session'
        sessionId = $null
        wtsConnectionState = 'unavailable'
        desktopLockState = 'unavailable'
        admissionResult = 'unavailable'
        admissionTimestampUtc = $timestamp
        reason = 'windows-host-required'
    }

    # This fail-only hook makes the preflight testable without ever allowing an environment
    # variable to bypass the native WTS check.
    $forcedFailure = $env:DEVFLOW_WINDOWS_SESSION_PREFLIGHT_TEST_STATE
    if ($forcedFailure -eq 'disconnected') {
        try {
            $result['sessionId'] = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
        }
        catch {
        }
        $result['wtsConnectionState'] = 'disconnected'
        $result['admissionResult'] = 'rejected'
        $result['reason'] = 'wts-connection-state-disconnected'
        return [pscustomobject] $result
    }
    if (-not [string]::IsNullOrWhiteSpace($forcedFailure)) {
        $result['reason'] = 'wts-connection-state-unavailable'
        return [pscustomobject] $result
    }

    if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return [pscustomobject] $result
    }

    try {
        $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
        $result['sessionId'] = $sessionId
    }
    catch {
        $result['reason'] = 'current-process-session-unavailable'
        return [pscustomobject] $result
    }

    if ($sessionId -lt 0) {
        $result['sessionId'] = $null
        $result['reason'] = 'current-process-session-unavailable'
        return [pscustomobject] $result
    }
    if ($sessionId -eq 0) {
        $result['admissionResult'] = 'rejected'
        $result['reason'] = 'session-zero-not-desktop'
        return [pscustomobject] $result
    }

    try {
        if ($null -eq ('DevFlow.FlowQa.WindowsSessionNative' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace DevFlow.FlowQa
{
    public static class WindowsSessionNative
    {
        const int WTSConnectState = 8;
        const int WTSSessionInfoEx = 25;

        public static int QueryConnectionState(int sessionId)
        {
            return QueryInt32(sessionId, WTSConnectState, 0);
        }

        public static int QuerySessionFlags(int sessionId)
        {
            return QueryInt32(sessionId, WTSSessionInfoEx, 12);
        }

        static int QueryInt32(int sessionId, int informationClass, int offset)
        {
            IntPtr buffer = IntPtr.Zero;
            int bytesReturned = 0;
            try
            {
                if (!WTSQuerySessionInformation(
                        IntPtr.Zero,
                        sessionId,
                        informationClass,
                        out buffer,
                        out bytesReturned) ||
                    buffer == IntPtr.Zero ||
                    bytesReturned < offset + sizeof(int))
                {
                    return -1;
                }

                if (informationClass == WTSSessionInfoEx &&
                    (Marshal.ReadInt32(buffer, 0) != 1 ||
                     Marshal.ReadInt32(buffer, sizeof(int)) != sessionId))
                {
                    return -1;
                }

                return Marshal.ReadInt32(buffer, offset);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    WTSFreeMemory(buffer);
                }
            }
        }

        [DllImport("Wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool WTSQuerySessionInformation(
            IntPtr hServer,
            int sessionId,
            int wtsInfoClass,
            out IntPtr ppBuffer,
            out int pBytesReturned);

        [DllImport("Wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr memory);
    }
}
'@
        }

        $connectionState = [DevFlow.FlowQa.WindowsSessionNative]::QueryConnectionState($sessionId)
    }
    catch {
        $result['reason'] = 'wts-connection-state-unavailable'
        return [pscustomobject] $result
    }

    $connectionStateName = switch ($connectionState) {
        0 { 'active'; break }
        1 { 'connected'; break }
        2 { 'connect-query'; break }
        3 { 'shadow'; break }
        4 { 'disconnected'; break }
        5 { 'idle'; break }
        6 { 'listen'; break }
        7 { 'reset'; break }
        8 { 'down'; break }
        9 { 'init'; break }
        default { $null }
    }
    if ($null -eq $connectionStateName) {
        $result['reason'] = 'wts-connection-state-unavailable'
        return [pscustomobject] $result
    }

    $result['wtsConnectionState'] = $connectionStateName
    if ($connectionStateName -ne 'active') {
        $result['admissionResult'] = 'rejected'
        $result['reason'] = "wts-connection-state-$connectionStateName"
        return [pscustomobject] $result
    }

    try {
        $sessionFlags = [DevFlow.FlowQa.WindowsSessionNative]::QuerySessionFlags($sessionId)
    }
    catch {
        $result['reason'] = 'desktop-lock-state-unavailable'
        return [pscustomobject] $result
    }

    switch ($sessionFlags) {
        1 {
            $result['desktopLockState'] = 'unlocked'
            $result['admissionResult'] = 'allowed'
            $result['reason'] = 'active-unlocked-desktop'
            break
        }
        0 {
            $result['desktopLockState'] = 'locked'
            $result['admissionResult'] = 'rejected'
            $result['reason'] = 'desktop-locked'
            break
        }
        default {
            $result['reason'] = 'desktop-lock-state-unavailable'
            break
        }
    }

    [pscustomobject] $result
}

function Get-StringDigest {
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    "sha256:$([System.Convert]::ToHexString($hash).ToLowerInvariant())"
}

function Write-HostDiagnostics {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)] $Metadata,
        [Parameter(Mandatory)][string] $Status,
        [Parameter(Mandatory)][string] $Classification
    )

    Write-AtomicJson -Path (Join-Path $Directory 'summary.json') -Value ([ordered]@{
            schema = 1
            kind = 'devflow-flow-qa-host-diagnostics'
            generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
            status = $Status
            classification = $Classification
            host = $Metadata
        })
}

function Write-WindowsSessionDiagnostic {
    param(
        [Parameter(Mandatory)][string] $Directory,
        [Parameter(Mandatory)] $Admission
    )

    Write-AtomicJson -Path (Join-Path $Directory 'windows-session.json') -Value ([ordered]@{
            schema = 1
            kind = 'devflow-windows-desktop-session'
            sessionId = $Admission.sessionId
            wtsConnectionState = $Admission.wtsConnectionState
            desktopLockState = $Admission.desktopLockState
            admissionResult = $Admission.admissionResult
            admissionTimestampUtc = $Admission.admissionTimestampUtc
            reason = $Admission.reason
        })
}

function Write-GenericManifest {
    param(
        [Parameter(Mandatory)][string] $ManifestPath,
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Commit,
        [Parameter(Mandatory)][string] $RunId,
        [Parameter(Mandatory)][string] $Platform,
        [Parameter(Mandatory)][string] $AppProject,
        [Parameter(Mandatory)] $FlowDigests,
        [Parameter(Mandatory)] $HostQa,
        [Parameter(Mandatory)] $Artifacts,
        [Parameter(Mandatory)] $Omissions,
        $ArtifactSummary,
        [string] $AppBuildFingerprint,
        [string] $PackageDigest
    )

    $appDigest = Get-FileDigest $AppProject
    $allOmissions = [System.Collections.Generic.List[object]]::new()
    foreach ($omission in @($Omissions)) {
        $allOmissions.Add($omission)
    }
    if ($null -eq $appDigest -and
        @($allOmissions | Where-Object { $_.kind -eq 'app-digest' }).Count -eq 0) {
        $allOmissions.Add([ordered]@{
                kind = 'app-digest'
                reason = 'The selected app project was unavailable or could not be hashed.'
            })
    }
    if ([string]::IsNullOrWhiteSpace($PackageDigest) -and
        @($allOmissions | Where-Object { $_.kind -eq 'package-digest' }).Count -eq 0) {
        $allOmissions.Add([ordered]@{
                kind = 'package-digest'
                reason = 'The platform host did not emit a packaged-app digest for this run.'
            })
    }

    Write-AtomicJson -Path $ManifestPath -Value ([ordered]@{
            schema = 1
            kind = 'devflow-flow-qa'
            generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
            repository = [ordered]@{ commit = $Commit }
            workflow = [ordered]@{ runId = $RunId; attempt = $env:GITHUB_RUN_ATTEMPT }
            experimental = if ($Platform -eq 'macos') { $true } else { $false }
            backend = if ($Platform -eq 'macos') { 'appkit' } else { $null }
            officialCoverage = if ($Platform -eq 'macos') { $false } else { $true }
            macCatalystEquivalent = if ($Platform -eq 'macos') { $false } else { $null }
            testing = [ordered]@{
                project = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path (Join-Path $RepositoryRoot 'src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/Microsoft.Maui.DevFlow.Agent.IntegrationTests.csproj')
                packageVersion = 'unknown'
            }
            platform = [ordered]@{
                name = $Platform
                experimental = if ($Platform -eq 'macos') { $true } else { $false }
                backend = if ($Platform -eq 'macos') { 'appkit' } else { $null }
                officialCoverage = if ($Platform -eq 'macos') { $false } else { $true }
                macCatalystEquivalent = if ($Platform -eq 'macos') { $false } else { $null }
                host = $HostQa.host
            }
            app = [ordered]@{
                project = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path $AppProject
                sourceDigest = $appDigest
                buildFingerprint = $AppBuildFingerprint
                packageDigest = $PackageDigest
            }

            flows = @($FlowDigests)
            hostQa = $HostQa
            artifacts = @($Artifacts)
            artifactSummary = $ArtifactSummary
            omissions = @($allOmissions)
            privacy = [ordered]@{
                excludedByDefault = @('screenshots', 'source', 'raw-model-context', 'environment', 'signing-inputs')
            }
        })
}

function Get-WindowsTierOneFacts {
    param([Parameter(Mandatory)][string] $ArtifactRoot)

    $path = Join-Path $ArtifactRoot 'windows-tier1-manifest.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return $null
    }

    try {
        $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $firstAttempt = @($manifest['flows'] |
            ForEach-Object { $_['firstAttempt'] } |
            Where-Object { $null -ne $_ }) |
            Select-Object -First 1
        return [ordered]@{
            manifestPath = $path
            appBuildFingerprint = $manifest['app']['buildFingerprint']
            packageDigest = $manifest['app']['packageDigest']
            resetFingerprint = $firstAttempt['resetFingerprint']
            seedFingerprint = $firstAttempt['seedFingerprint']
            backendStateFingerprint = $firstAttempt['backendStateFingerprint']
        }
    }
    catch {
        return $null
    }
}

function Finalize-AndroidManifest {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $ManifestPath,
        [Parameter(Mandatory)][string] $ArtifactRoot,
        [Parameter(Mandatory)][string] $ResultsRoot,
        [Parameter(Mandatory)][string] $Commit,
        [Parameter(Mandatory)][string] $RunId,
        [Parameter(Mandatory)] $HostQa
    )

    $finalizer = Join-Path $RepositoryRoot 'eng/devflow/Finalize-DevFlowFlowPilotManifest.ps1'
    try {
        # The finalizer is a PowerShell script, not a native command, so it never sets an exit
        # status of its own. Reading $LASTEXITCODE here reported whatever the last native process
        # left behind - normally the failing `dotnet test` this run is reporting on - and threw a
        # successful finalization away, replacing the shared manifest with the generic fallback on
        # every failing Android run. Failure is signalled by a terminating error under
        # $ErrorActionPreference = 'Stop'; success is confirmed from the artifact it must produce.
        & $finalizer `
            -ManifestPath $ManifestPath `
            -RepositoryRoot $RepositoryRoot `
            -ArtifactRoots @($ArtifactRoot, $ResultsRoot) `
            -Platform android `
            -RepositoryCommit $Commit `
            -WorkflowRunId $RunId `
            -AndroidApiLevel $env:DEVFLOW_TEST_ANDROID_API `
            -AndroidAvdName $env:DEVFLOW_TEST_ANDROID_AVD `
            -DeviceEvidenceKind emulator |
            # Discarded deliberately: anything the finalizer wrote to the success stream would be
            # folded into this function's return value, and `$result.ok` on an Object[] throws
            # under Set-StrictMode - ending the run before either report is written.
            Out-Null

        if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
            throw 'The shared manifest finalizer produced no manifest.'
        }

        $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable
        if ($null -eq $manifest -or
            [string]::IsNullOrWhiteSpace([string] $manifest['finalizedAt'])) {
            throw 'The shared manifest finalizer did not finalize the manifest.'
        }

        $manifest['hostQa'] = $HostQa
        Write-AtomicJson -Path $ManifestPath -Value $manifest
        return [ordered]@{ ok = $true }
    }
    catch {
        # Reported to the caller rather than repaired here: the caller owns both reports, and the
        # omission this failure produces has to reach the flow-run report as well as the manifest.
        return [ordered]@{ ok = $false }
    }
}

# The unfinalized flow-pilot manifest is the only account the test process wrote of the attempts it
# observed. Overwriting it with the generic manifest destroyed that evidence outright, so it is
# copied to a fixed, bounded name first and published as an artifact of this run. Nothing about the
# copy is taken from input: the name is fixed, the source is the manifest path this pass owns, and
# a manifest too large to bound is reported rather than copied.
function Save-UnfinalizedManifest {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $ManifestPath
    )

    $target = Join-Path (Split-Path -Parent $ManifestPath) $UnfinalizedManifestName
    $relativePath = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path $target
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return [ordered]@{
            ok = $false
            reason = 'no-manifest'
            path = $target
            relativePath = $relativePath
        }
    }

    $source = Get-Item -LiteralPath $ManifestPath -Force
    if ($source.Length -gt $MaxPreservedManifestBytes) {
        return [ordered]@{
            ok = $false
            reason = 'manifest-too-large'
            path = $target
            relativePath = $relativePath
        }
    }

    try {
        Copy-Item -LiteralPath $ManifestPath -Destination $target -Force
    }
    catch {
        return [ordered]@{
            ok = $false
            reason = 'copy-failed'
            path = $target
            relativePath = $relativePath
        }
    }

    $record = New-ArtifactRecord -RepositoryRoot $RepositoryRoot -Path $target
    if ($null -eq $record) {
        # Removed rather than left behind: a file in the published directory that no artifact entry
        # accounts for is evidence a consumer cannot verify.
        Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        return [ordered]@{
            ok = $false
            reason = 'hash-failed'
            path = $target
            relativePath = $relativePath
        }
    }

    return [ordered]@{
        ok = $true
        record = $record
        path = $target
        relativePath = $relativePath
    }
}

# The one decision the shared-manifest fallback has to make, in one place: preserve the pilot
# manifest when it exists and there is room for it inside the artifact cap, and state exactly what
# happened either way. The omission it returns is published in both reports.
function Resolve-PreservedPilotManifest {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $ManifestPath,
        [Parameter(Mandatory)][int] $RecordedArtifacts
    )

    $preserved = Save-UnfinalizedManifest -RepositoryRoot $RepositoryRoot -ManifestPath $ManifestPath
    if ($preserved.ok -and $RecordedArtifacts -lt $MaxArtifactRecords) {
        return [ordered]@{
            record = $preserved.record
            relativePath = $preserved.relativePath
            overCap = $false
            omission = [ordered]@{
                kind = 'shared-manifest'
                reason = 'The shared Android flow-pilot manifest could not be finalized, so the unfinalized manifest was preserved beside the generic one.'
                preserved = $true
                preservedPath = $preserved.record.path
            }
        }
    }

    if ($preserved.ok) {
        # Publishing a digest for a file the inventory has no room to list would claim evidence the
        # counts do not carry, so the copy is removed again rather than left unreferenced.
        Remove-Item -LiteralPath $preserved.path -Force -ErrorAction SilentlyContinue
        return [ordered]@{
            record = $null
            relativePath = $preserved.relativePath
            overCap = $true
            omission = [ordered]@{
                kind = 'shared-manifest'
                reason = 'The shared Android flow-pilot manifest could not be finalized, and the artifact cap left no room to preserve it.'
                preserved = $false
            }
        }
    }

    return [ordered]@{
        record = $null
        relativePath = $preserved.relativePath
        overCap = $false
        omission = [ordered]@{
            kind = 'shared-manifest'
            reason = 'The shared Android flow-pilot manifest could not be finalized and the unfinalized manifest could not be preserved.'
            preserved = $false
            preservedFailure = $preserved.reason
        }
    }
}

function Invoke-Qualification {
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Configuration,
        [Parameter(Mandatory)][bool] $NoBuild,
        [Parameter(Mandatory)][string] $Platform,
        [Parameter(Mandatory)][string] $ManifestPath,
        [Parameter(Mandatory)][string] $ArtifactRoot,
        [Parameter(Mandatory)][string] $DiagnosticDirectory,
        [string] $AccumulateDirectory,
        [string] $BaselinePath
    )

    $cliProject = Join-Path $RepositoryRoot 'src/Cli/Microsoft.Maui.Cli/Microsoft.Maui.Cli.csproj'
    $outputPath = Join-Path $ArtifactRoot 'qualification.json'
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.AddRange([string[]] @('run', '--project', $cliProject, '-f', 'net10.0', '--configuration', $Configuration))
    if ($NoBuild) {
        $arguments.Add('--no-build')
    }
    $arguments.AddRange([string[]] @(
            '--',
            'devflow', 'flow', 'qualify',
            '--platform', $Platform,
            '--corpus', (Join-Path $RepositoryRoot 'tests/DevFlow/InspectorCorpus'),
            '--artifact-manifest', $ManifestPath,
            '--output', $outputPath,
            '--json',
            '--fail-on-non-pass'))
    if (-not [string]::IsNullOrWhiteSpace($AccumulateDirectory)) {
        $arguments.AddRange([string[]] @('--accumulate', $AccumulateDirectory))
    }
    if (-not [string]::IsNullOrWhiteSpace($BaselinePath)) {
        $arguments.AddRange([string[]] @('--baseline', $BaselinePath))
    }

    $result = Invoke-RecordedCommand -FileName 'dotnet' -Arguments $arguments.ToArray() -DiagnosticPath (Join-Path $DiagnosticDirectory 'qualification-output.txt')
    $status = if ($result.exitCode -eq 0) {
        'qualified'
    }
    elseif (Test-Path -LiteralPath $outputPath -PathType Leaf) {
        try {
            $report = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
            if ($report.status -eq 'not-qualified') { 'not-qualified' } else { 'qualification-failed' }
        }
        catch {
            'qualification-failed'
        }
    }
    else {
        'qualification-failed'
    }

    [ordered]@{
        status = $status
        path = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path $outputPath
        diagnostic = Get-RepositoryRelativePath -RepositoryRoot $RepositoryRoot -Path $result.diagnosticPath
        exitCode = $result.exitCode
    }
}

$platform = $null
$repeat = 3
$resultsRootInput = $null
$configuration = 'Debug'
$flowFilter = $null
$noBuild = $false
$qualification = $false
$accumulateDirectory = $null
$baselinePath = $null
$experimental = $false
$physicalDevice = $false
$deviceId = $null
$iosRuntime = $null
$signingIdentity = $null
$provisioningProfile = $null
$keychain = $null
$verbosity = 'normal'
$dryRun = $false

for ($index = 0; $index -lt $CliArgs.Count; $index++) {
    $token = $CliArgs[$index].ToLowerInvariant()
    switch ($token) {
        '--help' {
            Write-Output (Write-Usage)
            exit $ExitSuccess
        }
        '-help' {
            Write-Output (Write-Usage)
            exit $ExitSuccess
        }
        '--platform' { $platform = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-platform' { $platform = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--repeat' { $repeat = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-repeat' { $repeat = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--results-root' { $resultsRootInput = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-results-root' { $resultsRootInput = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--configuration' { $configuration = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-configuration' { $configuration = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--flow-filter' { $flowFilter = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-flow-filter' { $flowFilter = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--no-build' { $noBuild = $true; break }
        '-no-build' { $noBuild = $true; break }
        '--qualification' { $qualification = $true; break }
        '-qualification' { $qualification = $true; break }
        '--accumulate' { $accumulateDirectory = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-accumulate' { $accumulateDirectory = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--baseline' { $baselinePath = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-baseline' { $baselinePath = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--experimental' { $experimental = $true; break }
        '-experimental' { $experimental = $true; break }
        '--physical-device' { $physicalDevice = $true; break }
        '-physical-device' { $physicalDevice = $true; break }
        '--device-id' { $deviceId = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-device-id' { $deviceId = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--ios-runtime' { $iosRuntime = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-ios-runtime' { $iosRuntime = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--signing-identity' { $signingIdentity = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--ios-signing-identity' { $signingIdentity = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--provisioning-profile' { $provisioningProfile = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--ios-provisioning-profile' { $provisioningProfile = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--keychain' { $keychain = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--ios-keychain' { $keychain = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--verbosity' { $verbosity = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '-verbosity' { $verbosity = Get-RequiredValue $token $index $CliArgs; $index++; break }
        '--verbose' { $verbosity = 'detailed'; break }
        '--dry-run' { $dryRun = $true; break }
        '-dry-run' { $dryRun = $true; break }
        default { Exit-Usage "Unknown option '$($CliArgs[$index])'." }
    }
}

if ([string]::IsNullOrWhiteSpace($platform)) {
    Exit-Usage '--platform is required.'
}
$platform = $platform.ToLowerInvariant()
if ($platform -notin @('android', 'windows', 'ios', 'maccatalyst', 'macos')) {
    Exit-Usage "--platform '$platform' is not supported."
}
if ([string]::IsNullOrWhiteSpace($resultsRootInput)) {
    Exit-Usage '--results-root is required.'
}
$parsedRepeat = 0
if (-not [int]::TryParse([string] $repeat, [ref] $parsedRepeat) -or $parsedRepeat -lt 1 -or $parsedRepeat -gt 20) {
    Exit-Usage '--repeat must be an integer from 1 through 20. Use --accumulate to merge evidence across independent runs instead of raising this cap.'
}
$repeat = $parsedRepeat
if (-not [string]::IsNullOrWhiteSpace($accumulateDirectory) -and -not $qualification) {
    Exit-Usage '--accumulate requires --qualification.'
}
if (-not [string]::IsNullOrWhiteSpace($baselinePath) -and -not $qualification) {
    Exit-Usage '--baseline requires --qualification.'
}
Test-OptionValue $configuration '--configuration'
if ($configuration -notmatch '^[A-Za-z0-9._-]+$') {
    Exit-Usage '--configuration may contain only letters, digits, dot, underscore, and hyphen.'
}
if ($verbosity -notin @('quiet', 'minimal', 'normal', 'detailed', 'diagnostic')) {
    Exit-Usage '--verbosity must be quiet, minimal, normal, detailed, or diagnostic.'
}
if ($flowFilter) { Test-OptionValue $flowFilter '--flow-filter' }
if ($deviceId) { Test-OptionValue $deviceId '--device-id' }
if ($iosRuntime) { Test-OptionValue $iosRuntime '--ios-runtime' }
if ($signingIdentity) { Test-OptionValue $signingIdentity '--signing-identity' }
if ($provisioningProfile) { Test-OptionValue $provisioningProfile '--provisioning-profile' }
if ($keychain) { Test-OptionValue $keychain '--keychain' }

if ($platform -eq 'macos' -and -not $experimental) {
    Exit-Usage '--platform macos is experimental and requires --experimental.'
}
if ($experimental -and $platform -ne 'macos') {
    Exit-Usage '--experimental applies only to the separately labeled macos/AppKit lane.'
}
if ($platform -eq 'macos' -and $qualification) {
    Exit-Usage '--qualification cannot be used for experimental AppKit; it never qualifies an official MAUI or Mac Catalyst gate.'
}
if ($physicalDevice -and $platform -ne 'ios') {
    Exit-Usage '--physical-device applies only to --platform ios.'
}
if ($physicalDevice -and $iosRuntime) {
    Exit-Usage '--ios-runtime is a simulator selector and cannot be combined with --physical-device.'
}
if ($iosRuntime -and $platform -ne 'ios') {
    Exit-Usage '--ios-runtime applies only to the iOS Simulator lane.'
}
if ($deviceId -and -not ($platform -eq 'android' -or ($platform -eq 'ios' -and $physicalDevice))) {
    Exit-Usage '--device-id applies to Android or to the physical-iOS lane.'
}
if ($platform -eq 'ios' -and $physicalDevice) {
    foreach ($required in @(
            @{ name = '--device-id'; value = $deviceId },
            @{ name = '--signing-identity'; value = $signingIdentity },
            @{ name = '--provisioning-profile'; value = $provisioningProfile },
            @{ name = '--keychain'; value = $keychain })) {
        if ([string]::IsNullOrWhiteSpace($required.value)) {
            Exit-Usage "Physical iOS requires $($required.name)."
        }
    }
}
elseif ($signingIdentity -or $provisioningProfile -or $keychain) {
    Exit-Usage 'Signing, provisioning, and keychain options apply only to --platform ios --physical-device.'
}

Register-SecretValue $signingIdentity
Register-SecretValue $provisioningProfile
Register-SecretValue $keychain
Register-SecretValue $env:DEVFLOW_APPLE_AGENT_SESSION_SECRET

$repositoryRoot = Get-CanonicalPath (Join-Path $PSScriptRoot '../..')
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'global.json') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'MauiLabs.slnx') -PathType Leaf)) {
    [Console]::Error.WriteLine('flow-qa: The script must be run from a maui-labs checkout with global.json.')
    exit $ExitPrerequisite
}

$resultsRoot = Resolve-ResultsRoot -InputPath $resultsRootInput -RepositoryRoot $repositoryRoot -Platform $platform
$runId = Get-RunId
$artifactRoot = Resolve-ArtifactRoot -RepositoryRoot $repositoryRoot -Platform $platform -RunId $runId
$diagnosticDirectory = Join-Path $artifactRoot 'host-diagnostics'
$manifestPath = Join-Path $artifactRoot 'manifest.json'
$flowRunPath = Join-Path $artifactRoot 'flow-run.json'
$testProject = Join-Path $repositoryRoot 'src/DevFlow/Microsoft.Maui.DevFlow.Agent.IntegrationTests/Microsoft.Maui.DevFlow.Agent.IntegrationTests.csproj'
$appProject = if ($platform -eq 'macos') {
    Join-Path $repositoryRoot 'samples/DevFlow.Sample.MacOS/DevFlow.Sample.MacOS.csproj'
}
else {
    Join-Path $repositoryRoot 'samples/DevFlow.Sample/DevFlow.Sample.csproj'
}
$appleTestAgentRoot = Join-Path $repositoryRoot 'src/DevFlow/Microsoft.Maui.DevFlow.TestAgent'
$appleTestAgentHostProject = Join-Path $repositoryRoot 'src/DevFlow/Microsoft.Maui.DevFlow.TestAgent.Host/Microsoft.Maui.DevFlow.TestAgent.Host.csproj'
$appleTestAgentNativeProject = Join-Path $appleTestAgentRoot 'AppleXCTestAgent/DevFlowAppleTestAgent.xcodeproj/project.pbxproj'
$appleTestAgentSourceAvailable =
    (Test-Path -LiteralPath $appleTestAgentHostProject -PathType Leaf) -and
    (Test-Path -LiteralPath $appleTestAgentNativeProject -PathType Leaf)
# The PowerShell entry point intentionally remains a non-executing Apple lane. Only the guarded
# macOS shell --apple-spike command can establish the runtime proof and emit its machine-readable
# report, so source presence must never convert this lane into a passing test invocation.
$appleTestAgentAvailable = $false
$baseFilter = switch ($platform) {
    'android' { 'Category=FlowPilot' }
    'windows' { 'Category=WindowsFlowQa' }
    'macos' { 'Category=AppKitFlowQa' }
    default { 'Category=AppleTestAgent' }
}
$testFilter = if ($flowFilter) { "$baseFilter&($flowFilter)" } else { $baseFilter }
$trxFileName = if ($platform -in @('android', 'windows')) {
    "devflow-flow-$platform-$runId.trx"
}
else {
    "devflow-flow-$platform-$runId-attempt-{attempt}.trx"
}
$testArguments = [System.Collections.Generic.List[string]]::new()
$testArguments.AddRange([string[]] @(
        'test',
        $testProject,
        '--configuration',
        $configuration,
        '--filter',
        $testFilter,
        '--logger',
        "trx;LogFileName=$trxFileName",
        '--logger',
        "console;verbosity=$verbosity",
        '--results-directory',
        $resultsRoot))
if ($noBuild) {
    $testArguments.Add('--no-build')
}

function Get-TestArgumentsForAttempt {
    param([Parameter(Mandatory)][int] $Attempt)

    $arguments = [string[]] @($testArguments)
    for ($index = 0; $index -lt $arguments.Length; $index++) {
        $arguments[$index] = $arguments[$index].Replace('{attempt}', $Attempt.ToString())
    }

    $arguments
}

$dryRunObject = [ordered]@{
    schema = 1
    kind = 'devflow-flow-qa-dry-run'
    status = 'dry-run'
    platform = $platform
    repeat = $repeat
    configuration = $configuration
    testFilter = $testFilter
    noBuild = $noBuild
    qualificationRequested = $qualification
    experimental = $experimental
    backend = if ($platform -eq 'macos') { 'appkit' } else { $null }
    officialCoverage = if ($platform -eq 'macos') { $false } else { $true }
    macCatalystEquivalent = if ($platform -eq 'macos') { $false } else { $null }
    physicalDevice = $physicalDevice
    signingInputsConfigured = [bool] ($signingIdentity -and $provisioningProfile -and $keychain)
    appProject = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $appProject
    command = [ordered]@{
        tool = 'dotnet'
        arguments = @($testArguments)
    }
    artifactPaths = [ordered]@{
        testResults = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $resultsRoot
        artifactRoot = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $artifactRoot
        manifest = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $manifestPath
        flowRun = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $flowRunPath
    }
    capability = [ordered]@{
        required = if ($platform -in @('ios', 'maccatalyst', 'macos')) { 'apple-test-agent' } else { 'platform-fixture' }
        available = if ($platform -in @('ios', 'maccatalyst', 'macos')) { $appleTestAgentAvailable } else { $true }
        # The same three states the shell entry point reports, for the same reasons: a checked-in
        # agent source tree means the runtime proof is required but has not been established
        # ('proof-required'); no source at all means the spike itself is still pending
        # ('pending-spike'); every non-Apple lane runs a fixture that is planned and present.
        state = if ($platform -in @('ios', 'maccatalyst', 'macos')) {
            if ($appleTestAgentSourceAvailable) { 'proof-required' } else { 'pending-spike' }
        }
        else {
            'planned'
        }
        sourceAvailable = if ($platform -in @('ios', 'maccatalyst', 'macos')) { $appleTestAgentSourceAvailable } else { $true }
    }
}

if ($dryRun) {
    $dryRunObject | ConvertTo-Json -Depth 16 -Compress
    exit $ExitSuccess
}

Push-Location $repositoryRoot
try {
    New-Item -ItemType Directory -Force -Path $resultsRoot, $artifactRoot, $diagnosticDirectory | Out-Null
    Assert-ExistingPathIsNotReparsePoint -RepositoryRoot $repositoryRoot -Path $resultsRoot
    Assert-ExistingPathIsNotReparsePoint -RepositoryRoot $repositoryRoot -Path $artifactRoot
    if ((Test-Path -LiteralPath $manifestPath) -or (Test-Path -LiteralPath $flowRunPath)) {
        [Console]::Error.WriteLine("flow-qa: Refusing to overwrite existing run artifacts for '$runId'.")
        exit $ExitUsage
    }

    $commit = Get-RepositoryCommit $repositoryRoot
    $hostMetadata = Get-HostMetadata -Platform $platform -IosRuntime $iosRuntime -PhysicalDevice $physicalDevice -DeviceId $deviceId
    $flowDigests = Get-FlowDigests -RepositoryRoot $repositoryRoot -Platform $platform
    $attempts = [System.Collections.Generic.List[object]]::new()
    $omissions = [System.Collections.Generic.List[object]]::new()
    $omissions.Add([ordered]@{
            kind = 'diagnostic-rerun'
            reason = 'No automatic diagnostic rerun was performed because replay may mutate state.'
        })
    $status = 'pending'
    $classification = 'pending'
    $qualificationResult = $null

    $hostQa = [ordered]@{
        runId = $runId
        configuration = $configuration
        repeat = $repeat
        platformFilter = $baseFilter
        testFilterDigest = Get-StringDigest $testFilter
        noBuild = $noBuild
        host = $hostMetadata
        resetSeed = [ordered]@{
            resetFingerprint = 'not-observed'
            seedFingerprint = 'not-observed'
            backendStateFingerprint = 'not-observed'
        }
        windowsSession = $null
        firstAttempt = $null
        cleanAttempts = @()
        diagnosticReruns = @()
        diagnosticRerunPolicy = 'No automatic diagnostic rerun is performed because replay may mutate state.'
        repeatOwner = if ($platform -eq 'windows') {
            'windows-fixture-per-flow-clean-attempts'
        }
        elseif ($platform -eq 'android') {
            'android-fixture-per-flow-clean-attempts'
        }
        else {
            'script-per-attempt'
        }
    }

    $writeArtifacts = {
        param([string] $CurrentStatus, [string] $CurrentClassification)

        $windowsTierOneFacts = if ($platform -eq 'windows') {
            Get-WindowsTierOneFacts -ArtifactRoot $artifactRoot
        }
        else {
            $null
        }
        $windowsAppBuildFingerprint = $null
        $windowsPackageDigest = $null
        if ($null -ne $windowsTierOneFacts) {
            $windowsAppBuildFingerprint = $windowsTierOneFacts.appBuildFingerprint
            $windowsPackageDigest = $windowsTierOneFacts.packageDigest
            if ([string]::IsNullOrWhiteSpace($windowsAppBuildFingerprint)) {
                $windowsAppBuildFingerprint = $null
            }
            if ([string]::IsNullOrWhiteSpace($windowsPackageDigest)) {
                $windowsPackageDigest = $null
            }
            $hostQa.resetSeed.resetFingerprint = $windowsTierOneFacts.resetFingerprint ?? 'not-observed'
            $hostQa.resetSeed.seedFingerprint = $windowsTierOneFacts.seedFingerprint ?? 'not-observed'
            $hostQa.resetSeed.backendStateFingerprint = $windowsTierOneFacts.backendStateFingerprint ?? 'not-observed'
            $hostQa.tierOneManifest = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $windowsTierOneFacts.manifestPath
        }

        if ($attempts.Count -gt 0) {
            $hostQa.firstAttempt = $attempts[0]
        }
        $hostQa.cleanAttempts = @($attempts)
        $hostQa.status = $CurrentStatus
        $hostQa.classification = $CurrentClassification
        $hostQa.qualification = $qualificationResult

        # Write-local omissions are derived again on every write pass from what is observable at
        # that moment, and are never appended to the run-scoped list. A second pass after
        # qualification would otherwise restate the same facts and publish a manifest whose
        # omissions grow with the number of writes rather than with what was actually omitted.
        $writeOmissions = [System.Collections.Generic.List[object]]::new()
        foreach ($omission in @($omissions)) {
            $writeOmissions.Add($omission)
        }
        if ($CurrentClassification -in @('flow-failure', 'infrastructure-failure')) {
            $failureTrace = Get-ChildItem -LiteralPath $artifactRoot -Recurse -Filter '*.mauitrace' -File -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($null -eq $failureTrace) {
                $writeOmissions.Add([ordered]@{
                        kind = 'failure-evidence'
                        reason = 'No failure .mauitrace was available for this terminal outcome.'
                    })
            }
        }
        if ($platform -ne 'android' -and [string]::IsNullOrWhiteSpace($windowsPackageDigest)) {
            $writeOmissions.Add([ordered]@{
                    kind = 'package-digest'
                    reason = 'The platform host did not emit a packaged-app digest for this run.'
                })
        }
        $appSourceDigest = Get-FileDigest $appProject
        if ($null -eq $appSourceDigest) {
            $writeOmissions.Add([ordered]@{
                    kind = 'app-digest'
                    reason = 'The selected app project was unavailable or could not be hashed.'
                })
        }

        Write-HostDiagnostics -Directory $diagnosticDirectory -Metadata $hostMetadata -Status $CurrentStatus -Classification $CurrentClassification

        # One artifact pass decides both reports. The manifest and the flow-run report are both
        # written here, so neither can appear in a list taken before it exists; every other
        # artifact fact - what could not be hashed, how many references the cap dropped, and how
        # many were recorded - is computed once and published identically in both files.
        $artifactData = Get-ArtifactRecords -RepositoryRoot $repositoryRoot -Roots @($artifactRoot, $resultsRoot) -ResultsRoot $resultsRoot -RunId $runId -ExcludePaths @($manifestPath, $flowRunPath)
        foreach ($omission in @($artifactData.omissions)) {
            $writeOmissions.Add($omission)
        }

        $artifactRecords = [System.Collections.Generic.List[object]]::new()
        foreach ($record in @($artifactData.records)) {
            $artifactRecords.Add($record)
        }
        $flowRunWithinCap = $artifactRecords.Count -lt $MaxArtifactRecords
        $omittedByLimit = [int] $artifactData.omittedByLimit
        # Every reference this pass excluded from the inventory, not only the ones the cap dropped.
        # A summary that counted the cap alone reported a complete inventory for a run whose
        # unhashable or out-of-repository evidence was missing from the list beside it.
        $omittedOther = [int] $artifactData.omittedOther
        if (-not $flowRunWithinCap) {
            $omittedByLimit++
        }
        $recordedArtifacts = $artifactRecords.Count + $(if ($flowRunWithinCap) { 1 } else { 0 })
        if ($omittedByLimit -gt 0) {
            $writeOmissions.Add([ordered]@{
                    kind = 'artifact-limit'
                    reason = "Only the first $MaxArtifactRecords artifact references were hashed."
                    omittedArtifacts = $omittedByLimit
                })
        }
        # `truncated` stays the narrower fact it names: the cap, and only the cap, was reached.
        $newArtifactSummary = {
            param([int] $Recorded, [int] $ByLimit, [int] $Other)
            [ordered]@{
                maxArtifacts = $MaxArtifactRecords
                recordedArtifacts = $Recorded
                omittedArtifacts = $ByLimit + $Other
                truncated = $ByLimit -gt 0
            }
        }
        $artifactSummary = & $newArtifactSummary $recordedArtifacts $omittedByLimit $omittedOther

        $flowRun = [ordered]@{
            schema = 1
            kind = 'devflow-flow-qa-run'
            generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
            repository = [ordered]@{ commit = $commit }
            platform = $platform
            experimental = if ($platform -eq 'macos') { $true } else { $false }
            backend = if ($platform -eq 'macos') { 'appkit' } else { $null }
            officialCoverage = if ($platform -eq 'macos') { $false } else { $true }
            macCatalystEquivalent = if ($platform -eq 'macos') { $false } else { $null }
            hostQa = $hostQa
            flows = @($flowDigests)
            app = [ordered]@{
                project = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $appProject
                sourceDigest = $appSourceDigest
                buildFingerprint = $windowsAppBuildFingerprint
                packageDigest = $windowsPackageDigest
            }
            firstAttempt = $hostQa.firstAttempt
            diagnosticReruns = @()
            artifactSummary = $artifactSummary
            omissions = @($writeOmissions)
            privacy = [ordered]@{
                excludedByDefault = @('screenshots', 'source', 'raw-model-context', 'environment', 'signing-inputs')
            }
        }
        Write-AtomicJson -Path $flowRunPath -Value $flowRun

        # The flow-run report is final for this pass now, so hashing it here describes the bytes
        # that are actually on disk. The manifest is written last and is excluded from its own
        # digest list for the same reason.
        if ($flowRunWithinCap) {
            $flowRunRecord = New-ArtifactRecord -RepositoryRoot $repositoryRoot -Path $flowRunPath
            if ($null -eq $flowRunRecord) {
                # The report could not be hashed after all, so the count it published is wrong and
                # both files have to say so. The report carries no digest of itself, so correcting
                # it in place is safe.
                $flowRunWithinCap = $false
                $omittedOther++
                $writeOmissions.Add([ordered]@{
                        kind = 'artifact-hash'
                        reason = 'An artifact could not be hashed.'
                        path = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $flowRunPath
                    })
                $artifactSummary = & $newArtifactSummary $artifactRecords.Count $omittedByLimit $omittedOther
                $flowRun['artifactSummary'] = $artifactSummary
                $flowRun['omissions'] = @($writeOmissions)
                Write-AtomicJson -Path $flowRunPath -Value $flowRun
            }
            else {
                $artifactRecords.Add($flowRunRecord)
            }
        }

        # Android alone shares its manifest with the test process, so its manifest is finalized
        # rather than written here. A finalization that fails still owes a consumer the pilot
        # evidence the test process wrote, so it is preserved beside the fallback and both reports
        # state the omission before either is written.
        $sharedManifestFailed = $false
        if ($platform -eq 'android') {
            $finalization = Finalize-AndroidManifest -RepositoryRoot $repositoryRoot -ManifestPath $manifestPath -ArtifactRoot $artifactRoot -ResultsRoot $resultsRoot -Commit $commit -RunId $runId -HostQa $hostQa
            if (-not $finalization.ok) {
                $sharedManifestFailed = $true
                $preserved = Resolve-PreservedPilotManifest `
                    -RepositoryRoot $repositoryRoot `
                    -ManifestPath $manifestPath `
                    -RecordedArtifacts $artifactRecords.Count
                $preservedPath = [string] $preserved.relativePath
                if (-not [string]::IsNullOrWhiteSpace($preservedPath)) {
                    for ($index = $artifactRecords.Count - 1; $index -ge 0; $index--) {
                        if ([string] $artifactRecords[$index].path -ceq $preservedPath) {
                            $artifactRecords.RemoveAt($index)
                        }
                    }
                }
                if ($null -ne $preserved.record) {
                    # A previous run under the same run id may have left a copy this pass's artifact
                    # scan already hashed. Its digest describes bytes that were just overwritten, so
                    # the stale record is dropped before the fresh one is added: two entries for one
                    # path, only one of which matches the file, disqualifies the whole manifest.
                    $artifactRecords.Add($preserved.record)
                }
                if ($preserved.overCap) {
                    # Counted with the other exclusions rather than against the cap: the
                    # artifact-limit omission has already been written with its own number, and
                    # adding to that number here would leave the two disagreeing.
                    $omittedOther++
                }
                $writeOmissions.Add($preserved.omission)

                $artifactSummary = & $newArtifactSummary $artifactRecords.Count $omittedByLimit $omittedOther
                $flowRun['artifactSummary'] = $artifactSummary
                $flowRun['omissions'] = @($writeOmissions)
                Write-AtomicJson -Path $flowRunPath -Value $flowRun

                # The report was rewritten, so the digest published for it describes bytes that no
                # longer exist. It is hashed again only when a record for it is already in the
                # list: appending one here would publish one more artifact than the counts claim.
                if ($flowRunWithinCap) {
                    $flowRunRelative = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $flowRunPath
                    $existingIndex = -1
                    for ($index = 0; $index -lt $artifactRecords.Count; $index++) {
                        if ([string] $artifactRecords[$index].path -ceq $flowRunRelative) {
                            $existingIndex = $index
                            break
                        }
                    }

                    if ($existingIndex -ge 0) {
                        $refreshed = New-ArtifactRecord -RepositoryRoot $repositoryRoot -Path $flowRunPath
                        if ($null -eq $refreshed) {
                            $artifactRecords.RemoveAt($existingIndex)
                            $omittedOther++
                            $writeOmissions.Add([ordered]@{
                                    kind = 'artifact-hash'
                                    reason = 'An artifact could not be hashed.'
                                    path = $flowRunRelative
                                })
                            $artifactSummary = & $newArtifactSummary $artifactRecords.Count $omittedByLimit $omittedOther
                            $flowRun['artifactSummary'] = $artifactSummary
                            $flowRun['omissions'] = @($writeOmissions)
                            Write-AtomicJson -Path $flowRunPath -Value $flowRun
                        }
                        else {
                            $artifactRecords[$existingIndex] = $refreshed
                        }
                    }
                }
            }
        }

        # Ordinal, not culture-aware: `Sort-Object -CaseSensitive` still orders by the current
        # culture's collation, which disagrees with the shell entry point's byte ordering on the
        # underscores, spaces, and mixed case that real .trx names carry.
        $orderedArtifacts = [System.Collections.Generic.List[object]]::new()
        foreach ($record in $artifactRecords) {
            $orderedArtifacts.Add($record)
        }
        $orderedArtifacts.Sort([System.Comparison[object]] {
                param($left, $right)
                [string]::CompareOrdinal([string] $left.path, [string] $right.path)
            })

        $fallbackArguments = @{
            ManifestPath = $manifestPath
            RepositoryRoot = $repositoryRoot
            Commit = $commit
            RunId = $runId
            Platform = $platform
            AppProject = $appProject
            FlowDigests = $flowDigests
            HostQa = $hostQa
            Artifacts = $orderedArtifacts
            ArtifactSummary = $artifactSummary
            Omissions = @($writeOmissions)
            AppBuildFingerprint = $windowsAppBuildFingerprint
            PackageDigest = $windowsPackageDigest
        }
        if ($platform -ne 'android' -or $sharedManifestFailed) {
            Write-GenericManifest @fallbackArguments
        }
    }
    $writeStatus = {
        param([string] $CurrentStatus, [string] $CurrentClassification)
        [Console]::Error.WriteLine(
            "flow-qa: platform=$platform status=$CurrentStatus classification=$CurrentClassification artifacts=$(Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $artifactRoot)")
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $status = 'failed'
        $classification = 'prerequisite-missing'
        $omissions.Add([ordered]@{ kind = 'prerequisite'; reason = 'dotnet was not found. The script does not install SDKs or workloads.' })
        & $writeArtifacts $status $classification
        & $writeStatus $status $classification
        exit $ExitPrerequisite
    }
    if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
        $status = 'failed'
        $classification = 'prerequisite-missing'
        $omissions.Add([ordered]@{ kind = 'test-project'; reason = 'The integration test project was unavailable.' })
        & $writeArtifacts $status $classification
        & $writeStatus $status $classification
        exit $ExitPrerequisite
    }

    $runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
    $runningOnMacOS = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)
    if (($platform -eq 'windows' -and -not $runningOnWindows) -or
        ($platform -in @('ios', 'maccatalyst', 'macos') -and -not $runningOnMacOS)) {
        $status = 'failed'
        $classification = 'prerequisite-missing'
        $omissions.Add([ordered]@{ kind = 'host-platform'; reason = "The '$platform' lane requires its matching host OS." })
        & $writeArtifacts $status $classification
        & $writeStatus $status $classification
        exit $ExitPrerequisite
    }

    if ($platform -eq 'windows') {
        $windowsSessionAdmission = Get-WindowsDesktopSessionAdmission
        $hostQa.windowsSession = $windowsSessionAdmission
        Write-WindowsSessionDiagnostic -Directory $diagnosticDirectory -Admission $windowsSessionAdmission
        if ($windowsSessionAdmission.admissionResult -ne 'allowed') {
            $status = 'failed'
            $classification = 'infrastructure-failure'
            $hostQa.firstAttempt = [ordered]@{
                kind = 'preflight'
                repetition = 1
                outcome = 'infrastructure-error'
                failureClass = 'infrastructure'
                failurePhase = 'windows-desktop-session-admission'
                mutationDispatched = $false
            }
            $omissions.Add([ordered]@{
                    kind = 'windows-desktop-session'
                    reason = 'Windows desktop session admission failed before the test process launched or any flow replay was dispatched.'
                })
            & $writeArtifacts $status $classification
            & $writeStatus $status $classification
            [Console]::Error.WriteLine(
                "flow-qa: Windows requires an active, unlocked desktop session; admission=$($windowsSessionAdmission.admissionResult) reason=$($windowsSessionAdmission.reason).")
            exit $ExitPrerequisite
        }
    }

    if ($platform -eq 'macos' -and -not (Test-Path -LiteralPath $appProject -PathType Leaf)) {
        $status = 'unsupported'
        $classification = 'unsupported-platform'
        $omissions.Add([ordered]@{ kind = 'appkit-sample'; reason = 'No experimental AppKit sample or fixture project is available.' })
        & $writeArtifacts $status $classification
        & $writeStatus $status $classification
        exit $ExitPending
    }

    if ($platform -in @('ios', 'maccatalyst', 'macos') -and -not $appleTestAgentAvailable) {
        $status = 'pending-spike'
        $classification = 'capability-missing'
        $omissions.Add([ordered]@{
                kind = 'apple-test-agent'
                reason = 'Apple XCTest agent source alone is not runtime proof. Run the guarded macOS shell --apple-spike command to prove target foreground, authenticated transport, cancellation, parity, and artifacts.'
            })
        & $writeArtifacts $status $classification
        & $writeStatus $status $classification
        [Console]::Error.WriteLine("flow-qa: $platform is pending the Apple Test Agent spike (capability-missing).")
        exit $ExitPending
    }

    if ($platform -eq 'android') {
        $env:DEVFLOW_TEST_PLATFORM = 'android'
        $env:DEVFLOW_RUN_ANDROID_FLOW_PILOT = '1'
        $env:DEVFLOW_FLOW_PILOT_REPEAT = $repeat.ToString()
        $env:DEVFLOW_FLOW_PILOT_ARTIFACT_ROOT = $artifactRoot
        $env:DEVFLOW_FLOW_PILOT_RESULTS_ROOT = $resultsRoot
        $env:DEVFLOW_FLOW_PILOT_WORKFLOW_RUN_ID = $runId
        $env:DEVFLOW_FLOW_PILOT_REPOSITORY_COMMIT = $commit
        $env:DEVFLOW_FLOW_PILOT_DEVICE_EVIDENCE_KIND = 'emulator'
        if ($deviceId) {
            $env:DEVFLOW_TEST_ANDROID_SERIAL = $deviceId
        }

        $result = Invoke-RecordedCommand -FileName 'dotnet' -Arguments (Get-TestArgumentsForAttempt -Attempt 1) -DiagnosticPath (Join-Path $diagnosticDirectory 'test-output-attempt-1.txt')
        $resolved = Resolve-AttemptClassification -ExitCode $result.exitCode -Output $result.output -ArtifactRoot $artifactRoot -ScriptReportPath $flowRunPath -Markers $result.markers
        $attempts.Add([ordered]@{
                kind = 'clean'
                repetition = 1
                exitCode = $result.exitCode
                classification = $resolved.classification
                classificationSource = $resolved.source
                diagnosticTruncated = $result.truncated
                diagnostic = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $result.diagnosticPath
            })
    }
    elseif ($platform -eq 'windows') {
        $env:DEVFLOW_TEST_PLATFORM = 'windows'
        $env:DEVFLOW_RUN_WINDOWS_FLOW_QA = '1'
        $env:DEVFLOW_FLOW_QA_RUN_ID = $runId
        $env:DEVFLOW_FLOW_QA_REPEAT = $repeat.ToString()
        $env:DEVFLOW_FLOW_QA_ARTIFACT_ROOT = $artifactRoot
        $env:DEVFLOW_FLOW_QA_APP_PROJECT = $appProject

        # WindowsFixture owns each Tier-1 clean reset/relaunch/seed attempt. Invoking the test
        # host once avoids multiplying a requested three clean attempts into nine replays.
        $result = Invoke-RecordedCommand -FileName 'dotnet' -Arguments (Get-TestArgumentsForAttempt -Attempt 1) -DiagnosticPath (Join-Path $diagnosticDirectory 'test-output-attempt-1.txt')
        $resolved = Resolve-AttemptClassification -ExitCode $result.exitCode -Output $result.output -ArtifactRoot $artifactRoot -ScriptReportPath $flowRunPath -Markers $result.markers
        $attempts.Add([ordered]@{
                kind = 'tier-1-corpus'
                repetition = 1
                cleanRepetitionsPerFlow = $repeat
                exitCode = $result.exitCode
                classification = $resolved.classification
                classificationSource = $resolved.source
                diagnosticTruncated = $result.truncated
                diagnostic = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $result.diagnosticPath
            })
    }
    else {
        $env:DEVFLOW_TEST_PLATFORM = $platform
        $env:DEVFLOW_FLOW_QA_RUN_ID = $runId
        $env:DEVFLOW_FLOW_QA_REPEAT = $repeat.ToString()
        $env:DEVFLOW_FLOW_QA_APP_PROJECT = $appProject
        if ($physicalDevice) {
            $env:DEVFLOW_FLOW_QA_PHYSICAL_DEVICE = '1'
            $env:DEVFLOW_FLOW_QA_DEVICE_ID = $deviceId
            $env:DEVFLOW_IOS_SIGNING_IDENTITY = $signingIdentity
            $env:DEVFLOW_IOS_PROVISIONING_PROFILE = $provisioningProfile
            $env:DEVFLOW_IOS_KEYCHAIN = $keychain
        }
        if ($iosRuntime) {
            $env:DEVFLOW_TEST_IOS_VERSION = $iosRuntime
        }

        for ($attempt = 1; $attempt -le $repeat; $attempt++) {
            $result = Invoke-RecordedCommand -FileName 'dotnet' -Arguments (Get-TestArgumentsForAttempt -Attempt $attempt) -DiagnosticPath (Join-Path $diagnosticDirectory "test-output-attempt-$attempt.txt")
            $resolved = Resolve-AttemptClassification -ExitCode $result.exitCode -Output $result.output -ArtifactRoot $artifactRoot -ScriptReportPath $flowRunPath -Markers $result.markers
            $attempts.Add([ordered]@{
                    kind = 'clean'
                    repetition = $attempt
                    exitCode = $result.exitCode
                    classification = $resolved.classification
                    classificationSource = $resolved.source
                    diagnosticTruncated = $result.truncated
                    diagnostic = Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $result.diagnosticPath
                })
        }
    }

    $attemptClassifications = @($attempts | ForEach-Object { $_.classification })
    if ($attemptClassifications -contains 'capability-missing') {
        $status = 'capability-missing'
        $classification = 'capability-missing'
    }
    elseif ($attemptClassifications -contains 'infrastructure-failure') {
        $status = 'failed'
        $classification = 'infrastructure-failure'
    }
    elseif ($attemptClassifications -contains 'flow-failure') {
        $status = 'failed'
        $classification = 'flow-failure'
    }
    else {
        $status = 'passed'
        $classification = 'passed'
    }

    & $writeArtifacts $status $classification

    if ($qualification -and $classification -eq 'passed') {
        $qualificationResult = Invoke-Qualification -RepositoryRoot $repositoryRoot -Configuration $configuration -NoBuild $noBuild -Platform $platform -ManifestPath $manifestPath -ArtifactRoot $artifactRoot -DiagnosticDirectory $diagnosticDirectory -AccumulateDirectory $accumulateDirectory -BaselinePath $baselinePath
        $hostQa.qualification = $qualificationResult
        if ($qualificationResult.status -eq 'not-qualified') {
            $status = 'not-qualified'
            $classification = 'not-qualified'
        }
        elseif ($qualificationResult.status -ne 'qualified') {
            $status = 'failed'
            $classification = 'infrastructure-failure'
        }
        & $writeArtifacts $status $classification
    }
    elseif ($qualification) {
        $omissions.Add([ordered]@{ kind = 'qualification'; reason = 'Qualification was not run because platform execution did not pass.' })
        & $writeArtifacts $status $classification
    }

    [Console]::Error.WriteLine("flow-qa: platform=$platform status=$status classification=$classification artifacts=$(Get-RepositoryRelativePath -RepositoryRoot $repositoryRoot -Path $artifactRoot)")
    switch ($classification) {
        'passed' { exit $ExitSuccess }
        'flow-failure' { exit $ExitFlowFailure }
        'capability-missing' { exit $ExitPending }
        'not-qualified' { exit $ExitPending }
        default { exit $ExitPrerequisite }
    }
}
finally {
    Pop-Location
}
