[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [Parameter(Mandatory = $false)]
    [string[]] $Include = @("*.cs")
)

$ErrorActionPreference = "Stop"

function Add-Finding {
    param(
        [System.Collections.Generic.List[object]] $Findings,
        [string] $File,
        [int] $Line,
        [string] $Category,
        [string] $Severity,
        [string] $Message,
        [string] $Text
    )

    $Findings.Add([ordered]@{
        file = $File
        line = $Line
        category = $Category
        severity = $Severity
        message = $Message
        text = $Text.Trim()
    }) | Out-Null
}

try {
    $root = (Resolve-Path $Path).Path
    $files = foreach ($pattern in $Include) {
        Get-ChildItem -Path $root -Filter $pattern -Recurse -File
    }

    $findings = New-Object System.Collections.Generic.List[object]

    foreach ($file in $files | Sort-Object FullName -Unique) {
        $lines = Get-Content -Path $file.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $lineNumber = $i + 1
            $line = $lines[$i]
            $previousWindowStart = [Math]::Max(0, $i - 4)
            $previousWindow = ($lines[$previousWindowStart..$i] -join "`n")

            if ($line -match '\[Verify(?:\(|\])') {
                Add-Finding $findings $file.FullName $lineNumber "VerifyAttribute" "Review" "Objective Sharpie marked this API for manual verification." $line
            }

            if ($line -match 'initWithCoder:|InitWithCoder') {
                Add-Finding $findings $file.FullName $lineNumber "InitWithCoder" "Review" "Generated NSCoder constructors are often not useful in app-facing bindings; keep only if intentionally supported." $line
            }

            if ($line -match 'System\.nint|System\.nuint|System\.nfloat') {
                Add-Finding $findings $file.FullName $lineNumber "LegacyNativeTypes" "Fix" "Replace legacy Xamarin native numeric type references with modern nint/nuint/nfloat equivalents." $line
            }

            if ($line -match 'Action<.*NSError|Action<.*NSError\?>|NSError.*Action<') {
                if ($previousWindow -notmatch '\[Async\]') {
                    Add-Finding $findings $file.FullName $lineNumber "AsyncCandidate" "Review" "Completion handler with NSError may need [Async] if it should expose a Task-based API." $line
                }
            }

            if ($line -match '\[Export\("([^"]+)"\)\]') {
                $selector = $Matches[1]
                $colonCount = ([regex]::Matches($selector, ':')).Count
                $nextLine = if ($i + 1 -lt $lines.Count) { $lines[$i + 1] } else { "" }
                if ($nextLine -match '\)\s*(;|=>|\{)?\s*$' -and $nextLine -match '\((.*)\)') {
                    $params = $Matches[1].Trim()
                    $paramsWithoutGenerics = [regex]::Replace($params, '<[^<>]*>', '')
                    if ($params.Length -eq 0) {
                        $parameterCount = 0
                    } else {
                        $parameterCount = ([regex]::Matches($paramsWithoutGenerics, ',')).Count + 1
                    }

                    if ($colonCount -ne $parameterCount) {
                        Add-Finding $findings $file.FullName $lineNumber "SelectorShape" "Hint" "Selector colon count may not match the next member's parameter count. This is a low-confidence heuristic; verify against the native header before editing." $line
                    }
                }
            }
        }
    }

    $byCategory = $findings |
        Group-Object category |
        ForEach-Object { [ordered]@{ category = $_.Name; count = $_.Count } }

    [ordered]@{
        status = "OK"
        root = $root
        fileCount = @($files).Count
        findingCount = $findings.Count
        summary = @($byCategory)
        findings = @($findings)
    } | ConvertTo-Json -Depth 12
} catch {
    [ordered]@{
        status = "Failed"
        message = $_.Exception.Message
        path = $Path
    } | ConvertTo-Json -Depth 8
    exit 1
}
