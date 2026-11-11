$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptPath = Join-Path $PSScriptRoot '../community-resource-acceptance.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $scriptPath), [ref] $tokens, [ref] $errors)
if ($errors.Count -ne 0) { throw "Acceptance script parse errors: $errors" }
foreach ($name in @('Get-ResourcePublishArguments', 'Invoke-DotNet')) {
    $definition = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $definition) { throw "Missing production function $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

foreach ($backend in @('appkit', 'gtk', 'gtk-net10', 'wpf')) {
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
function Invoke-FakeDotNet { $global:LASTEXITCODE = $script:fakeExitCode }
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
Write-Host 'PASS resource acceptance: four backend command contracts and four exit-status contracts.'
