#Requires -Version 7.3
[CmdletBinding()]
param(
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
    [string] $DefaultBranch,

    [Parameter(Mandatory)]
    [string] $WorkflowConclusion,

    [Parameter(Mandatory)]
    [Int64] $RunId,

    [Parameter(Mandatory)]
    [Int32] $RunAttempt,

    [Parameter(Mandatory)]
    [string] $CommitSha,

    [Int32] $PullRequestNumber = 0,

    [string] $ArchivePath,

    [string] $GitHubToken = $env:GITHUB_TOKEN,

    [string] $GitHubApiBaseUrl = 'https://api.github.com',

    [switch] $VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedWorkflowName = 'DevFlow Integration Tests'
$expectedWorkflowPath = '.github/workflows/devflow-integration.yml'
$issueLabel = 'devflow-ci-failure'
$publisherBotLogin = 'github-actions[bot]'
$manifestEntryName = 'manifest.json'
$handoffEntryName = 'handoff.json'
$maximumArchiveBytes = 1MB
$maximumEntryCount = 2
$maximumEntryBytes = 256KB
$maximumTotalUncompressedBytes = 512KB
$maximumCompressionRatio = 100.0
$maximumJsonSafeInteger = 9007199254740991
$maximumRunAttempt = 1000
$script:LoopbackTestApi = $false

function New-PublisherResult {
    param(
        [Parameter(Mandatory)]
        [string] $Status,

        [Parameter(Mandatory)]
        [string] $Reason,

        [string] $Fingerprint,

        [Nullable[Int32]] $IssueNumber
    )

    $result = [ordered]@{
        status = $Status
        reason = $Reason
    }
    if ($Fingerprint) {
        $result['fingerprint'] = $Fingerprint
    }
    if ($null -ne $IssueNumber) {
        $result['issueNumber'] = [Int32] $IssueNumber
    }

    return $result
}

function Write-PublisherResult {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Result)

    Write-Output ($Result | ConvertTo-Json -Depth 5 -Compress)
}

function Test-TrustedInputs {
    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        return 'invalid-repository'
    }
    if (-not [string]::Equals($WorkflowName, $expectedWorkflowName, [StringComparison]::Ordinal)) {
        return 'unexpected-workflow-name'
    }
    if (-not [string]::Equals($WorkflowPath, $expectedWorkflowPath, [StringComparison]::Ordinal)) {
        return 'unexpected-workflow-path'
    }
    if ($SourceEvent -cnotin @('pull_request', 'workflow_dispatch', 'schedule')) {
        return 'unexpected-source-event'
    }
    if ($HeadRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        return 'invalid-head-repository'
    }
    if ([string]::IsNullOrWhiteSpace($HeadRef) -or
        $HeadRef.Length -gt 255 -or
        $HeadRef.IndexOfAny([char[]] "`r`n`0") -ge 0) {
        return 'invalid-head-ref'
    }
    if ([string]::IsNullOrWhiteSpace($DefaultBranch) -or
        $DefaultBranch.Length -gt 255 -or
        $DefaultBranch.IndexOfAny([char[]] "`r`n`0") -ge 0) {
        return 'invalid-default-branch'
    }
    if ($WorkflowConclusion -notin @('success', 'failure', 'neutral', 'cancelled', 'skipped', 'timed_out', 'action_required', 'stale')) {
        return 'unexpected-workflow-conclusion'
    }
    if ($RunId -le 0 -or
        $RunId -gt $maximumJsonSafeInteger -or
        $RunAttempt -le 0 -or
        $RunAttempt -gt $maximumRunAttempt -or
        $PullRequestNumber -lt 0) {
        return 'invalid-run-identity'
    }
    if ($CommitSha -cnotmatch '^[0-9a-f]{40}$') {
        return 'invalid-commit-sha'
    }
    if (($SourceEvent -ceq 'pull_request' -and $PullRequestNumber -le 0) -or
        ($SourceEvent -cne 'pull_request' -and $PullRequestNumber -ne 0)) {
        return 'source-pr-inconsistent'
    }

    $apiBaseUri = $null
    if (-not [Uri]::TryCreate($GitHubApiBaseUrl, [UriKind]::Absolute, [ref] $apiBaseUri) -or
        -not [string]::IsNullOrEmpty($apiBaseUri.UserInfo) -or
        -not [string]::IsNullOrEmpty($apiBaseUri.Query) -or
        -not [string]::IsNullOrEmpty($apiBaseUri.Fragment) -or
        $apiBaseUri.AbsolutePath.Trim('/') -ne '') {
        return 'invalid-github-api-base-url'
    }
    $isGitHubApi = $apiBaseUri.Scheme -ceq 'https' -and
        $apiBaseUri.Host -ceq 'api.github.com'
    $isLoopbackTestApi = $env:DEVFLOW_PUBLISHER_ALLOW_LOOPBACK_TEST_API -ceq '1' -and
        $apiBaseUri.IsLoopback -and
        $apiBaseUri.Scheme -cin @('http', 'https')
    if (-not $isGitHubApi -and -not $isLoopbackTestApi) {
        return 'untrusted-github-api-base-url'
    }
    # A loopback API is a test double, not GitHub. Publishing against it would send a real
    # installation token to a local listener, so the mode is confined to verification, which needs
    # no credential at all, and the held token is dropped before any request can be built.
    $script:LoopbackTestApi = $isLoopbackTestApi
    if ($isLoopbackTestApi) {
        $script:GitHubToken = ''
        if (-not $VerifyOnly) {
            return 'loopback-test-api-requires-verify-only'
        }
    }

    return $null
}

function Test-PublicationTrust {
    if ($SourceEvent -cnotin @('schedule', 'workflow_dispatch')) {
        return 'source-event-not-publishable'
    }
    if ($PullRequestNumber -ne 0) {
        return 'source-pr-associated'
    }
    if (-not [string]::Equals($HeadRepository, $Repository, [StringComparison]::Ordinal)) {
        return 'source-head-repository-untrusted'
    }
    if (-not [string]::Equals($HeadRef, $DefaultBranch, [StringComparison]::Ordinal)) {
        return 'source-head-ref-untrusted'
    }

    return $null
}

