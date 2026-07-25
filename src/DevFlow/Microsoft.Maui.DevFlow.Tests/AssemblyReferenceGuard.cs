using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Reads assembly references straight out of the PE metadata so a compiled assembly can be
/// inspected without loading it. This works for any target framework — including the
/// <c>net10.0-android</c> / <c>-ios</c> / <c>-maccatalyst</c> / <c>-macos</c> outputs that a
/// <c>net10.0</c> test process could never load.
/// </summary>
internal static class AssemblyReferenceGuard
{
    /// <summary>Assemblies in the DevFlow product family. These are allowed everywhere.</summary>
    private const string DevFlowPrefix = "Microsoft.Maui.DevFlow";

    /// <summary>Assembly-name prefixes that indicate a dependency on .NET MAUI.</summary>
    private static readonly string[] MauiPrefixes =
    [
        "Microsoft.Maui",
        "Microsoft.AspNetCore.Components.WebView.Maui",
    ];

    public static IReadOnlyList<string> GetReferencedAssemblyNames(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();

        return reader.AssemblyReferences
            .Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetMauiReferences(string assemblyPath)
        => GetReferencedAssemblyNames(assemblyPath).Where(IsMauiAssembly).ToArray();

    /// <summary>
    /// Asserts that the compiled assembly at <paramref name="assemblyPath"/> carries no reference
    /// to any .NET MAUI assembly.
    /// </summary>
    public static void AssertMauiFree(string assemblyPath)
    {
        Assert.True(File.Exists(assemblyPath), $"Expected assembly was not found: {assemblyPath}");

        var mauiReferences = GetMauiReferences(assemblyPath);

        Assert.True(
            mauiReferences.Count == 0,
            $"'{Path.GetFileName(assemblyPath)}' must not reference .NET MAUI, but references: " +
            $"{string.Join(", ", mauiReferences)}.{Environment.NewLine}" +
            $"Assembly: {assemblyPath}{Environment.NewLine}" +
            "Framework-specific code belongs behind a backend interface in an implementation assembly.");
    }

    /// <summary>
    /// Asserts that a project file opts out of MAUI entirely — no <c>UseMaui</c>, no
    /// <c>UseMauiEssentials</c>, and no <c>Microsoft.Maui.*</c> package reference. This catches
    /// re-coupling at the project level even when the project has not been built locally.
    /// </summary>
    public static void AssertProjectDeclaresNoMaui(string projectPath)
    {
        Assert.True(File.Exists(projectPath), $"Expected project was not found: {projectPath}");

        var project = XDocument.Load(projectPath);

        var mauiProperties = project
            .Descendants()
            .Where(element =>
                element.Name.LocalName is "UseMaui" or "UseMauiEssentials" or "UseMauiCore" &&
                !string.Equals(element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Name.LocalName)
            .ToArray();

        Assert.True(
            mauiProperties.Length == 0,
            $"'{Path.GetFileName(projectPath)}' must not enable MAUI, but sets: {string.Join(", ", mauiProperties)}.");

        var mauiPackages = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => include is not null && IsMauiAssembly(include))
            .ToArray();

        Assert.True(
            mauiPackages.Length == 0,
            $"'{Path.GetFileName(projectPath)}' must not reference MAUI packages, but references: " +
            $"{string.Join(", ", mauiPackages)}.");
    }

    private static bool IsMauiAssembly(string name)
    {
        if (name.StartsWith(DevFlowPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var prefix in MauiPrefixes)
        {
            if (name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
