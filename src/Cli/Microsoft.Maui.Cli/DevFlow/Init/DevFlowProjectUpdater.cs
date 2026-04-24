namespace Microsoft.Maui.Cli.DevFlow.Init;

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
    /// Detect whether the project uses Central Package Management by asking dotnet msbuild for the
    /// evaluated <c>ManagePackageVersionsCentrally</c> property (which is set by
    /// Directory.Packages.props). Falls back to walking up the directory tree for
    /// Directory.Packages.props if evaluation is unavailable.
    /// </summary>
    static bool DetectCentralPackageManagement(string projectPath, string workspaceRoot, out string? directoryPackagesPropsPath)
    {
        directoryPackagesPropsPath = FindDirectoryPackagesProps(projectPath, workspaceRoot);

        var rawValue = DotnetCliProjectReader.GetProperty(projectPath, "ManagePackageVersionsCentrally");
        if (!string.IsNullOrEmpty(rawValue))
            return string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);

        // Fallback: if the file simply exists on disk, assume CPM is in effect
        // (the property defaults to true when Directory.Packages.props is present).
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
        // Check if the package is already referenced (using evaluated data from dotnet msbuild).
        var existingIds = DotnetCliProjectReader.GetPackageReferenceIds(projectPath);
        if (existingIds.Contains(package.PackageId))
        {
            return new DevFlowInitOperationResult
            {
                Name = $"Ensure {package.PackageId}",
                Status = DevFlowInitStatus.AlreadyPresent,
                Detail = $"{package.PackageId} is already configured."
            };
        }

        if (dryRun)
        {
            return new DevFlowInitOperationResult
            {
                Name = $"Ensure {package.PackageId}",
                Status = DevFlowInitStatus.Success,
                Detail = $"Would add {package.PackageId} (dry-run).",
                FilesChanged = [projectPath]
            };
        }

        // Use `dotnet add package --no-restore` to add the reference. This handles both
        // regular projects (adds version to csproj) and CPM projects (adds PackageReference
        // to csproj without version, adds PackageVersion to Directory.Packages.props).
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("add");
            psi.ArgumentList.Add(projectPath);
            psi.ArgumentList.Add("package");
            psi.ArgumentList.Add(package.PackageId);
            psi.ArgumentList.Add("--version");
            psi.ArgumentList.Add(package.Version);
            psi.ArgumentList.Add("--no-restore");

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                return new DevFlowInitOperationResult
                {
                    Name = $"Ensure {package.PackageId}",
                    Status = DevFlowInitStatus.Failed,
                    Detail = $"Could not start `dotnet add package`.",
                    ManualSteps = [$"Run: dotnet add {Path.GetFileName(projectPath)} package {package.PackageId} --version {package.Version}"]
                };
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit(30_000);
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : "Unknown error";
                return new DevFlowInitOperationResult
                {
                    Name = $"Ensure {package.PackageId}",
                    Status = DevFlowInitStatus.Failed,
                    Detail = $"dotnet add package failed: {detail}",
                    ManualSteps = [$"Run: dotnet add {Path.GetFileName(projectPath)} package {package.PackageId} --version {package.Version}"]
                };
            }

            var filesChanged = new List<string> { projectPath };

            // dotnet add package --no-restore does not handle CPM, so post-process:
            // strip Version from the PackageReference in the csproj, and add a
            // PackageVersion entry to Directory.Packages.props.
            if (useCentralPackageManagement && directoryPackagesPropsPath != null)
            {
                PostProcessCpmPackageReference(projectPath, directoryPackagesPropsPath, package.PackageId, package.Version);
                filesChanged.Add(directoryPackagesPropsPath);
            }

            return new DevFlowInitOperationResult
            {
                Name = $"Ensure {package.PackageId}",
                Status = DevFlowInitStatus.Success,
                Detail = $"Configured {package.PackageId}.",
                FilesChanged = filesChanged
            };
        }
        catch (Exception ex)
        {
            return new DevFlowInitOperationResult
            {
                Name = $"Ensure {package.PackageId}",
                Status = DevFlowInitStatus.Failed,
                Detail = $"Could not add {package.PackageId}: {ex.Message}",
                ManualSteps = [$"Run: dotnet add {Path.GetFileName(projectPath)} package {package.PackageId} --version {package.Version}"]
            };
        }
    }

    /// <summary>
    /// After <c>dotnet add package --no-restore</c>, which always writes the Version attribute
    /// into the csproj PackageReference, fixup for CPM: strip the Version from the csproj and
    /// add a PackageVersion entry to Directory.Packages.props.
    /// </summary>
    static void PostProcessCpmPackageReference(string projectPath, string directoryPackagesPropsPath, string packageId, string version)
    {
        // 1. Remove Version attribute from the PackageReference in the csproj
        var projectDoc = System.Xml.Linq.XDocument.Load(projectPath, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var ns = projectDoc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
        var pkgRef = projectDoc.Root?
            .Descendants(ns + "PackageReference")
            .FirstOrDefault(e => string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));
        if (pkgRef != null)
        {
            pkgRef.Attribute("Version")?.Remove();
            using var writer = new System.IO.StreamWriter(projectPath, false, new System.Text.UTF8Encoding(true));
            projectDoc.Save(writer, System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        // 2. Add PackageVersion to Directory.Packages.props
        var propsDoc = System.Xml.Linq.XDocument.Load(directoryPackagesPropsPath, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var propsNs = propsDoc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
        var existingPv = propsDoc.Root?
            .Descendants(propsNs + "PackageVersion")
            .FirstOrDefault(e => string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));
        if (existingPv == null)
        {
            var itemGroup = propsDoc.Root?.Descendants(propsNs + "ItemGroup").LastOrDefault();
            if (itemGroup == null)
            {
                itemGroup = new System.Xml.Linq.XElement(propsNs + "ItemGroup");
                propsDoc.Root?.Add(itemGroup);
            }
            itemGroup.Add(new System.Xml.Linq.XElement(propsNs + "PackageVersion",
                new System.Xml.Linq.XAttribute("Include", packageId),
                new System.Xml.Linq.XAttribute("Version", version)));
            using var writer = new System.IO.StreamWriter(directoryPackagesPropsPath, false, new System.Text.UTF8Encoding(true));
            propsDoc.Save(writer, System.Xml.Linq.SaveOptions.DisableFormatting);
        }
        else
        {
            existingPv.SetAttributeValue("Version", version);
            using var writer = new System.IO.StreamWriter(directoryPackagesPropsPath, false, new System.Text.UTF8Encoding(true));
            propsDoc.Save(writer, System.Xml.Linq.SaveOptions.DisableFormatting);
        }
    }
}
