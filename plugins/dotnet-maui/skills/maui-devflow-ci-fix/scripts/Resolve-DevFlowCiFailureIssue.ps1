#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Issue,

    [string] $Repository,

    [string] $IssueJsonPath,

    [string] $RepositoryJsonPath,

    [string] $RunJsonPath,

    [string] $ArtifactsJsonPath,

    [string] $CommentsJsonPath,

    [string] $RunJsonDirectory,

    [string] $ArtifactsJsonDirectory,

    [switch] $OfflineFixture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$maximumJsonBytes = 1MB
$expectedWorkflowName = 'DevFlow Integration Tests'
$expectedWorkflowPath = '.github/workflows/devflow-integration.yml'
$productionIssueLabel = 'devflow-ci-failure'
$demoIssueLabel = 'devflow-ci-failure-demo'
$publisherLogin = 'github-actions[bot]'

# Exactly one lane profile is resolved, from the labels the trusted publisher applied. The two
# lanes are disjoint: distinct labels, markers, title prefixes, first headings, data fields, and
# artifact prefixes. A production issue can never be read through the demo profile and a demo issue
# can never be read through the production profile, and an issue carrying both labels is refused
# rather than guessed at.
function Get-LaneResolverProfile {
    param([Parameter(Mandatory)] [string] $Name)

    switch ($Name) {
        'demo' {
            return [ordered]@{
                lane = 'demo'
                demo = $true
                issueLabel = $demoIssueLabel
                markerPrefix = 'devflow-ci-failure-demo'
                titlePrefix = '[DevFlow CI DEMO - NOT QUALIFIED]'
                firstHeading = '## Demo handoff (not qualified)'
                dataSuffix = ' lane=(demo-emulator-showcase) device=(emulator) qualification=(not-qualified)'
                sourceEventPattern = '(workflow_dispatch)'
                allowedSourceEvents = @('workflow_dispatch')
                handoffArtifactPrefix = 'devflow-demo-handoff'
                evidenceArtifactPrefix = 'devflow-demo-evidence'
                allowedPlatforms = @('android')
                qualification = 'not-qualified'
                repairAuthority = 'none'
            }
        }
        default {
            return [ordered]@{
                lane = 'production'
                demo = $false
                issueLabel = $productionIssueLabel
                markerPrefix = 'devflow-ci-failure'
                titlePrefix = '[DevFlow CI]'
                firstHeading = '## Verified handoff'
                dataSuffix = ''
                sourceEventPattern = '(schedule|workflow_dispatch)'
                allowedSourceEvents = @('schedule', 'workflow_dispatch')
                handoffArtifactPrefix = 'devflow-failure-handoff'
                evidenceArtifactPrefix = 'devflow-flow-evidence'
                allowedPlatforms = $null
                qualification = 'qualified'
                repairAuthority = 'none'
            }
        }
    }
}

function Write-ResolverResult {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Result)

    Write-Output ($Result | ConvertTo-Json -Compress -Depth 8)
}

function Stop-Resolver {
    param([Parameter(Mandatory)] [string] $Code)

    throw [InvalidOperationException]::new($Code)
}

function Read-BoundedJsonFile {
    param([Parameter(Mandatory)] [string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Stop-Resolver 'json-file-missing'
    }

    $item = Get-Item -LiteralPath $fullPath
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Stop-Resolver 'json-file-linked'
    }
    if ($item.Length -le 0 -or $item.Length -gt $maximumJsonBytes) {
        Stop-Resolver 'json-file-size-invalid'
    }

    try {
        $value = (Get-Content -LiteralPath $fullPath -Raw -Encoding utf8) |
            ConvertFrom-Json -AsHashtable -Depth 32 -NoEnumerate
    }
    catch {
        Stop-Resolver 'json-file-invalid'
    }
    if ($value -isnot [System.Collections.IDictionary]) {
        Stop-Resolver 'json-root-invalid'
    }
    return $value
}

function Read-BoundedJsonArrayFile {
    param([Parameter(Mandatory)] [string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Stop-Resolver 'json-file-missing'
    }

    $item = Get-Item -LiteralPath $fullPath
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Stop-Resolver 'json-file-linked'
    }
    if ($item.Length -lt 0 -or $item.Length -gt $maximumJsonBytes) {
        Stop-Resolver 'json-file-size-invalid'
    }

    try {
        $value = (Get-Content -LiteralPath $fullPath -Raw -Encoding utf8) |
            ConvertFrom-Json -AsHashtable -Depth 32 -NoEnumerate
    }
    catch {
        Stop-Resolver 'json-file-invalid'
    }
    if ($value -isnot [System.Array]) {
        Stop-Resolver 'json-array-root-invalid'
    }
    return ,([System.Array] $value)
}

