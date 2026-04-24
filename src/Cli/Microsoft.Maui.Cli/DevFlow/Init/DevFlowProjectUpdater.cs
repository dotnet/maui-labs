using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Maui.Cli.DevFlow.Init;

[RequiresUnreferencedCode("DevFlow init uses MSBuild evaluation/mutation which relies on reflection-heavy code paths.")]
[RequiresDynamicCode("DevFlow init uses MSBuild evaluation/mutation which relies on reflection-heavy code paths.")]
internal static class DevFlowProjectUpdater
{
    public static DevFlowInitProjectResult Apply(DevFlowProjectCandidate candidate, DevFlowInitManifest manifest, bool dryRun, string? workspaceRoot = null)
    {
        var result = new DevFlowInitProjectResult
        {
            ProjectPath = candidate.ProjectPath,
            RelativePath = candidate.RelativePath,
            Flavor = candidate.Flavor
        };

        if (!candidate.IsSupported)
        {
            result.Operations.Add(new DevFlowInitOperationResult
            {
                Name = "Project support",
                Status = DevFlowInitStatus.ManualRequired,
                Detail = "This project flavor is not supported by the current init implementation.",
                ManualSteps =
                [
                    $"Add DevFlow to {candidate.RelativePath} manually:",
                    "1. Add a PackageReference to `Microsoft.Maui.DevFlow.Agent` in the .csproj",
                    "2. In MauiProgram.cs, add `using Microsoft.Maui.DevFlow.Agent;`",
                    "3. After `var builder = MauiApp.CreateBuilder();`, add `#if DEBUG\\nbuilder.AddMauiDevFlowAgent();\\n#endif`"
                ]
            });
            result.ManualSteps.AddRange(result.Operations[^1].ManualSteps);
            result.OverallStatus = DevFlowInitStatus.ManualRequired;
            return result;
        }

        var isGtk = candidate.Flavor.StartsWith("gtk", StringComparison.OrdinalIgnoreCase);
        var usesCpm = DetectCentralPackageManagement(candidate.ProjectPath, workspaceRoot ?? Path.GetDirectoryName(candidate.ProjectPath)!, out var directoryPackagesPropsPath);

        var agentPkg = isGtk ? manifest.Packages.AgentGtk : manifest.Packages.Agent;
        var agentPackage = EnsurePackageReference(
            candidate.ProjectPath,
            directoryPackagesPropsPath,
            usesCpm,
            agentPkg,
            dryRun);
        AddOperation(result, agentPackage);

        if (candidate.NeedsBlazor)
        {
            var blazorPkg = isGtk ? manifest.Packages.BlazorGtk : manifest.Packages.Blazor;
            var blazorPackage = EnsurePackageReference(
                candidate.ProjectPath,
                directoryPackagesPropsPath,
                usesCpm,
                blazorPkg,
                dryRun);
            AddOperation(result, blazorPackage);
        }

        if (candidate.MauiProgramPath == null)
        {
            var agentNs = isGtk ? "Microsoft.Maui.DevFlow.Agent.Gtk" : "Microsoft.Maui.DevFlow.Agent";
            var snippet = $"using {agentNs};\n\n// Inside CreateMauiApp(), after var builder = MauiApp.CreateBuilder():\n#if DEBUG\nbuilder.AddMauiDevFlowAgent();\n#endif";
            AddOperation(result, new DevFlowInitOperationResult
            {
                Name = "Patch MauiProgram.cs",
                Status = DevFlowInitStatus.ManualRequired,
                Detail = "Could not find MauiProgram.cs.",
                ManualSteps =
                [
                    $"Locate MauiProgram.cs (or equivalent) in {candidate.RelativePath} and add the following:",
                    $"```csharp\n{snippet}\n```"
                ]
            });
        }
        else
        {
            AddOperation(result, MauiProgramPatcher.EnsureRegistration(candidate.MauiProgramPath, candidate.NeedsBlazor, isGtk, dryRun));
        }

        result.OverallStatus = DetermineOverallStatus(result.Operations);
        return result;
    }

    static void AddOperation(DevFlowInitProjectResult result, DevFlowInitOperationResult operation)
    {
        result.Operations.Add(operation);
        foreach (var file in operation.FilesChanged)
        {
            if (!result.FilesChanged.Contains(file, StringComparer.OrdinalIgnoreCase))
                result.FilesChanged.Add(file);
        }

        foreach (var step in operation.ManualSteps)
        {
            if (!result.ManualSteps.Contains(step, StringComparer.Ordinal))
                result.ManualSteps.Add(step);
        }
    }

