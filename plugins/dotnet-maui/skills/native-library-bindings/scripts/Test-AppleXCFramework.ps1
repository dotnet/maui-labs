[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [Parameter(Mandatory = $false)]
    [string[]] $RequiredPlatforms = @()
)

$ErrorActionPreference = "Stop"

function Invoke-CommandText {
    param(
        [string] $Command,
        [string[]] $Arguments
    )

    $cmd = Get-Command $Command -ErrorAction SilentlyContinue
    if (-not $cmd) {
        return [ordered]@{
            status = "Unknown"
            command = $Command
            output = @()
            message = "Command not found."
        }
    }

    $output = & $cmd.Source @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    return [ordered]@{
        status = if ($exitCode -eq 0) { "OK" } else { "Failed" }
        command = "$Command $($Arguments -join ' ')"
        exitCode = $exitCode
        output = @($output | ForEach-Object { $_.ToString() })
    }
}

function Get-LibraryBinaryPath {
    param(
        [string] $Root,
        [object] $Library
    )

    $libraryRoot = Join-Path $Root $Library.LibraryIdentifier
    $libraryPath = Join-Path $libraryRoot $Library.LibraryPath

    if (Test-Path $libraryPath -PathType Leaf) {
        return $libraryPath
    }

    if ($libraryPath.EndsWith(".framework", [StringComparison]::OrdinalIgnoreCase)) {
        $frameworkName = [System.IO.Path]::GetFileNameWithoutExtension($libraryPath)
        $candidate = Join-Path $libraryPath $frameworkName
        if (Test-Path $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($Library.LibraryPath)
    $fallback = Get-ChildItem -Path $libraryPath -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $leaf -or $_.Name -like "lib*.a" -or $_.Name -like "*.dylib" } |
        Select-Object -First 1

    if ($fallback) {
        return $fallback.FullName
    }

    return $null
}

try {
    $xcframeworkPath = (Resolve-Path $Path).Path
    $infoPlist = Join-Path $xcframeworkPath "Info.plist"
    if (-not (Test-Path $infoPlist)) {
        throw "Info.plist not found under '$xcframeworkPath'. Is this an .xcframework directory?"
    }

    $plutil = Invoke-CommandText -Command "plutil" -Arguments @("-convert", "json", "-o", "-", $infoPlist)
    if ($plutil.status -ne "OK") {
        [ordered]@{
            status = "Failed"
            message = "Unable to read Info.plist with plutil."
            xcframeworkPath = $xcframeworkPath
            plutil = $plutil
        } | ConvertTo-Json -Depth 12
        exit 2
    }

    $plistJson = ($plutil.output -join [Environment]::NewLine)
    $plist = $plistJson | ConvertFrom-Json
    $libraries = @()

    foreach ($library in @($plist.AvailableLibraries)) {
        $binaryPath = Get-LibraryBinaryPath -Root $xcframeworkPath -Library $library
        $fileInfo = if ($binaryPath) { Invoke-CommandText -Command "file" -Arguments @($binaryPath) } else { $null }
        $lipoInfo = if ($binaryPath) { Invoke-CommandText -Command "lipo" -Arguments @("-info", $binaryPath) } else { $null }

        $libraries += [ordered]@{
            libraryIdentifier = $library.LibraryIdentifier
            libraryPath = $library.LibraryPath
            headersPath = $library.HeadersPath
            supportedPlatform = $library.SupportedPlatform
            supportedPlatformVariant = $library.SupportedPlatformVariant
            supportedArchitectures = @($library.SupportedArchitectures)
            binaryPath = $binaryPath
            file = $fileInfo
            lipo = $lipoInfo
        }
    }

    $platformKeys = @($libraries | ForEach-Object {
        if ($_.supportedPlatformVariant) {
            "$($_.supportedPlatform)-$($_.supportedPlatformVariant)"
        } else {
            $_.supportedPlatform
        }
    })

    $missing = @($RequiredPlatforms | Where-Object { $platformKeys -notcontains $_ })

    [ordered]@{
        status = if ($missing.Count -eq 0) { "OK" } else { "Warning" }
        xcframeworkPath = $xcframeworkPath
        requiredPlatforms = $RequiredPlatforms
        discoveredPlatforms = $platformKeys
        missingRequiredPlatforms = $missing
        libraries = $libraries
    } | ConvertTo-Json -Depth 16
} catch {
    [ordered]@{
        status = "Failed"
        message = $_.Exception.Message
        path = $Path
    } | ConvertTo-Json -Depth 8
    exit 1
}
