[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $SourceManifestPath,

    [Parameter(Mandatory)]
    [string] $QualificationPath,

    [string] $OutputDirectory,

    [Parameter(Mandatory)]
    [string] $Repository,

    [Parameter(Mandatory)]
    [string] $WorkflowName,

    [Parameter(Mandatory)]
    [string] $WorkflowPath,

    [Parameter(Mandatory)]
    [string] $SourceEvent,

    [Parameter(Mandatory)]
    [string] $HeadRepository,

    [Parameter(Mandatory)]
    [string] $HeadRef,

    [Parameter(Mandatory)]
    [Int64] $RunId,

    [Parameter(Mandatory)]
    [Int32] $RunAttempt,

    [Parameter(Mandatory)]
    [string] $CommitSha,

    [Int32] $PullRequestNumber = 0,

    [ValidateSet('android-emulator-pilot', 'demo-emulator-showcase', 'physical-device-flow-qa')]
    [string] $LaneKind = 'android-emulator-pilot',

    [switch] $VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedWorkflowName = 'DevFlow Integration Tests'
$expectedWorkflowPath = '.github/workflows/devflow-integration.yml'
$maximumSourceBytes = 1MB
$maximumHandoffBytes = 256KB
$maximumArchiveBytes = 1MB
$maximumJsonSafeInteger = 9007199254740991
$maximumRunAttempt = 1000
$maximumDeclaredArtifactBytes = 1TB
$maximumAttemptsPerFlow = 1000
$maximumQualificationItems = 4096
$maximumMetricCount = 1000000
$maximumFlows = 256
$maximumArtifacts = 256
$requiredQualificationGates = @(
    'android-device-overhead',
    'android-tier1-first-attempts',
    'classification-accuracy',
    'confidence-calibration',
    'corpus-contract',
    'deterministic-host-performance',
    'independent-review',
    'preview-safety-flags',
    'privacy-security-escapes',
    'product-analyzer-coverage',
    'repair-precision',
    'required-evidence',
    'selector-stability',
    'zero-false-heals'
)
$knownQualificationGates = @($requiredQualificationGates) + @('input-contract')
$safeQualificationArtifactKinds = @(
    'android-host-diagnostics',
    'artifact',
    'audit',
    'evidence',
    'fixture-initialization-diagnostic',
    'flow-digest',
    'flow-pilot-manifest',
    'flow-run-report',
    'host-diagnostic',
    'json',
    'mauitrace',
    'model-projection',
    'package-digest',
    'qualification-report',
    'report',
    'test-results'
)
$safeFailureValues = @(
    'action-rejected',
    'agent-disconnected',
    'app-crash',
    'assertion-failed',
    'cancelled',
    'capability-missing',
    'device-failure',
    'disabled',
    'drive-failed',
    'flow-invalid',
    'infrastructure',
    'lease-conflict',
    'lease-lost',
    'locator-ambiguous',
    'locator-not-found',
    'not-visible',
    'precondition-unsatisfied',
    'reset-failed',
    'route-state-drift',
    'schema-unsupported',
    'secret-unavailable',
    'timeout',
    'transport',
    'unknown-completion',
    'unsafe-value',
    'unstable-bounds',
    'workflow-command-conflict'
)

# Exactly one lane profile is resolved for this invocation, and every lane-specific fact the
# producer emits is read from it. The production lane is the only one that can ever emit a
# qualified handoff; the demo lane exists to showcase the local CI-fix route from a deliberately
# nonqualified Android emulator run and carries no repair authority at all. A lane with no profile
# (the ordinary Android emulator pilot) never produces an archive.
$laneProfiles = [ordered]@{
    'physical-device-flow-qa' = [ordered]@{
        laneKind = 'physical-device-flow-qa'
        demo = $false
        manifestSchema = 'devflow-ci-failure-manifest'
        handoffSchema = 'devflow-ci-failure-handoff'
        artifactBaseName = 'devflow-failure-handoff'
        qualification = 'qualified'
        requireQualificationPass = $true
        requiredDeviceKinds = @('physical-device', 'real-device')
        requiredRealDevice = $true
        requiredPlatforms = $null
        requiredSourceEvents = $null
        requiredQualificationStatuses = $null
        createdReason = 'qualified-incident'
        rejectedReason = 'source-lane-not-qualifying'
    }
    'demo-emulator-showcase' = [ordered]@{
        laneKind = 'demo-emulator-showcase'
        demo = $true
        manifestSchema = 'devflow-ci-demo-manifest'
        handoffSchema = 'devflow-ci-demo-handoff'
        artifactBaseName = 'devflow-demo-handoff'
        qualification = 'not-qualified'
        requireQualificationPass = $false
        requiredDeviceKinds = @('emulator')
        requiredRealDevice = $false
        requiredPlatforms = @('android')
        requiredSourceEvents = @('workflow_dispatch')
        requiredQualificationStatuses = @('not-qualified', 'fail')
        createdReason = 'demo-incident'
        rejectedReason = 'demo-lane-not-qualifying'
    }
}
$laneProfile = if ($laneProfiles.Contains($LaneKind)) { $laneProfiles[$LaneKind] } else { $null }

function New-ProducerResult {
    param(
        [Parameter(Mandatory)] [string] $Status,
        [Parameter(Mandatory)] [string] $Reason,
        [string] $ArchiveSha256,
        [string] $HandoffSha256
    )

    $result = [ordered]@{
        status = $Status
        reason = $Reason
    }
    if ($ArchiveSha256) {
        $result['archiveSha256'] = $ArchiveSha256
    }
    if ($HandoffSha256) {
        $result['handoffSha256'] = $HandoffSha256
    }

    return $result
}

function Write-ProducerResult {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Result)

    Write-Output ($Result | ConvertTo-Json -Compress -Depth 4)
}

function Test-JsonInteger {
    param($Value)

    return $Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [Int16] -or
        $Value -is [UInt16] -or
        $Value -is [Int32] -or
        $Value -is [UInt32] -or
        $Value -is [Int64]
}

function Test-JsonArray {
    param($Value)

    return $Value -is [System.Array]
}

function Test-JsonObject {
    param($Value)

    return $Value -is [System.Collections.IDictionary]
}

function Test-JsonIntegerRange {
    param(
        $Value,
        [Parameter(Mandatory)] [Int64] $Minimum,
        [Parameter(Mandatory)] [Int64] $Maximum
    )

    if (-not (Test-JsonInteger $Value)) {
        return $false
    }

    try {
        $number = [Int64] $Value
        return $number -ge $Minimum -and $number -le $Maximum
    }
    catch {
        return $false
    }
}

function Test-JsonNumberRange {
    param(
        $Value,
        [Parameter(Mandatory)] [double] $Minimum,
        [Parameter(Mandatory)] [double] $Maximum
    )

    if (-not (Test-JsonInteger $Value) -and $Value -isnot [double] -and $Value -isnot [single] -and $Value -isnot [decimal]) {
        return $false
    }

    try {
        $number = [double] $Value
        return [double]::IsFinite($number) -and $number -ge $Minimum -and $number -le $Maximum
    }
    catch {
        return $false
    }
}

function Test-RequiredString {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Int32] $MaximumLength = 4096
    )

    return $Object.Contains($Name) -and
        $Object[$Name] -is [string] -and
        -not [string]::IsNullOrWhiteSpace([string] $Object[$Name]) -and
        ([string] $Object[$Name]).Length -le $MaximumLength -and
        ([string] $Object[$Name]).IndexOf([char] 0) -lt 0
}

function Test-RequiredBoolean {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    return $Object.Contains($Name) -and $Object[$Name] -is [bool]
}

function Test-RequiredArray {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Int32] $MaximumCount = $maximumQualificationItems
    )

    return $Object.Contains($Name) -and
        (Test-JsonArray $Object[$Name]) -and
        ([System.Array] $Object[$Name]).Count -le $MaximumCount
}

function Test-RequiredObject {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    return $Object.Contains($Name) -and (Test-JsonObject $Object[$Name])
}

function Test-Sha256 {
    param([string] $Value)

    return $Value -cmatch '^sha256:[0-9a-f]{64}$'
}

function Test-Sha256Identity {
    param([string] $Value)

    return (Test-Sha256 $Value) -or $Value -cmatch '^[0-9a-f]{64}$'
}

function ConvertTo-Sha256Identity {
    param([Parameter(Mandatory)] [string] $Value)

    if (Test-Sha256 $Value) {
        return $Value
    }
    if ($Value -cmatch '^[0-9a-f]{64}$') {
        return "sha256:$Value"
    }
    throw 'sha256-identity-invalid'
}

function Get-Sha256Bytes {
    param([Parameter(Mandatory)] [byte[]] $Bytes)

    return "sha256:$([Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant())"
}

function Get-Sha256Text {
    param([Parameter(Mandatory)] [string] $Text)

    return Get-Sha256Bytes ([System.Text.Encoding]::UTF8.GetBytes($Text))
}

function Get-Fingerprint {
    param([Parameter(Mandatory)] [string] $Value)

    if (Test-Sha256 $Value) {
        return $Value.ToLowerInvariant()
    }

    return Get-Sha256Text $Value
}

