param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$BaselineRef,
    [Parameter(Mandatory)][string]$FixedDirectory,
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [Parameter(Mandatory)][string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Add-Type -AssemblyName System.Reflection.Metadata
$fixed = (Resolve-Path -LiteralPath $FixedDirectory).Path
$baseline = (Resolve-Path -LiteralPath $BaselineDirectory).Path
$output = [IO.Path]::GetFullPath($ResultsDirectory)
if ($fixed -ceq $baseline -or (Test-Path -LiteralPath $output)) { throw 'Separate checkouts and fresh results are required' }
New-Item -ItemType Directory -Path $output | Out-Null

function Git-Checked([string]$Directory, [string[]]$Arguments) {
    $text = & git -C $Directory @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed in $Directory : $Arguments" }
    $text
}

function Read-Module([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $mvid = $reader.GetGuid($reader.GetModuleDefinition().Mvid).ToString()
        } finally { $pe.Dispose() }
    } finally { $stream.Dispose() }
    [pscustomobject]@{ path=$Path; mvid=$mvid; sha256=(Get-FileHash -LiteralPath $Path).Hash }
}

$production = @(
    'platforms\Windows.WPF\src\Windows.WPF\Handlers\ShellHandler.Items.cs',
    'platforms\Windows.WPF\src\Windows.WPF\Handlers\ShellHandler.cs'
)
$fixtures = @(
    'platforms\Windows.WPF\tests\HandlerTests\ShellTabNavigationTests.cs',
    'platforms\Windows.WPF\tests\HandlerTests\ShellTabEnabledStateTests.cs'
)
$head = Git-Checked $fixed @('rev-parse', 'HEAD')
if ((Git-Checked $baseline @('rev-parse', 'HEAD')) -cne $head) { throw 'Checkouts must start at the same tested commit' }
Git-Checked $baseline @('fetch', '--no-tags', '--depth=1', 'origin', $BaselineRef) | Out-Null
# Keep the current host, tests and dependencies; replace only the production correction.
Git-Checked $baseline (@('restore', "--source=$BaselineRef", '--') + $production) | Out-Null
$changed = @(Git-Checked $baseline @('diff', '--name-only') | Sort-Object)
if (($changed -join '|') -cne (($production | ForEach-Object { $_.Replace('\', '/') } | Sort-Object) -join '|')) {
    throw 'Baseline must differ only in the two enabled-state production files'
}
$fixturePins = @(foreach ($path in $fixtures) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $fixed $path)).Hash
    if ((Get-FileHash -LiteralPath (Join-Path $baseline $path)).Hash -cne $hash) { throw 'Test-only baseline fixture mismatch' }
    [pscustomobject]@{path=$path;sha256=$hash}
})
$methods = [ordered]@{
    'initial-False' = 'NativeTabEnabled_InitialState_ProjectsToNativePeerAndSelection'
    'initial-True' = 'NativeTabEnabled_InitialState_ProjectsToNativePeerAndSelection'
    'runtime' = 'NativeTabEnabled_RuntimeChanges_PreserveIdentityAndNavigation'
    'mapper' = 'NativeTabEnabled_RuntimeChanges_PreserveCurrentItemMapperCustomization'
    'lifecycle' = 'NativeTabEnabled_Subscriptions_FollowCollectionAndHandlerLifecycle'
}
$stages = @{
    'initial-False' = @('before-select', 'after-select')
    'initial-True' = @('before-select', 'after-select')
    'runtime' = @('enabled-control', 'disabled-before-select', 'disabled-after-select', 'reenabled-before-select', 'reenabled-after-select', 'selected-section-disabled')
    'mapper' = @('enabled-control', 'disabled', 'reenabled')
    'lifecycle' = @('enabled-control', 'disabled', 'repeat-attach', 'removed-section-mutated', 'inactive-section-mutated', 'inactive-root-selected', 'reset-retired-sections-mutated', 'rebound-old-shell-mutated', 'disconnected-source-mutated', 'reconnected', 'reconnected-source-enabled')
}
$cases = @(foreach ($template in @($false, $true)) {
    foreach ($test in $methods.Keys) {
        $arguments = "useTemplate: $template"
        if ($test.StartsWith('initial-')) { $arguments += ", enabled: $($test.Substring(8))" }
        [pscustomobject]@{
            name="HandlerTests.ShellTabNavigationTests.$($methods[$test])($arguments)"
            test=$test; template=$template; prefix=$(if ($template) { 'd7t' } else { 'd7' })
        }
    }
})
$provenance = [pscustomobject]@{
    head=$head; tree=(Git-Checked $fixed @('rev-parse', 'HEAD^{tree}'))
    baselineProductRef=$BaselineRef; fixturePins=$fixturePins
    runId=$env:GITHUB_RUN_ID; attempt=$env:GITHUB_RUN_ATTEMPT; eventSha=$env:GITHUB_SHA
    sdk=(& dotnet --version); results=@()
}
if ($LASTEXITCODE -ne 0) { throw 'SDK version query failed' }
$provenance | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'provenance.json')
$oldResults = $env:SHELL_TAB_RESULTS
try {
    foreach ($variant in @('baseline', 'fixed')) {
        $repo = if ($variant -eq 'baseline') { $baseline } else { $fixed }
        $resultDirectory = Join-Path $output $variant
        New-Item -ItemType Directory -Path $resultDirectory | Out-Null
        $env:SHELL_TAB_RESULTS = Join-Path $resultDirectory 'tabs'
        Push-Location $repo
        try {
            & dotnet test 'platforms\Windows.WPF\tests\HandlerTests\HandlerTests.csproj' -c Release `
                -p:UseMaui=false -p:UseSharedCompilation=false -m:1 -nr:false `
                --filter 'FullyQualifiedName~HandlerTests.ShellTabNavigationTests.NativeTabEnabled_' `
                --logger 'trx;LogFileName=enabled.trx' --results-directory $resultDirectory `
                *> (Join-Path $resultDirectory 'process.log')
            $exitCode = $LASTEXITCODE
        } finally { Pop-Location }
        [xml]$trx = Get-Content -LiteralPath (Join-Path $resultDirectory 'enabled.trx') -Raw
        $rows = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
        if ($rows.Count -ne 10 -or @(Compare-Object -CaseSensitive @($cases.name | Sort-Object) @($rows.testName | Sort-Object)).Count) {
            throw "$variant did not execute the exact ten enabled-state identities"
        }
        $failed = if ($variant -eq 'baseline') { 8 } else { 0 }
        $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
        foreach ($counter in @{total=10;executed=10;passed=(10-$failed);failed=$failed;notExecuted=0;error=0;timeout=0;aborted=0}.GetEnumerator()) {
            if ($counters.GetAttribute($counter.Key) -cne [string]$counter.Value) { throw "$variant counter mismatch: $($counter.Key)" }
        }
        if ($exitCode -ne $(if ($failed) { 1 } else { 0 })) { throw "$variant unexpected process exit: $exitCode" }
        $expectedFiles = [Collections.Generic.List[string]]::new()
        $modules = @{}
        foreach ($case in $cases) {
            $row = @($rows | Where-Object testName -CEQ $case.name)
            $outcome = if ($failed -and $case.test -cne 'initial-True') { 'Failed' } else { 'Passed' }
            if ($row.Count -ne 1 -or $row[0].GetAttribute('outcome') -cne $outcome) { throw "$variant outcome mismatch: $($case.name)" }
            if ($outcome -eq 'Failed' -and (
                $row[0].Output.ErrorInfo.Message -notmatch 'Disabled ShellSection must project native TabItem.IsEnabled=false\.' -or
                $row[0].Output.ErrorInfo.StackTrace -notmatch 'AssertNativeEnabled')) {
                throw 'Baseline failed outside the native disabled-state assertion'
            }
            $caseStages = $stages[$case.test]
            if ($failed -and $case.test -eq 'runtime') { $caseStages = $caseStages[0..2] }
            elseif ($failed -and $case.test -in @('mapper','lifecycle')) { $caseStages = $caseStages[0..1] }
            foreach ($stage in $caseStages) {
                $name = "$($case.prefix)-enabled-$($case.test)-$stage"
                $expectedFiles.Add("$name.details.json"); $expectedFiles.Add("$name.png")
                $json = Get-Content -LiteralPath (Join-Path $env:SHELL_TAB_RESULTS "$name.details.json") -Raw
                $snapshot = $json | ConvertFrom-Json
                if ($snapshot.test -cne $case.test -or $snapshot.stage -cne $stage -or $snapshot.useTemplate -ne $case.template -or
                    !([string]$row[0].Output.StdOut).Replace("`r`n","`n").Contains($json.Replace("`r`n","`n"))) {
                    throw 'Snapshot identity or per-test stdout correlation failed'
                }
                foreach ($kind in @('handler','test')) {
                    $location = $snapshot.runtimeModules."${kind}Location"
                    if (!$location.StartsWith($repo + '\artifacts\bin\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime module escaped its checkout' }
                    if (!$modules.ContainsKey($location)) { $modules[$location] = Read-Module $location }
                    if ($modules[$location].mvid -cne $snapshot.runtimeModules."${kind}Mvid") { throw 'Runtime/disk MVID mismatch' }
                }
                $png = [IO.File]::ReadAllBytes((Join-Path $env:SHELL_TAB_RESULTS "$name.png"))
                if ($png.Length -lt 24 -or [Convert]::ToHexString($png[0..7]) -cne '89504E470D0A1A0A' -or
                    [Convert]::ToHexString($png[16..19]) -ceq '00000000' -or [Convert]::ToHexString($png[20..23]) -ceq '00000000') {
                    throw 'Invalid or empty screenshot'
                }
            }
        }
        $actualFiles = @(Get-ChildItem -LiteralPath $env:SHELL_TAB_RESULTS -Recurse -File | ForEach-Object {
            [IO.Path]::GetRelativePath($env:SHELL_TAB_RESULTS, $_.FullName)
        })
        if (@(Compare-Object -CaseSensitive @($expectedFiles | Sort-Object) @($actualFiles | Sort-Object)).Count) { throw 'Snapshot inventory mismatch' }
        if ($modules.Count -ne 2) { throw 'Expected one handler and one test runtime module per stage' }
        $record = [pscustomobject]@{
            variant=$variant; exitCode=$exitCode; passed=(10-$failed); failed=$failed; identities=@($rows.testName | Sort-Object)
            snapshotPairs=($expectedFiles.Count/2); modules=@($modules.Values)
            productionPins=@(foreach ($path in $production) { [pscustomobject]@{path=$path;sha256=(Get-FileHash (Join-Path $repo $path)).Hash} })
        }
        $record | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $resultDirectory 'classification.json')
        $provenance.results += $record
        $provenance | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'provenance.json')
    }
} finally { $env:SHELL_TAB_RESULTS = $oldResults }
