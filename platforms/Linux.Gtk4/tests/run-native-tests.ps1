param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')]
    [string] $TestClass,
    [string] $ResultsDirectory = 'artifacts/TestResults/gtk-runtime'
)

$ErrorActionPreference = 'Stop'
if (-not $IsLinux) {
    throw 'Native GTK tests must run on Linux; skipped tests are not runtime evidence.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$project = Join-Path $PSScriptRoot 'Linux.Gtk4.Tests/Linux.Gtk4.Tests.csproj'
$results = [IO.Path]::GetFullPath($ResultsDirectory, $repoRoot)
$trxPath = Join-Path $results "$TestClass.trx"
New-Item -ItemType Directory -Force -Path $results | Out-Null
if (Test-Path -LiteralPath $trxPath) {
    throw "Results already exist at $trxPath; choose a new ResultsDirectory to preserve evidence."
}

$env:RUN_GTK_RUNTIME_TESTS = '1'
$env:GSK_RENDERER = 'cairo'
$filter = "FullyQualifiedName~Microsoft.Maui.Platforms.Linux.Gtk4.Tests.$TestClass."

& dbus-run-session -- xvfb-run --auto-servernum dotnet test $project `
    --configuration Release --filter $filter `
    --logger 'console;verbosity=detailed' `
    --logger "trx;LogFileName=$TestClass.trx" `
    --results-directory $results --blame-hang-timeout 3m '-m:1' '-nr:false'
if ($LASTEXITCODE -ne 0) {
    throw "Native GTK class $TestClass failed (exit code $LASTEXITCODE)."
}
if (-not (Test-Path -LiteralPath $trxPath)) {
    throw "Native GTK class $TestClass produced no TRX results."
}

[xml] $trx = Get-Content -LiteralPath $trxPath -Raw
$counters = $trx.TestRun.ResultSummary.Counters
$total = [int] $counters.total
if ($total -le 0 -or [int] $counters.executed -ne $total -or
    [int] $counters.passed -ne $total -or [int] $counters.failed -ne 0 -or
    [int] $counters.notExecuted -ne 0) {
    throw "Native GTK class $TestClass did not execute and pass every test: $($counters.OuterXml)"
}
Write-Host "PASS: native GTK class $TestClass executed $total tests with no failures or skips."