# The GitHub API authenticates every write this publisher makes, so the header carries the token
# this invocation actually holds. The value is built only here, is never written to a result, an
# artifact, or a diagnostic, and is omitted outright when no credential is held: a header with an
# empty scheme value is an unauthenticated request the API answers with 401, which the publisher
# would then report as an operational fault rather than as the missing credential it really is.
function Get-GitHubHeaders {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Token)

    $headers = @{
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'maui-labs-devflow-failure-publisher'
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers['Authorization'] = "Bearer $Token"
    }

    return $headers
}

function Get-GitHubUri {
    param([Parameter(Mandatory)] [string] $Path)

    return "$($GitHubApiBaseUrl.TrimEnd('/'))$Path"
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('GET', 'POST', 'PATCH')]
        [string] $Method,

        [Parameter(Mandatory)]
        [string] $Path,

        [System.Collections.IDictionary] $Body,

        [switch] $AllowNotFound
    )

    $parameters = @{
        Method = $Method
        Uri = Get-GitHubUri $Path
        Headers = Get-GitHubHeaders $GitHubToken
        SkipHttpErrorCheck = $true
    }
    if ($null -ne $Body) {
        $parameters['Body'] = $Body | ConvertTo-Json -Depth 10 -Compress
        $parameters['ContentType'] = 'application/json'
    }

    $response = Invoke-WebRequest @parameters
    $statusCode = [Int32] $response.StatusCode
    if ($statusCode -in @(401, 403)) {
        throw [InvalidOperationException]::new('github-api-unauthorized')
    }
    if ($statusCode -eq 404 -and $AllowNotFound) {
        return $null
    }
    if ($statusCode -lt 200 -or $statusCode -ge 300) {
        throw [InvalidOperationException]::new("github-api-http-$statusCode")
    }
    if ([string]::IsNullOrWhiteSpace([string] $response.Content)) {
        return $null
    }

    $value = ([string] $response.Content) | ConvertFrom-Json -Depth 32 -NoEnumerate
    if ($value -is [System.Array]) {
        Write-Output -NoEnumerate $value
        return
    }
    return $value
}

function Test-ApiRun {
    param([Parameter(Mandatory)] $Run)

    if (-not (Test-JsonIntegerRange $Run.id 1 $maximumJsonSafeInteger) -or
        [Int64] $Run.id -ne $RunId) {
        return 'api-run-id-mismatch'
    }
    if ($null -eq $Run.repository -or
        -not [string]::Equals([string] $Run.repository.full_name, $Repository, [StringComparison]::Ordinal)) {
        return 'api-repository-mismatch'
    }
    if (-not [string]::Equals([string] $Run.name, $WorkflowName, [StringComparison]::Ordinal)) {
        return 'api-workflow-name-mismatch'
    }
    if (-not [string]::Equals([string] $Run.path, $WorkflowPath, [StringComparison]::Ordinal)) {
        return 'api-workflow-path-mismatch'
    }
    if (-not [string]::Equals([string] $Run.event, $SourceEvent, [StringComparison]::Ordinal)) {
        return 'api-event-mismatch'
    }
    if (-not [string]::Equals([string] $Run.conclusion, $WorkflowConclusion, [StringComparison]::Ordinal)) {
        return 'api-conclusion-mismatch'
    }
    if (-not (Test-JsonIntegerRange $Run.run_attempt 1 $maximumRunAttempt) -or
        [Int32] $Run.run_attempt -ne $RunAttempt) {
        return 'api-run-attempt-mismatch'
    }
    if (-not [string]::Equals([string] $Run.head_sha, $CommitSha, [StringComparison]::Ordinal)) {
        return 'api-commit-mismatch'
    }
    if ($null -eq $Run.head_repository -or
        -not [string]::Equals([string] $Run.head_repository.full_name, $HeadRepository, [StringComparison]::Ordinal)) {
        return 'api-head-repository-mismatch'
    }
    if (-not [string]::Equals([string] $Run.head_branch, $HeadRef, [StringComparison]::Ordinal)) {
        return 'api-head-ref-mismatch'
    }

    if ($Run.pull_requests -isnot [System.Array]) {
        return 'api-pr-invalid'
    }
    $pullRequests = [System.Array] $Run.pull_requests
    if ($pullRequests.Count -gt 1) {
        return 'api-pr-ambiguous'
    }
    if ($pullRequests.Count -eq 1 -and
        -not (Test-JsonIntegerRange $pullRequests[0].number 1 ([Int32]::MaxValue))) {
        return 'api-pr-invalid'
    }
    $apiPullRequestNumber = if ($pullRequests.Count -eq 1) { [Int32] $pullRequests[0].number } else { 0 }
    if ($apiPullRequestNumber -ne $PullRequestNumber) {
        return 'api-pr-mismatch'
    }

    return $null
}

function Test-RepositoryMetadata {
    param([Parameter(Mandatory)] $Metadata)

    if (-not [string]::Equals([string] $Metadata.full_name, $Repository, [StringComparison]::Ordinal)) {
        return [ordered]@{ ok = $false; reason = 'api-repository-metadata-mismatch' }
    }
    if (-not [string]::Equals([string] $Metadata.default_branch, $DefaultBranch, [StringComparison]::Ordinal)) {
        return [ordered]@{ ok = $false; reason = 'api-default-branch-mismatch' }
    }
    if ($Metadata.PSObject.Properties.Name -cnotcontains 'has_issues' -or
        $Metadata.has_issues -isnot [bool]) {
        return [ordered]@{ ok = $false; reason = 'api-issues-setting-invalid' }
    }

    return [ordered]@{
        ok = $true
        issuesEnabled = [bool] $Metadata.has_issues
    }
}

function Ensure-DedicatedIssueLabel {
    $encodedLabel = [Uri]::EscapeDataString($issueLabel)
    $label = Invoke-GitHubJson -Method GET -Path "/repos/$Repository/labels/$encodedLabel" -AllowNotFound
    if ($null -eq $label) {
        $label = Invoke-GitHubJson -Method POST -Path "/repos/$Repository/labels" -Body @{
            name = $issueLabel
            color = '5319e7'
            description = 'Verified DevFlow CI failure handoff'
        }
    }

    if (-not [string]::Equals([string] $label.name, $issueLabel, [StringComparison]::Ordinal)) {
        throw [InvalidOperationException]::new('github-api-label-invalid')
    }
}

