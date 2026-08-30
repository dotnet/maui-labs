#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ManifestPath,

    [Parameter(Mandatory)]
    [string] $RepositoryRoot,

    [Parameter(Mandatory)]
    [string[]] $ArtifactRoots,

    [string] $Platform = 'android',
    [string] $RepositoryCommit = $env:DEVFLOW_FLOW_PILOT_REPOSITORY_COMMIT,
    [string] $WorkflowRunId = $env:DEVFLOW_FLOW_PILOT_WORKFLOW_RUN_ID,
    [string] $TestingPackageVersion = 'unknown',
    [string] $PackageId = 'com.companyname.mauitodo',
    [string] $AndroidApiLevel = $env:DEVFLOW_TEST_ANDROID_API,
    [string] $AndroidAvdName = $env:DEVFLOW_TEST_ANDROID_AVD,
    # The accepted kinds are exactly the ones the manifest producer and its consumers agree on.
    # 'unknown' was accepted here and rejected there, so a finalized manifest could carry a device
    # evidence kind that no consumer would credit.
    [ValidateSet('physical-device', 'real-device', 'emulator', 'desktop-host')]
    [string] $DeviceEvidenceKind = 'emulator',
    [switch] $RealDevice
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$maxArtifacts = 256
# A run id is published into a consumer's index and read back as a directory name, so it is bounded
# and restricted to a plain identifier here rather than wherever it happens to be consumed.
$maxRunIdLength = 96
# A discovered report is read only to learn the run it names. The scan is bounded in both
# directions - how many reports it opens and how large one may be - so an artifact tree cannot make
# finalization unbounded work.
$maxReportBytes = 1048576
$maxReportsRead = 64
# The omissions and counters this script derives from what it can observe right now. A manifest
# that is finalized a second time must restate them from this pass rather than add to the previous
# pass's numbers, which would report an artifact as omitted once per finalization. Every one it
# writes is stamped, because the test process publishes some of the same kinds and those describe
# references this script can never rediscover - dropping them by kind would erase them.
$finalizerOmissionSource = 'finalizer'

function New-FallbackManifest {
    param([string] $Reason)

    return [ordered]@{
        schema = 1
        kind = 'devflow-flow-pilot'
        generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
        repository = [ordered]@{ commit = $(if ($RepositoryCommit) { $RepositoryCommit } else { 'unknown' }) }
        workflow = [ordered]@{ runId = $(if ($WorkflowRunId) { $WorkflowRunId } else { 'unknown' }) }
        testing = [ordered]@{ packageVersion = $TestingPackageVersion }
        platform = [ordered]@{
            name = $Platform
            deviceEvidence = [ordered]@{
                kind = $DeviceEvidenceKind
                realDevice = ($RealDevice -and $DeviceEvidenceKind -in @('physical-device', 'real-device'))
            }
            androidSdk = [ordered]@{
                apiLevel = $(if ($AndroidApiLevel) { $AndroidApiLevel } else { 'unknown' })
                avdName = $(if ($AndroidAvdName) { $AndroidAvdName } else { 'unknown' })
            }
        }
        app = [ordered]@{ packageId = $PackageId }
        flows = @()
        artifacts = @()
        privacy = [ordered]@{
            excludedByDefault = @('screenshots', 'source', 'raw-model-context')
        }
        truncated = $false
        truncation = [ordered]@{ maxArtifacts = $maxArtifacts; omittedArtifacts = 0 }
        omissions = @(
            [ordered]@{
                kind = 'pilot-manifest'
                reason = $Reason
            }
        )
        validationErrors = @($Reason)
    }
}

function Get-CanonicalPath {
    param([Parameter(Mandatory)][string] $Path)

    [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]] @('\', '/'))
}

function Get-RepositoryRelativePath {
    param([string] $Path)

    return [System.IO.Path]::GetRelativePath($RepositoryRoot, $Path) -replace '\\', '/'
}

