$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptPath = Join-Path $PSScriptRoot '../community-resource-acceptance.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $scriptPath), [ref] $tokens, [ref] $errors)
if ($errors.Count -ne 0) { throw "Acceptance script parse errors: $errors" }
foreach ($name in @('Get-ResourcePublishArguments', 'Invoke-DotNet', 'Assert-SingleFile', 'Assert-NoFile')) {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Missing production function $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

foreach ($backend in @('appkit', 'gtk', 'gtk-net10', 'wpf', 'wpf-net10')) {
    $arguments = @(Get-ResourcePublishArguments $backend 'App with spaces.csproj' 'publish with spaces')
    if ($arguments[0] -ne 'publish' -or
        $arguments -notcontains 'App with spaces.csproj' -or
        $arguments -notcontains '--no-restore' -or
        $arguments -notcontains '-p:PublishDir=publish with spaces' -or
        ($arguments -contains '--no-build') -ne ($backend -ne 'appkit')) {
        throw "Incorrect $backend publish arguments: $arguments"
    }
}

$DotNet = 'Invoke-FakeDotNet'
$script:fakeExitCode = 0
$script:fakeOutput = ''
function Invoke-FakeDotNet {
    if ($script:fakeOutput) { Write-Output $script:fakeOutput }
    $global:LASTEXITCODE = $script:fakeExitCode
}
Invoke-DotNet @('publish', '--no-build')

$script:fakeExitCode = 1
$rejected = $false
try { Invoke-DotNet @('publish') } catch {
    if ($_.Exception.Message -notlike '*failed with exit code 1') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'A failing supported publish was accepted.' }
Invoke-DotNet @('publish') -ExpectFailure
if ($LASTEXITCODE -ne 0) { throw 'An expected collision left a failing wrapper exit code.' }

$script:fakeExitCode = 0
$rejected = $false
try { Invoke-DotNet @('publish') -ExpectFailure } catch {
    if ($_.Exception.Message -notlike '*unexpectedly succeeded') { throw }
    $rejected = $true
}
if (-not $rejected) { throw 'A successful collision publish was accepted.' }

$script:fakeExitCode = 1
foreach ($code in @('MAUIAPPKIT001', 'NETSDK1152')) {
    $script:fakeOutput = "project: error ${code}: Duplicate resource."
    Invoke-DotNet @('publish') -ExpectFailure -ExpectedErrorCode $code
    $script:fakeOutput = 'project: error NETSDK1085: Unsupported no-build publish.'
    $rejected = $false
    try { Invoke-DotNet @('publish') -ExpectFailure -ExpectedErrorCode $code } catch {
        if ($_.Exception.Message -notlike "*without the expected error $code*") { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Unrelated NETSDK1085 was accepted as collision $code." }
}
$global:LASTEXITCODE = 0

$root = Join-Path $PSScriptRoot "../../../artifacts/resource-contract-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $root -Force | Out-Null
$file = Join-Path $root "cafe$([char]0x0301).txt"
$bytes = [byte[]] @(1, 2, 3)
try {
    [System.IO.File]::WriteAllBytes($file, $bytes)
    $composedName = "caf$([char]0x00e9).txt"
    Assert-SingleFile $root $composedName $bytes | Out-Null
    $rejected = $false
    try { Assert-NoFile $root $composedName } catch {
        if ($_.Exception.Message -notlike '*to be absent*found 1.') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'A canonically equivalent stale filename was ignored.' }
}
finally {
    Remove-Item -LiteralPath $file
    Remove-Item -LiteralPath $root
}
Write-Host 'PASS resource acceptance: five backend commands, four exit-status contracts, and Unicode payload/stale-file checks.'