function Test-SafeRelativePath {
    param(
        [string] $Value,
        [string] $RequiredSuffix
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value.Length -gt 1024 -or
        $Value.Contains('\') -or
        $Value.StartsWith('/', [StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($Value)) {
        return $false
    }

    $segments = $Value.Split('/')
    if (@($segments | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
        return $false
    }

    return [string]::IsNullOrEmpty($RequiredSuffix) -or
        $Value.EndsWith($RequiredSuffix, [StringComparison]::Ordinal)
}

function ConvertTo-CanonicalJson {
    param($Value)

    if ($null -eq $Value) {
        return 'null'
    }
    if (Test-JsonObject $Value) {
        $keys = @($Value.Keys | ForEach-Object { [string] $_ })
        [Array]::Sort($keys, [StringComparer]::Ordinal)
        $members = foreach ($key in $keys) {
            "$($key | ConvertTo-Json -Compress):$(ConvertTo-CanonicalJson $Value[$key])"
        }
        return "{$($members -join ',')}"
    }
    if (Test-JsonArray $Value) {
        $items = foreach ($item in [System.Array] $Value) {
            ConvertTo-CanonicalJson $item
        }
        return "[$($items -join ',')]"
    }
    if ($Value -is [string]) {
        return ([string] $Value | ConvertTo-Json -Compress)
    }
    if ($Value -is [bool]) {
        return $(if ([bool] $Value) { 'true' } else { 'false' })
    }
    if (Test-JsonInteger $Value) {
        return ([Int64] $Value).ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [double] -or $Value -is [single] -or $Value -is [decimal]) {
        return ([double] $Value | ConvertTo-Json -Compress)
    }

    throw 'canonical-json-type-invalid'
}

function ConvertFrom-StrictJsonElement {
    param([Parameter(Mandatory)] [System.Text.Json.JsonElement] $Element)

    switch ($Element.ValueKind) {
        ([System.Text.Json.JsonValueKind]::Object) {
            $result = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                if ($result.Contains($property.Name)) {
                    throw 'json-property-duplicate'
                }
                $result[$property.Name] = ConvertFrom-StrictJsonElement $property.Value
            }
            return $result
        }
        ([System.Text.Json.JsonValueKind]::Array) {
            $items = [System.Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $items.Add((ConvertFrom-StrictJsonElement $item))
            }
            return ,$items.ToArray()
        }
        ([System.Text.Json.JsonValueKind]::String) {
            return $Element.GetString()
        }
        ([System.Text.Json.JsonValueKind]::Number) {
            [Int64] $integer = 0
            if ($Element.TryGetInt64([ref] $integer)) {
                return $integer
            }
            $number = $Element.GetDouble()
            if (-not [double]::IsFinite($number)) {
                throw 'json-number-invalid'
            }
            return $number
        }
        ([System.Text.Json.JsonValueKind]::True) {
            return $true
        }
        ([System.Text.Json.JsonValueKind]::False) {
            return $false
        }
        ([System.Text.Json.JsonValueKind]::Null) {
            return $null
        }
        default {
            throw 'json-value-invalid'
        }
    }
}

function ConvertFrom-StrictJson {
    param([Parameter(Mandatory)] [string] $Text)

    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 32
    $document = [System.Text.Json.JsonDocument]::Parse($Text, $options)
    try {
        return ConvertFrom-StrictJsonElement $document.RootElement
    }
    finally {
        $document.Dispose()
    }
}

function Get-StrictJsonFile {
    param([Parameter(Mandatory)] [string] $Path)

    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            return [ordered]@{ ok = $false; reason = 'source-missing' }
        }

        $info = Get-Item -LiteralPath $Path -Force
        if ($info.Length -le 0 -or $info.Length -gt $maximumSourceBytes) {
            return [ordered]@{ ok = $false; reason = 'source-size-invalid' }
        }

        $bytes = [System.IO.File]::ReadAllBytes($info.FullName)
        $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $value = ConvertFrom-StrictJson $text
        if ($value -isnot [System.Collections.IDictionary]) {
            return [ordered]@{ ok = $false; reason = 'source-json-invalid' }
        }

        return [ordered]@{
            ok = $true
            value = $value
            bytes = $bytes
            text = $text
            sha256 = Get-Sha256Bytes $bytes
        }
    }
    catch {
        return [ordered]@{ ok = $false; reason = 'source-json-invalid' }
    }
}

function Test-TrustedInputs {
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        return 'trusted-repository-invalid'
    }
    if (-not [string]::Equals($WorkflowName, $expectedWorkflowName, [StringComparison]::Ordinal)) {
        return 'trusted-workflow-name-invalid'
    }
    if (-not [string]::Equals($WorkflowPath, $expectedWorkflowPath, [StringComparison]::Ordinal)) {
        return 'trusted-workflow-path-invalid'
    }
    if ($SourceEvent -cnotin @('pull_request', 'workflow_dispatch', 'schedule')) {
        return 'trusted-source-event-invalid'
    }
    if ($HeadRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        return 'trusted-head-repository-invalid'
    }
    if ([string]::IsNullOrWhiteSpace($HeadRef) -or
        $HeadRef.Length -gt 255 -or
        $HeadRef.IndexOfAny([char[]] "`r`n`0") -ge 0) {
        return 'trusted-head-ref-invalid'
    }
    if ($RunId -le 0 -or
        $RunId -gt $maximumJsonSafeInteger -or
        $RunAttempt -le 0 -or
        $RunAttempt -gt $maximumRunAttempt -or
        $PullRequestNumber -lt 0) {
        return 'trusted-run-identity-invalid'
    }
    if ($CommitSha -cnotmatch '^[0-9a-f]{40}$') {
        return 'trusted-commit-invalid'
    }
    if (($SourceEvent -ceq 'pull_request' -and $PullRequestNumber -le 0) -or
        ($SourceEvent -cne 'pull_request' -and $PullRequestNumber -ne 0)) {
        return 'trusted-source-pr-inconsistent'
    }
    if ($SourceEvent -cne 'pull_request' -and
        -not [string]::Equals($HeadRepository, $Repository, [StringComparison]::Ordinal)) {
        return 'trusted-head-repository-inconsistent'
    }

    return $null
}

function Test-SourceProvenance {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Manifest)

    if (-not (Test-RequiredObject $Manifest 'repository') -or
        -not (Test-RequiredString $Manifest['repository'] 'commit') -or
        -not [string]::Equals([string] $Manifest['repository']['commit'], $CommitSha, [StringComparison]::Ordinal)) {
        return $false
    }

    if (-not (Test-RequiredObject $Manifest 'workflow') -or
        -not (Test-RequiredString $Manifest['workflow'] 'runId') -or
        -not (Test-RequiredString $Manifest['workflow'] 'attempt') -or
        -not (Test-RequiredString $Manifest['workflow'] 'name') -or
        -not [string]::Equals([string] $Manifest['workflow']['name'], $WorkflowName, [StringComparison]::Ordinal)) {
        return $false
    }

    $runId = [string] $Manifest['workflow']['runId']
    if ($runId -cnotin @($RunId.ToString(), "$RunId-$RunAttempt") -or
        [string] $Manifest['workflow']['attempt'] -cne $RunAttempt.ToString()) {
        return $false
    }

    return $true
}

function Test-FirstAttempt {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Attempt,
        [Parameter(Mandatory)] [bool] $RequireFailure,
        [Parameter(Mandatory)] [bool] $RequireProductionFacts,
        [Int32] $ExpectedRepetition = 1
    )

    if (-not (Test-RequiredString $Attempt 'runKind') -or
        [string] $Attempt['runKind'] -cne 'clean' -or
        -not $Attempt.Contains('repetition') -or
        -not (Test-JsonIntegerRange $Attempt['repetition'] 1 $maximumAttemptsPerFlow) -or
        [Int32] $Attempt['repetition'] -ne $ExpectedRepetition -or
        -not (Test-RequiredString $Attempt 'outcome') -or
        [string] $Attempt['outcome'] -cnotin @(
            'passed',
            'failed',
            'cancelled',
            'timed-out',
            'lease-lost',
            'infrastructure-error',
            'unknown-completion',
            'orphaned')) {
        return $false
    }

    if ($RequireProductionFacts) {
        if (-not (Test-RequiredString $Attempt 'runId' 256) -or
            -not (Test-RequiredBoolean $Attempt 'verified') -or
            -not (Test-RequiredString $Attempt 'reportPath' 1024) -or
            -not (Test-SafeRelativePath ([string] $Attempt['reportPath']) '/flow-run.json') -or
            -not (Test-RequiredString $Attempt 'reportDigest' 71) -or
            -not (Test-Sha256Identity ([string] $Attempt['reportDigest'])) -or
            -not (Test-RequiredString $Attempt 'resetFingerprint' 256) -or
            -not (Test-RequiredString $Attempt 'seedFingerprint' 256) -or
            -not (Test-RequiredString $Attempt 'backendStateFingerprint' 256) -or
            -not (Test-RequiredString $Attempt 'appBuildFingerprint' 256) -or
            -not (Test-RequiredString $Attempt 'agentInstanceId' 256)) {
            return $false
        }
    }

    if ($RequireFailure) {
        if (-not (Test-RequiredString $Attempt 'failureClass') -or
            -not (Test-RequiredString $Attempt 'failureCode') -or
            -not (Test-RequiredString $Attempt 'failurePhase' 128) -or
            [string] $Attempt['failureClass'] -cnotin $safeFailureValues -or
            [string] $Attempt['failureCode'] -cnotin $safeFailureValues) {
            return $false
        }
    }

    if (-not $RequireProductionFacts -and
        $Attempt.Contains('reportDigest') -and
        $null -ne $Attempt['reportDigest']) {
        if ($Attempt['reportDigest'] -isnot [string] -or
            -not (Test-Sha256Identity ([string] $Attempt['reportDigest']))) {
            return $false
        }
    }

    return $true
}

function Test-FirstAttemptMatches {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $FirstAttempt,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $RecordedAttempt
    )

    try {
        $firstCanonical = ConvertTo-CanonicalJson $FirstAttempt
        $recordedCanonical = ConvertTo-CanonicalJson $RecordedAttempt
        if (-not [string]::Equals($firstCanonical, $recordedCanonical, [StringComparison]::Ordinal)) {
            return $false
        }
        return [string]::Equals(
            (Get-Sha256Text $firstCanonical),
            (Get-Sha256Text $recordedCanonical),
            [StringComparison]::Ordinal)
    }
    catch {
        return $false
    }
}

function Get-FailureCategory {
    param([Parameter(Mandatory)] [string] $FailureClass)

    # 'app-crash' is emitted by MauiFlowFailureClassifier only when the platform proved the app
    # under test exited abnormally. 'device-failure' is not produced by any current C# classifier
    # and remains reserved for a future device-layer signal.
    switch ($FailureClass) {
        'app-crash' { return 'app-crash' }
        'device-failure' { return 'device-failure' }
        'timeout' { return 'timeout' }
        'transport' { return 'harness-failure' }
        'agent-disconnected' { return 'harness-failure' }
        'drive-failed' { return 'harness-failure' }
        'infrastructure' { return 'infrastructure' }
        default { return 'test-failure' }
    }
}

