[CmdletBinding()]
param(
    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'UPSTREAM.json'
$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
$importRoot = Join-Path $PSScriptRoot $manifest.importRoot

function Get-Sha256FromBytes {
    param([byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

$actualFiles = @(Get-ChildItem -Path $importRoot -File)
$sourceFiles = @($manifest.files | Where-Object role -eq 'source')

if ($sourceFiles.Count -ne $manifest.sourceFileCount) {
    throw "Manifest expected $($manifest.sourceFileCount) source files but records $($sourceFiles.Count)."
}

if (@($actualFiles | Where-Object Extension -eq '.cs').Count -ne $manifest.sourceFileCount) {
    throw "Import must contain exactly $($manifest.sourceFileCount) C# source files."
}

if ($actualFiles.Count -ne $manifest.files.Count) {
    throw "Import contains $($actualFiles.Count) files but the manifest records $($manifest.files.Count)."
}

$httpClient = if ($Offline) { $null } else { [System.Net.Http.HttpClient]::new() }

try {
    foreach ($file in $manifest.files) {
        $localPath = Join-Path $importRoot $file.path
        if (-not (Test-Path -LiteralPath $localPath -PathType Leaf)) {
            throw "Missing imported file: $($file.path)"
        }

        $localHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $localPath).Hash.ToLowerInvariant()
        if ($localHash -ne $file.sha256) {
            throw "Local hash mismatch for $($file.path): expected $($file.sha256), got $localHash."
        }

        if (-not $Offline) {
            $relativePath = "$($manifest.sourcePath)/$($file.path)"
            $uri = "$($manifest.sourceRepository)/raw/$($manifest.commit)/$relativePath"
            $upstreamBytes = $httpClient.GetByteArrayAsync($uri).GetAwaiter().GetResult()
            $upstreamHash = Get-Sha256FromBytes -Bytes $upstreamBytes

            if ($upstreamHash -ne $file.sha256) {
                throw "Upstream hash mismatch for $($file.path): expected $($file.sha256), got $upstreamHash."
            }
        }
    }
}
finally {
    if ($null -ne $httpClient) {
        $httpClient.Dispose()
    }
}

$mode = if ($Offline) { 'manifest hashes' } else { "upstream commit $($manifest.commit)" }
Write-Host "Verified $($manifest.files.Count) files, including $($manifest.sourceFileCount) C# sources, against $mode."
