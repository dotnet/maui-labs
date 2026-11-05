$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Install-AndroidSdk.ps1"

$directory = Join-Path $PSScriptRoot "../../artifacts/android-sdk-install-tests/$([guid]::NewGuid())"
$sdkRoot = Join-Path $directory 'Android SDK'
$packages = @('build-tools;37.0.0', 'emulator', 'system-images;android-35;google_apis;x86_64')
$script:passed = 0

function Invoke-AndroidSdkCommand {
    param([string[]] $Arguments)

    $script:attempts++
    $script:lastArguments = $Arguments
    if ($script:attempts -le $script:failures) {
        return @{ ExitCode = $script:failureExitCode; Output = $script:errorOutput }
    }
    if ($script:writeMetadata) {
        foreach ($package in $packages) {
            $packageDirectory = $sdkRoot
            foreach ($segment in $package.Split(';')) {
                $packageDirectory = Join-Path $packageDirectory $segment
            }
            New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
            $path = if ($script:wrongMetadata) { 'unrequested-package' } else { $package }
            $metadata = if ($script:malformedMetadata) { '<repository>' } else { @"
<repository><localPackage path="$path" /></repository>
"@ }
            Set-Content -LiteralPath (Join-Path $packageDirectory 'package.xml') -Value $metadata
        }
    }
    return @{ ExitCode = 0; Output = '' }
}

function Start-Sleep {
    param([int] $Seconds)
    $script:delays.Add($Seconds)
}

function Assert-Install {
    param(
        [string] $Name,
        [int] $Failures = 0,
        [string] $Output = '',
        [int] $ExitCode = 1,
        [int] $ExpectedAttempts = 1,
        [int[]] $ExpectedDelays = @(),
        [string] $ExpectedError = '',
        [switch] $WithoutMetadata,
        [switch] $WrongMetadata,
        [switch] $MalformedMetadata
    )

    if (Test-Path -LiteralPath $sdkRoot) { Remove-Item -LiteralPath $sdkRoot -Recurse -Force }
    $script:attempts = 0
    $script:failures = $Failures
    $script:errorOutput = $Output
    $script:failureExitCode = $ExitCode
    $script:writeMetadata = !$WithoutMetadata
    $script:wrongMetadata = $WrongMetadata
    $script:malformedMetadata = $MalformedMetadata
    $script:delays = [System.Collections.Generic.List[int]]::new()
    $errorMessage = ''
    try { Install-AndroidSdkPackages -Packages $packages -SdkRoot $sdkRoot 6>$null }
    catch { $errorMessage = $_.Exception.Message }

    if ($ExpectedError) {
        if (!$errorMessage.Contains($ExpectedError)) { throw "${Name}: unexpected error '$errorMessage'." }
    }
    elseif ($errorMessage) { throw "${Name}: $errorMessage" }
    if ($script:attempts -ne $ExpectedAttempts) {
        throw "${Name}: expected $ExpectedAttempts attempts, got $script:attempts."
    }
    if (($script:delays -join ',') -cne ($ExpectedDelays -join ',')) {
        throw "${Name}: unexpected delays '$($script:delays -join ',')'."
    }
    $expectedArguments = @('sdk', 'install', '--home', $sdkRoot)
    foreach ($package in $packages) { $expectedArguments += @('--package', $package) }
    if (($script:lastArguments -join "`n") -cne ($expectedArguments -join "`n")) {
        throw "${Name}: AndroidSdk.Tool arguments changed."
    }
    $script:passed++
    Write-Host "Passed: $Name"
}

try {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Assert-Install -Name 'successful install verifies package identities and preserves arguments'
    foreach ($errorOutput in @(
        'Warning: An error occurred while preparing SDK package Google APIs: Error on ZipFile unknown archive.',
        'java.util.zip.ZipException: zip END header not found',
        'java.net.SocketTimeoutException: Read timed out',
        'java.net.UnknownHostException: dl.google.com',
        "java.net.UnknownHostException: dl.google.com`nWarning: Failed to find package `"emulator`"",
        'java.io.IOException: Connection reset',
        'Warning: An error occurred during download: unexpected end of stream.',
        'Server returned HTTP response code: 503 for URL: https://dl.google.com/image.zip',
        'HTTP 429 Too Many Requests'
    )) {
        Assert-Install -Name "transient recovery: $errorOutput" -Failures 2 -Output $errorOutput `
            -ExpectedAttempts 3 -ExpectedDelays @(5, 10)
    }
    Assert-Install -Name 'exhaustion remains a failure' -Failures 99 -Output 'Error on ZipFile unknown archive' `
        -ExitCode 23 -ExpectedAttempts 3 -ExpectedDelays @(5, 10) -ExpectedError 'last exit code 23'
    foreach ($errorOutput in @(
        'Warning: Failed to find package "system-images;android-invalid;google_apis;x86_64"',
        'License not accepted.',
        'Permission denied',
        'No space left on device',
        'Download failed: Server returned HTTP response code: 404 for URL: https://dl.google.com/missing.zip',
        'Unexpected SDK installer failure'
    )) {
        Assert-Install -Name "permanent failure: $errorOutput" -Failures 99 -Output $errorOutput `
            -ExitCode 7 -ExpectedError 'not a retryable ZIP/download error'
    }
    Assert-Install -Name 'zero-exit ZIP error still retries' -Failures 2 -Output 'Error on ZipFile unknown archive' `
        -ExitCode 0 -ExpectedAttempts 3 -ExpectedDelays @(5, 10)
    Assert-Install -Name 'zero-exit permanent error fails' -Failures 99 -Output 'sdkmanager exited with an error status' `
        -ExitCode 0 -ExpectedError 'not a retryable ZIP/download error'
    Assert-Install -Name 'zero-exit missing package cannot pass' -WithoutMetadata -ExpectedError 'did not produce package.xml'
    Assert-Install -Name 'incorrect package metadata cannot pass' -WrongMetadata -ExpectedError 'does not identify'
    Assert-Install -Name 'malformed package metadata cannot pass' -MalformedMetadata -ExpectedError 'Cannot convert'

    foreach ($invalidPackages in @(
        @{ Name = 'unsafe package ID'; Value = @('platforms;../outside') },
        @{ Name = 'empty package list'; Value = @() }
    )) {
        $script:attempts = 0
        try {
            Install-AndroidSdkPackages -Packages $invalidPackages.Value -SdkRoot $sdkRoot 6>$null
            throw "Accepted $($invalidPackages.Name)."
        }
        catch [System.Management.Automation.ParameterBindingException] {
            if ($script:attempts -ne 0) { throw 'An invalid package ID reached the installer.' }
            $script:passed++
        }
    }
    if ($script:passed -ne 24) { throw "Expected 24 retry regressions, got $script:passed." }
    Write-Host "All $script:passed Android SDK provisioning regressions passed."
}
finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
