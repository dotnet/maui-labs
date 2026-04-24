using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Cli.DevFlow.Init;

/// <summary>
/// Read-only snapshot of a project evaluation. All data is extracted upfront so this class
/// has zero references to <c>Microsoft.Build.*</c> types — safe to use from any call site
/// without triggering MSBuild assembly loading.
/// </summary>
internal sealed class EvaluatedProject
{
    readonly Dictionary<string, string> _properties;
    readonly IReadOnlyList<EvaluatedPackageReference> _packageReferences;
    readonly HashSet<string> _packageReferenceIds;

    EvaluatedProject(
        IReadOnlyList<string> targetFrameworks,
        Dictionary<string, string> properties,
        IReadOnlyList<EvaluatedPackageReference> packageReferences)
    {
        TargetFrameworks = targetFrameworks;
        _properties = properties;
        _packageReferences = packageReferences;
        _packageReferenceIds = new HashSet<string>(
            packageReferences.Select(r => r.Id),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> TargetFrameworks { get; }

    public string GetPropertyValue(string name)
        => _properties.TryGetValue(name, out var value) ? value : string.Empty;

    public bool GetBooleanProperty(string name)
        => string.Equals(GetPropertyValue(name), "true", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<EvaluatedPackageReference> GetPackageReferences() => _packageReferences;

    public bool HasPackageReference(string packageId)
        => _packageReferenceIds.Contains(packageId);

    /// <summary>
    /// Try to evaluate the project using the MSBuild API. Returns <c>null</c> if MSBuild
    /// is not available, the SDK is missing, or evaluation fails for any reason.
    /// </summary>
    /// <remarks>
    /// The gateway method contains NO <c>Microsoft.Build.*</c> type references so its JIT
    /// compilation never triggers assembly loading. All MSBuild interaction is delegated to
    /// <see cref="LoadCore"/> which is <c>[NoInlining]</c> and only JIT'd after
    /// <see cref="MsBuildEnvironment.EnsureRegistered"/> has installed the assembly resolver.
    /// </remarks>
    public static EvaluatedProject? TryLoad(string projectPath)
    {
        try
        {
            MsBuildEnvironment.EnsureRegistered();
            return LoadCore(projectPath);
        }
        catch
        {
            return null;
        }
    }

    // --- Properties extracted from MSBuild evaluation ---
    static readonly string[] s_propertiesToExtract =
    [
        "UseMaui",
        "ManagePackageVersionsCentrally",
        "TargetFramework",
        "TargetFrameworks"
    ];

    /// <summary>
    /// Performs the actual MSBuild evaluation. Isolated behind <c>[NoInlining]</c> so the
    /// JIT only compiles this method (and resolves Microsoft.Build types) after
    /// <see cref="MsBuildEnvironment.EnsureRegistered"/> has installed the assembly resolver.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DevFlow init is a dev-time command; not used at app runtime.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "DevFlow init is a dev-time command; not used at app runtime.")]
    static EvaluatedProject LoadCore(string projectPath)
    {
        var collection = new Microsoft.Build.Evaluation.ProjectCollection();
        try
        {
            const Microsoft.Build.Evaluation.ProjectLoadSettings loadSettings =
                Microsoft.Build.Evaluation.ProjectLoadSettings.IgnoreMissingImports
              | Microsoft.Build.Evaluation.ProjectLoadSettings.IgnoreInvalidImports
              | Microsoft.Build.Evaluation.ProjectLoadSettings.IgnoreEmptyImports
              | Microsoft.Build.Evaluation.ProjectLoadSettings.RecordDuplicateButNotCircularImports;

            var project = new Microsoft.Build.Evaluation.Project(
                projectPath, globalProperties: null, toolsVersion: null, collection, loadSettings);

            // Extract properties.
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in s_propertiesToExtract)
            {
                var value = project.GetPropertyValue(name);
                if (!string.IsNullOrEmpty(value))
                    properties[name] = value;
            }

            // Extract target frameworks.
            var tfms = ParseTargetFrameworks(properties);

            // Extract package references.
            var packageReferences = project.GetItems("PackageReference")
                .Select(item => new EvaluatedPackageReference(
                    item.EvaluatedInclude,
                    item.GetMetadataValue("Version") is { Length: > 0 } v ? v : null))
                .Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .ToList();

            collection.UnloadProject(project);

            return new EvaluatedProject(tfms, properties, packageReferences);
        }
        finally
        {
            collection.Dispose();
        }
    }

    static IReadOnlyList<string> ParseTargetFrameworks(Dictionary<string, string> properties)
    {
        var list = new List<string>();

        if (properties.TryGetValue("TargetFramework", out var tf))
            AddEntries(list, tf);
        if (properties.TryGetValue("TargetFrameworks", out var tfs))
            AddEntries(list, tfs);

        return list
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        static void AddEntries(List<string> list, string raw)
        {
            foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(entry);
        }
    }
}

internal sealed record EvaluatedPackageReference(string Id, string? Version);

/// <summary>
/// Format-preserving mutator for .csproj / Directory.Packages.props files using
/// <c>Microsoft.Build.Construction.ProjectRootElement</c>. Only touches the literal
/// file — no full evaluation.
/// </summary>
internal static class MsBuildProjectMutator
{
    public record AddOrUpdateResult(bool Changed, string? PreviousVersion, string? NewVersion);

    /// <summary>
    /// Ensure a <c>&lt;PackageReference&gt;</c> exists in the project file.
    /// Pass <paramref name="version"/> as <c>null</c> when using Central Package Management.
    /// </summary>
    public static AddOrUpdateResult EnsurePackageReference(
        string projectPath,
        string packageId,
        string? version,
        bool dryRun)
    {
        MsBuildEnvironment.EnsureRegistered();
        return EnsureItemCore(projectPath, "PackageReference", packageId, version, requireVersion: false, dryRun);
    }

    /// <summary>
    /// Ensure a <c>&lt;PackageVersion&gt;</c> exists in Directory.Packages.props.
    /// </summary>
    public static AddOrUpdateResult EnsurePackageVersion(
        string directoryPackagesPropsPath,
        string packageId,
        string version,
        bool dryRun)
    {
        MsBuildEnvironment.EnsureRegistered();
        return EnsureItemCore(directoryPackagesPropsPath, "PackageVersion", packageId, version, requireVersion: true, dryRun);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DevFlow init is a dev-time command; not used at app runtime.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "DevFlow init is a dev-time command; not used at app runtime.")]
    static AddOrUpdateResult EnsureItemCore(
        string filePath,
        string itemType,
        string includeValue,
        string? version,
        bool requireVersion,
        bool dryRun)
    {
        var root = Microsoft.Build.Construction.ProjectRootElement.Open(
            filePath,
            Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection,
            preserveFormatting: true);
        try
        {
            var existing = FindItem(root, itemType, includeValue);
            string? previous = GetVersion(existing);

            var changed = false;
            if (existing == null)
            {
                var itemGroup = GetOrCreateItemGroup(root, itemType);
                var newItem = itemGroup.AddItem(itemType, includeValue);
                if (requireVersion || !string.IsNullOrWhiteSpace(version))
                {
                    var metadata = newItem.AddMetadata("Version", version ?? string.Empty);
                    metadata.ExpressedAsAttribute = true;
                }
                changed = true;
            }
            else if (!string.IsNullOrWhiteSpace(version))
            {
                changed = SetVersion(existing, version);
            }
            else if (!requireVersion)
            {
                // CPM mode: remove any leftover Version metadata so CPM controls the version.
                changed = RemoveVersion(existing);
            }

            if (changed && !dryRun)
                root.Save();

            return new AddOrUpdateResult(changed, previous, version ?? previous);
        }
        finally
        {
            try { Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection.TryUnloadProject(root); } catch { }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Microsoft.Build.Construction.ProjectItemElement? FindItem(
        Microsoft.Build.Construction.ProjectRootElement root, string itemType, string includeValue)
    {
        foreach (var group in root.ItemGroups)
        {
            foreach (var item in group.Items)
            {
                if (!string.Equals(item.ItemType, itemType, StringComparison.OrdinalIgnoreCase))
                    continue;

                var include = string.IsNullOrEmpty(item.Include) ? item.Update : item.Include;
                if (string.Equals(include, includeValue, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Microsoft.Build.Construction.ProjectItemGroupElement GetOrCreateItemGroup(
        Microsoft.Build.Construction.ProjectRootElement root, string childItemType)
    {
        var existing = root.ItemGroups.FirstOrDefault(group =>
            group.Items.Any(item => string.Equals(item.ItemType, childItemType, StringComparison.OrdinalIgnoreCase)));

        return existing ?? root.AddItemGroup();
    }

    static string? GetVersion(Microsoft.Build.Construction.ProjectItemElement? item)
    {
        if (item == null)
            return null;

        return item.Metadata
            .FirstOrDefault(m => string.Equals(m.Name, "Version", StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    static bool SetVersion(Microsoft.Build.Construction.ProjectItemElement item, string version)
    {
        var existing = item.Metadata.FirstOrDefault(m => string.Equals(m.Name, "Version", StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (string.Equals(existing.Value, version, StringComparison.OrdinalIgnoreCase))
                return false;
            existing.Value = version;
            existing.ExpressedAsAttribute = true;
            return true;
        }

        var metadata = item.AddMetadata("Version", version);
        metadata.ExpressedAsAttribute = true;
        return true;
    }

    static bool RemoveVersion(Microsoft.Build.Construction.ProjectItemElement item)
    {
        var existing = item.Metadata.FirstOrDefault(m => string.Equals(m.Name, "Version", StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return false;
        item.RemoveChild(existing);
        return true;
    }
}
