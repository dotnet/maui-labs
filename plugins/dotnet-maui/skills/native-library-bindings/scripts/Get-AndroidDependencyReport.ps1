[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $ProjectPath,

    [Parameter(Mandatory = $false)]
    [string] $Module = "app",

    [Parameter(Mandatory = $false)]
    [string] $Configuration = "releaseRuntimeClasspath",

    [Parameter(Mandatory = $false)]
    [string[]] $Coordinates = @(),

    [Parameter(Mandatory = $false)]
    [string[]] $Repositories = @("google", "mavenCentral"),

    [Parameter(Mandatory = $false)]
    [string] $AndroidGradlePluginVersion = "8.7.3",

    [Parameter(Mandatory = $false)]
    [int] $CompileSdk = 35,

    [switch] $AllowProjectExecution,

    [switch] $KeepWorkingDirectory
)

$ErrorActionPreference = "Stop"

function ConvertTo-RepositoryBlock {
    param([string[]] $Values)

    $lines = foreach ($value in $Values) {
        switch -Regex ($value) {
            '(?i)^google$' { "        google()"; continue }
            '(?i)^mavenCentral$' { "        mavenCentral()"; continue }
            '^https://' {
                $escaped = ConvertTo-KotlinStringLiteral -Value $value
                "        maven { url = uri(`"$escaped`") }"
                continue
            }
            default {
                throw "Unsupported repository '$value'. Use google, mavenCentral, or an http(s) URL."
            }
        }
    }

    return ($lines -join [Environment]::NewLine)
}

function ConvertTo-KotlinStringLiteral {
    param([string] $Value)

    return $Value.Replace('\', '\\').Replace('"', '\"').Replace('$', '\$')
}

function Assert-NoControlOrKotlinInterpolation {
    param(
        [string] $Name,
        [string] $Value
    )

    if ($Value -match '[\x00-\x1F\x7F"''`$]') {
        throw "$Name contains unsupported characters."
    }
}

function Assert-SafeInputs {
    if ($AndroidGradlePluginVersion -notmatch '^[0-9A-Za-z][0-9A-Za-z._+-]*$') {
        throw "AndroidGradlePluginVersion must be a simple version string."
    }

    foreach ($coordinate in $Coordinates) {
        Assert-NoControlOrKotlinInterpolation -Name "Coordinate '$coordinate'" -Value $coordinate
        if ($coordinate -notmatch '^[A-Za-z0-9_.-]+:[A-Za-z0-9_.-]+:[A-Za-z0-9_.+-]+$') {
            throw "Coordinate '$coordinate' must use group:artifact:version with safe Maven identifier characters."
        }
    }

    foreach ($repository in $Repositories) {
        if ($repository -match '(?i)^(google|mavenCentral)$') {
            continue
        }

        Assert-NoControlOrKotlinInterpolation -Name "Repository '$repository'" -Value $repository
        $uri = $null
        if (-not [Uri]::TryCreate($repository, [UriKind]::Absolute, [ref] $uri) -or $uri.Scheme -ne "https" -or $uri.UserInfo) {
            throw "Repository '$repository' must be 'google', 'mavenCentral', or an HTTPS URL without embedded credentials."
        }
    }
}

function New-TemporaryGradleProject {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("maui-binding-deps-" + [Guid]::NewGuid().ToString("N"))
    $app = Join-Path $root "app"
    New-Item -ItemType Directory -Force -Path $app | Out-Null

    $repositoryBlock = ConvertTo-RepositoryBlock -Values $Repositories
    $dependencyLines = foreach ($coordinate in $Coordinates) {
        $escaped = ConvertTo-KotlinStringLiteral -Value $coordinate
        "    implementation(`"$escaped`")"
    }

    @"
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
$repositoryBlock
    }
}

rootProject.name = "MauiBindingDependencyReport"
include(":app")
"@ | Set-Content -Path (Join-Path $root "settings.gradle.kts") -Encoding UTF8

    @"
plugins {
    id("com.android.library") version "$AndroidGradlePluginVersion" apply false
}
"@ | Set-Content -Path (Join-Path $root "build.gradle.kts") -Encoding UTF8

    @"
plugins {
    id("com.android.library")
}

android {
    namespace = "com.example.mauibindingdependencyreport"
    compileSdk = $CompileSdk

    defaultConfig {
        minSdk = 23
    }
}

dependencies {
$($dependencyLines -join [Environment]::NewLine)
}
"@ | Set-Content -Path (Join-Path $app "build.gradle.kts") -Encoding UTF8

    return $root
}

function Get-GradleCommand {
    param([string] $Root)

    $wrapper = if ($IsWindows) { "gradlew.bat" } else { "gradlew" }
    $wrapperPath = Join-Path $Root $wrapper
    if (Test-Path $wrapperPath) {
        if (-not $IsWindows) {
            & chmod +x $wrapperPath 2>$null
        }
        return $wrapperPath
    }

    $gradle = Get-Command "gradle" -ErrorAction SilentlyContinue
    if ($gradle) {
        return $gradle.Source
    }

    return $null
}

function Parse-DependencyArtifacts {
    param([string[]] $Lines)

    $items = New-Object System.Collections.Generic.List[object]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'

    foreach ($line in $Lines) {
        if ($line -match '^[\s|+\\-]*[-+\\]---\s+([A-Za-z0-9_.-]+):([A-Za-z0-9_.-]+):([^\s()]+)(?:\s+->\s+([^\s()]+))?') {
            $group = $Matches[1]
            $artifact = $Matches[2]
            $requested = $Matches[3]
            $resolved = if ($Matches[4]) { $Matches[4] } else { $requested }
            $key = "$group`:$artifact`:$resolved"

            if ($seen.Add($key)) {
                $items.Add([ordered]@{
                    group = $group
                    artifact = $artifact
                    requestedVersion = $requested
                    resolvedVersion = $resolved
                    mavenCoordinate = "$group`:$artifact`:$resolved"
                    sourceLine = $line.Trim()
                }) | Out-Null
            }
        }
    }

    return @($items)
}

$createdTemporaryProject = $false
$root = $null

try {
    Assert-SafeInputs

    if ($ProjectPath) {
        if (-not $AllowProjectExecution) {
            throw "Refusing to execute Gradle for -ProjectPath without -AllowProjectExecution. Gradle project files and wrappers are executable code; use -Coordinates for a generated report project, or pass -AllowProjectExecution only for a trusted project."
        }

        $root = (Resolve-Path $ProjectPath).Path
    } elseif ($Coordinates.Count -gt 0) {
        $root = New-TemporaryGradleProject
        $createdTemporaryProject = $true
    } else {
        throw "Provide -ProjectPath or at least one -Coordinates value."
    }

    $gradleCommand = Get-GradleCommand -Root $root
    if (-not $gradleCommand) {
        [ordered]@{
            status = "Failed"
            message = "No Gradle wrapper found in '$root' and no 'gradle' command found on PATH."
            projectPath = $root
            requestedCoordinates = $Coordinates
        } | ConvertTo-Json -Depth 8
        exit 2
    }

    $args = @(":$Module:dependencies", "--configuration", $Configuration, "--console=plain")
    Push-Location $root
    try {
        $output = & $gradleCommand @args 2>&1
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    $lines = @($output | ForEach-Object { $_.ToString() })
    $artifacts = Parse-DependencyArtifacts -Lines $lines

    [ordered]@{
        status = if ($exitCode -eq 0) { "OK" } else { "Failed" }
        exitCode = $exitCode
        projectPath = $root
        createdTemporaryProject = $createdTemporaryProject
        command = "$gradleCommand $($args -join ' ')"
        module = $Module
        configuration = $Configuration
        requestedCoordinates = $Coordinates
        resolvedArtifacts = $artifacts
        output = $lines
    } | ConvertTo-Json -Depth 12
} catch {
    [ordered]@{
        status = "Failed"
        message = $_.Exception.Message
        projectPath = $root
        requestedCoordinates = $Coordinates
    } | ConvertTo-Json -Depth 8
    exit 1
} finally {
    if ($createdTemporaryProject -and -not $KeepWorkingDirectory -and $root -and (Test-Path $root)) {
        Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
    }
}