function Test-ArtifactEvidence {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Manifest)

    if (-not (Test-RequiredArray $Manifest 'artifacts' $maximumArtifacts)) {
        return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
    }

    $artifacts = [System.Array] $Manifest['artifacts']
    if ($artifacts.Count -eq 0 -or $artifacts.Count -gt $maximumArtifacts) {
        return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
    }

    $seenPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($artifact in $artifacts) {
        if (-not (Test-JsonObject $artifact) -or
            -not (Test-RequiredString $artifact 'kind' 128) -or
            -not (Test-RequiredString $artifact 'path' 1024) -or
            -not (Test-SafeRelativePath ([string] $artifact['path']) '') -or
            -not (Test-RequiredString $artifact 'sha256') -or
            -not (Test-Sha256 ([string] $artifact['sha256'])) -or
            -not $artifact.Contains('sizeBytes') -or
            -not (Test-JsonIntegerRange $artifact['sizeBytes'] 1 $maximumDeclaredArtifactBytes) -or
            -not (Test-RequiredBoolean $artifact 'redacted') -or
            -not $seenPaths.Add([string] $artifact['path'])) {
            return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
        }

        if ($artifact.Contains('mediaType') -and
            $null -ne $artifact['mediaType'] -and
            (-not ($artifact['mediaType'] -is [string]) -or
                [string]::IsNullOrWhiteSpace([string] $artifact['mediaType']) -or
                ([string] $artifact['mediaType']).Length -gt 128)) {
            return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
        }
        if ($artifact.Contains('runId') -and
            $null -ne $artifact['runId'] -and
            (-not ($artifact['runId'] -is [string]) -or
                [string]::IsNullOrWhiteSpace([string] $artifact['runId']) -or
                ([string] $artifact['runId']).Length -gt 256)) {
            return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
        }
    }

    if (-not (Test-RequiredBoolean $Manifest 'truncated')) {
        return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
    }
    if ([bool] $Manifest['truncated']) {
        return [ordered]@{ ok = $false; reason = 'source-evidence-insufficient' }
    }
    if (-not (Test-RequiredObject $Manifest 'truncation') -or
        -not $Manifest['truncation'].Contains('maxArtifacts') -or
        -not (Test-JsonIntegerRange $Manifest['truncation']['maxArtifacts'] 1 $maximumArtifacts) -or
        [Int64] $Manifest['truncation']['maxArtifacts'] -ne $maximumArtifacts -or
        -not $Manifest['truncation'].Contains('omittedArtifacts') -or
        -not (Test-JsonIntegerRange $Manifest['truncation']['omittedArtifacts'] 0 $maximumArtifacts) -or
        [Int64] $Manifest['truncation']['omittedArtifacts'] -ne 0) {
        return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
    }

    if (-not (Test-RequiredArray $Manifest 'omissions' $maximumArtifacts)) {
        return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
    }
    foreach ($omission in [System.Array] $Manifest['omissions']) {
        if (-not (Test-JsonObject $omission) -or
            -not (Test-RequiredString $omission 'kind' 128) -or
            -not (Test-RequiredString $omission 'reason' 1024)) {
            return [ordered]@{ ok = $false; reason = 'source-artifacts-invalid' }
        }
        if ([string] $omission['kind'] -cin @(
                'artifact-hash',
                'artifact-enumeration',
                'artifact-inherited',
                'artifact-limit',
                'artifact-missing',
                'failure-evidence',
                'flow-run-report',
                'shared-manifest')) {
            return [ordered]@{ ok = $false; reason = 'source-evidence-insufficient' }
        }
    }

    return [ordered]@{
        ok = $true
        artifacts = $artifacts
    }
}

function Test-SelectedEvidence {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Attempt,
        [Parameter(Mandatory)] [System.Array] $Artifacts,
        [Parameter(Mandatory)] [System.Array] $QualificationArtifactRefs
    )

    $runId = [string] $Attempt['runId']
    $reportPath = [string] $Attempt['reportPath']
    $reportDigest = ConvertTo-Sha256Identity ([string] $Attempt['reportDigest'])
    $reportReference = Get-Fingerprint $reportPath
    $reportArtifacts = @($Artifacts | Where-Object {
            [string] $_['kind'] -ceq 'flow-run-report' -and
            [string] $_['path'] -ceq $reportPath -and
            [string] $_['runId'] -ceq $runId -and
            [string] $_['mediaType'] -ceq 'application/json' -and
            $_['redacted'] -eq $true
        })
    if ($reportArtifacts.Count -ne 1) {
        return [ordered]@{ ok = $false; reason = 'source-evidence-insufficient' }
    }

    $reportArtifact = $reportArtifacts[0]
    $reportIdentityRefs = @($QualificationArtifactRefs | Where-Object {
            [string] $_['kind'] -ceq 'report' -and
            [string] $_['digest'] -ceq $reportDigest -and
            [string] $_['reference'] -ceq $reportReference -and
            $_['redacted'] -eq $true
        })
    $reportByteRefs = @($QualificationArtifactRefs | Where-Object {
            [string] $_['kind'] -ceq 'flow-run-report' -and
            [string] $_['digest'] -ceq [string] $reportArtifact['sha256'] -and
            [string] $_['reference'] -ceq $reportReference -and
            $_['redacted'] -eq $true
        })
    if ($reportIdentityRefs.Count -ne 1 -or $reportByteRefs.Count -ne 1) {
        return [ordered]@{ ok = $false; reason = 'qualification-evidence-unbound' }
    }

    $reportDirectory = $reportPath.Substring(0, $reportPath.LastIndexOf('/'))
    $traceArtifacts = @($Artifacts | Where-Object {
            [string] $_['kind'] -ceq 'mauitrace' -and
            [string] $_['runId'] -ceq $runId -and
            [string] $_['mediaType'] -ceq 'application/vnd.maui.evidence+zip' -and
            $_['redacted'] -eq $true -and
            ([string] $_['path']).StartsWith("$reportDirectory/", [StringComparison]::Ordinal) -and
            ([string] $_['path']).EndsWith('.mauitrace', [StringComparison]::Ordinal)
        })
    if ($traceArtifacts.Count -eq 0) {
        return [ordered]@{ ok = $false; reason = 'source-evidence-insufficient' }
    }

    foreach ($trace in $traceArtifacts) {
        $traceReference = Get-Fingerprint ([string] $trace['path'])
        $matches = @($QualificationArtifactRefs | Where-Object {
                [string] $_['kind'] -ceq 'mauitrace' -and
                [string] $_['digest'] -ceq [string] $trace['sha256'] -and
                [string] $_['reference'] -ceq $traceReference -and
                $_['redacted'] -eq $true
            })
        if ($matches.Count -eq 1) {
            return [ordered]@{ ok = $true }
        }
    }

    return [ordered]@{ ok = $false; reason = 'qualification-evidence-unbound' }
}

