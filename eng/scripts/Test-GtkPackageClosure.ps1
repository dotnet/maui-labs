param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$packages = @{}
foreach ($file in Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg') {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $entry = @($zip.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($entry.Count -ne 1) { throw "Expected one nuspec in $($file.Name)." }
        $reader = [System.IO.StreamReader]::new($entry[0].Open())
        try { [xml] $spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $id = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText
        $version = $metadata.SelectSingleNode('*[local-name()="version"]').InnerText
        $packages["$id/$version"] = $spec
    }
    finally { $zip.Dispose() }
}

$pending = [System.Collections.Generic.Queue[string]]::new()
foreach ($root in 'Microsoft.Maui.DevFlow.Agent.Gtk', 'Microsoft.Maui.DevFlow.Blazor.Gtk') {
    $keys = @($packages.Keys | Where-Object { $_.StartsWith("$root/", [StringComparison]::OrdinalIgnoreCase) })
    if ($keys.Count -ne 1) { throw "Expected exactly one shipping version of $root." }
    $pending.Enqueue($keys[0])
}

$visited = @{}
while ($pending.Count -gt 0) {
    $key = $pending.Dequeue()
    if ($visited.ContainsKey($key)) { continue }
    $visited[$key] = $true
    $spec = $packages[$key]
    foreach ($dependency in $spec.SelectNodes('//*[local-name()="dependency"]')) {
        $id = $dependency.GetAttribute('id')
        if ($id -like 'Platform.Maui.Linux.Gtk*') {
            throw "Superseded GTK backend dependency in ${key}: $id."
        }
        if ($id -notlike 'Microsoft.Maui.DevFlow.*' -and $id -notlike 'Microsoft.Maui.Platforms.Linux.Gtk4*') {
            continue
        }
        # ProjectReference packing emits a minimum version, optionally as [version, ).
        # Require the matching build's package, not an unrelated higher version.
        $version = $dependency.GetAttribute('version').Trim()
        if ($version -match '^\[([^,\]]+)(?:,\s*\)|\])$') { $version = $Matches[1].Trim() }
        $dependencyKey = "$id/$version"
        if (!$packages.ContainsKey($dependencyKey)) {
            throw "Incomplete GTK publication set: $key requires $dependencyKey."
        }
        $pending.Enqueue($dependencyKey)
    }
}
Write-Host "GTK publication closure verified: $($visited.Count) packages."
