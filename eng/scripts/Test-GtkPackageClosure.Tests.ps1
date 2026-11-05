$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$directory = Join-Path ([System.IO.Path]::GetTempPath()) ("gtk-publish-tests-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $directory | Out-Null

function New-TestPackage([string] $Id, [string] $Version, [string] $Dependencies = '') {
    $path = Join-Path $directory "$Id.$Version.nupkg"
    $zip = [System.IO.Compression.ZipFile]::Open($path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $writer = [System.IO.StreamWriter]::new($zip.CreateEntry("$Id.nuspec").Open())
        try {
            $writer.Write("<package><metadata><id>$Id</id><version>$Version</version><dependencies><group targetFramework='net10.0'>$Dependencies</group></dependencies></metadata></package>")
        }
        finally { $writer.Dispose() }
    }
    finally { $zip.Dispose() }
    return $path
}

function Assert-Rejected([string] $Expected) {
    try { & "$PSScriptRoot\Test-GtkPackageClosure.ps1" -PackageDirectory $directory }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        return
    }
    throw "Expected publication rejection: $Expected."
}

try {
    $version = '0.1.0-preview.12.26421.1'
    $backend = New-TestPackage 'Microsoft.Maui.Platforms.Linux.Gtk4' $version
    $blazorBackend = New-TestPackage 'Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView' $version "<dependency id='Microsoft.Maui.Platforms.Linux.Gtk4' version='[$version, )' />"
    $core = New-TestPackage 'Microsoft.Maui.DevFlow.Agent.Core' $version
    $agent = New-TestPackage 'Microsoft.Maui.DevFlow.Agent.Gtk' $version "<dependency id='Microsoft.Maui.Platforms.Linux.Gtk4' version='$version' /><dependency id='Microsoft.Maui.DevFlow.Agent.Core' version='$version' />"
    $blazor = New-TestPackage 'Microsoft.Maui.DevFlow.Blazor.Gtk' $version "<dependency id='Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView' version='$version' /><dependency id='Microsoft.Maui.DevFlow.Agent.Core' version='$version' />"
    & "$PSScriptRoot\Test-GtkPackageClosure.ps1" -PackageDirectory $directory

    Remove-Item -LiteralPath $backend
    Assert-Rejected 'Incomplete GTK publication set'
    $wrongBackend = New-TestPackage 'Microsoft.Maui.Platforms.Linux.Gtk4' '0.1.0-preview.999'
    Assert-Rejected 'Incomplete GTK publication set'
    Remove-Item -LiteralPath $wrongBackend
    $backend = New-TestPackage 'Microsoft.Maui.Platforms.Linux.Gtk4' $version

    Remove-Item -LiteralPath $core
    Assert-Rejected 'Incomplete GTK publication set'
    $core = New-TestPackage 'Microsoft.Maui.DevFlow.Agent.Core' $version "<dependency id='Platform.Maui.Linux.Gtk4' version='0.6.0' />"
    Assert-Rejected 'Superseded GTK backend'
    Remove-Item -LiteralPath $core
    $core = New-TestPackage 'Microsoft.Maui.DevFlow.Agent.Core' $version

    Remove-Item -LiteralPath $blazor
    Assert-Rejected 'Expected exactly one shipping version'
    Write-Host 'Passed: complete closure, missing backend, mismatched version, missing transitive dependency, legacy backend, missing Blazor root.'
}
finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