function Test-SourceManifest {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Manifest)

    if (-not $Manifest.Contains('schema') -or
        -not (Test-JsonInteger $Manifest['schema']) -or
        [Int64] $Manifest['schema'] -ne 1 -or
        -not (Test-RequiredString $Manifest 'kind') -or
        [string] $Manifest['kind'] -cne 'devflow-flow-pilot' -or
        -not (Test-RequiredString $Manifest 'generatedAt' 64)) {
        return [ordered]@{ ok = $false; reason = 'source-manifest-schema-invalid' }
    }
    $generatedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            [string] $Manifest['generatedAt'],
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $generatedAt)) {
        return [ordered]@{ ok = $false; reason = 'source-manifest-schema-invalid' }
    }

    if (-not (Test-RequiredArray $Manifest 'validationErrors' $maximumFlows)) {
        return [ordered]@{ ok = $false; reason = 'source-manifest-validation-invalid' }
    }
    if (([System.Array] $Manifest['validationErrors']).Count -ne 0) {
        return [ordered]@{ ok = $false; reason = 'source-manifest-validation-errors' }
    }

    if (-not (Test-SourceProvenance $Manifest)) {
        return [ordered]@{ ok = $false; reason = 'source-provenance-mismatch' }
    }

    if (-not (Test-RequiredObject $Manifest 'testing') -or
        -not (Test-RequiredString $Manifest['testing'] 'packageVersion' 256) -or
        [string] $Manifest['testing']['packageVersion'] -ceq 'unknown' -or
        -not (Test-RequiredObject $Manifest 'app') -or
        -not (Test-RequiredString $Manifest['app'] 'packageId' 256) -or
        -not (Test-RequiredString $Manifest['app'] 'buildFingerprint' 256) -or
        -not (Test-RequiredString $Manifest['app'] 'packageDigest' 256) -or
        -not (Test-Sha256 ([string] $Manifest['app']['buildFingerprint'])) -or
        -not (Test-Sha256 ([string] $Manifest['app']['packageDigest']))) {
        return [ordered]@{ ok = $false; reason = 'source-production-facts-invalid' }
    }

    if (-not (Test-RequiredObject $Manifest 'platform') -or
        -not (Test-RequiredString $Manifest['platform'] 'name') -or
        [string] $Manifest['platform']['name'] -cnotin @('android', 'ios', 'maccatalyst', 'windows') -or
        -not (Test-RequiredBoolean $Manifest['platform'] 'experimental') -or
        [bool] $Manifest['platform']['experimental'] -ne $false -or
        -not (Test-RequiredBoolean $Manifest['platform'] 'officialCoverage') -or
        [bool] $Manifest['platform']['officialCoverage'] -ne $true -or
        -not (Test-RequiredString $Manifest['platform'] 'deviceId' 256) -or
        -not (Test-Sha256 ([string] $Manifest['platform']['deviceId'])) -or
        -not (Test-RequiredString $Manifest['platform'] 'deviceProfile' 256) -or
        -not (Test-RequiredString $Manifest['platform'] 'agentInstanceId' 256) -or
        -not (Test-RequiredObject $Manifest['platform'] 'deviceEvidence') -or
        -not (Test-RequiredString $Manifest['platform']['deviceEvidence'] 'kind') -or
        [string] $Manifest['platform']['deviceEvidence']['kind'] -cnotin @('physical-device', 'real-device', 'emulator', 'simulator', 'desktop-host') -or
        -not (Test-RequiredBoolean $Manifest['platform']['deviceEvidence'] 'realDevice') -or
        ([bool] $Manifest['platform']['deviceEvidence']['realDevice'] -and
            [string] $Manifest['platform']['deviceEvidence']['kind'] -cnotin @('physical-device', 'real-device'))) {
        return [ordered]@{ ok = $false; reason = 'source-platform-invalid' }
    }

    $runtimeFact = $null
    if ([string] $Manifest['platform']['name'] -ceq 'android') {
        if (-not (Test-RequiredObject $Manifest['platform'] 'androidSdk') -or
            -not (Test-RequiredString $Manifest['platform']['androidSdk'] 'apiLevel' 32)) {
            return [ordered]@{ ok = $false; reason = 'source-platform-invalid' }
        }
        $runtimeFact = [string] $Manifest['platform']['androidSdk']['apiLevel']
    }
    elseif (Test-RequiredString $Manifest['platform'] 'runtime' 256) {
        $runtimeFact = [string] $Manifest['platform']['runtime']
    }
    else {
        return [ordered]@{ ok = $false; reason = 'source-platform-invalid' }
    }

    if (-not (Test-RequiredObject $Manifest 'privacy') -or
        -not (Test-RequiredArray $Manifest['privacy'] 'excludedByDefault' 32)) {
        return [ordered]@{ ok = $false; reason = 'source-privacy-invalid' }
    }
    $privacyExclusions = [System.Array] $Manifest['privacy']['excludedByDefault']
    if ($privacyExclusions.Count -eq 0 -or
        @($privacyExclusions | Where-Object { $_ -isnot [string] }).Count -gt 0) {
        return [ordered]@{ ok = $false; reason = 'source-privacy-invalid' }
    }
    foreach ($requiredExclusion in @('raw-model-context', 'screenshots', 'source')) {
        if ($requiredExclusion -cnotin $privacyExclusions) {
            return [ordered]@{ ok = $false; reason = 'source-privacy-invalid' }
        }
    }

    $artifactResult = Test-ArtifactEvidence $Manifest
    if (-not $artifactResult.ok) {
        return $artifactResult
    }

    if (-not (Test-RequiredArray $Manifest 'flows' $maximumFlows)) {
        return [ordered]@{ ok = $false; reason = 'source-flows-invalid' }
    }

    $flows = [System.Array] $Manifest['flows']
    if ($flows.Count -eq 0 -or $flows.Count -gt $maximumFlows) {
        return [ordered]@{ ok = $false; reason = 'source-flows-invalid' }
    }

    $seenFlowDigests = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $validatedFlows = [System.Collections.Generic.List[object]]::new()
    foreach ($flow in $flows) {
        if (-not (Test-JsonObject $flow) -or
            -not (Test-RequiredString $flow 'digest') -or
            -not (Test-Sha256 ([string] $flow['digest'])) -or
            -not $seenFlowDigests.Add([string] $flow['digest']) -or
            -not (Test-RequiredString $flow 'tier') -or
            [string] $flow['tier'] -cne 'tier-1' -or
            -not (Test-RequiredObject $flow 'firstAttempt')) {
            return [ordered]@{ ok = $false; reason = 'source-flow-invalid' }
        }

        $firstAttempt = $flow['firstAttempt']
        $outcome = if (Test-RequiredString $firstAttempt 'outcome') { [string] $firstAttempt['outcome'] } else { $null }
        $requiresFailure = $outcome -ne 'passed'
        $requiresProductionFacts = $outcome -notin @('infrastructure-error', 'unknown-completion', 'orphaned')
        if (-not (Test-FirstAttempt `
                -Attempt $firstAttempt `
                -RequireFailure $requiresFailure `
                -RequireProductionFacts $requiresProductionFacts)) {
            return [ordered]@{ ok = $false; reason = 'source-first-attempt-invalid' }
        }
        if ($requiresProductionFacts -and
                [string] $firstAttempt['appBuildFingerprint'] -cne [string] $Manifest['app']['buildFingerprint']) {
            return [ordered]@{ ok = $false; reason = 'source-production-facts-invalid' }
        }

        if (-not (Test-RequiredArray $flow 'cleanAttempts' $maximumAttemptsPerFlow)) {
            return [ordered]@{ ok = $false; reason = 'source-first-attempt-not-immutable' }
        }
        $cleanAttempts = [System.Array] $flow['cleanAttempts']
        if ($cleanAttempts.Count -eq 0 -or
            -not (Test-JsonObject $cleanAttempts[0]) -or
            -not (Test-FirstAttemptMatches -FirstAttempt $firstAttempt -RecordedAttempt $cleanAttempts[0])) {
            return [ordered]@{ ok = $false; reason = 'source-first-attempt-not-immutable' }
        }
        for ($index = 0; $index -lt $cleanAttempts.Count; $index++) {
            $attempt = $cleanAttempts[$index]
            if (-not (Test-JsonObject $attempt) -or
                -not (Test-FirstAttempt `
                    -Attempt $attempt `
                    -RequireFailure ([string] $attempt['outcome'] -ne 'passed') `
                    -RequireProductionFacts ([string] $attempt['outcome'] -notin @('infrastructure-error', 'unknown-completion', 'orphaned')) `
                    -ExpectedRepetition ($index + 1))) {
                return [ordered]@{ ok = $false; reason = 'source-clean-attempts-invalid' }
            }
            # Every attempt must exercise the same app build; that is what makes three attempts
            # comparable evidence. The agent instance deliberately differs between them: a clean
            # attempt relaunches the app, which is precisely what makes it clean, so requiring every
            # attempt to report the manifest's single instance id asserted something a multi-attempt
            # corpus can never satisfy. Presence and shape are still enforced per attempt by
            # Test-FirstAttempt, and the manifest-level id is still pinned to the first attempt above.
            if ([string] $attempt['outcome'] -notin @('infrastructure-error', 'unknown-completion', 'orphaned') -and
                    [string] $attempt['appBuildFingerprint'] -cne [string] $Manifest['app']['buildFingerprint']) {
                return [ordered]@{ ok = $false; reason = 'source-production-facts-invalid' }
            }
        }

        if (-not (Test-RequiredArray $flow 'diagnosticReruns' $maximumAttemptsPerFlow)) {
            return [ordered]@{ ok = $false; reason = 'source-diagnostic-reruns-invalid' }
        }

        $validatedFlows.Add([ordered]@{
                digest = [string] $flow['digest']
                tier = [string] $flow['tier']
                outcome = $outcome
                failureClass = if ($requiresFailure) { [string] $firstAttempt['failureClass'] } else { $null }
                attempt = $firstAttempt
            })
    }

    return [ordered]@{
        ok = $true
        platform = [string] $Manifest['platform']['name']
        deviceKind = [string] $Manifest['platform']['deviceEvidence']['kind']
        realDevice = [bool] $Manifest['platform']['deviceEvidence']['realDevice']
        experimental = [bool] $Manifest['platform']['experimental']
        officialCoverage = [bool] $Manifest['platform']['officialCoverage']
        testingPackageVersion = [string] $Manifest['testing']['packageVersion']
        packageId = [string] $Manifest['app']['packageId']
        packageDigest = [string] $Manifest['app']['packageDigest']
        buildFingerprint = [string] $Manifest['app']['buildFingerprint']
        deviceFingerprint = [string] $Manifest['platform']['deviceId']
        runtimeFact = $runtimeFact
        artifacts = [System.Array] $artifactResult.artifacts
        flows = @($validatedFlows)
    }
}

function Test-QualificationExclusions {
    param($Value)

    if (-not (Test-JsonArray $Value) -or ([System.Array] $Value).Count -gt $maximumQualificationItems) {
        return $false
    }
    foreach ($item in [System.Array] $Value) {
        if (-not (Test-JsonObject $item) -or
            -not (Test-RequiredString $item 'kind' 128) -or
            -not $item.Contains('count') -or
            -not (Test-JsonIntegerRange $item['count'] 0 $maximumMetricCount) -or
            -not (Test-RequiredString $item 'reason' 256)) {
            return $false
        }
    }
    return $true
}

function Get-Wilson95Interval {
    param(
        [Parameter(Mandatory)] [Int64] $Successes,
        [Parameter(Mandatory)] [Int64] $Trials
    )

    $z = 1.959963984540054
    $p = [double] $Successes / [double] $Trials
    $z2 = $z * $z
    $denominator = 1 + ($z2 / $Trials)
    $center = ($p + ($z2 / (2 * $Trials))) / $denominator
    $margin = $z * [Math]::Sqrt(
        ($p * (1 - $p) / $Trials) + ($z2 / (4 * $Trials * $Trials))) / $denominator
    return [ordered]@{
        lower = [Math]::Clamp($center - $margin, [double] 0, [double] 1)
        upper = [Math]::Clamp($center + $margin, [double] 0, [double] 1)
    }
}

function Test-RateMetric {
    param(
        $Metric,
        [bool] $RequireMeasured
    )

    if (-not (Test-JsonObject $Metric) -or
        -not (Test-RequiredString $Metric 'state' 32) -or
        [string] $Metric['state'] -cnotin @('measured', 'missing') -or
        -not $Metric.Contains('numerator') -or
        -not (Test-JsonIntegerRange $Metric['numerator'] 0 $maximumMetricCount) -or
        -not $Metric.Contains('denominator') -or
        -not (Test-JsonIntegerRange $Metric['denominator'] 0 $maximumMetricCount) -or
        [Int64] $Metric['numerator'] -gt [Int64] $Metric['denominator'] -or
        -not (Test-RequiredArray $Metric 'sampleSources' 16) -or
        -not (Test-QualificationExclusions $Metric['exclusions'])) {
        return $false
    }
    foreach ($source in [System.Array] $Metric['sampleSources']) {
        if ($source -isnot [string] -or
            [string] $source -cnotin @('curated', 'curated-derived', 'generated', 'device-backed')) {
            return $false
        }
    }
    if ($Metric.Contains('independentDeviceRuns') -and
        $null -ne $Metric['independentDeviceRuns'] -and
        $Metric['independentDeviceRuns'] -isnot [bool]) {
        return $false
    }

    if ([string] $Metric['state'] -ceq 'measured') {
        if ([Int64] $Metric['denominator'] -le 0 -or
            -not $Metric.Contains('value') -or
            -not (Test-JsonNumberRange $Metric['value'] 0 1)) {
            return $false
        }
        $expected = [double] $Metric['numerator'] / [double] $Metric['denominator']
        if ([Math]::Abs(([double] $Metric['value']) - $expected) -gt 0.000000001) {
            return $false
        }
        if (-not (Test-RequiredObject $Metric 'confidenceInterval')) {
            return $false
        }
        $interval = $Metric['confidenceInterval']
        if (-not (Test-RequiredString $interval 'method' 32) -or
            [string] $interval['method'] -cne 'wilson-95' -or
            -not $interval.Contains('confidenceLevel') -or
            -not (Test-JsonNumberRange $interval['confidenceLevel'] 0.5 0.999999) -or
            [Math]::Abs(([double] $interval['confidenceLevel']) - 0.95) -gt 0.000000001 -or
            -not $interval.Contains('lower') -or
            -not (Test-JsonNumberRange $interval['lower'] 0 1) -or
            -not $interval.Contains('upper') -or
            -not (Test-JsonNumberRange $interval['upper'] 0 1) -or
            [double] $interval['lower'] -gt [double] $interval['upper']) {
            return $false
        }
        $expectedInterval = Get-Wilson95Interval `
            -Successes ([Int64] $Metric['numerator']) `
            -Trials ([Int64] $Metric['denominator'])
        if ([Math]::Abs(([double] $interval['lower']) - [double] $expectedInterval.lower) -gt 0.000000001 -or
            [Math]::Abs(([double] $interval['upper']) - [double] $expectedInterval.upper) -gt 0.000000001) {
            return $false
        }
    }
    elseif ($RequireMeasured) {
        return $false
    }

    return $true
}

function Test-DurationMetric {
    param(
        $Metric,
        [bool] $RequireMeasured
    )

    if (-not (Test-JsonObject $Metric) -or
        -not (Test-RequiredString $Metric 'state' 32) -or
        [string] $Metric['state'] -cnotin @('measured', 'missing') -or
        -not (Test-RequiredString $Metric 'operation' 128) -or
        -not $Metric.Contains('sampleCount') -or
        -not (Test-JsonIntegerRange $Metric['sampleCount'] 0 $maximumMetricCount) -or
        ($Metric.Contains('missingReason') -and
            $null -ne $Metric['missingReason'] -and
            ($Metric['missingReason'] -isnot [string] -or
                ([string] $Metric['missingReason']).Length -gt 256))) {
        return $false
    }
    if ([string] $Metric['state'] -ceq 'measured') {
        if ([Int64] $Metric['sampleCount'] -le 0) {
            return $false
        }
        foreach ($field in @('p50Ms', 'p95Ms', 'maxMs')) {
            if (-not $Metric.Contains($field) -or
                -not (Test-JsonNumberRange $Metric[$field] 0 86400000)) {
                return $false
            }
        }
        if ([double] $Metric['p50Ms'] -gt [double] $Metric['p95Ms'] -or
            [double] $Metric['p95Ms'] -gt [double] $Metric['maxMs']) {
            return $false
        }
    }
    elseif ($RequireMeasured) {
        return $false
    }
    elseif (-not $Metric.Contains('missingReason') -or
        $Metric['missingReason'] -isnot [string] -or
        [string]::IsNullOrWhiteSpace([string] $Metric['missingReason'])) {
        return $false
    }
    return $true
}

function Test-QualificationMetrics {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Metrics,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Thresholds,
        [Parameter(Mandatory)] [System.Array] $ManifestFlows,
        [Parameter(Mandatory)] [bool] $RequirePass
    )

    foreach ($name in @(
            'recordingValidity',
            'selectorStability',
            'repairPrecision',
            'repairRecall',
            'falseHeals',
            'abstention')) {
        if (-not (Test-RequiredObject $Metrics $name) -or
            -not (Test-RateMetric $Metrics[$name] $RequirePass)) {
            return $false
        }
    }

    if (-not (Test-RequiredObject $Metrics 'humanDecisionOutcomes')) {
        return $false
    }
    foreach ($field in @('approved', 'rejected', 'expired', 'abstained', 'unresolved')) {
        if (-not $Metrics['humanDecisionOutcomes'].Contains($field) -or
            -not (Test-JsonIntegerRange $Metrics['humanDecisionOutcomes'][$field] 0 $maximumMetricCount)) {
            return $false
        }
    }

    if (-not (Test-RequiredObject $Metrics 'calibration')) {
        return $false
    }
    $calibration = $Metrics['calibration']
    if (-not (Test-RequiredString $calibration 'state' 32) -or
        -not (Test-RequiredBoolean $calibration 'probabilityLikeConfidenceDisplayed') -or
        -not $calibration.Contains('sampleCount') -or
        -not (Test-JsonIntegerRange $calibration['sampleCount'] 0 $maximumMetricCount) -or
        -not (Test-RequiredArray $calibration 'buckets' 100)) {
        return $false
    }
    if ([bool] $calibration['probabilityLikeConfidenceDisplayed'] -and
        (-not $calibration.Contains('ece') -or
            -not (Test-JsonNumberRange $calibration['ece'] 0 1))) {
        return $false
    }
    if ($calibration.Contains('brier') -and
        $null -ne $calibration['brier'] -and
        -not (Test-JsonNumberRange $calibration['brier'] 0 1)) {
        return $false
    }
    foreach ($bucket in [System.Array] $calibration['buckets']) {
        if (-not (Test-JsonObject $bucket) -or
            -not $bucket.Contains('lowerInclusive') -or
            -not (Test-JsonNumberRange $bucket['lowerInclusive'] 0 1) -or
            -not $bucket.Contains('upperInclusive') -or
            -not (Test-JsonNumberRange $bucket['upperInclusive'] 0 1) -or
            [double] $bucket['lowerInclusive'] -gt [double] $bucket['upperInclusive'] -or
            -not $bucket.Contains('sampleCount') -or
            -not (Test-JsonIntegerRange $bucket['sampleCount'] 0 $maximumMetricCount)) {
            return $false
        }
        foreach ($field in @('meanConfidence', 'empiricalRate')) {
            if ($bucket.Contains($field) -and
                $null -ne $bucket[$field] -and
                -not (Test-JsonNumberRange $bucket[$field] 0 1)) {
                return $false
            }
        }
    }

    if (-not (Test-RequiredObject $Metrics 'timeToDiagnosis') -or
        -not (Test-DurationMetric $Metrics['timeToDiagnosis'] $RequirePass) -or
        -not (Test-RequiredObject $Metrics 'traceReportSize')) {
        return $false
    }
    $trace = $Metrics['traceReportSize']
    foreach ($field in @(
            'expectedReportCount',
            'reportPresent',
            'reportSchemaValid',
            'reportComplete',
            'traceSampleCount')) {
        if (-not $trace.Contains($field) -or
            -not (Test-JsonIntegerRange $trace[$field] 0 $maximumMetricCount)) {
            return $false
        }
    }
    if (-not (Test-RequiredString $trace 'state' 32)) {
        return $false
    }
    foreach ($field in @(
            'reportCompleteness',
            'reportP50Bytes',
            'reportP95Bytes',
            'traceP50Bytes',
            'traceP95Bytes')) {
        if ($trace.Contains($field) -and
            $null -ne $trace[$field] -and
            -not (Test-JsonNumberRange $trace[$field] 0 $maximumDeclaredArtifactBytes)) {
            return $false
        }
    }
    if ($null -ne $trace['reportCompleteness'] -and
        -not (Test-JsonNumberRange $trace['reportCompleteness'] 0 1)) {
        return $false
    }
    if ($trace.Contains('missingReason') -and
        $null -ne $trace['missingReason'] -and
        $trace['missingReason'] -isnot [string]) {
        return $false
    }

    if (-not (Test-RequiredObject $Metrics 'runtimeOverhead')) {
        return $false
    }
    $runtime = $Metrics['runtimeOverhead']
    if (-not (Test-RequiredArray $runtime 'hostOperations' 32) -or
        -not (Test-RequiredObject $runtime 'deviceOverhead')) {
        return $false
    }
    foreach ($operation in [System.Array] $runtime['hostOperations']) {
        if (-not (Test-DurationMetric $operation $RequirePass)) {
            return $false
        }
    }
    if (-not (Test-DurationMetric $runtime['deviceOverhead'] $RequirePass)) {
        return $false
    }

    if (-not (Test-RequiredObject $Metrics 'flakeFirstAttemptStability')) {
        return $false
    }
    $firstAttempts = $Metrics['flakeFirstAttemptStability']
    if (-not (Test-RequiredString $firstAttempts 'state' 32) -or
        -not (Test-RequiredObject $firstAttempts 'stability') -or
        -not (Test-RateMetric $firstAttempts['stability'] $RequirePass) -or
        -not (Test-RequiredArray $firstAttempts 'flows' $maximumFlows) -or
        -not $firstAttempts.Contains('diagnosticRerunsIgnored') -or
        -not (Test-JsonIntegerRange $firstAttempts['diagnosticRerunsIgnored'] 0 $maximumMetricCount) -or
        -not $firstAttempts.Contains('infrastructureExclusions') -or
        -not (Test-QualificationExclusions $firstAttempts['infrastructureExclusions'])) {
        return $false
    }
    $flowMetrics = [System.Array] $firstAttempts['flows']
    foreach ($flowMetric in $flowMetrics) {
        if (-not (Test-JsonObject $flowMetric) -or
            -not (Test-RequiredString $flowMetric 'flowId' 71) -or
            -not (Test-Sha256 ([string] $flowMetric['flowId'])) -or
            -not $flowMetric.Contains('cleanFirstAttempts') -or
            -not (Test-JsonIntegerRange $flowMetric['cleanFirstAttempts'] 0 $maximumMetricCount) -or
            -not $flowMetric.Contains('passedFirstAttempts') -or
            -not (Test-JsonIntegerRange $flowMetric['passedFirstAttempts'] 0 $maximumMetricCount) -or
            [Int64] $flowMetric['passedFirstAttempts'] -gt [Int64] $flowMetric['cleanFirstAttempts'] -or
            -not (Test-RequiredBoolean $flowMetric 'realDeviceEvidence')) {
            return $false
        }
        if ([Int64] $flowMetric['cleanFirstAttempts'] -gt 0 -and
            (-not $flowMetric.Contains('stability') -or
                -not (Test-JsonNumberRange $flowMetric['stability'] 0 1))) {
            return $false
        }
        if ([Int64] $flowMetric['cleanFirstAttempts'] -gt 0) {
            $expectedStability =
                [double] $flowMetric['passedFirstAttempts'] /
                [double] $flowMetric['cleanFirstAttempts']
            if ([Math]::Abs(([double] $flowMetric['stability']) - $expectedStability) -gt 0.000000001) {
                return $false
            }
        }
    }

    if (-not (Test-RequiredObject $Metrics 'privacySecurityEscapes')) {
        return $false
    }
    $privacy = $Metrics['privacySecurityEscapes']
    if (-not (Test-RequiredString $privacy 'state' 32) -or
        -not $privacy.Contains('testCount') -or
        -not (Test-JsonIntegerRange $privacy['testCount'] 0 $maximumMetricCount) -or
        -not $privacy.Contains('escapeCount') -or
        -not (Test-JsonIntegerRange $privacy['escapeCount'] 0 $maximumMetricCount) -or
        -not (Test-RequiredArray $privacy 'caseIds' $maximumQualificationItems) -or
        -not $privacy.Contains('canaryScanPassed') -or
        ($null -ne $privacy['canaryScanPassed'] -and $privacy['canaryScanPassed'] -isnot [bool])) {
        return $false
    }
    foreach ($caseId in [System.Array] $privacy['caseIds']) {
        if ($caseId -isnot [string] -or -not (Test-Sha256 ([string] $caseId))) {
            return $false
        }
    }
    if ($privacy.Contains('missingReason') -and
        $null -ne $privacy['missingReason'] -and
        $privacy['missingReason'] -isnot [string]) {
        return $false
    }

    if (-not $RequirePass) {
        return $true
    }

    $hostRegressions = @([System.Array] $runtime['hostOperations'] | Where-Object {
            [double] $_['p95Ms'] -gt [double] $Thresholds['hostOperationP95BudgetMs']
        })
    if ([Int64] $Metrics['humanDecisionOutcomes']['unresolved'] -ne 0 -or
        [string] $Metrics['recordingValidity']['state'] -cne 'measured' -or
        [Int64] $Metrics['recordingValidity']['numerator'] -ne [Int64] $Metrics['recordingValidity']['denominator'] -or
        $Metrics['recordingValidity']['independentDeviceRuns'] -ne $true -or
        [Int64] $Metrics['repairPrecision']['denominator'] -lt [Int64] $Thresholds['minimumRepairEvaluations'] -or
        [double] $Metrics['repairPrecision']['confidenceInterval']['lower'] -lt [double] $Thresholds['minimumRepairPrecision'] -or
        [Int64] $Metrics['falseHeals']['denominator'] -lt [Int64] $Thresholds['minimumNoRepairEvaluations'] -or
        [Int64] $Metrics['falseHeals']['numerator'] -gt [Int64] $Thresholds['maximumFalseHeals'] -or
        [Int64] $Metrics['selectorStability']['denominator'] -lt [Int64] $Thresholds['minimumSelectorObservations'] -or
        [double] $Metrics['selectorStability']['value'] -lt [double] $Thresholds['minimumSelectorStability'] -or
        $Metrics['selectorStability']['independentDeviceRuns'] -ne $true -or
        ([bool] $calibration['probabilityLikeConfidenceDisplayed'] -and
            [double] $calibration['ece'] -gt [double] $Thresholds['maximumCalibrationEce']) -or
        [Int64] $privacy['testCount'] -le 0 -or
        [Int64] $privacy['escapeCount'] -ne 0 -or
        $privacy['canaryScanPassed'] -ne $true -or
        ([System.Array] $runtime['hostOperations']).Count -eq 0 -or
        $hostRegressions.Count -gt 0) {
        return $false
    }
    if ([string] $runtime['deviceOverhead']['state'] -cne 'measured' -or
        [Int64] $trace['expectedReportCount'] -le 0 -or
        [Int64] $trace['reportPresent'] -ne [Int64] $trace['expectedReportCount'] -or
        [Int64] $trace['reportSchemaValid'] -ne [Int64] $trace['expectedReportCount'] -or
        [Int64] $trace['reportComplete'] -ne [Int64] $trace['expectedReportCount']) {
        return $false
    }

    foreach ($manifestFlow in $ManifestFlows) {
        $matches = @($flowMetrics | Where-Object {
                [string] $_['flowId'] -ceq [string] $manifestFlow.digest
            })
        if ($matches.Count -ne 1) {
            return $false
        }
        $flowMetric = $matches[0]
        if ($flowMetric['realDeviceEvidence'] -ne $true -or
            [Int64] $flowMetric['cleanFirstAttempts'] -lt [Int64] $Thresholds['minimumCleanFirstAttemptsPerTier1Flow'] -or
            [double] $flowMetric['stability'] -lt [double] $Thresholds['minimumFirstAttemptStability']) {
            return $false
        }
    }

    return $true
}

function Test-Qualification {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Qualification,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $ManifestResult,
        [Parameter(Mandatory)] [string] $ManifestSha256
    )

    if (-not $Qualification.Contains('schema') -or
        -not (Test-JsonIntegerRange $Qualification['schema'] 1 1) -or
        -not (Test-RequiredString $Qualification 'kind') -or
        [string] $Qualification['kind'] -cne 'maui-preview-qualification' -or
        -not (Test-RequiredString $Qualification 'contractVersion') -or
        [string] $Qualification['contractVersion'] -cne 'preview-qualification-v1' -or
        -not (Test-RequiredString $Qualification 'generatedAt' 64) -or
        -not (Test-RequiredString $Qualification 'platform' 32) -or
        -not [string]::Equals([string] $Qualification['platform'], [string] $ManifestResult.platform, [StringComparison]::Ordinal) -or
        -not (Test-RequiredString $Qualification 'status' 32) -or
        [string] $Qualification['status'] -cnotin @('pass', 'fail', 'not-qualified') -or
        -not (Test-RequiredObject $Qualification 'fingerprints') -or
        -not (Test-RequiredArray $Qualification 'profiles' 64) -or
        -not (Test-RequiredObject $Qualification 'featureFlags') -or
        -not (Test-RequiredObject $Qualification 'review') -or
        -not (Test-RequiredObject $Qualification 'corpus') -or
        -not (Test-RequiredObject $Qualification 'metrics') -or
        -not (Test-RequiredObject $Qualification 'thresholds') -or
        -not (Test-RequiredArray $Qualification 'gates' 64) -or
        -not (Test-RequiredArray $Qualification 'reasons' $maximumQualificationItems) -or
        -not (Test-RequiredArray $Qualification 'artifactRefs' $maximumQualificationItems) -or
        -not (Test-RequiredArray $Qualification 'exclusions' $maximumQualificationItems)) {
        return [ordered]@{ ok = $false; reason = 'qualification-schema-invalid' }
    }

    $generatedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            [string] $Qualification['generatedAt'],
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $generatedAt)) {
        return [ordered]@{ ok = $false; reason = 'qualification-schema-invalid' }
    }

    $requirePass = [string] $Qualification['status'] -ceq 'pass'
    $fingerprints = $Qualification['fingerprints']
    $fingerprintFields = @(
        'corpusVersion',
        'corpusFingerprint',
        'repositoryCommit',
        'testingPackageVersion',
        'packageId',
        'packageFingerprint',
        'toolVersion',
        'toolFingerprint',
        'policyVersion',
        'policyFingerprint'
    )
    foreach ($field in $fingerprintFields) {
        $value = [string] $fingerprints[$field]
        $validForStatus = if ($requirePass) {
            Test-Sha256 $value
        }
        else {
            $value -ceq 'unknown' -or (Test-Sha256 $value)
        }
        if (-not (Test-RequiredString $fingerprints $field 71) -or -not $validForStatus) {
            return [ordered]@{ ok = $false; reason = 'qualification-fingerprints-invalid' }
        }
    }
    if ($requirePass -and (
            [string] $fingerprints['repositoryCommit'] -cne (Get-Fingerprint $CommitSha) -or
            [string] $fingerprints['testingPackageVersion'] -cne (Get-Fingerprint ([string] $ManifestResult.testingPackageVersion)) -or
            [string] $fingerprints['packageId'] -cne (Get-Fingerprint ([string] $ManifestResult.packageId)) -or
            [string] $fingerprints['packageFingerprint'] -cne [string] $ManifestResult.packageDigest)) {
        return [ordered]@{ ok = $false; reason = 'qualification-fingerprints-mismatch' }
    }

    $profiles = [System.Array] $Qualification['profiles']
    if ($profiles.Count -eq 0) {
        return [ordered]@{ ok = $false; reason = 'qualification-profiles-invalid' }
    }
    $matchingProfiles = [System.Collections.Generic.List[object]]::new()
    foreach ($profile in $profiles) {
        if (-not (Test-JsonObject $profile) -or
            -not (Test-RequiredString $profile 'platform' 32) -or
            -not (Test-RequiredString $profile 'scope' 71) -or
            -not (Test-RequiredString $profile 'deviceEvidenceKind' 32) -or
            -not (Test-RequiredBoolean $profile 'realDevice') -or
            -not (Test-RequiredString $profile 'deviceFingerprint' 71) -or
            -not (Test-RequiredString $profile 'runtimeFingerprint' 71) -or
            -not (Test-RequiredString $profile 'buildFingerprint' 71) -or
            -not (Test-RequiredString $profile 'packageFingerprint' 71) -or
            -not (Test-RequiredString $profile 'seedFingerprint' 71) -or
            -not (Test-RequiredString $profile 'backendStateFingerprint' 71) -or
            -not (Test-RequiredString $profile 'firstAttemptMode' 71)) {
            return [ordered]@{ ok = $false; reason = 'qualification-profiles-invalid' }
        }

        if ([string] $profile['platform'] -ceq [string] $ManifestResult.platform -and
            [string] $profile['deviceEvidenceKind'] -ceq [string] $ManifestResult.deviceKind -and
            $profile['realDevice'] -eq $ManifestResult.realDevice -and
            [string] $profile['deviceFingerprint'] -ceq [string] $ManifestResult.deviceFingerprint -and
            [string] $profile['runtimeFingerprint'] -ceq (Get-Fingerprint ([string] $ManifestResult.runtimeFact)) -and
            [string] $profile['buildFingerprint'] -ceq [string] $ManifestResult.buildFingerprint -and
            [string] $profile['packageFingerprint'] -ceq [string] $ManifestResult.packageDigest -and
            [string] $profile['firstAttemptMode'] -ceq (Get-Fingerprint 'manifest-first-attempt')) {
            if ((Test-Sha256 ([string] $profile['scope'])) -and
                (Test-Sha256 ([string] $profile['seedFingerprint'])) -and
                (Test-Sha256 ([string] $profile['backendStateFingerprint']))) {
                $matchingProfiles.Add($profile)
            }
        }
    }
    if ($requirePass -and $matchingProfiles.Count -ne 1) {
        return [ordered]@{ ok = $false; reason = 'qualification-profiles-mismatch' }
    }

    $flags = $Qualification['featureFlags']
    foreach ($field in @(
            'workbenchEnabled',
            'agentAuthoringEnabled',
            'repairProposalsEnabled',
            'sourceProposalsEnabled',
            'traceImportExportEnabled',
            'autoApplyRepair',
            'autoApplySource',
            'modelProviderEnabled',
            'telemetryEgressEnabled',
            'requiredPullRequestGate')) {
        if (-not (Test-RequiredBoolean $flags $field)) {
            return [ordered]@{ ok = $false; reason = 'qualification-feature-flags-invalid' }
        }
    }
    if (-not $flags.Contains('schema') -or
        -not (Test-JsonIntegerRange $flags['schema'] 1 1) -or
        -not (Test-RequiredString $flags 'policyVersion' 64) -or
        [string] $flags['policyVersion'] -cne 'preview-flags-v1' -or
        -not (Test-RequiredArray $flags 'killSwitches' 32) -or
        $flags['autoApplyRepair'] -ne $false -or
        $flags['autoApplySource'] -ne $false -or
        $flags['modelProviderEnabled'] -ne $false -or
        $flags['telemetryEgressEnabled'] -ne $false -or
        $flags['requiredPullRequestGate'] -ne $false) {
        return [ordered]@{ ok = $false; reason = 'qualification-feature-flags-invalid' }
    }
    foreach ($killSwitch in [System.Array] $flags['killSwitches']) {
        if ($killSwitch -isnot [string] -or
            [string] $killSwitch -cnotin @(
                'workbench',
                'agent-authoring',
                'repair-proposals',
                'source-proposals',
                'trace-import-export')) {
            return [ordered]@{ ok = $false; reason = 'qualification-feature-flags-invalid' }
        }
    }

    $review = $Qualification['review']
    if (-not (Test-RequiredString $review 'planReviewStatus' 32) -or
        -not (Test-RequiredString $review 'rubberDuckReviewStatus' 32) -or
        -not (Test-RequiredString $review 'independentReviewStatus' 32) -or
        -not (Test-RequiredArray $review 'reviewerFingerprints' 64) -or
        -not (Test-RequiredArray $review 'artifactRefs' 64)) {
        return [ordered]@{ ok = $false; reason = 'qualification-review-invalid' }
    }
    if ($requirePass) {
        if (-not (Test-RequiredString $review 'planId' 71) -or
            -not (Test-Sha256 ([string] $review['planId'])) -or
            -not $review.Contains('planRevision') -or
            -not (Test-JsonIntegerRange $review['planRevision'] 1 $maximumMetricCount) -or
            -not (Test-RequiredString $review 'reviewedAt' 64) -or
            [string] $review['planReviewStatus'] -cnotin @('approved', 'passed') -or
            [string] $review['rubberDuckReviewStatus'] -cnotin @('approved', 'passed') -or
            [string] $review['independentReviewStatus'] -cnotin @('approved', 'passed') -or
            ([System.Array] $review['reviewerFingerprints']).Count -eq 0 -or
            ([System.Array] $review['artifactRefs']).Count -eq 0) {
            return [ordered]@{ ok = $false; reason = 'qualification-review-invalid' }
        }
        $reviewedAt = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse(
                [string] $review['reviewedAt'],
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind,
                [ref] $reviewedAt)) {
            return [ordered]@{ ok = $false; reason = 'qualification-review-invalid' }
        }
        foreach ($digest in @(
                [System.Array] $review['reviewerFingerprints'] +
                [System.Array] $review['artifactRefs'])) {
            if ($digest -isnot [string] -or -not (Test-Sha256 ([string] $digest))) {
                return [ordered]@{ ok = $false; reason = 'qualification-review-invalid' }
            }
        }
    }

    $corpus = $Qualification['corpus']
    foreach ($field in @('staticOnly', 'manifestValid', 'caseSchemaValid')) {
        if (-not (Test-RequiredBoolean $corpus $field)) {
            return [ordered]@{ ok = $false; reason = 'qualification-corpus-invalid' }
        }
    }
    foreach ($field in @('curatedCases', 'generatedCases', 'deviceBackedCases')) {
        if (-not $corpus.Contains($field) -or
            -not (Test-JsonIntegerRange $corpus[$field] 0 $maximumMetricCount)) {
            return [ordered]@{ ok = $false; reason = 'qualification-corpus-invalid' }
        }
    }
    if (-not $corpus.Contains('mutationSeed') -or
        -not (Test-JsonIntegerRange $corpus['mutationSeed'] 0 $maximumJsonSafeInteger) -or
        -not (Test-RequiredString $corpus 'version' 71) -or
        -not (Test-RequiredString $corpus 'manifestFingerprint' 71) -or
        -not (Test-RequiredString $corpus 'generatorVersion' 71) -or
        -not (Test-RequiredArray $corpus 'errors' $maximumQualificationItems)) {
        return [ordered]@{ ok = $false; reason = 'qualification-corpus-invalid' }
    }
    foreach ($errorCode in [System.Array] $corpus['errors']) {
        if ($errorCode -isnot [string] -or
            [string]::IsNullOrWhiteSpace([string] $errorCode) -or
            ([string] $errorCode).Length -gt 128) {
            return [ordered]@{ ok = $false; reason = 'qualification-corpus-invalid' }
        }
    }
    if ($requirePass -and (
            $corpus['manifestValid'] -ne $true -or
            $corpus['caseSchemaValid'] -ne $true -or
            [string] $corpus['version'] -cne [string] $fingerprints['corpusVersion'] -or
            [string] $corpus['manifestFingerprint'] -cne [string] $fingerprints['corpusFingerprint'] -or
            -not (Test-Sha256 ([string] $corpus['generatorVersion'])) -or
            ([System.Array] $corpus['errors']).Count -ne 0)) {
        return [ordered]@{ ok = $false; reason = 'qualification-corpus-invalid' }
    }

    $thresholds = $Qualification['thresholds']
    foreach ($field in @(
            'minimumRepairEvaluations',
            'minimumNoRepairEvaluations',
            'maximumFalseHeals',
            'minimumSelectorObservations',
            'minimumCleanFirstAttemptsPerTier1Flow')) {
        $minimum = if ($field -eq 'maximumFalseHeals') { 0 } else { 1 }
        if (-not $thresholds.Contains($field) -or
            -not (Test-JsonIntegerRange $thresholds[$field] $minimum $maximumMetricCount)) {
            return [ordered]@{ ok = $false; reason = 'qualification-thresholds-invalid' }
        }
    }
    foreach ($field in @(
            'confidenceLevel',
            'minimumRepairPrecision',
            'minimumSelectorStability',
            'maximumCalibrationEce',
            'minimumFirstAttemptStability')) {
        if (-not $thresholds.Contains($field) -or
            -not (Test-JsonNumberRange $thresholds[$field] 0 1)) {
            return [ordered]@{ ok = $false; reason = 'qualification-thresholds-invalid' }
        }
    }
    if (-not (Test-RequiredString $thresholds 'policyVersion' 71) -or
        -not (Test-Sha256 ([string] $thresholds['policyVersion'])) -or
        [string] $thresholds['policyVersion'] -cne [string] $fingerprints['policyVersion'] -or
        -not $thresholds.Contains('hostOperationP95BudgetMs') -or
        -not (Test-JsonNumberRange $thresholds['hostOperationP95BudgetMs'] 1 86400000) -or
        -not (Test-RequiredBoolean $thresholds 'requireRealAndroidDeviceEvidence') -or
        -not (Test-RequiredBoolean $thresholds 'requireRecordedReviews') -or
        ($requirePass -and (
                [double] $thresholds['confidenceLevel'] -le 0 -or
                [double] $thresholds['confidenceLevel'] -ge 1 -or
                [Int64] $thresholds['maximumFalseHeals'] -ne 0 -or
                $thresholds['requireRealAndroidDeviceEvidence'] -ne $true -or
                $thresholds['requireRecordedReviews'] -ne $true))) {
        return [ordered]@{ ok = $false; reason = 'qualification-thresholds-invalid' }
    }

    if (-not (Test-QualificationMetrics `
            -Metrics $Qualification['metrics'] `
            -Thresholds $thresholds `
            -ManifestFlows ([System.Array] $ManifestResult.flows) `
            -RequirePass $requirePass)) {
        return [ordered]@{ ok = $false; reason = 'qualification-metrics-invalid' }
    }

    $gateStates = @{}
    foreach ($gate in [System.Array] $Qualification['gates']) {
        if (-not (Test-JsonObject $gate) -or
            -not (Test-RequiredString $gate 'gateId' 128) -or
            [string] $gate['gateId'] -cnotin $knownQualificationGates -or
            $gateStates.ContainsKey([string] $gate['gateId']) -or
            -not (Test-RequiredString $gate 'status' 32) -or
            [string] $gate['status'] -cnotin @('pass', 'fail', 'not-qualified') -or
            -not (Test-RequiredString $gate 'message' 1024) -or
            -not (Test-RequiredArray $gate 'reasonCodes' 64) -or
            -not (Test-RequiredArray $gate 'artifactRefs' 64)) {
            return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
        }
        foreach ($code in [System.Array] $gate['reasonCodes']) {
            if ($code -isnot [string] -or
                [string]::IsNullOrWhiteSpace([string] $code) -or
                ([string] $code).Length -gt 128) {
                return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
            }
        }
        foreach ($reference in [System.Array] $gate['artifactRefs']) {
            if ($reference -isnot [string] -or
                -not (Test-Sha256 ([string] $reference))) {
                return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
            }
        }
        if ([string] $gate['status'] -ceq 'pass') {
            $passingReasons = [System.Array] $gate['reasonCodes']
            if ([string] $gate['gateId'] -ceq 'product-analyzer-coverage') {
                if ($passingReasons.Count -ne 1 -or
                    [string] $passingReasons[0] -cne 'provenance-self-reported') {
                    return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
                }
            }
            elseif ($passingReasons.Count -ne 0) {
                return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
            }
        }
        $gateStates[[string] $gate['gateId']] = [string] $gate['status']
    }
    foreach ($requiredGate in $requiredQualificationGates) {
        if (-not $gateStates.ContainsKey($requiredGate)) {
            return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
        }
    }
    if ($gateStates.ContainsKey('input-contract')) {
        return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
    }
    $nonPassingGateStates = @($gateStates.Values | Where-Object { $_ -cne 'pass' })
    $aggregateStatus = if ($gateStates.Values -ccontains 'fail') {
        'fail'
    }
    elseif ($nonPassingGateStates.Count -gt 0) {
        'not-qualified'
    }
    else {
        'pass'
    }
    if ([string] $Qualification['status'] -cne $aggregateStatus -or
        ($requirePass -and $nonPassingGateStates.Count -gt 0)) {
        return [ordered]@{ ok = $false; reason = 'qualification-gates-invalid' }
    }

    foreach ($reason in [System.Array] $Qualification['reasons']) {
        if (-not (Test-JsonObject $reason) -or
            -not (Test-RequiredString $reason 'code' 128) -or
            -not (Test-RequiredString $reason 'severity' 16) -or
            [string] $reason['severity'] -cnotin @('warning', 'error') -or
            -not (Test-RequiredString $reason 'message' 1024)) {
            return [ordered]@{ ok = $false; reason = 'qualification-reasons-invalid' }
        }
    }
    if ($requirePass -and ([System.Array] $Qualification['reasons']).Count -ne 0) {
        return [ordered]@{ ok = $false; reason = 'qualification-reasons-invalid' }
    }

    $artifactRefs = [System.Array] $Qualification['artifactRefs']
    if ($artifactRefs.Count -eq 0) {
        return [ordered]@{ ok = $false; reason = 'qualification-artifacts-invalid' }
    }
    foreach ($artifactRef in $artifactRefs) {
        $referenceValid = Test-RequiredString $artifactRef 'reference' 71
        if ($referenceValid) {
            $reference = [string] $artifactRef['reference']
            $referenceValid = $reference -ceq 'unknown' -or (Test-Sha256 $reference)
        }
        if (-not (Test-JsonObject $artifactRef) -or
            -not (Test-RequiredString $artifactRef 'kind' 128) -or
            [string] $artifactRef['kind'] -cnotin $safeQualificationArtifactKinds -or
            -not (Test-RequiredString $artifactRef 'digest' 71) -or
            -not (Test-Sha256 ([string] $artifactRef['digest'])) -or
            -not $referenceValid -or
            -not (Test-RequiredBoolean $artifactRef 'redacted')) {
            return [ordered]@{ ok = $false; reason = 'qualification-artifacts-invalid' }
        }
    }
    $manifestRefs = @($artifactRefs | Where-Object {
            [string] $_['kind'] -ceq 'flow-pilot-manifest' -and
            [string] $_['digest'] -ceq $ManifestSha256 -and
            $_['redacted'] -eq $true
        })
    if ($manifestRefs.Count -ne 1) {
        return [ordered]@{ ok = $false; reason = 'qualification-manifest-unbound' }
    }

    if (-not (Test-QualificationExclusions $Qualification['exclusions'])) {
        return [ordered]@{ ok = $false; reason = 'qualification-exclusions-invalid' }
    }

    switch ([string] $Qualification['status']) {
        'not-qualified' {
            return [ordered]@{
                ok = $true
                qualified = $false
                status = 'not-qualified'
                reason = 'qualification-not-qualified'
                artifactRefs = $artifactRefs
            }
        }
        'fail' {
            return [ordered]@{
                ok = $true
                qualified = $false
                status = 'fail'
                reason = 'qualification-not-qualified'
                artifactRefs = $artifactRefs
            }
        }
        'pass' {
            return [ordered]@{
                ok = $true
                qualified = $true
                status = 'pass'
                reason = 'qualification-passed'
                artifactRefs = $artifactRefs
                profile = $matchingProfiles[0]
            }
        }
    }
}

function Get-TestIdentity {
    param(
        [Parameter(Mandatory)] [string] $Platform,
        [Parameter(Mandatory)] [string] $Tier,
        [Parameter(Mandatory)] [string] $FlowDigest
    )

    return Get-Sha256Text ("devflow-ci-test-identity-v1`n$Platform`n$Tier`n$FlowDigest")
}

function Write-DeterministicArchive {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [byte[]] $ManifestBytes,
        [Parameter(Mandatory)] [byte[]] $HandoffBytes
    )

    if ($ManifestBytes.LongLength -le 0 -or $ManifestBytes.LongLength -gt $maximumHandoffBytes -or
        $HandoffBytes.LongLength -le 0 -or $HandoffBytes.LongLength -gt $maximumHandoffBytes) {
        throw 'handoff-size-invalid'
    }

    $parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($Path))
    [void] (New-Item -ItemType Directory -Force -Path $parent)
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $stream = [System.IO.File]::Open(
            $temporary,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try {
            $archive = [System.IO.Compression.ZipArchive]::new(
                $stream,
                [System.IO.Compression.ZipArchiveMode]::Create,
                $true)
            try {
                foreach ($item in @(
                    [ordered]@{ name = 'manifest.json'; bytes = $ManifestBytes },
                    [ordered]@{ name = 'handoff.json'; bytes = $HandoffBytes })) {
                    $entry = $archive.CreateEntry([string] $item['name'], [System.IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                    $entryStream = $entry.Open()
                    try {
                        $entryStream.Write([byte[]] $item['bytes'], 0, ([byte[]] $item['bytes']).Length)
                    }
                    finally {
                        $entryStream.Dispose()
                    }
                }
            }
            finally {
                $archive.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        $size = (Get-Item -LiteralPath $temporary).Length
        if ($size -le 0 -or $size -gt $maximumArchiveBytes) {
            throw 'archive-size-invalid'
        }
        [System.IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

function Write-HandoffStagingDirectory {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [byte[]] $ManifestBytes,
        [Parameter(Mandatory)] [byte[]] $HandoffBytes
    )

    $parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($Path))
    [void] (New-Item -ItemType Directory -Force -Path $parent)
    if (Test-Path -LiteralPath $Path) {
        throw 'handoff-staging-already-exists'
    }

    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        [void] (New-Item -ItemType Directory -Path $temporary)
        [System.IO.File]::WriteAllBytes((Join-Path $temporary 'manifest.json'), $ManifestBytes)
        [System.IO.File]::WriteAllBytes((Join-Path $temporary 'handoff.json'), $HandoffBytes)
        [System.IO.Directory]::Move($temporary, $Path)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Recurse -Force
        }
    }
}

$trustedInputFailure = Test-TrustedInputs
if ($trustedInputFailure) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason $trustedInputFailure)
    exit 0
}

$sourceResult = Get-StrictJsonFile $SourceManifestPath
if (-not $sourceResult.ok) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason "manifest-$($sourceResult.reason)")
    exit 0
}