function Invoke-GitHubApiJson {
    param([Parameter(Mandatory)] [string] $Path)

    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
        Stop-Resolver 'github-cli-unavailable'
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'gh'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    [void] $startInfo.ArgumentList.Add('api')
    [void] $startInfo.ArgumentList.Add($Path)

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            Stop-Resolver 'github-cli-start-failed'
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            Stop-Resolver 'github-api-timeout'
        }

        $text = $standardOutput.GetAwaiter().GetResult()
        [void] $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            Stop-Resolver 'github-api-failed'
        }
        if ([string]::IsNullOrWhiteSpace($text) -or
            [Text.Encoding]::UTF8.GetByteCount($text) -gt $maximumJsonBytes) {
            Stop-Resolver 'github-response-size-invalid'
        }

        try {
            $value = $text | ConvertFrom-Json -AsHashtable -Depth 32 -NoEnumerate
        }
        catch {
            Stop-Resolver 'github-response-invalid'
        }
        if ($value -isnot [System.Collections.IDictionary]) {
            Stop-Resolver 'github-response-root-invalid'
        }
        return $value
    }
    finally {
        $process.Dispose()
    }
}

function Read-JsonSource {
    param(
        [string] $Path,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $ApiPath
    )

    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        return Read-BoundedJsonFile $Path
    }
    return Invoke-GitHubApiJson $ApiPath
}

function Read-OccurrenceJsonSource {
    param(
        [string] $SinglePath,
        [string] $Directory,
        [Parameter(Mandatory)] [string] $FilePrefix,
        [Parameter(Mandatory)] [Int64] $RunId,
        [Parameter(Mandatory)] [Int32] $RunAttempt,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $ApiPath
    )

    if (-not [string]::IsNullOrWhiteSpace($Directory)) {
        $fileName = "$FilePrefix-$RunId-$RunAttempt.json"
        return Read-BoundedJsonFile (Join-Path ([IO.Path]::GetFullPath($Directory)) $fileName)
    }
    return Read-JsonSource $SinglePath $ApiPath
}

function Read-RunArtifacts {
    param(
        [Parameter(Mandatory)] [string] $ResolvedRepository,
        [Parameter(Mandatory)] [Int64] $RunId,
        [Parameter(Mandatory)] [Int32] $RunAttempt
    )

    if (-not [string]::IsNullOrWhiteSpace($ArtifactsJsonPath) -or
        -not [string]::IsNullOrWhiteSpace($ArtifactsJsonDirectory)) {
        return Read-OccurrenceJsonSource `
            -SinglePath $ArtifactsJsonPath `
            -Directory $ArtifactsJsonDirectory `
            -FilePrefix 'artifacts' `
            -RunId $RunId `
            -RunAttempt $RunAttempt `
            -ApiPath ''
    }

    $all = [Collections.Generic.List[object]]::new()
    [Int64] $declaredTotal = -1
    for ($page = 1; $page -le 10; $page++) {
        $record = Invoke-GitHubApiJson `
            "/repos/$ResolvedRepository/actions/runs/$RunId/artifacts?per_page=100&page=$page"
        $pageArtifacts = Get-ArtifactArray $record
        if ($declaredTotal -lt 0) {
            [void] [Int64]::TryParse([string] $record['total_count'], [ref] $declaredTotal)
        }
        foreach ($artifact in $pageArtifacts) {
            $all.Add($artifact)
        }
        if ($pageArtifacts.Count -lt 100) {
            break
        }
    }
    if ($declaredTotal -lt 0 -or $declaredTotal -gt 1000 -or $all.Count -ne $declaredTotal) {
        Stop-Resolver 'artifacts-response-truncated'
    }

    return [ordered]@{
        total_count = $all.Count
        artifacts = $all.ToArray()
    }
}

function Read-IssueComments {
    param(
        [Parameter(Mandatory)] [string] $ResolvedRepository,
        [Parameter(Mandatory)] [Int32] $IssueNumber
    )

    if (-not [string]::IsNullOrWhiteSpace($CommentsJsonPath)) {
        return Read-BoundedJsonArrayFile $CommentsJsonPath
    }

    $comments = [Collections.Generic.List[object]]::new()
    for ($page = 1; $page -le 10; $page++) {
        $response = Invoke-GitHubApiJsonArray `
            "/repos/$ResolvedRepository/issues/$IssueNumber/comments?per_page=100&page=$page"
        foreach ($comment in $response) {
            $comments.Add($comment)
        }
        if ($response.Count -lt 100) {
            return ,$comments.ToArray()
        }
    }

    Stop-Resolver 'issue-comments-truncated'
}