function Get-ExpectedArtifact {
    $expectedName = "devflow-failure-handoff-$RunId-$RunAttempt"
    $matches = [System.Collections.Generic.List[object]]::new()
    $page = 1

    while ($true) {
        $response = Invoke-GitHubJson -Method GET -Path "/repos/$Repository/actions/runs/$RunId/artifacts?per_page=100&page=$page"
        if ($null -eq $response -or $response.artifacts -isnot [System.Array]) {
            return [ordered]@{ status = 'metadata-invalid' }
        }
        $artifacts = [System.Array] $response.artifacts
        foreach ($artifact in $artifacts) {
            if ([string]::Equals([string] $artifact.name, $expectedName, [StringComparison]::Ordinal)) {
                $matches.Add($artifact)
            }
        }
        if ($artifacts.Count -lt 100) {
            break
        }
        $page++
    }

    if ($matches.Count -eq 0) {
        return [ordered]@{ status = 'missing' }
    }
    if ($matches.Count -ne 1) {
        return [ordered]@{ status = 'ambiguous' }
    }

    $match = $matches[0]
    if ($match.expired -isnot [bool]) {
        return [ordered]@{ status = 'metadata-invalid' }
    }
    if ([bool] $match.expired) {
        return [ordered]@{ status = 'expired' }
    }
    if (-not (Test-JsonIntegerRange $match.id 1 $maximumJsonSafeInteger) -or
        -not (Test-JsonIntegerRange $match.size_in_bytes 1 $maximumArchiveBytes)) {
        return [ordered]@{ status = 'invalid-size' }
    }
    $workflowRunProperty = $match.PSObject.Properties['workflow_run']
    if ($null -ne $workflowRunProperty -and
        $null -ne $workflowRunProperty.Value -and
        (-not (Test-JsonIntegerRange $workflowRunProperty.Value.id 1 $maximumJsonSafeInteger) -or
            [Int64] $workflowRunProperty.Value.id -ne $RunId)) {
        return [ordered]@{ status = 'run-mismatch' }
    }

    return [ordered]@{
        status = 'found'
        artifact = $match
    }
}

function Save-GitHubArtifactArchive {
    param(
        [Parameter(Mandatory)]
        [Int64] $ArtifactId,

        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    Add-Type -AssemblyName System.Net.Http
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $true
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
    # No credential is attached unless the request is going to the trusted GitHub API with a token
    # this invocation actually holds. A loopback test double never receives one.
    if (-not $script:LoopbackTestApi -and -not [string]::IsNullOrWhiteSpace($GitHubToken)) {
        $client.DefaultRequestHeaders.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $GitHubToken)
    }
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('maui-labs-devflow-failure-publisher')
    $client.DefaultRequestHeaders.Add('X-GitHub-Api-Version', '2022-11-28')

    $response = $null
    $source = $null
    $destination = $null
    $succeeded = $false
    try {
        $uri = Get-GitHubUri "/repos/$Repository/actions/artifacts/$ArtifactId/zip"
        $response = $client.GetAsync(
            $uri,
            [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $statusCode = [Int32] $response.StatusCode
        if ($statusCode -in @(401, 403)) {
            throw [InvalidOperationException]::new('github-api-unauthorized')
        }
        if (-not $response.IsSuccessStatusCode) {
            throw [InvalidOperationException]::new("github-api-http-$statusCode")
        }

        $contentLength = $response.Content.Headers.ContentLength
        if ($null -ne $contentLength -and
            [Int64] $contentLength -gt $maximumArchiveBytes) {
            throw 'archive-download-too-large'
        }

        $parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($DestinationPath))
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            [void] (New-Item -ItemType Directory -Force -Path $parent)
        }

        $source = $response.Content.ReadAsStream()
        $destination = [System.IO.File]::Open(
            $DestinationPath,
            [System.IO.FileMode]::Create,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $buffer = [byte[]]::new(81920)
        [Int64] $total = 0
        while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += $read
            if ($total -gt $maximumArchiveBytes) {
                throw 'archive-download-too-large'
            }
            $destination.Write($buffer, 0, $read)
        }
        if ($total -le 0) {
            throw 'archive-download-empty'
        }
        $succeeded = $true
    }
    finally {
        if ($null -ne $destination) {
            $destination.Dispose()
        }
        if ($null -ne $source) {
            $source.Dispose()
        }
        if ($null -ne $response) {
            $response.Dispose()
        }
        $client.Dispose()
        $handler.Dispose()
        if (-not $succeeded -and (Test-Path -LiteralPath $DestinationPath -PathType Leaf)) {
            Remove-Item -LiteralPath $DestinationPath -Force
        }
    }
}

function Get-Sha256Bytes {
    param([Parameter(Mandatory)] [byte[]] $Bytes)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return "sha256:$([Convert]::ToHexString($sha.ComputeHash($Bytes)).ToLowerInvariant())"
    }
    finally {
        $sha.Dispose()
    }
}

function Get-Sha256Text {
    param([Parameter(Mandatory)] [string] $Text)

    return Get-Sha256Bytes ([System.Text.Encoding]::UTF8.GetBytes($Text))
}

function Read-ZipEntryBytes {
    param([Parameter(Mandatory)] [System.IO.Compression.ZipArchiveEntry] $Entry)

    $stream = $Entry.Open()
    $memory = [System.IO.MemoryStream]::new()
    try {
        $buffer = [byte[]]::new(81920)
        [Int64] $total = 0
        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += $read
            if ($total -gt $maximumEntryBytes -or $total -gt $Entry.Length) {
                throw 'entry-stream-size-mismatch'
            }
            $memory.Write($buffer, 0, $read)
        }
        if ($total -ne $Entry.Length) {
            throw 'entry-stream-size-mismatch'
        }

        return ,$memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $stream.Dispose()
    }
}

function ConvertFrom-StrictUtf8Json {
    param([Parameter(Mandatory)] [byte[]] $Bytes)

    $encoding = [System.Text.UTF8Encoding]::new($false, $true)
    $text = $encoding.GetString($Bytes)
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 20
    $document = [System.Text.Json.JsonDocument]::Parse($text, $options)
    try {
        return ConvertFrom-StrictJsonElement $document.RootElement
    }
    finally {
        $document.Dispose()
    }
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

function Test-RequiredString {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Object,
        [Parameter(Mandatory)] [string] $Name
    )

    return $Object.Contains($Name) -and
        $Object[$Name] -is [string] -and
        -not [string]::IsNullOrWhiteSpace([string] $Object[$Name]) -and
        ([string] $Object[$Name]).Length -le 4096 -and
        ([string] $Object[$Name]).IndexOf([char] 0) -lt 0
}