    static string DetermineOverallStatus(IEnumerable<DevFlowInitOperationResult> operations)
    {
        var statuses = operations.Select(operation => operation.Status).ToList();
        if (statuses.Contains(DevFlowInitStatus.Failed, StringComparer.Ordinal))
            return DevFlowInitStatus.Failed;
        if (statuses.Contains(DevFlowInitStatus.ManualRequired, StringComparer.Ordinal))
            return DevFlowInitStatus.ManualRequired;
        if (statuses.All(status => status == DevFlowInitStatus.AlreadyPresent))
            return DevFlowInitStatus.AlreadyPresent;
        return DevFlowInitStatus.Success;
    }

    /// <summary>
    /// Detect whether the project uses Central Package Management by asking MSBuild for the
    /// evaluated <c>ManagePackageVersionsCentrally</c> property (which is set by
    /// Directory.Packages.props). Falls back to walking up the directory tree for
    /// Directory.Packages.props if evaluation is unavailable.
    /// </summary>
    static bool DetectCentralPackageManagement(string projectPath, string workspaceRoot, out string? directoryPackagesPropsPath)
    {
        directoryPackagesPropsPath = FindDirectoryPackagesProps(projectPath, workspaceRoot);

        var evaluated = EvaluatedProject.TryLoad(projectPath);
        if (evaluated != null && evaluated.GetBooleanProperty("ManagePackageVersionsCentrally"))
            return true;

        // Fallback: if the file simply exists on disk, assume CPM is in effect.
        return directoryPackagesPropsPath != null;
    }

    static string? FindDirectoryPackagesProps(string projectPath, string workspaceRoot)
    {
        var current = Path.GetDirectoryName(projectPath);
        var rootFull = Path.GetFullPath(workspaceRoot);
        while (!string.IsNullOrEmpty(current)
               && current.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            var propsPath = Path.Combine(current, "Directory.Packages.props");
            if (File.Exists(propsPath))
                return propsPath;

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    static DevFlowInitOperationResult EnsurePackageReference(
        string projectPath,
        string? directoryPackagesPropsPath,
        bool useCentralPackageManagement,
        DevFlowNuGetPackageManifest package,
        bool dryRun)
    {
        var filesChanged = new List<string>();
        var manualSteps = new List<string>();

        // Write the PackageReference to the csproj — without a version when CPM is in effect,
        // with a pinned version otherwise.
        MsBuildProjectMutator.AddOrUpdateResult projectResult;
        try
        {
            projectResult = MsBuildProjectMutator.EnsurePackageReference(
                projectPath,
                package.PackageId,
                useCentralPackageManagement ? null : package.Version,
                dryRun);
        }
        catch (Exception ex)
        {
            return new DevFlowInitOperationResult
            {
                Name = $"Ensure {package.PackageId}",
                Status = DevFlowInitStatus.Failed,
                Detail = $"Could not update {Path.GetFileName(projectPath)}: {ex.Message}",
                ManualSteps = [$"Add a <PackageReference Include=\"{package.PackageId}\" /> to {Path.GetFileName(projectPath)}."]
            };
        }

        if (projectResult.Changed)
            filesChanged.Add(projectPath);

        var versionChanged = false;
        if (useCentralPackageManagement)
        {
            if (directoryPackagesPropsPath == null)
            {
                manualSteps.Add($"Add the following to Directory.Packages.props:\n`<PackageVersion Include=\"{package.PackageId}\" Version=\"{package.Version}\" />`");
            }
            else
            {
                try
                {
                    var versionResult = MsBuildProjectMutator.EnsurePackageVersion(
                        directoryPackagesPropsPath,
                        package.PackageId,
                        package.Version,
                        dryRun);
                    if (versionResult.Changed)
                    {
                        filesChanged.Add(directoryPackagesPropsPath);
                        versionChanged = true;
                    }
                }
                catch (Exception ex)
                {
                    manualSteps.Add(
                        $"Add <PackageVersion Include=\"{package.PackageId}\" Version=\"{package.Version}\" /> to {Path.GetFileName(directoryPackagesPropsPath)} ({ex.Message}).");
                }
            }
        }

        if (manualSteps.Count > 0)
        {
            return new DevFlowInitOperationResult
            {
                Name = $"Ensure {package.PackageId}",
                Status = DevFlowInitStatus.ManualRequired,
                Detail = $"Central package management requires a version entry for {package.PackageId}.",
                FilesChanged = filesChanged,
                ManualSteps = manualSteps
            };
        }

        if (!projectResult.Changed && !versionChanged)
        {
            return new DevFlowInitOperationResult
            {
                Name = $"Ensure {package.PackageId}",
                Status = DevFlowInitStatus.AlreadyPresent,
                Detail = $"{package.PackageId} is already configured."
            };
        }

        return new DevFlowInitOperationResult
        {
            Name = $"Ensure {package.PackageId}",
            Status = DevFlowInitStatus.Success,
            Detail = $"Configured {package.PackageId}.",
            FilesChanged = filesChanged
        };
    }
}