function Invoke-GitHubApiJsonArray {
    param([Parameter(Mandatory)] [string] $Path)

    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
        Stop-Resolver 'github-cli-unavailable'
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'gh'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    [void] $startInfo.ArgumentList.Add('api')
    [void] $startInfo.ArgumentList.Add($Path)

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            Stop-Resolver 'github-cli-start-failed'
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            Stop-Resolver 'github-api-timeout'
        }

        $text = $standardOutput.GetAwaiter().GetResult()
        [void] $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            Stop-Resolver 'github-api-failed'
        }
        if ([Text.Encoding]::UTF8.GetByteCount($text) -gt $maximumJsonBytes) {
            Stop-Resolver 'github-response-size-invalid'
        }

        try {
            $value = $text | ConvertFrom-Json -AsHashtable -Depth 32 -NoEnumerate
        }
        catch {
            Stop-Resolver 'github-response-invalid'
        }
        if ($value -isnot [System.Array]) {
            Stop-Resolver 'github-response-root-invalid'
        }
        return ,([System.Array] $value)
    }
    finally {
        $process.Dispose()
    }
}

function Get-RequiredString {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Value,
        [Parameter(Mandatory)] [string] $Name,
        [int] $MaximumLength = 4096
    )

    if (-not $Value.Contains($Name) -or
        $Value[$Name] -isnot [string] -or
        [string]::IsNullOrWhiteSpace([string] $Value[$Name])) {
        Stop-Resolver "missing-$Name"
    }
    $text = [string] $Value[$Name]
    if ($text.Length -gt $MaximumLength) {
        Stop-Resolver "invalid-$Name"
    }
    return $text
}

function Get-RequiredObject {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Value,
        [Parameter(Mandatory)] [string] $Name
    )

    if (-not $Value.Contains($Name) -or
        $Value[$Name] -isnot [System.Collections.IDictionary]) {
        Stop-Resolver "missing-$Name"
    }
    return [System.Collections.IDictionary] $Value[$Name]
}