function Test-ArchivePathName {
    param([Parameter(Mandatory)] [string] $Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Contains('\') -or
        $Name.StartsWith('/', [StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($Name)) {
        return $false
    }

    $segments = $Name.Split('/')
    return @($segments | Where-Object { $_ -in @('', '.', '..') }).Count -eq 0
}

function Test-HandoffArchive {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'archive-missing' }
    }

    $archiveFile = Get-Item -LiteralPath $Path
    if ($archiveFile.Length -le 0 -or $archiveFile.Length -gt $maximumArchiveBytes) {
        return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'archive-size-out-of-range' }
    }

    Add-Type -AssemblyName System.IO.Compression
    $stream = $null
    $archive = $null
    try {
        $stream = [System.IO.File]::Open(
            $archiveFile.FullName,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Read,
            $false)

        $entries = @($archive.Entries)
        if ($entries.Count -ne $maximumEntryCount) {
            return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-count-invalid' }
        }

        $allowedNames = @($manifestEntryName, $handoffEntryName)
        $seenNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        [Int64] $totalUncompressed = 0
        $entryBytes = @{}

        foreach ($entry in $entries) {
            if (-not (Test-ArchivePathName $entry.FullName)) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-path-invalid' }
            }
            if ($entry.FullName -cnotin $allowedNames) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-name-not-allowed' }
            }
            if (-not $seenNames.Add($entry.FullName)) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-name-duplicate' }
            }
            if ($entry.Length -le 0 -or $entry.Length -gt $maximumEntryBytes) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-size-out-of-range' }
            }

            $totalUncompressed += $entry.Length
            if ($totalUncompressed -gt $maximumTotalUncompressedBytes) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'archive-uncompressed-size-exceeded' }
            }
            if ($entry.CompressedLength -le 0) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-compressed-size-invalid' }
            }

            $ratio = [double] $entry.Length / [double] $entry.CompressedLength
            if ($ratio -gt $maximumCompressionRatio) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-compression-ratio-exceeded' }
            }

            try {
                $entryBytes[$entry.FullName] = Read-ZipEntryBytes $entry
            }
            catch {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'entry-stream-invalid' }
            }
        }

        try {
            $manifest = ConvertFrom-StrictUtf8Json $entryBytes[$manifestEntryName]
            $handoff = ConvertFrom-StrictUtf8Json $entryBytes[$handoffEntryName]
        }
        catch {
            return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'json-invalid' }
        }

        if (-not (Test-JsonObject $manifest) -or
            -not (Test-RequiredString $manifest 'schema') -or
            -not [string]::Equals([string] $manifest['schema'], 'devflow-ci-failure-manifest', [StringComparison]::Ordinal) -or
            -not $manifest.Contains('version') -or
            -not (Test-JsonIntegerRange $manifest['version'] 1 1) -or
            [Int64] $manifest['version'] -ne 1 -or
            -not $manifest.Contains('entries') -or
            -not (Test-JsonArray $manifest['entries'])) {
            return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'manifest-schema-invalid' }
        }

        $declarations = [System.Array] $manifest['entries']
        if ($declarations.Count -ne 1 -or -not (Test-JsonObject $declarations[0])) {
            return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'manifest-declarations-invalid' }
        }

        $declaration = $declarations[0]
        if (-not (Test-RequiredString $declaration 'name') -or
            -not [string]::Equals([string] $declaration['name'], $handoffEntryName, [StringComparison]::Ordinal) -or
            -not (Test-RequiredString $declaration 'sha256') -or
            [string] $declaration['sha256'] -cnotmatch '^sha256:[0-9a-f]{64}$' -or
            -not $declaration.Contains('sizeBytes') -or
            -not (Test-JsonIntegerRange $declaration['sizeBytes'] 1 $maximumEntryBytes) -or
            [Int64] $declaration['sizeBytes'] -ne $entryBytes[$handoffEntryName].LongLength -or
            -not [string]::Equals(
                [string] $declaration['sha256'],
                (Get-Sha256Bytes $entryBytes[$handoffEntryName]),
                [StringComparison]::Ordinal)) {
            return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'handoff-declaration-invalid' }
        }

        if (-not (Test-JsonObject $handoff) -or
            -not (Test-RequiredString $handoff 'schema') -or
            -not [string]::Equals([string] $handoff['schema'], 'devflow-ci-failure-handoff', [StringComparison]::Ordinal) -or
            -not $handoff.Contains('version') -or
            -not (Test-JsonIntegerRange $handoff['version'] 1 1) -or
            [Int64] $handoff['version'] -ne 1 -or
            -not $handoff.Contains('provenance') -or
            -not (Test-JsonObject $handoff['provenance'])) {
            return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'handoff-schema-invalid' }
        }

        $provenance = $handoff['provenance']
        $provenanceValid =
            (Test-RequiredString $provenance 'repository') -and
            [string]::Equals([string] $provenance['repository'], $Repository, [StringComparison]::Ordinal) -and
            (Test-RequiredString $provenance 'workflowName') -and
            [string]::Equals([string] $provenance['workflowName'], $WorkflowName, [StringComparison]::Ordinal) -and
            (Test-RequiredString $provenance 'workflowPath') -and
            [string]::Equals([string] $provenance['workflowPath'], $WorkflowPath, [StringComparison]::Ordinal) -and
            (Test-RequiredString $provenance 'sourceEvent') -and
            [string]::Equals([string] $provenance['sourceEvent'], $SourceEvent, [StringComparison]::Ordinal) -and
            (Test-RequiredString $provenance 'headRepository') -and
            [string]::Equals([string] $provenance['headRepository'], $HeadRepository, [StringComparison]::Ordinal) -and
            (Test-RequiredString $provenance 'headRefSha256') -and
            [string]::Equals(
                [string] $provenance['headRefSha256'],
                (Get-Sha256Text $HeadRef),
                [StringComparison]::Ordinal) -and
            $provenance.Contains('runId') -and
            (Test-JsonIntegerRange $provenance['runId'] 1 $maximumJsonSafeInteger) -and
            [Int64] $provenance['runId'] -eq $RunId -and
            $provenance.Contains('runAttempt') -and
            (Test-JsonIntegerRange $provenance['runAttempt'] 1 $maximumRunAttempt) -and
            [Int32] $provenance['runAttempt'] -eq $RunAttempt -and
            (Test-RequiredString $provenance 'commitSha') -and
            [string]::Equals([string] $provenance['commitSha'], $CommitSha, [StringComparison]::Ordinal) -and
            $provenance.Contains('pullRequestNumber') -and
            (Test-JsonIntegerRange $provenance['pullRequestNumber'] 0 ([Int32]::MaxValue)) -and
            [Int32] $provenance['pullRequestNumber'] -eq $PullRequestNumber
        if (-not $provenanceValid) {
            return [ordered]@{ ok = $false; kind = 'unverifiable'; reason = 'provenance-mismatch' }
        }

        $requiredStrings = @(
            'outcome',
            'qualification',
            'category',
            'platform',
            'testIdentitySha256',
            'evidenceSufficiency'
        )
        foreach ($field in $requiredStrings) {
            if (-not (Test-RequiredString $handoff $field)) {
                return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'handoff-field-missing' }
            }
        }

        if ([string] $handoff['outcome'] -cnotin @('failure', 'pass', 'pending') -or
            [string] $handoff['qualification'] -cnotin @('qualified', 'not-qualified', 'pending') -or
            [string] $handoff['category'] -cnotin @(
                'test-failure',
                'app-crash',
                'timeout',
                'device-failure',
                'harness-failure',
                'infrastructure',
                'unknown') -or
            [string] $handoff['platform'] -cnotin @(
                'android',
                'ios',
                'maccatalyst',
                'macos',
                'windows',
                'cross-platform',
                'unknown') -or
            [string] $handoff['testIdentitySha256'] -cnotmatch '^sha256:[0-9a-f]{64}$' -or
            [string] $handoff['evidenceSufficiency'] -cnotin @('sufficient', 'partial', 'insufficient')) {
            return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'handoff-enum-invalid' }
        }

        $archiveHash = "sha256:$((Get-FileHash -LiteralPath $archiveFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
        return [ordered]@{
            ok = $true
            handoff = $handoff
            archiveSha256 = $archiveHash
            handoffSha256 = [string] $declaration['sha256']
        }
    }
    catch {
        return [ordered]@{ ok = $false; kind = 'malformed'; reason = 'archive-unreadable' }
    }
    finally {
        if ($null -ne $archive) {
            $archive.Dispose()
        }
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Get-PublicationDisposition {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Handoff)

    if ([string] $Handoff['outcome'] -eq 'pass') {
        return [ordered]@{ publish = $false; status = 'ignored-pass'; reason = 'artifact-outcome-pass' }
    }
    if ([string] $Handoff['outcome'] -eq 'pending' -or [string] $Handoff['qualification'] -eq 'pending') {
        return [ordered]@{ publish = $false; status = 'ignored-pending'; reason = 'artifact-pending' }
    }
    if ([string] $Handoff['qualification'] -eq 'not-qualified' -or
        [string] $Handoff['evidenceSufficiency'] -eq 'insufficient') {
        return [ordered]@{ publish = $false; status = 'ignored-not-qualified'; reason = 'artifact-not-qualified' }
    }
    if ([string] $Handoff['outcome'] -ne 'failure' -or [string] $Handoff['qualification'] -ne 'qualified') {
        return [ordered]@{ publish = $false; status = 'ignored-unverifiable'; reason = 'artifact-disposition-inconsistent' }
    }
    if ($WorkflowConclusion -notin @('failure', 'timed_out')) {
        return [ordered]@{ publish = $false; status = 'ignored-unverifiable'; reason = 'trusted-run-not-failed' }
    }

    return [ordered]@{ publish = $true; status = 'qualified'; reason = 'qualified-failure' }
}

