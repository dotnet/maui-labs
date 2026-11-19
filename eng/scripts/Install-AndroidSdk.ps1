function Invoke-AndroidSdkCommand {
    param([string[]] $Arguments)

    $PSNativeCommandUseErrorActionPreference = $false
    $output = @(& dotnet tool run android -- @Arguments 2>&1)
    return @{
        ExitCode = $LASTEXITCODE
        Output = ($output | ForEach-Object { "$_" }) -join [Environment]::NewLine
    }
}

function Install-AndroidSdkPackages {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [ValidatePattern('^[a-zA-Z0-9_-]+(?:[.;][a-zA-Z0-9_-]+)*$')]
        [string[]] $Packages,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $SdkRoot
    )

    $ErrorActionPreference = 'Stop'
    $arguments = @('sdk', 'install', '--home', $SdkRoot)
    foreach ($package in $Packages) {
        $arguments += @('--package', $package)
    }
    $permanentError = 'Permission denied|No space left on device|License.*not.*accepted|' +
        'HTTP (response code:|status code:|status:)?\s*(400|401|403|404|410)([^0-9]|$)'
    $transientError = 'Error on ZipFile|ZipException|zip END header not found|Unexpected end of (ZIP|ZLIB)|' +
        'invalid (zip|archive)|corrupt(ed)? (zip|archive)|not a zip (file|archive)|' +
        'SocketTimeoutException|ConnectException|UnknownHostException|SSLException|' +
        'Connection (reset|timed out|refused|aborted)|Read timed out|Connect timed out|Broken pipe|' +
        'Temporary failure in name resolution|Could not resolve host|Network (error|is unreachable)|' +
        'Error (occurred )?(while|during) download|Download (failed|error)|' +
        'HTTP (response code:|status code:|status:)?\s*(408|429|5[0-9]{2})([^0-9]|$)'
    $reportedError = 'Exception\b|exited with an error status|Failed to find package|(?m)^\s*Error:'

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Write-Host "Installing Android SDK packages (attempt $attempt/3): $($Packages -join ', ')"
        $result = Invoke-AndroidSdkCommand -Arguments $arguments
        $output = $result.Output
        if ($output) { Write-Host $output }

        if ($result.ExitCode -eq 0 -and $output -notmatch "$reportedError|$permanentError|$transientError") {
            # AndroidSdk.Tool 0.35.1 can return zero after reporting a command failure.
            # Require sdkmanager's completed-install metadata rather than trusting that exit code.
            foreach ($package in $Packages) {
                $packageDirectory = $SdkRoot
                foreach ($segment in $package.Split(';')) {
                    $packageDirectory = Join-Path $packageDirectory $segment
                }
                $metadata = Join-Path $packageDirectory 'package.xml'
                if (!(Test-Path -LiteralPath $metadata -PathType Leaf)) {
                    throw "Android SDK installation did not produce package.xml for '$package'."
                }
                [xml]$document = Get-Content -LiteralPath $metadata -Raw
                $installedPackage = $document.SelectSingleNode('/*/*[local-name()="localPackage"]')
                if ($null -eq $installedPackage -or $installedPackage.GetAttribute('path') -cne $package) {
                    throw "Android SDK installation metadata does not identify the requested package '$package'."
                }
            }
            Write-Host "Verified all $($Packages.Count) requested Android SDK packages."
            return
        }

        if ($output -match $permanentError -or $output -notmatch $transientError) {
            throw "Android SDK installation failed (exit code $($result.ExitCode)); not a retryable ZIP/download error.`n$output"
        }
        if ($attempt -eq 3) {
            throw "Android SDK installation failed after 3 attempts (last exit code $($result.ExitCode)).`n$output"
        }
        $delay = $attempt * 5
        Write-Host "Retrying Android SDK installation after a ZIP/download error in ${delay}s."
        Start-Sleep -Seconds $delay
    }
}