$manifestResult = Test-SourceManifest $sourceResult.value
if (-not $manifestResult.ok) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason $manifestResult.reason)
    exit 0
}

$qualificationSource = Get-StrictJsonFile $QualificationPath
if (-not $qualificationSource.ok) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason "qualification-$($qualificationSource.reason)")
    exit 0
}

$qualificationResult = Test-Qualification `
    -Qualification $qualificationSource.value `
    -ManifestResult $manifestResult `
    -ManifestSha256 ([string] $sourceResult.sha256)
if (-not $qualificationResult.ok) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason $qualificationResult.reason)
    exit 0
}

# The production lane is unchanged: it emits only for a passing qualification on real-device,
# officially covered, nonexperimental evidence. The demo lane is the mirror image and is never a
# substitute for it: it emits only when qualification explicitly did not pass, the evidence is an
# Android emulator, and the run was an operator-triggered default-branch workflow_dispatch.
if ($null -eq $laneProfile -or $laneProfile['requireQualificationPass']) {
    if (-not $qualificationResult.qualified) {
        Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason $qualificationResult.reason)
        exit 0
    }
}

if ($null -eq $laneProfile) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason 'source-lane-not-qualifying')
    exit 0
}
if ($manifestResult.deviceKind -cnotin ([string[]] $laneProfile['requiredDeviceKinds']) -or
    [bool] $manifestResult.realDevice -ne [bool] $laneProfile['requiredRealDevice'] -or
    $manifestResult.experimental -or
    -not $manifestResult.officialCoverage -or
    ($null -ne $laneProfile['requiredPlatforms'] -and
        $manifestResult.platform -cnotin ([string[]] $laneProfile['requiredPlatforms'])) -or
    ($null -ne $laneProfile['requiredSourceEvents'] -and
        $SourceEvent -cnotin ([string[]] $laneProfile['requiredSourceEvents'])) -or
    ($null -ne $laneProfile['requiredQualificationStatuses'] -and
        [string] $qualificationResult.status -cnotin
            ([string[]] $laneProfile['requiredQualificationStatuses']))) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason ([string] $laneProfile['rejectedReason']))
    exit 0
}

$nonPassing = @($manifestResult.flows | Where-Object { $_.outcome -ne 'passed' })
$candidates = [System.Collections.Generic.List[object]]::new()
$unresolved = @($nonPassing | Where-Object { $_.outcome -notin @('failed', 'timed-out') })
foreach ($flow in $nonPassing) {
    if ($flow.outcome -notin @('failed', 'timed-out')) {
        continue
    }

    $category = Get-FailureCategory $flow.failureClass
    if ($category -eq 'infrastructure') {
        continue
    }

    $candidates.Add([ordered]@{
            digest = $flow.digest
            tier = $flow.tier
            category = $category
            attempt = $flow.attempt
        })
}

if ($candidates.Count -gt 0 -and $unresolved.Count -gt 0) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason 'mixed-unresolved-outcomes')
    exit 0
}
if ($candidates.Count -gt 1) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason 'ambiguous-incidents')
    exit 0
}
if ($candidates.Count -eq 0) {
    $reason = if ($nonPassing.Count -eq 0) { 'source-pass' } else { 'source-evidence-insufficient' }
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason $reason)
    exit 0
}

$candidate = $candidates[0]
# Only a passing qualification carries a matched profile, so only the production lane can bind the
# selected incident to it. The demo lane still runs the full selected-evidence binding below.
if ($laneProfile['requireQualificationPass']) {
    $profile = $qualificationResult.profile
    if ([string] $profile['seedFingerprint'] -cne (Get-Fingerprint ([string] $candidate.attempt['seedFingerprint'])) -or
        [string] $profile['backendStateFingerprint'] -cne (Get-Fingerprint ([string] $candidate.attempt['backendStateFingerprint']))) {
        Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason 'qualification-profiles-mismatch')
        exit 0
    }
}

$evidenceResult = Test-SelectedEvidence `
    -Attempt $candidate.attempt `
    -Artifacts ([System.Array] $manifestResult.artifacts) `
    -QualificationArtifactRefs ([System.Array] $qualificationResult.artifactRefs)
