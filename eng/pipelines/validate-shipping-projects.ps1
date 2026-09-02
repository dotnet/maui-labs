param(
    [Parameter(Mandatory = $true)]
    [string] $ProductRoot,

    [Parameter(Mandatory = $true)]
    [string] $SolutionFilter
)

$productRootPath = (Resolve-Path $ProductRoot).Path
$solutionFilterPath = (Resolve-Path $SolutionFilter).Path
$solutionFilterDirectory = Split-Path $solutionFilterPath
$filter = Get-Content $solutionFilterPath -Raw | ConvertFrom-Json

$includedProjects = @(
    $filter.solution.projects |
        ForEach-Object {
            $relativePath = $_ -replace '[\\/]', [System.IO.Path]::DirectorySeparatorChar
            [System.IO.Path]::GetFullPath((Join-Path $solutionFilterDirectory $relativePath))
        }
)

$shippingProjects = @(
    Get-ChildItem $productRootPath -Recurse -Filter '*.csproj' |
        Where-Object {
            [xml] $project = Get-Content $_.FullName -Raw
            $propertyGroups = @($project.Project.PropertyGroup)
            $propertyGroups.IsPackable -contains 'true' -and
                $propertyGroups.IsShipping -contains 'true'
        } |
        ForEach-Object { $_.FullName }
)

$missingProjects = @($shippingProjects | Where-Object { $_ -notin $includedProjects })
if ($missingProjects.Count -gt 0) {
    $relativePaths = $missingProjects |
        ForEach-Object { [System.IO.Path]::GetRelativePath($productRootPath, $_) }
    throw "Shipping projects missing from $SolutionFilter`: $($relativePaths -join ', ')"
}