# A path on another volume, a UNC share, or outside the repository yields a rooted relative path
# rather than a '../' one, so a rooted result is refused as firmly as an escaping one. Nothing
# absolute may ever reach artifacts[].path: a consumer resolves those entries against its own
# checkout and would otherwise be handed a machine-local location it cannot verify.
function Test-PathInsideRepository {
    param([Parameter(Mandatory)][string] $Path)

    $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, (Get-CanonicalPath $Path))
    return -not (
        [string]::IsNullOrEmpty($relative) -or
        $relative -ceq '..' -or
        $relative.StartsWith('..' + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::Ordinal) -or
        $relative.StartsWith('../', [System.StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($relative))
}

# A symbolic link or junction inside the repository can point anywhere, so a root reached through
# one is refused before any file under it is hashed.
function Test-PathTraversesReparsePoint {
    param([Parameter(Mandatory)][string] $Path)

    $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, (Get-CanonicalPath $Path))
    $current = Get-CanonicalPath $RepositoryRoot
    foreach ($segment in ($relative -split '[\\/]')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -ceq '.') {
            continue
        }

        $current = Join-Path $current $segment
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $true
            }
        }
    }

    return $false
}

function Get-ArtifactKind {
    param([System.IO.FileInfo] $File)

    if ($File.Name -ceq 'flow-run.json') {
        return 'flow-run-report'
    }
    switch ($File.Extension.ToLowerInvariant()) {
        '.trx' { return 'test-results' }
        '.mauitrace' { return 'mauitrace' }
        '.json' { return 'json' }
        default { return 'host-diagnostic' }
    }
}

function Get-ArtifactMediaType {
    param([string] $Kind, [System.IO.FileInfo] $File)

    switch ($Kind) {
        'flow-run-report' { return 'application/json' }
        'mauitrace' { return 'application/vnd.maui.evidence+zip' }
        'json' { return 'application/json' }
        default { return $null }
    }
}

# A run id is republished as evidence and is read back by consumers that key directories and index
# entries off it. It is taken from a directory name and from a report this script did not write, so
# it is accepted only when it is a bounded, plain identifier. An unbounded or path-shaped value is
# refused rather than sanitized into something that no longer names the run it came from.
function Test-PublishableRunId {
    param([AllowNull()][string] $RunId)

    if ([string]::IsNullOrWhiteSpace($RunId) -or $RunId.Length -gt $maxRunIdLength) {
        return $false
    }
    if ($RunId -ceq '.' -or $RunId -ceq '..') {
        return $false
    }

    # '\z', not '$': .NET's '$' also matches before a trailing newline, so "run-1`n" would have been
    # accepted here and refused by the C# producer, which is exactly the disagreement this shared
    # rule exists to prevent.
    return $RunId -cmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\z'
}