function Get-Fingerprint {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Handoff)

    $identity = @(
        $Repository,
        $WorkflowPath,
        [string] $Handoff['category'],
        [string] $Handoff['platform'],
        [string] $Handoff['testIdentitySha256']
    ) -join "`n"
    return Get-Sha256Text $identity
}

function Get-RunUrl {
    return "https://github.com/$Repository/actions/runs/$RunId/attempts/$RunAttempt"
}

function Get-OccurrenceMarker {
    return "<!-- devflow-ci-failure-occurrence:v1 run=$RunId attempt=$RunAttempt -->"
}

function Get-IssueDataMarker {
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Handoff)

    return "<!-- devflow-ci-failure-data:v1 category=$($Handoff['category']) platform=$($Handoff['platform']) testIdentity=$($Handoff['testIdentitySha256']) evidence=$($Handoff['evidenceSufficiency']) -->"
}

function New-IssueBody {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Handoff,
        [Parameter(Mandatory)] [string] $Fingerprint,
        [Parameter(Mandatory)] [string] $ArchiveSha256,
        [Parameter(Mandatory)] [string] $HandoffSha256,
        [Parameter(Mandatory)] [Int64] $ArtifactId
    )

    $occurrence = Get-OccurrenceMarker
    $dataMarker = Get-IssueDataMarker $Handoff
    $runUrl = Get-RunUrl
    $artifactUrl = "https://github.com/$Repository/actions/runs/$RunId/artifacts/$ArtifactId"

    $payloadLines = @(
        $occurrence,
        $dataMarker,
        '',
        '## Verified handoff',
        '',
        'A qualified default-branch DevFlow Integration Tests failure recurred. The publisher read the ZIP without generally extracting it and matched its provenance to the trusted workflow-run API.',
        '',
        "- Run: [#$RunId attempt $RunAttempt]($runUrl)",
        "- Source event: ``$SourceEvent``",
        '- Pull request: none; PR-originated runs are diagnostic-only and are never published',
        "- Commit: ``$CommitSha``",
        "- Category: ``$($Handoff['category'])``",
        "- Platform: ``$($Handoff['platform'])``",
        "- Test identity: ``$($Handoff['testIdentitySha256'])``",
        '',
        '## Evidence',
        '',
        "- Sufficiency: ``$($Handoff['evidenceSufficiency'])``",
        "- Handoff entry: ``$HandoffSha256``",
        "- Downloaded ZIP: ``$ArchiveSha256``",
        "- Failure fingerprint: ``$Fingerprint``",
        '',
        'No raw test name, log text, stack trace, untrusted branch name, artifact filename, or model-authored text was copied into this issue.',
        '',
        '## Artifact handoff',
        '',
        "- Download: [retained workflow artifact]($artifactUrl)",
        '- Retention: the artifact was unexpired when verified; the producer contract expects 30-day retention.',
        '- Contents: exactly `manifest.json` and `handoff.json`; do not generally extract the ZIP.',
        '',
        '## Local handoff',
        '',
        'Download the ZIP to a trusted local checkout, then verify it before using it:',
        '',
        '```powershell',
        "pwsh ./eng/devflow/Publish-DevFlowFailureIssue.ps1 -VerifyOnly -ArchivePath ./devflow-failure-handoff.zip -Repository '$Repository' -WorkflowName '$WorkflowName' -WorkflowPath '$WorkflowPath' -SourceEvent '$SourceEvent' -HeadRepository '$HeadRepository' -HeadRef '$HeadRef' -DefaultBranch '$DefaultBranch' -WorkflowConclusion '$WorkflowConclusion' -RunId $RunId -RunAttempt $RunAttempt -CommitSha '$CommitSha' -PullRequestNumber $PullRequestNumber",
        '```',
        '',
        'After verification, map the test-identity digest to the committed flow that produced it:',
        '',
        '```powershell',
        "maui devflow flow identity --resolve $($Handoff['testIdentitySha256']) --platform $($Handoff['platform'])",
        '```',
        '',
        'Run it from a trusted checkout of the commit above; `matched-superseded` means the flow was edited since this run. To have an agent triage this issue, assign it to Copilot and select the `devflow-ci-repair` agent. That agent proposes a reviewable repair and cannot run the test itself, so validate on a real device before closing. This issue is a handoff, not repair authority.'
    )

    $payload = $payloadLines -join "`n"
    $bodyDigest = Get-Sha256Text $payload
    $marker = "<!-- devflow-ci-failure:v1 fingerprint=$Fingerprint body=$bodyDigest -->"
    return "$marker`n$payload"
}