if (-not $evidenceResult.ok) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason ([string] $evidenceResult.reason))
    exit 0
}

$handoff = [ordered]@{
    schema = [string] $laneProfile['handoffSchema']
    version = 1
    provenance = [ordered]@{
        repository = $Repository
        workflowName = $WorkflowName
        workflowPath = $WorkflowPath
        sourceEvent = $SourceEvent
        headRepository = $HeadRepository
        headRefSha256 = Get-Sha256Text $HeadRef
        runId = $RunId
        runAttempt = $RunAttempt
        commitSha = $CommitSha
        pullRequestNumber = $PullRequestNumber
    }
    outcome = 'failure'
    qualification = [string] $laneProfile['qualification']
    category = $candidate.category
    platform = $manifestResult.platform
    testIdentitySha256 = Get-TestIdentity -Platform $manifestResult.platform -Tier $candidate.tier -FlowDigest $candidate.digest
    evidenceSufficiency = 'sufficient'
}
# Demo facts are appended after the shared fields, so the production handoff bytes are exactly the
# bytes this producer has always written. Every one of these fields is a refusal the demo publisher
# and the local resolver check: a demo handoff can never be read as production qualification and
# never grants broker or source repair authority.
if ($laneProfile['demo']) {
    $handoff['demo'] = $true
    $handoff['laneKind'] = [string] $laneProfile['laneKind']
    $handoff['deviceEvidenceKind'] = [string] $manifestResult.deviceKind
    $handoff['repairAuthority'] = 'none'
    $handoff['qualificationStatus'] = [string] $qualificationResult.status
}
$handoffBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(($handoff | ConvertTo-Json -Compress -Depth 8))
$handoffSha256 = Get-Sha256Bytes $handoffBytes
$archiveManifest = [ordered]@{
    schema = [string] $laneProfile['manifestSchema']
    version = 1
    entries = @(
        [ordered]@{
            name = 'handoff.json'
            sha256 = $handoffSha256
            sizeBytes = $handoffBytes.LongLength
        })
}
$archiveManifestBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(($archiveManifest | ConvertTo-Json -Compress -Depth 6))
$laneReason = [string] $laneProfile['createdReason']
$archiveBaseName = "$([string] $laneProfile['artifactBaseName'])-$RunId-$RunAttempt"