# The digest and the size describe one read of one file. Taking the size from a directory entry
# captured earlier let a file that was rewritten in between be published with a hash of its new
# bytes and the length of its old ones - a pair no consumer can reproduce, which disqualifies the
# whole manifest. The size published here is the number of bytes that were actually hashed.
function Get-FileDigestAndSize {
    param([Parameter(Mandatory)][string] $Path)

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

# The run a piece of evidence belongs to is the first directory below the artifact root - but only
# when that directory is a run at all. The shipped layout also puts run-independent evidence in
# fixed directories directly below the root, so "host-diagnostics/summary.json" was credited to a
# run named "host-diagnostics" that no attempt in the manifest matches. A consumer that must tie a
# report and its trace to one attempt refuses a manifest whose runId names nothing, so the segment
# is published only when it matches a run this manifest, or a discovered report, actually recorded.
function Get-ArtifactRunId {
    param(
        [System.IO.FileInfo] $File,
        [string] $ArtifactRoot,
        [AllowNull()] $KnownRunIds)

    $relative = [System.IO.Path]::GetRelativePath(
        (Get-CanonicalPath $ArtifactRoot),
        [System.IO.Path]::GetFullPath($File.FullName))
    if ($relative.StartsWith('..', [System.StringComparison]::Ordinal) -or
        [System.IO.Path]::IsPathRooted($relative)) {
        return $null
    }

    $segments = @($relative -split '[\\/]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -cne '.' })
    if ($segments.Count -le 1) {
        return $null
    }

    if (-not (Test-PublishableRunId $segments[0])) {
        return $null
    }

    if ($null -eq $KnownRunIds -or -not $KnownRunIds.Contains($segments[0])) {
        return $null
    }

    return $segments[0]
}

# The run ids this manifest already recorded for its own attempts. They are the only names a
# directory segment may be credited against.
function Get-ManifestRunIds {
    param([AllowNull()] $Manifest)

    $runIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    if ($Manifest -isnot [System.Collections.IDictionary] -or $null -eq $Manifest['flows']) {
        # Returned as a single object: PowerShell unrolls an enumerable on output, so a set with
        # no members would otherwise reach the caller as $null.
        return , $runIds
    }

    foreach ($flow in @($Manifest['flows'])) {
        if ($flow -isnot [System.Collections.IDictionary]) {
            continue
        }

        $attempts = [System.Collections.Generic.List[object]]::new()
        if ($flow['firstAttempt'] -is [System.Collections.IDictionary]) {
            $attempts.Add($flow['firstAttempt'])
        }
        foreach ($group in @('cleanAttempts', 'diagnosticReruns')) {
            if ($null -ne $flow[$group]) {
                foreach ($attempt in @($flow[$group])) {
                    if ($attempt -is [System.Collections.IDictionary]) {
                        $attempts.Add($attempt)
                    }
                }
            }
        }

        foreach ($attempt in $attempts) {
            $runId = [string] $attempt['runId']
            if (Test-PublishableRunId $runId) {
                [void] $runIds.Add($runId)
            }
        }
    }

    return , $runIds
}

# A run report names its own run, so a directory that holds one is a run even when the manifest
# recorded no attempt for it - an infrastructure failure leaves exactly that shape. Only files this
# pass accepted are read: a report reached through a symbolic link resolves wherever its target
# points, so believing the run id inside it would let a link outside the repository decide how this
# manifest is indexed. The scan is bounded in both directions, and a value that is not a plain
# bounded identifier is refused rather than published.
function Add-ReportRunIds {
    param(
        [Parameter(Mandatory)] $RunIds,
        [Parameter(Mandatory)] $Files)

    $reportsRead = 0
    foreach ($entry in $Files) {
        if ($reportsRead -ge $maxReportsRead) {
            break
        }
        if ($entry.File.Name -cne 'flow-run.json' -or -not $entry.Eligible) {
            continue
        }
        if ($entry.File.Length -gt $maxReportBytes) {
            continue
        }

        $reportsRead++
        try {
            $report = Get-Content -LiteralPath $entry.File.FullName -Raw | ConvertFrom-Json -AsHashtable
        }
        catch {
            continue
        }

        # A file named flow-run.json is not necessarily a report this repository wrote. A top-level
        # array, or a hostQa that is not an object, throws on the reads below under strict mode and
        # would end finalization over a file that is merely odd.
        if ($report -isnot [System.Collections.IDictionary]) {
            continue
        }

        $candidates = [System.Collections.Generic.List[string]]::new()
        $candidates.Add([string] $report['runId'])
        if ($report['hostQa'] -is [System.Collections.IDictionary]) {
            $candidates.Add([string] $report['hostQa']['runId'])
        }

        foreach ($candidate in $candidates) {
            if (Test-PublishableRunId $candidate) {
                [void] $RunIds.Add($candidate)
            }
        }
    }
}

function New-ArtifactReference {
    param(
        [Parameter(Mandatory)][System.IO.FileInfo] $File,
        [Parameter(Mandatory)][string] $RelativePath,
        [Parameter(Mandatory)][string] $ArtifactRoot,
        [AllowNull()] $KnownRunIds)

    $measured = Get-FileDigestAndSize $File.FullName
    $kind = Get-ArtifactKind $File
    $reference = [ordered]@{
        kind = $kind
        path = $RelativePath
        sha256 = $measured.Sha256
        sizeBytes = $measured.SizeBytes
        redacted = $true
    }
    $mediaType = Get-ArtifactMediaType $kind $File
    if ($null -ne $mediaType) {
        $reference['mediaType'] = $mediaType
    }
    $runId = Get-ArtifactRunId $File $ArtifactRoot $KnownRunIds
    if (-not [string]::IsNullOrWhiteSpace($runId)) {
        $reference['runId'] = $runId
    }

    return $reference
}

# An inherited reference states two different kinds of fact. Whether the bytes were redacted, and
# what they are, is the producer's account of evidence this script never saw being written, so it
# is carried through untouched. The digest, the size, and the run the file belongs to are facts
# about the file as it is now, and this pass can read all three - so it does, rather than
# republishing what an earlier pass measured.
function Update-ArtifactReferenceFromLiveFile {
    param(
        [Parameter(Mandatory)] $Reference,
        [Parameter(Mandatory)][System.IO.FileInfo] $File,
        [Parameter(Mandatory)][string] $RelativePath,
        [Parameter(Mandatory)][string] $ArtifactRoot,
        [AllowNull()] $KnownRunIds)

    $measured = Get-FileDigestAndSize $File.FullName
    $declaredKind = [string] $Reference['kind']
    $kind = if ([string]::IsNullOrWhiteSpace($declaredKind)) { Get-ArtifactKind $File } else { $declaredKind }

    $updated = [ordered]@{
        kind = $kind
        path = $RelativePath
        sha256 = $measured.Sha256
        sizeBytes = $measured.SizeBytes
        redacted = $(if ($Reference['redacted'] -is [bool]) { [bool] $Reference['redacted'] } else { $true })
    }

    $declaredMediaType = [string] $Reference['mediaType']
    $mediaType = if ([string]::IsNullOrWhiteSpace($declaredMediaType)) {
        Get-ArtifactMediaType $kind $File
    }
    else {
        $declaredMediaType
    }
    if (-not [string]::IsNullOrWhiteSpace($mediaType)) {
        $updated['mediaType'] = $mediaType
    }

    # Refreshed, never inherited: a run id this pass cannot tie to a run the manifest or a
    # discovered report named is not one a consumer can resolve, so it is dropped rather than
    # carried forward on the producer's word.
    $runId = Get-ArtifactRunId $File $ArtifactRoot $KnownRunIds
    if (-not [string]::IsNullOrWhiteSpace($runId)) {
        $updated['runId'] = $runId
    }

    foreach ($key in @($Reference.Keys)) {
        if (-not $updated.Contains($key) -and $key -ine 'runId') {
            $updated[$key] = $Reference[$key]
        }
    }

    return $updated
}

$manifestFull = [System.IO.Path]::GetFullPath($ManifestPath)
$manifestDirectory = Split-Path -Parent $manifestFull
New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null

$manifest = $null
if (Test-Path -LiteralPath $manifestFull -PathType Leaf) {
    try {
        $manifest = Get-Content -LiteralPath $manifestFull -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        $manifest = New-FallbackManifest 'The test process wrote an unreadable flow-pilot manifest.'
    }
}
else {
    $manifest = New-FallbackManifest 'The test process did not produce a flow-pilot manifest.'
}

# The manifest on disk was written by another process, so its shape is an input rather than an
# assumption. Anything that is not the object this script needs to read is replaced with one it
# owns; indexing a scalar or an array with a name is a terminating error under strict mode, and
# ending finalization over a malformed field would replace the whole pilot manifest with a
# generic one.
if ($manifest -isnot [System.Collections.IDictionary]) {
    $manifest = New-FallbackManifest 'The test process wrote a flow-pilot manifest that is not an object.'
}
if ($null -eq $manifest['artifacts']) {
    $manifest['artifacts'] = @()
}
if ($null -eq $manifest['omissions']) {
    $manifest['omissions'] = @()
}
if ($manifest['truncation'] -isnot [System.Collections.IDictionary]) {
    $manifest['truncation'] = [ordered]@{ maxArtifacts = $maxArtifacts; omittedArtifacts = 0 }
}
if ($manifest['platform'] -isnot [System.Collections.IDictionary]) {
    $manifest['platform'] = [ordered]@{ name = $Platform }
}
if ($manifest['platform']['deviceEvidence'] -isnot [System.Collections.IDictionary]) {
    $manifest['platform']['deviceEvidence'] = [ordered]@{
        kind = $DeviceEvidenceKind
        realDevice = ($RealDevice -and $DeviceEvidenceKind -in @('physical-device', 'real-device'))
    }
}

# The same tree is finalized again on a rerun, and every omission and counter this script derives
# describes what it can observe right now. Adding this pass's numbers to the previous pass's would
# report one dropped reference twice and grow the omission list with every finalization, so the
# accounting this script owns is restated. Ownership is read from the stamp this script writes,
# never from the omission kind: the test process publishes 'artifact-limit' and 'artifact-path'
# omissions of its own about references that never reached disk, and this script cannot rediscover
# those, so dropping them by kind would delete evidence outright.
$previousFinalizerLimit = 0
$omissions = [System.Collections.Generic.List[object]]::new()
foreach ($omission in @($manifest['omissions'])) {
    if ($omission -isnot [System.Collections.IDictionary]) {
        continue
    }
    if (([string] $omission['source']) -ceq $finalizerOmissionSource) {
        if (([string] $omission['kind']) -ceq 'artifact-limit') {
            $previousFinalizerLimit += [int] $omission['omittedArtifacts']
        }

        continue
    }

    $omissions.Add($omission)
}

# Only this script's own earlier contribution is taken back off the counter; whatever the test
# process recorded there stays.
$manifest['truncation']['omittedArtifacts'] =
    [Math]::Max(0, [int] $manifest['truncation']['omittedArtifacts'] - $previousFinalizerLimit)
$manifest['truncated'] = [int] $manifest['truncation']['omittedArtifacts'] -gt 0

$rejectedRoots = [System.Collections.Generic.List[string]]::new()
$acceptedRoots = [System.Collections.Generic.List[string]]::new()
foreach ($artifactRoot in $ArtifactRoots) {
    if ([string]::IsNullOrWhiteSpace($artifactRoot) -or -not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
        continue
    }

    if (-not (Test-PathInsideRepository $artifactRoot)) {
        $rejectedRoots.Add('outside-repository')
        continue
    }
    if (Test-PathTraversesReparsePoint $artifactRoot) {
        $rejectedRoots.Add('reparse-point')
        continue
    }

    $acceptedRoots.Add($artifactRoot)
}

# Every file under an accepted root is enumerated once, before anything is hashed. The list decides
# three separate questions - which run ids exist, which inherited references still describe a live
# file, and what has not been recorded yet - and all three have to see the same tree.
$discoveredFiles = [System.Collections.Generic.List[object]]::new()
$liveFiles = @{}
$linkedRelativePaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$enumerationErrors = 0
foreach ($artifactRoot in $acceptedRoots) {
    # A directory this pass could not read is not an empty directory. Suppressing the error left a
    # manifest that presented a partial inventory as a complete one, and an inherited reference
    # whose file lives in the unreadable directory would have been reported as gone. Every failure
    # is counted and stated; none of the error text, which names host paths, is published.
    $rootErrors = $null
    $rootFiles = @(
        Get-ChildItem -LiteralPath $artifactRoot -Recurse -File `
            -ErrorAction SilentlyContinue -ErrorVariable rootErrors |
            Sort-Object -Property FullName)
    $enumerationErrors += @($rootErrors).Count

    foreach ($file in $rootFiles) {
        if ($file.FullName -eq $manifestFull -or $file.Name.EndsWith('.tmp', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if (-not (Test-PathInsideRepository $file.FullName)) {
            continue
        }

        $relativePath = Get-RepositoryRelativePath $file.FullName
        if ($relativePath.StartsWith('../', [System.StringComparison]::Ordinal) -or
            [System.IO.Path]::IsPathRooted($relativePath)) {
            continue
        }

        # A safe root is not a safe file. A symbolic link or other reparse point that sits under an
        # accepted root still resolves wherever its target points, so the hash published for it
        # would describe bytes from outside the repository entirely, and the run id inside it would
        # be read out of a file this repository does not own. Enumeration does not descend into
        # linked directories, so the file's own attributes are the whole test, and a linked file is
        # marked ineligible before anything reads or hashes it.
        $linked = ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
        $entry = [pscustomobject]@{
            File = $file
            Root = $artifactRoot
            RelativePath = $relativePath
            Eligible = -not $linked
        }
        $discoveredFiles.Add($entry)

        if ($linked) {
            [void] $linkedRelativePaths.Add($relativePath)
            continue
        }
        if (-not $liveFiles.ContainsKey($relativePath)) {
            $liveFiles[$relativePath] = $entry
        }
    }
}

$knownRunIds = Get-ManifestRunIds $manifest
Add-ReportRunIds -RunIds $knownRunIds -Files $discoveredFiles

$existingPaths = @{}
$artifacts = [System.Collections.Generic.List[object]]::new()
$rejectedExistingPaths = 0
$unverifiedInheritedPaths = 0
$unhashableArtifacts = 0
foreach ($artifact in @($manifest['artifacts'])) {
    if ($artifact -isnot [System.Collections.IDictionary]) {
        $rejectedExistingPaths++
        continue
    }

    $existingPath = [string] $artifact['path']
    # An inherited reference is held to the same rule as one discovered here: a rooted or escaping
    # path is not resolvable against a consumer's checkout, so it is dropped rather than republished.
    if ([string]::IsNullOrWhiteSpace($existingPath) -or
        [System.IO.Path]::IsPathRooted($existingPath) -or
        $existingPath.StartsWith('../', [System.StringComparison]::Ordinal) -or
        $existingPath.Contains('\')) {
        $rejectedExistingPaths++
        continue
    }

    # An inherited digest describes the bytes some earlier pass saw. The same file is routinely
    # rewritten between passes - flow-run.json and the host diagnostic summary always are - so
    # republishing the old hash hands a consumer a digest that no longer matches the file it can
    # read, and the whole manifest is refused. Anything still present under an accepted root is
    # therefore hashed again from the live bytes; only a reference this pass cannot see at all is
    # carried over, and it is reported as unverified rather than presented as checked.
    if ($linkedRelativePaths.Contains($existingPath)) {
        # Counted once by the discovery pass below, which is where the link is actually observed.
        continue
    }

    $live = $liveFiles[$existingPath]
    if ($null -ne $live) {
        # Published under the casing this pass observed on disk, not the casing the reference was
        # inherited with: on a case-sensitive filesystem the two can differ, and only the observed
        # one resolves against a consumer's checkout.
        $refreshed = $null
        try {
            $refreshed = Update-ArtifactReferenceFromLiveFile `
                -Reference $artifact `
                -File $live.File `
                -RelativePath $live.RelativePath `
                -ArtifactRoot $live.Root `
                -KnownRunIds $knownRunIds
        }
        catch {
            # The file was there when the tree was enumerated and could not be read now. Publishing
            # the inherited digest would present an unverified claim as a checked one, so the
            # reference is dropped and counted, exactly as both entry points do.
            $unhashableArtifacts++
        }

        if ($null -ne $refreshed) {
            $artifacts.Add($refreshed)
        }

        $existingPaths[$existingPath] = $true
        $existingPaths[$live.RelativePath] = $true
        continue
    }

    $unverifiedInheritedPaths++
    $existingPaths[$existingPath] = $true
    $artifacts.Add($artifact)
}

if ($rejectedExistingPaths -gt 0) {
    $omissions.Add([ordered]@{
            source = $finalizerOmissionSource
            kind = 'artifact-path'
            reason = 'An artifact reference without a repository-relative path was excluded.'
            omittedArtifacts = $rejectedExistingPaths
        })
}

if ($unverifiedInheritedPaths -gt 0) {
    $omissions.Add([ordered]@{
            source = $finalizerOmissionSource
            kind = 'artifact-inherited'
            reason = 'An inherited artifact reference was republished with its recorded digest because no file for it was present under an accepted artifact root.'
            unverifiedArtifacts = $unverifiedInheritedPaths
        })
}

if ($enumerationErrors -gt 0) {
    $omissions.Add([ordered]@{
            source = $finalizerOmissionSource
            kind = 'artifact-enumeration'
            reason = 'An artifact directory could not be fully enumerated, so this inventory may be incomplete.'
            # Counted as one omitted reference per unreadable directory so the summary and the
            # omissions reconcile. It is a floor, not a measurement: an unreadable directory may
            # have held any number of references, which is what makes it worth publishing at all.
            omittedArtifacts = $enumerationErrors
            enumerationErrors = $enumerationErrors
        })
}

$omittedByLimit = 0
$omittedLinkedFiles = 0

foreach ($entry in $discoveredFiles) {
    $relativePath = $entry.RelativePath
    if ($existingPaths.ContainsKey($relativePath)) {
        continue
    }

    if ($linkedRelativePaths.Contains($relativePath)) {
        $omittedLinkedFiles++
        $existingPaths[$relativePath] = $true
        continue
    }
    # Every artifact past the cap is counted. Recording only the first one told a consumer
    # that a single reference was dropped when hundreds may have been.
    if ($artifacts.Count -ge $maxArtifacts) {
        $omittedByLimit++
        continue
    }

    try {
        $artifacts.Add((New-ArtifactReference `
                    -File $entry.File `
                    -RelativePath $relativePath `
                    -ArtifactRoot $entry.Root `
                    -KnownRunIds $knownRunIds))
    }
    catch {
        # Enumerated a moment ago and unreadable now. Both entry points report this as an
        # artifact-hash omission and continue; ending the whole finalization over one file would
        # replace the pilot manifest with a generic one for no gain.
        $unhashableArtifacts++
    }

    $existingPaths[$relativePath] = $true
}

if ($unhashableArtifacts -gt 0) {
    $omissions.Add([ordered]@{
            source = $finalizerOmissionSource
            kind = 'artifact-hash'
            reason = 'An artifact could not be hashed when the manifest was finalized.'
            omittedArtifacts = $unhashableArtifacts
        })
}

if ($omittedByLimit -gt 0) {
    $manifest['truncated'] = $true
    $manifest['truncation']['maxArtifacts'] = $maxArtifacts
    $manifest['truncation']['omittedArtifacts'] = [int] $manifest['truncation']['omittedArtifacts'] + $omittedByLimit
    $omissions.Add([ordered]@{
            source = $finalizerOmissionSource
            kind = 'artifact-limit'
            reason = "Only the first $maxArtifacts artifact references were hashed."
            omittedArtifacts = $omittedByLimit
        })
}

if ($omittedLinkedFiles -gt 0) {
    $omissions.Add([ordered]@{
            source = $finalizerOmissionSource
            kind = 'artifact-link'
            reason = 'An artifact reached through a symbolic link or reparse point was excluded.'
            omittedArtifacts = $omittedLinkedFiles
        })
}

foreach ($rejectedReason in @($rejectedRoots | Sort-Object -Unique)) {
    $omissions.Add([ordered]@{
            source = $finalizerOmissionSource
            kind = 'artifact-root'
            reason = $(if ($rejectedReason -ceq 'reparse-point') {
                    'An artifact root reached through a symbolic link or reparse point was excluded.'
                }
                else {
                    'An artifact root outside the repository was excluded.'
                })
        })
}

$manifest['omissions'] = @($omissions)
$manifest['artifacts'] = @($artifacts)

# The same four artifact facts both flow-QA reports publish, so a consumer reads one shape whether
# it was handed the script-owned manifest or the finalized pilot manifest. They are derived from
# this manifest's own final list, never restated from an earlier pass.
if ($null -eq $manifest['truncated']) {
    $manifest['truncated'] = $false
}
# Every reference this pass excluded from the published inventory, not only the ones the cap
# dropped. A summary that counted the cap alone reported "omittedArtifacts: 0" for a run whose
# linked, unresolvable, or unreadable evidence was silently missing from the list beside it.
# `truncated` stays the narrower fact it names: the cap, and only the cap, was reached.
$omittedArtifacts = [int] $manifest['truncation']['omittedArtifacts'] +
    $omittedLinkedFiles +
    $rejectedExistingPaths +
    $unhashableArtifacts +
    $enumerationErrors
$manifest['artifactSummary'] = [ordered]@{
    maxArtifacts = $maxArtifacts
    recordedArtifacts = $artifacts.Count
    omittedArtifacts = $omittedArtifacts
    truncated = [bool] $manifest['truncated']
}

# The test process validates the manifest before this script exists, so it records "at least one
# artifact is required" against a manifest whose artifact references are, by design, gathered here
# afterwards. Left alone that error is permanent and false: the finalized manifest carries the
# artifacts, but the downstream failure handoff refuses any manifest with a non-empty
# validationErrors array, so the CI failure-to-issue path could never start. Only the invariant this
# script has actually satisfied is cleared; every other recorded error is preserved untouched, and
# nothing is cleared when no artifact was found.
if ($artifacts.Count -gt 0 -and $manifest['validationErrors'] -is [System.Collections.IEnumerable]) {
    $manifest['validationErrors'] = @(
        @($manifest['validationErrors']) |
            Where-Object { [string] $_ -ne 'At least one artifact is required.' })
}

$manifest['finalizedAt'] = [DateTimeOffset]::UtcNow.ToString('O')

$temporary = "$manifestFull.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $json = $manifest | ConvertTo-Json -Depth 20
    [System.IO.File]::WriteAllText($temporary, $json, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::Move($temporary, $manifestFull, $true)
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Force
    }
}
