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
if ($sdkVersion -ne $sdkFeatureBand) {
    throw "Expected .NET SDK $sdkFeatureBand, but '$DotNet' selected $sdkVersion."
}

$sdkLine = & $DotNet --list-sdks |
    Where-Object { $_ -like "$sdkVersion *" } |
    Select-Object -Last 1
if ($LASTEXITCODE -ne 0 -or $sdkLine -notmatch '^\S+\s+\[(.+)\]$') {
    throw "Failed to locate the SDK root for $sdkVersion."
}

$dotnetRoot = Split-Path $Matches[1] -Parent
$workloadSetDirectory = Join-Path $dotnetRoot "sdk-manifests/$sdkFeatureBand/workloadsets/$workloadVersion"
New-Item -ItemType Directory -Force -Path $workloadSetDirectory | Out-Null
Copy-Item `
    (Join-Path $PSScriptRoot 'workloads/microsoft.net.workloads.workloadset.json') `
    (Join-Path $workloadSetDirectory 'microsoft.net.workloads.workloadset.json') `
    -Force

& $DotNet workload config --update-mode workload-set
if ($LASTEXITCODE -ne 0) {
    throw "Failed to enable workload-set update mode."
}

$workloadIds = $Workloads.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
& $DotNet workload install @workloadIds --version $workloadVersion
if ($LASTEXITCODE -ne 0) {
    throw "Workload installation failed with exit code $LASTEXITCODE."
}