function New-RecurrenceComment {
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Handoff,
        [Parameter(Mandatory)] [string] $ArchiveSha256,
        [Parameter(Mandatory)] [string] $HandoffSha256,
        [Parameter(Mandatory)] [Int64] $ArtifactId
    )

    $runUrl = Get-RunUrl
    $artifactUrl = "https://github.com/$Repository/actions/runs/$RunId/artifacts/$ArtifactId"
    $payloadLines = @(
        '',
        '## Recurrence',
        '',
        "- Run: [#$RunId attempt $RunAttempt]($runUrl)",
        "- Commit: ``$CommitSha``",
        "- Category/platform: ``$($Handoff['category'])`` / ``$($Handoff['platform'])``",
        "- Test identity: ``$($Handoff['testIdentitySha256'])``",
        "- Evidence sufficiency: ``$($Handoff['evidenceSufficiency'])``",
        "- Artifact: [download]($artifactUrl)",
        "- Handoff entry: ``$HandoffSha256``",
        "- Downloaded ZIP: ``$ArchiveSha256``",
        '',
        'Verify the retained ZIP with the local handoff command in the issue body before using it. This recurrence grants no repair authority.'
    )

    $payload = $payloadLines -join "`n"
    $bodyDigest = Get-Sha256Text $payload
    $marker = "<!-- devflow-ci-failure-occurrence:v1 run=$RunId attempt=$RunAttempt body=$bodyDigest -->"
    return "$marker`n$payload"
}

function Test-PublisherBotAuthor {
    param([Parameter(Mandatory)] $Item)

    return $null -ne $Item.user -and
        [string]::Equals([string] $Item.user.login, $publisherBotLogin, [StringComparison]::Ordinal) -and
        [string]::Equals([string] $Item.user.type, 'Bot', [StringComparison]::Ordinal)
}

