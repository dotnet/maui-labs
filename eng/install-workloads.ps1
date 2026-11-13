[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Workloads,

    [string] $DotNet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
# The servicing workload-set package is not published yet, so install the
# feed-available manifests through a local workload set instead.
$workloadVersion = '11.0.100-servicing.26530.3'
$sdkFeatureBand = '11.0.100'

$sdkVersion = (& $DotNet --version).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Failed to determine the SDK version from '$DotNet'."
}

function Get-SdkFeatureBand([string] $version) {
    $parsedVersion = [version]::Parse($version.Split('-', 2)[0])
    $featureBand = [Math]::Floor($parsedVersion.Build / 100) * 100
    return "$($parsedVersion.Major).$($parsedVersion.Minor).$featureBand"
}

if ((Get-SdkFeatureBand $sdkVersion) -ne (Get-SdkFeatureBand $sdkFeatureBand)) {
    throw "Expected a .NET SDK in feature band $sdkFeatureBand, but '$DotNet' selected $sdkVersion."
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "maui-labs-workloads-$PID-$([Guid]::NewGuid().ToString('N'))"
try {
    $packageSource = Join-Path $tempRoot 'packages'
    $artifactsPath = Join-Path $tempRoot 'artifacts'
    $configFile = Join-Path $tempRoot 'NuGet.config'
    New-Item -ItemType Directory -Force -Path $packageSource | Out-Null
    Copy-Item (Join-Path $PSScriptRoot '../NuGet.config') $configFile

    & $DotNet pack `
        (Join-Path $PSScriptRoot 'workloads/WorkloadSet.csproj') `
        --output $packageSource `
        --artifacts-path $artifactsPath `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create the local workload-set package."
    }

    & $DotNet nuget add source $packageSource `
        --name maui-labs-workload-set `
        --configfile $configFile
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to add the local workload-set package source."
    }

    $workloadIds = $Workloads.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    & $DotNet workload install @workloadIds `
        --version $workloadVersion `
        --configfile $configFile
    if ($LASTEXITCODE -ne 0) {
        throw "Workload installation failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