if ($VerifyOnly) {
    Write-ProducerResult (New-ProducerResult -Status 'verified' -Reason $laneReason -HandoffSha256 $handoffSha256)
    exit 0
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason 'output-directory-missing')
    exit 0
}

$archivePath = Join-Path $OutputDirectory "$archiveBaseName.zip"
$stagingPath = $null
if (-not $PSCmdlet.ShouldProcess($archivePath, 'Create deterministic DevFlow CI failure handoff')) {
    Write-ProducerResult (New-ProducerResult -Status 'would-create' -Reason $laneReason -HandoffSha256 $handoffSha256)
    exit 0
}

try {
    $stagingPath = Join-Path $OutputDirectory $archiveBaseName
    Write-HandoffStagingDirectory -Path $stagingPath -ManifestBytes $archiveManifestBytes -HandoffBytes $handoffBytes
    Write-DeterministicArchive -Path $archivePath -ManifestBytes $archiveManifestBytes -HandoffBytes $handoffBytes
    $archiveSha256 = "sha256:$((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant())"
    Write-ProducerResult (New-ProducerResult -Status 'created' -Reason $laneReason -ArchiveSha256 $archiveSha256 -HandoffSha256 $handoffSha256)
}
catch {
    if ($null -ne $stagingPath -and (Test-Path -LiteralPath $stagingPath)) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
    Write-ProducerResult (New-ProducerResult -Status 'skipped' -Reason 'archive-write-failed')
}