function Test-DedicatedIssueLabel {
    param([Parameter(Mandatory)] $Issue)

    if ($Issue.labels -isnot [System.Array]) {
        return $false
    }
    foreach ($label in [System.Array] $Issue.labels) {
        $name = if ($label -is [string]) { $label } else { [string] $label.name }
        if ([string]::Equals($name, $issueLabel, [StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

function Get-TrustedIssueBody {
    param(
        [Parameter(Mandatory)] $Issue,
        [Parameter(Mandatory)] [string] $Fingerprint
    )

    if ($Issue.PSObject.Properties.Name -contains 'pull_request' -or
        -not (Test-PublisherBotAuthor $Issue) -or
        -not (Test-DedicatedIssueLabel $Issue)) {
        return [ordered]@{ trusted = $false; reason = 'issue-identity-untrusted' }
    }

    $body = [string] $Issue.body
    if ([string]::IsNullOrWhiteSpace($body) -or $body.Length -gt 65000 -or
        [regex]::Matches($body, '<!-- devflow-ci-failure:v1 ').Count -ne 1) {
        return [ordered]@{ trusted = $false; reason = 'issue-marker-invalid' }
    }

    $markerMatch = [regex]::Match(
        $body,
        '\A<!-- devflow-ci-failure:v1 fingerprint=(sha256:[0-9a-f]{64}) body=(sha256:[0-9a-f]{64}) -->\n',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $markerMatch.Success -or
        -not [string]::Equals($markerMatch.Groups[1].Value, $Fingerprint, [StringComparison]::Ordinal)) {
        return [ordered]@{ trusted = $false; reason = 'issue-marker-invalid' }
    }

    $payload = $body.Substring($markerMatch.Length)
    if (-not [string]::Equals(
            (Get-Sha256Text $payload),
            $markerMatch.Groups[2].Value,
            [StringComparison]::Ordinal)) {
        return [ordered]@{ trusted = $false; reason = 'issue-body-digest-mismatch' }
    }

    $occurrenceMatches = [regex]::Matches(
        $payload,
        '(?m)^<!-- devflow-ci-failure-occurrence:v1 run=[1-9][0-9]* attempt=[1-9][0-9]* -->$')
    $dataMatches = [regex]::Matches(
        $payload,
        '(?m)^<!-- devflow-ci-failure-data:v1 category=(test-failure|app-crash|timeout|device-failure|harness-failure|infrastructure|unknown) platform=(android|ios|maccatalyst|macos|windows|cross-platform|unknown) testIdentity=(sha256:[0-9a-f]{64}) evidence=(sufficient|partial) -->$')
    if ($occurrenceMatches.Count -ne 1 -or $dataMatches.Count -ne 1 -or
        -not $payload.StartsWith("$($occurrenceMatches[0].Value)`n$($dataMatches[0].Value)`n`n## Verified handoff`n", [StringComparison]::Ordinal)) {
        return [ordered]@{ trusted = $false; reason = 'issue-template-invalid' }
    }

    $headings = @('## Verified handoff', '## Evidence', '## Artifact handoff', '## Local handoff')
    $previousIndex = -1
    foreach ($heading in $headings) {
        if ([regex]::Matches($payload, "(?m)^$([regex]::Escape($heading))$").Count -ne 1) {
            return [ordered]@{ trusted = $false; reason = 'issue-template-invalid' }
        }
        $index = $payload.IndexOf($heading, [StringComparison]::Ordinal)
        if ($index -le $previousIndex) {
            return [ordered]@{ trusted = $false; reason = 'issue-template-invalid' }
        }
        $previousIndex = $index
    }

    $category = $dataMatches[0].Groups[1].Value
    $platform = $dataMatches[0].Groups[2].Value
    $testIdentity = $dataMatches[0].Groups[3].Value
    $evidence = $dataMatches[0].Groups[4].Value
    $expectedTitle = "[DevFlow CI] $category on $platform ($($testIdentity.Substring(7, 12)))"
    if (-not [string]::Equals([string] $Issue.title, $expectedTitle, [StringComparison]::Ordinal)) {
        return [ordered]@{ trusted = $false; reason = 'issue-title-invalid' }
    }

    return [ordered]@{
        trusted = $true
        category = $category
        platform = $platform
        testIdentity = $testIdentity
        evidence = $evidence
    }
}

function Find-FingerprintIssue {
    param([Parameter(Mandatory)] [string] $Fingerprint)

    $matches = [System.Collections.Generic.List[object]]::new()
    $untrustedMatch = $false
    $encodedLabel = [Uri]::EscapeDataString($issueLabel)
    $fingerprintPattern = "<!-- devflow-ci-failure:v1 fingerprint=$([regex]::Escape($Fingerprint))(?:\s|-->)"
    $page = 1
    while ($true) {
        $issues = Invoke-GitHubJson -Method GET -Path "/repos/$Repository/issues?state=all&labels=$encodedLabel&sort=updated&direction=desc&per_page=100&page=$page"
        if ($issues -isnot [System.Array]) {
            return [ordered]@{ status = 'untrusted' }
        }
        foreach ($issue in $issues) {
            $body = [string] $issue.body
            if (-not [regex]::IsMatch($body, $fingerprintPattern)) {
                continue
            }

            $trust = Get-TrustedIssueBody -Issue $issue -Fingerprint $Fingerprint
            if (-not $trust['trusted']) {
                $untrustedMatch = $true
                continue
            }
            $matches.Add($issue)
        }
        if ($issues.Count -lt 100) {
            break
        }
        $page++
    }

    if ($untrustedMatch) {
        return [ordered]@{ status = 'untrusted' }
    }
    if ($matches.Count -gt 1) {
        return [ordered]@{ status = 'ambiguous' }
    }
    if ($matches.Count -eq 1) {
        return [ordered]@{ status = 'found'; issue = $matches[0] }
    }

    return [ordered]@{ status = 'none' }
}

function Test-TrustedRecurrenceComment {
    param(
        [Parameter(Mandatory)] $Comment
    )

    if (-not (Test-PublisherBotAuthor $Comment)) {
        return $false
    }

    $body = [string] $Comment.body
    if ([regex]::Matches($body, '<!-- devflow-ci-failure-occurrence:v1 ').Count -ne 1) {
        return $false
    }
    $match = [regex]::Match(
        $body,
        '\A<!-- devflow-ci-failure-occurrence:v1 run=([1-9][0-9]*) attempt=([1-9][0-9]*) body=(sha256:[0-9a-f]{64}) -->\n',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success -or
        [Int64] $match.Groups[1].Value -ne $RunId -or
        [Int32] $match.Groups[2].Value -ne $RunAttempt) {
        return $false
    }

    $payload = $body.Substring($match.Length)
    return $payload.StartsWith("`n## Recurrence`n", [StringComparison]::Ordinal) -and
        [string]::Equals((Get-Sha256Text $payload), $match.Groups[3].Value, [StringComparison]::Ordinal)
}

function Get-OccurrencePublicationState {
    param([Parameter(Mandatory)] $Issue)

    $occurrenceMarker = Get-OccurrenceMarker
    if (([string] $Issue.body).IndexOf($occurrenceMarker, [StringComparison]::Ordinal) -ge 0) {
        return 'found'
    }

    $claimPrefix = "<!-- devflow-ci-failure-occurrence:v1 run=$RunId attempt=$RunAttempt"
    $page = 1
    while ($true) {
        $comments = Invoke-GitHubJson -Method GET -Path "/repos/$Repository/issues/$($Issue.number)/comments?per_page=100&page=$page"
        if ($comments -isnot [System.Array]) {
            return 'untrusted'
        }
        foreach ($comment in $comments) {
            $body = [string] $comment.body
            if ($body.IndexOf($claimPrefix, [StringComparison]::Ordinal) -lt 0) {
                continue
            }
            if (Test-TrustedRecurrenceComment $comment) {
                return 'found'
            }
            return 'untrusted'
        }
        if ($comments.Count -lt 100) {
            break
        }
        $page++
    }

    return 'missing'
}

$downloadedArchive = $false
$resolvedArchivePath = $ArchivePath
try {
    $trustedInputError = Test-TrustedInputs
    if ($trustedInputError) {
        Write-PublisherResult (New-PublisherResult -Status 'ignored-unverifiable' -Reason $trustedInputError)
        return
    }

    $publicationTrustError = Test-PublicationTrust
    if (-not $VerifyOnly -and $publicationTrustError) {
        Write-PublisherResult (New-PublisherResult -Status 'ignored-untrusted-source' -Reason $publicationTrustError)
        return
    }

    $artifactId = [Int64] 0
    if (-not $VerifyOnly) {
        if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
            Write-PublisherResult (New-PublisherResult -Status 'ignored-unverifiable' -Reason 'github-token-missing')
            return
        }

        $repositoryMetadata = Invoke-GitHubJson -Method GET -Path "/repos/$Repository"
        $repositoryMetadataResult = Test-RepositoryMetadata $repositoryMetadata
        if (-not $repositoryMetadataResult['ok']) {
            Write-PublisherResult (New-PublisherResult -Status 'ignored-unverifiable' -Reason ([string] $repositoryMetadataResult['reason']))
            return
        }
        if (-not $repositoryMetadataResult['issuesEnabled']) {
            Write-PublisherResult (New-PublisherResult -Status 'ignored-issues-disabled' -Reason 'repository-issues-disabled')
            return
        }

        $apiRun = Invoke-GitHubJson -Method GET -Path "/repos/$Repository/actions/runs/$RunId"
        $apiRunError = Test-ApiRun $apiRun
        if ($apiRunError) {
            Write-PublisherResult (New-PublisherResult -Status 'ignored-unverifiable' -Reason $apiRunError)
            return
        }

        $artifactResult = Get-ExpectedArtifact
        switch ($artifactResult['status']) {
            'missing' {
                Write-PublisherResult (New-PublisherResult -Status 'ignored-no-artifact' -Reason 'expected-artifact-missing')
                return
            }
            'found' {
                break
            }
            default {
                Write-PublisherResult (New-PublisherResult -Status 'ignored-unverifiable' -Reason "artifact-$($artifactResult['status'])")
                return
            }
        }

        $artifact = $artifactResult['artifact']
        $artifactId = [Int64] $artifact.id
        if ([string]::IsNullOrWhiteSpace($resolvedArchivePath)) {
            $resolvedArchivePath = Join-Path (Get-Location) ".devflow-failure-handoff-$RunId-$RunAttempt.zip"
        }
        Save-GitHubArtifactArchive -ArtifactId $artifactId -DestinationPath $resolvedArchivePath
        $downloadedArchive = $true
    }
    elseif ([string]::IsNullOrWhiteSpace($resolvedArchivePath)) {
        Write-PublisherResult (New-PublisherResult -Status 'ignored-malformed' -Reason 'archive-path-required')
        return
    }

    $verification = Test-HandoffArchive $resolvedArchivePath
    if (-not $verification['ok']) {
        $status = if ($verification['kind'] -eq 'unverifiable') { 'ignored-unverifiable' } else { 'ignored-malformed' }
        Write-PublisherResult (New-PublisherResult -Status $status -Reason ([string] $verification['reason']))
        return
    }

    $handoff = $verification['handoff']
    $disposition = Get-PublicationDisposition $handoff
    if (-not $disposition['publish']) {
        Write-PublisherResult (New-PublisherResult -Status ([string] $disposition['status']) -Reason ([string] $disposition['reason']))
        return
    }

    $fingerprint = Get-Fingerprint $handoff
    if ($VerifyOnly) {
        $status = if ($publicationTrustError) { 'verified-diagnostic-only' } else { 'verified' }
        $reason = if ($publicationTrustError) { $publicationTrustError } else { 'qualified-failure' }
        Write-PublisherResult (New-PublisherResult -Status $status -Reason $reason -Fingerprint $fingerprint)
        return
    }

    Ensure-DedicatedIssueLabel
    $issueResult = Find-FingerprintIssue $fingerprint
    if ($issueResult['status'] -in @('untrusted', 'ambiguous')) {
        Write-PublisherResult (New-PublisherResult `
                -Status 'ignored-unverifiable' `
                -Reason "issue-match-$($issueResult['status'])" `
                -Fingerprint $fingerprint)
        return
    }

    if ($issueResult['status'] -eq 'none') {
        $testDigestPrefix = ([string] $handoff['testIdentitySha256']).Substring(7, 12)
        $title = "[DevFlow CI] $($handoff['category']) on $($handoff['platform']) ($testDigestPrefix)"
        $body = New-IssueBody `
            -Handoff $handoff `
            -Fingerprint $fingerprint `
            -ArchiveSha256 ([string] $verification['archiveSha256']) `
            -HandoffSha256 ([string] $verification['handoffSha256']) `
            -ArtifactId $artifactId
        $created = Invoke-GitHubJson -Method POST -Path "/repos/$Repository/issues" -Body @{
            title = $title
            body = $body
            labels = @($issueLabel)
        }
        Write-PublisherResult (New-PublisherResult `
                -Status 'created' `
                -Reason 'qualified-failure-created' `
                -Fingerprint $fingerprint `
                -IssueNumber ([Int32] $created.number))
        return
    }

    $existingIssue = $issueResult['issue']
    $occurrenceState = Get-OccurrencePublicationState $existingIssue
    if ($occurrenceState -eq 'untrusted') {
        Write-PublisherResult (New-PublisherResult `
                -Status 'ignored-unverifiable' `
                -Reason 'occurrence-marker-untrusted' `
                -Fingerprint $fingerprint `
                -IssueNumber ([Int32] $existingIssue.number))
        return
    }
    if ($occurrenceState -eq 'found') {
        Write-PublisherResult (New-PublisherResult `
                -Status 'already-published' `
                -Reason 'run-attempt-already-recorded' `
                -Fingerprint $fingerprint `
                -IssueNumber ([Int32] $existingIssue.number))
        return
    }

    $reopened = $false
    if ([string]::Equals([string] $existingIssue.state, 'closed', [StringComparison]::Ordinal)) {
        [void] (Invoke-GitHubJson -Method PATCH -Path "/repos/$Repository/issues/$($existingIssue.number)" -Body @{
                state = 'open'
            })
        $reopened = $true
    }

    $commentBody = New-RecurrenceComment `
        -Handoff $handoff `
        -ArchiveSha256 ([string] $verification['archiveSha256']) `
        -HandoffSha256 ([string] $verification['handoffSha256']) `
        -ArtifactId $artifactId
    [void] (Invoke-GitHubJson -Method POST -Path "/repos/$Repository/issues/$($existingIssue.number)/comments" -Body @{
            body = $commentBody
        })

    Write-PublisherResult (New-PublisherResult `
            -Status $(if ($reopened) { 'reopened-and-commented' } else { 'commented' }) `
            -Reason 'qualified-failure-recurrence' `
            -Fingerprint $fingerprint `
            -IssueNumber ([Int32] $existingIssue.number))
}
catch {
    $exceptionMessage = [string] $_.Exception.Message
    $reason = switch ($exceptionMessage) {
        'github-api-unauthorized' { 'github-api-unauthorized'; break }
        'github-api-label-invalid' { 'github-api-label-invalid'; break }
        default {
            if ($exceptionMessage -match '^github-api-http-[0-9]{3}$') {
                $exceptionMessage
            }
            else {
                'unexpected-operational-error'
            }
        }
    }
    Write-PublisherResult (New-PublisherResult -Status 'publisher-error' -Reason $reason)
    exit 1
}
finally {
    if ($downloadedArchive -and
        -not [string]::IsNullOrWhiteSpace($resolvedArchivePath) -and
        (Test-Path -LiteralPath $resolvedArchivePath -PathType Leaf)) {
        Remove-Item -LiteralPath $resolvedArchivePath -Force
    }
}