function Get-Sha256Text {
    param([Parameter(Mandatory)] [string] $Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return 'sha256:' + [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Resolve-IssueAddress {
    $resolvedRepository = if ($null -eq $Repository) { '' } else { $Repository.Trim() }
    [Int64] $issueNumber = 0
    $issueText = $Issue.Trim()

    $url = [regex]::Match(
        $issueText,
        '\Ahttps://github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)/issues/([1-9][0-9]*)/?\z',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if ($url.Success) {
        $fromUrl = "$($url.Groups[1].Value)/$($url.Groups[2].Value)"
        if (-not [string]::IsNullOrWhiteSpace($resolvedRepository) -and
            -not [string]::Equals($resolvedRepository, $fromUrl, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Resolver 'issue-repository-mismatch'
        }
        $resolvedRepository = $fromUrl
        if (-not [Int64]::TryParse($url.Groups[3].Value, [ref] $issueNumber)) {
            Stop-Resolver 'issue-number-invalid'
        }
    }
    elseif (-not [Int64]::TryParse($issueText, [ref] $issueNumber)) {
        Stop-Resolver 'issue-address-invalid'
    }

    if ($resolvedRepository -cnotmatch '\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z') {
        Stop-Resolver 'repository-invalid'
    }
    if ($issueNumber -le 0 -or $issueNumber -gt [Int32]::MaxValue) {
        Stop-Resolver 'issue-number-invalid'
    }

    return [ordered]@{
        repository = $resolvedRepository
        issueNumber = [Int32] $issueNumber
    }
}

function Get-SingleMatch {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $ErrorCode,
        [Text.RegularExpressions.RegexOptions] $Options =
            [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )

    $matches = [regex]::Matches($Text, $Pattern, $Options)
    if ($matches.Count -ne 1) {
        Stop-Resolver $ErrorCode
    }
    return $matches[0]
}

function Get-TrustedRecurrenceCandidates {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [System.Array] $Comments,
        [Parameter(Mandatory)] [string] $ResolvedRepository,
        [Parameter(Mandatory)] [string] $Category,
        [Parameter(Mandatory)] [string] $Platform,
        [Parameter(Mandatory)] [string] $TestIdentity
    )

    $candidates = [Collections.Generic.List[object]]::new()
    $multiline = [Text.RegularExpressions.RegexOptions]::Multiline -bor
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
    $escapedPrefix = [regex]::Escape([string] $script:laneProfile['markerPrefix'])
    foreach ($commentValue in $Comments) {
        if ($commentValue -isnot [System.Collections.IDictionary]) {
            continue
        }
        $comment = [System.Collections.IDictionary] $commentValue
        if (-not $comment.Contains('user') -or
            $comment['user'] -isnot [System.Collections.IDictionary] -or
            -not $comment.Contains('body') -or
            $comment['body'] -isnot [string]) {
            continue
        }
        $author = [System.Collections.IDictionary] $comment['user']
        if (-not $author.Contains('login') -or
            -not $author.Contains('type') -or
            -not [string]::Equals([string] $author['login'], $publisherLogin, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string] $author['type'], 'Bot', [StringComparison]::Ordinal)) {
            continue
        }

        $body = [string] $comment['body']
        if ([regex]::Matches($body, "<!-- $escapedPrefix-occurrence:v1 ").Count -ne 1) {
            continue
        }
        $marker = [regex]::Match(
            $body,
            "\A<!-- $escapedPrefix-occurrence:v1 run=([1-9][0-9]*) attempt=([1-9][0-9]*) body=(sha256:[0-9a-f]{64}) -->\n",
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $marker.Success) {
            continue
        }
        $payload = $body.Substring($marker.Length)
        if (-not $payload.StartsWith("`n## Recurrence`n", [StringComparison]::Ordinal) -or
            -not [string]::Equals(
                (Get-Sha256Text $payload),
                $marker.Groups[3].Value,
                [StringComparison]::Ordinal)) {
            continue
        }

        $commit = [regex]::Matches($payload, '^- Commit: `([0-9a-f]{40})`$', $multiline)
        $classification = [regex]::Matches(
            $payload,
            '^- Category/platform: `(test-failure|app-crash|timeout|device-failure|harness-failure|infrastructure|unknown)` / `(android|ios|maccatalyst|macos|windows|cross-platform|unknown)`$',
            $multiline)
        $identity = [regex]::Matches(
            $payload,
            '^- Test identity: `(sha256:[0-9a-f]{64})`$',
            $multiline)
        $evidence = [regex]::Matches(
            $payload,
            '^- Evidence sufficiency: `(sufficient|partial|insufficient)`$',
            $multiline)
        if ($commit.Count -ne 1 -or
            $classification.Count -ne 1 -or
            $identity.Count -ne 1 -or
            $evidence.Count -ne 1 -or
            -not [string]::Equals($classification[0].Groups[1].Value, $Category, [StringComparison]::Ordinal) -or
            -not [string]::Equals($classification[0].Groups[2].Value, $Platform, [StringComparison]::Ordinal) -or
            -not [string]::Equals($identity[0].Groups[1].Value, $TestIdentity, [StringComparison]::Ordinal)) {
            continue
        }

        [Int64] $runId = 0
        [Int32] $runAttempt = 0
        if (-not [Int64]::TryParse($marker.Groups[1].Value, [ref] $runId) -or
            -not [Int32]::TryParse($marker.Groups[2].Value, [ref] $runAttempt) -or
            $runAttempt -le 0 -or
            $runAttempt -gt 1000) {
            continue
        }
        $artifactPattern =
            '^- Artifact: \[download\]\(https://github\.com/' +
            [regex]::Escape($ResolvedRepository) +
            '/actions/runs/' + $runId +
            '/artifacts/([1-9][0-9]*)\)$'
        $artifact = [regex]::Matches($payload, $artifactPattern, $multiline)
        [Int64] $artifactId = 0
        if ($artifact.Count -ne 1 -or
            -not [Int64]::TryParse($artifact[0].Groups[1].Value, [ref] $artifactId)) {
            continue
        }

        $candidates.Add([ordered]@{
                runId = $runId
                runAttempt = $runAttempt
                commitSha = $commit[0].Groups[1].Value
                category = $classification[0].Groups[1].Value
                platform = $classification[0].Groups[2].Value
                testIdentity = $identity[0].Groups[1].Value
                evidenceSufficiency = $evidence[0].Groups[1].Value
                handoffArtifactId = $artifactId
                sourceEvent = $null
                occurrenceSource = 'recurrence-comment'
            })
    }

    return $candidates.ToArray()
}

function Get-Artifact {
    param(
        [Parameter(Mandatory)] [System.Array] $Artifacts,
        [Parameter(Mandatory)] [string] $Name,
        [Nullable[Int64]] $ExpectedId,
        [bool] $Required = $true
    )

    $matches = @($Artifacts | Where-Object {
            $_ -is [System.Collections.IDictionary] -and
            $_.Contains('name') -and
            [string]::Equals([string] $_['name'], $Name, [StringComparison]::Ordinal)
        })
    if ($matches.Count -eq 0 -and -not $Required) {
        return $null
    }
    if ($matches.Count -ne 1) {
        Stop-Resolver 'artifact-match-invalid'
    }

    $artifact = [System.Collections.IDictionary] $matches[0]
    [Int64] $artifactId = 0
    if (-not $artifact.Contains('id') -or
        -not [Int64]::TryParse([string] $artifact['id'], [ref] $artifactId) -or
        $artifactId -le 0) {
        Stop-Resolver 'artifact-id-invalid'
    }
    if ($null -ne $ExpectedId -and $artifactId -ne [Int64] $ExpectedId) {
        Stop-Resolver 'artifact-id-mismatch'
    }
    if (-not $artifact.Contains('expired') -or $artifact['expired'] -isnot [bool] -or
        [bool] $artifact['expired']) {
        Stop-Resolver 'artifact-unavailable'
    }

    return [ordered]@{
        id = $artifactId
        name = $Name
    }
}

function Get-ArtifactArray {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Record)

    if (-not $Record.Contains('artifacts') -or
        $Record['artifacts'] -isnot [System.Array]) {
        Stop-Resolver 'artifacts-response-invalid'
    }
    [Int64] $artifactCount = 0
    if (-not $Record.Contains('total_count') -or
        -not [Int64]::TryParse([string] $Record['total_count'], [ref] $artifactCount) -or
        $artifactCount -lt 0) {
        Stop-Resolver 'artifacts-response-invalid'
    }
    return ,([System.Array] $Record['artifacts'])
}

function Get-ValidatedRunMetadata {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $RunRecord,
        [Parameter(Mandatory)] [Int64] $RunId,
        [Parameter(Mandatory)] [Int32] $RunAttempt,
        [Parameter(Mandatory)] [string] $CommitSha,
        [AllowNull()] [string] $ExpectedSourceEvent,
        [Parameter(Mandatory)] [string] $DefaultBranch,
        [Parameter(Mandatory)] [string] $ResolvedRepository
    )

    [Int64] $apiRunId = 0
    [Int32] $apiAttempt = 0
    if (-not $RunRecord.Contains('id') -or
        -not [Int64]::TryParse([string] $RunRecord['id'], [ref] $apiRunId) -or
        $apiRunId -ne $RunId -or
        -not $RunRecord.Contains('run_attempt') -or
        -not [Int32]::TryParse([string] $RunRecord['run_attempt'], [ref] $apiAttempt) -or
        $apiAttempt -ne $RunAttempt) {
        Stop-Resolver 'workflow-run-identity-mismatch'
    }

    $runSourceEvent = Get-RequiredString $RunRecord 'event' 64
    if ($runSourceEvent -cnotin ([string[]] $script:laneProfile['allowedSourceEvents']) -or
        (-not [string]::IsNullOrWhiteSpace($ExpectedSourceEvent) -and
            -not [string]::Equals(
                $runSourceEvent,
                $ExpectedSourceEvent,
                [StringComparison]::Ordinal)) -or
        -not [string]::Equals(
            (Get-RequiredString $RunRecord 'name' 256),
            $expectedWorkflowName,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            (Get-RequiredString $RunRecord 'path' 512),
            $expectedWorkflowPath,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            (Get-RequiredString $RunRecord 'head_branch' 256),
            $DefaultBranch,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            (Get-RequiredString $RunRecord 'head_sha' 64),
            $CommitSha,
            [StringComparison]::Ordinal)) {
        Stop-Resolver 'workflow-run-metadata-mismatch'
    }

    $headRepository = Get-RequiredObject $RunRecord 'head_repository'
    if (-not [string]::Equals(
            (Get-RequiredString $headRepository 'full_name' 256),
            $ResolvedRepository,
            [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Resolver 'workflow-run-repository-mismatch'
    }
    $conclusion = Get-RequiredString $RunRecord 'conclusion' 32
    if ($conclusion -cnotin @('failure', 'timed_out')) {
        Stop-Resolver 'workflow-run-not-failed'
    }
    if (-not $RunRecord.Contains('pull_requests') -or
        $RunRecord['pull_requests'] -isnot [System.Array] -or
        ([System.Array] $RunRecord['pull_requests']).Count -ne 0) {
        Stop-Resolver 'workflow-run-pull-request-invalid'
    }

    return [ordered]@{
        sourceEvent = $runSourceEvent
        conclusion = $conclusion
    }
}

try {
    $fixtureInputs = @(
        $IssueJsonPath,
        $RepositoryJsonPath,
        $RunJsonPath,
        $ArtifactsJsonPath,
        $CommentsJsonPath,
        $RunJsonDirectory,
        $ArtifactsJsonDirectory) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($fixtureInputs.Count -gt 0 -and
        (-not $OfflineFixture -or $env:DEVFLOW_CI_FIX_TEST_FIXTURES -ne '1')) {
        Stop-Resolver 'offline-fixture-not-enabled'
    }

    $address = Resolve-IssueAddress
    $resolvedRepository = [string] $address['repository']
    $issueNumber = [Int32] $address['issueNumber']
    $issueRecord = Read-JsonSource $IssueJsonPath "/repos/$resolvedRepository/issues/$issueNumber"

    [Int64] $apiIssueNumber = 0
    if (-not $issueRecord.Contains('number') -or
        -not [Int64]::TryParse([string] $issueRecord['number'], [ref] $apiIssueNumber) -or
        $apiIssueNumber -ne $issueNumber) {
        Stop-Resolver 'issue-number-mismatch'
    }
    if ($issueRecord.Contains('pull_request')) {
        Stop-Resolver 'issue-is-pull-request'
    }
    if (-not [string]::Equals(
            (Get-RequiredString $issueRecord 'state' 16),
            'open',
            [StringComparison]::Ordinal)) {
        Stop-Resolver 'issue-not-open'
    }
    $author = Get-RequiredObject $issueRecord 'user'
    if (-not [string]::Equals(
            (Get-RequiredString $author 'login' 128),
            $publisherLogin,
            [StringComparison]::Ordinal) -or
        -not [string]::Equals(
            (Get-RequiredString $author 'type' 32),
            'Bot',
            [StringComparison]::Ordinal)) {
        Stop-Resolver 'issue-author-untrusted'
    }

    if (-not $issueRecord.Contains('labels') -or $issueRecord['labels'] -isnot [System.Array]) {
        Stop-Resolver 'issue-labels-invalid'
    }
    $labelNames = @([System.Array] $issueRecord['labels'] | ForEach-Object {
            if ($_ -is [System.Collections.IDictionary] -and $_.Contains('name')) {
                [string] $_['name']
            }
        })
    $hasProductionLabel = $productionIssueLabel -cin $labelNames
    $hasDemoLabel = $demoIssueLabel -cin $labelNames
    if ($hasProductionLabel -and $hasDemoLabel) {
        Stop-Resolver 'issue-label-ambiguous'
    }
    if (-not $hasProductionLabel -and -not $hasDemoLabel) {
        Stop-Resolver 'issue-label-missing'
    }
    $script:laneProfile = Get-LaneResolverProfile $(if ($hasDemoLabel) { 'demo' } else { 'production' })
    $laneProfile = $script:laneProfile
    $markerPrefix = [regex]::Escape([string] $laneProfile['markerPrefix'])

    $body = Get-RequiredString $issueRecord 'body' 65000
    if ([regex]::Matches($body, "<!-- $markerPrefix`:v1 ").Count -ne 1) {
        Stop-Resolver 'issue-marker-invalid'
    }
    $outer = [regex]::Match(
        $body,
        "\A<!-- $markerPrefix`:v1 fingerprint=(sha256:[0-9a-f]{64}) body=(sha256:[0-9a-f]{64}) -->\n",
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $outer.Success) {
        Stop-Resolver 'issue-marker-invalid'
    }
    $payload = $body.Substring($outer.Length)
    if (-not [string]::Equals(
            (Get-Sha256Text $payload),
            $outer.Groups[2].Value,
            [StringComparison]::Ordinal)) {
        Stop-Resolver 'issue-body-digest-mismatch'
    }

    $multiline = [Text.RegularExpressions.RegexOptions]::Multiline -bor
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
    $occurrence = Get-SingleMatch $payload `
        "^<!-- $markerPrefix-occurrence:v1 run=([1-9][0-9]*) attempt=([1-9][0-9]*) -->`$" `
        'issue-occurrence-invalid' $multiline
    $platformPattern = if ($null -eq $laneProfile['allowedPlatforms']) {
        '(android|ios|maccatalyst|macos|windows|cross-platform|unknown)'
    }
    else {
        "($(([string[]] $laneProfile['allowedPlatforms']) -join '|'))"
    }
    $data = Get-SingleMatch $payload `
        ("^<!-- $markerPrefix-data:v1 category=(test-failure|app-crash|timeout|device-failure|harness-failure|infrastructure|unknown) " +
            "platform=$platformPattern testIdentity=(sha256:[0-9a-f]{64}) evidence=(sufficient|partial|insufficient)" +
            "$([string] $laneProfile['dataSuffix']) -->`$") `
        'issue-data-invalid' $multiline
    $firstHeading = [string] $laneProfile['firstHeading']
    if (-not $payload.StartsWith(
            "$($occurrence.Value)`n$($data.Value)`n`n$firstHeading`n",
            [StringComparison]::Ordinal)) {
        Stop-Resolver 'issue-template-invalid'
    }

    $headings = @($firstHeading, '## Evidence', '## Artifact handoff', '## Local handoff')
    $previousIndex = -1
    foreach ($heading in $headings) {
        if ([regex]::Matches(
                $payload,
                "(?m)^$([regex]::Escape($heading))$",
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne 1) {
            Stop-Resolver 'issue-template-invalid'
        }
        $index = $payload.IndexOf($heading, [StringComparison]::Ordinal)
        if ($index -le $previousIndex) {
            Stop-Resolver 'issue-template-invalid'
        }
        $previousIndex = $index
    }

    [Int64] $runId = 0
    [Int32] $runAttempt = 0
    if (-not [Int64]::TryParse($occurrence.Groups[1].Value, [ref] $runId) -or
        -not [Int32]::TryParse($occurrence.Groups[2].Value, [ref] $runAttempt) -or
        $runAttempt -le 0 -or $runAttempt -gt 1000) {
        Stop-Resolver 'issue-occurrence-invalid'
    }
    $category = $data.Groups[1].Value
    $platform = $data.Groups[2].Value
    $testIdentity = $data.Groups[3].Value
    $evidenceSufficiency = $data.Groups[4].Value

    $title = Get-RequiredString $issueRecord 'title' 512
    $expectedTitle = "$([string] $laneProfile['titlePrefix']) $category on $platform ($($testIdentity.Substring(7, 12)))"
    if (-not [string]::Equals($title, $expectedTitle, [StringComparison]::Ordinal)) {
        Stop-Resolver 'issue-title-invalid'
    }

    $commit = Get-SingleMatch $payload '^- Commit: `([0-9a-f]{40})`$' `
        'issue-commit-invalid' $multiline
    $sourceEvent = Get-SingleMatch $payload `
        "^- Source event: ``$([string] $laneProfile['sourceEventPattern'])```$" `
        'issue-source-event-invalid' $multiline
    $artifactPattern =
        '^- Download: \[retained workflow artifact\]\(https://github\.com/' +
        [regex]::Escape($resolvedRepository) +
        '/actions/runs/' + $runId +
        '/artifacts/([1-9][0-9]*)\)$'
    $artifactLink = Get-SingleMatch $payload $artifactPattern 'issue-artifact-link-invalid' $multiline
    [Int64] $handoffArtifactId = 0
    if (-not [Int64]::TryParse($artifactLink.Groups[1].Value, [ref] $handoffArtifactId)) {
        Stop-Resolver 'issue-artifact-link-invalid'
    }

    $occurrences = [Collections.Generic.List[object]]::new()
    $occurrences.Add([ordered]@{
            runId = $runId
            runAttempt = $runAttempt
            commitSha = $commit.Groups[1].Value
            category = $category
            platform = $platform
            testIdentity = $testIdentity
            evidenceSufficiency = $evidenceSufficiency
            handoffArtifactId = $handoffArtifactId
            sourceEvent = $sourceEvent.Groups[1].Value
            occurrenceSource = 'issue-body'
        })
    $comments = Read-IssueComments -ResolvedRepository $resolvedRepository -IssueNumber $issueNumber
    foreach ($recurrence in Get-TrustedRecurrenceCandidates `
            -Comments $comments `
            -ResolvedRepository $resolvedRepository `
            -Category $category `
            -Platform $platform `
            -TestIdentity $testIdentity) {
        $occurrences.Add($recurrence)
    }

    $repositoryRecord = Read-JsonSource $RepositoryJsonPath "/repos/$resolvedRepository"
    if (-not [string]::Equals(
            (Get-RequiredString $repositoryRecord 'full_name' 256),
            $resolvedRepository,
            [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Resolver 'repository-metadata-mismatch'
    }
    $defaultBranch = Get-RequiredString $repositoryRecord 'default_branch' 256

    $orderedOccurrences = @($occurrences |
        Sort-Object `
            @{ Expression = { [Int64] $_['runId'] }; Descending = $true }, `
            @{ Expression = { [Int32] $_['runAttempt'] }; Descending = $true })
    $selectedOccurrence = $null
    $prefetchedRunRecord = $null
    $prefetchedRunMetadata = $null
    $prefetchedArtifactsRecord = $null
    if (-not [string]::IsNullOrWhiteSpace($RunJsonPath) -or
        -not [string]::IsNullOrWhiteSpace($ArtifactsJsonPath)) {
        $selectedOccurrence = $orderedOccurrences[0]
    }
    else {
        foreach ($candidate in $orderedOccurrences) {
            try {
                $candidateRunId = [Int64] $candidate['runId']
                $candidateRunAttempt = [Int32] $candidate['runAttempt']
                $candidateRunRecord = Read-OccurrenceJsonSource `
                    -SinglePath '' `
                    -Directory $RunJsonDirectory `
                    -FilePrefix 'run' `
                    -RunId $candidateRunId `
                    -RunAttempt $candidateRunAttempt `
                    -ApiPath "/repos/$resolvedRepository/actions/runs/$candidateRunId/attempts/$candidateRunAttempt"
                $candidateRunMetadata = Get-ValidatedRunMetadata `
                    -RunRecord $candidateRunRecord `
                    -RunId $candidateRunId `
                    -RunAttempt $candidateRunAttempt `
                    -CommitSha ([string] $candidate['commitSha']) `
                    -ExpectedSourceEvent $candidate['sourceEvent'] `
                    -DefaultBranch $defaultBranch `
                    -ResolvedRepository $resolvedRepository
                $candidateArtifactsRecord = Read-RunArtifacts `
                    -ResolvedRepository $resolvedRepository `
                    -RunId $candidateRunId `
                    -RunAttempt $candidateRunAttempt
                $candidateArtifacts = Get-ArtifactArray $candidateArtifactsRecord
                [void] (Get-Artifact `
                        $candidateArtifacts `
                        "$([string] $laneProfile['handoffArtifactPrefix'])-$candidateRunId-$candidateRunAttempt" `
                        ([Int64] $candidate['handoffArtifactId']))
                $selectedOccurrence = $candidate
                $prefetchedRunRecord = $candidateRunRecord
                $prefetchedRunMetadata = $candidateRunMetadata
                $prefetchedArtifactsRecord = $candidateArtifactsRecord
                break
            }
            catch {
                if ($_.Exception.Message -cnotmatch '\A(?:artifact-|workflow-run-|missing-)' ) {
                    throw
                }
            }
        }
    }
    if ($null -eq $selectedOccurrence) {
        Stop-Resolver 'occurrence-artifact-unavailable'
    }
    $runId = [Int64] $selectedOccurrence['runId']
    $runAttempt = [Int32] $selectedOccurrence['runAttempt']
    $commitSha = [string] $selectedOccurrence['commitSha']
    $evidenceSufficiency = [string] $selectedOccurrence['evidenceSufficiency']
    $handoffArtifactId = [Int64] $selectedOccurrence['handoffArtifactId']
    $occurrenceSource = [string] $selectedOccurrence['occurrenceSource']
    $expectedSourceEvent = $selectedOccurrence['sourceEvent']

    $runRecord = if ($null -ne $prefetchedRunRecord) {
        $prefetchedRunRecord
    }
    else {
        Read-OccurrenceJsonSource `
            -SinglePath $RunJsonPath `
            -Directory $RunJsonDirectory `
            -FilePrefix 'run' `
            -RunId $runId `
            -RunAttempt $runAttempt `
            -ApiPath "/repos/$resolvedRepository/actions/runs/$runId/attempts/$runAttempt"
    }
    $runMetadata = if ($null -ne $prefetchedRunMetadata) {
        $prefetchedRunMetadata
    }
    else {
        Get-ValidatedRunMetadata `
            -RunRecord $runRecord `
            -RunId $runId `
            -RunAttempt $runAttempt `
            -CommitSha $commitSha `
            -ExpectedSourceEvent $expectedSourceEvent `
            -DefaultBranch $defaultBranch `
            -ResolvedRepository $resolvedRepository
    }
    $runSourceEvent = [string] $runMetadata['sourceEvent']
    $conclusion = [string] $runMetadata['conclusion']

    $artifactsRecord = if ($null -ne $prefetchedArtifactsRecord) {
        $prefetchedArtifactsRecord
    }
    else {
        Read-RunArtifacts `
            -ResolvedRepository $resolvedRepository `
            -RunId $runId `
            -RunAttempt $runAttempt
    }
    $artifacts = Get-ArtifactArray $artifactsRecord
    $handoffArtifactName = "$([string] $laneProfile['handoffArtifactPrefix'])-$runId-$runAttempt"
    # Demo evidence never maps to a production evidence artifact name, and vice versa.
    $evidenceArtifactName = if ($laneProfile['demo']) {
        if ($platform -ceq 'android') {
            "$([string] $laneProfile['evidenceArtifactPrefix'])-android-$runId-$runAttempt"
        }
        else {
            $null
        }
    }
    else {
        $evidenceArtifactPlatform = switch ($platform) {
            'android' { 'android' }
            'ios' { 'ios' }
            'maccatalyst' { 'maccatalyst' }
            'macos' { 'macos-appkit' }
            'windows' { 'windows' }
            default { $null }
        }
        if ($null -eq $evidenceArtifactPlatform) {
            $null
        }
        else {
            "$([string] $laneProfile['evidenceArtifactPrefix'])-$evidenceArtifactPlatform-$runId-$runAttempt"
        }
    }
    $handoffArtifact = Get-Artifact $artifacts $handoffArtifactName $handoffArtifactId
    $evidenceArtifact = if ($null -eq $evidenceArtifactName) {
        $null
    }
    else {
        Get-Artifact $artifacts $evidenceArtifactName $null $false
    }

    Write-ResolverResult ([ordered]@{
            ok = $true
            lane = [string] $laneProfile['lane']
            demo = [bool] $laneProfile['demo']
            qualification = [string] $laneProfile['qualification']
            repairAuthority = [string] $laneProfile['repairAuthority']
            repository = $resolvedRepository
            issueNumber = $issueNumber
            issueUrl = "https://github.com/$resolvedRepository/issues/$issueNumber"
            fingerprint = $outer.Groups[1].Value
            category = $category
            platform = $platform
            testIdentity = $testIdentity
            evidenceSufficiency = $evidenceSufficiency
            runId = $runId
            runAttempt = $runAttempt
            occurrenceSource = $occurrenceSource
            commitSha = $commitSha
            workflowName = $expectedWorkflowName
            workflowPath = $expectedWorkflowPath
            sourceEvent = $runSourceEvent
            headRepository = $resolvedRepository
            headRef = $defaultBranch
            defaultBranch = $defaultBranch
            workflowConclusion = $conclusion
            pullRequestNumber = 0
            handoffArtifactId = $handoffArtifact['id']
            handoffArtifactName = $handoffArtifact['name']
            evidenceAvailable = $null -ne $evidenceArtifact
            evidenceArtifactId = if ($null -eq $evidenceArtifact) { 0 } else { $evidenceArtifact['id'] }
            evidenceArtifactName = if ($null -eq $evidenceArtifact) { $evidenceArtifactName } else { $evidenceArtifact['name'] }
        })
}
catch {
    $code = $_.Exception.Message
    if ($code -cnotmatch '\A[a-z0-9][a-z0-9_-]{0,127}\z') {
        $code = 'issue-resolver-failed'
    }
    Write-ResolverResult ([ordered]@{
            ok = $false
            error = $code
        })
    exit 1
}
