namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Locates well-known paths in the repository from the test output directory.
/// </summary>
internal static class TestRepo
{
    private static readonly Lazy<string> LazyRoot = new(FindRoot);

    public static string Root => LazyRoot.Value;

    /// <summary>
    /// Enumerates every built assembly with the supplied simple name under <c>artifacts/bin</c>.
    /// Returns an empty sequence when the project has not been built.
    /// </summary>
    public static IReadOnlyList<string> FindBuiltAssemblies(string assemblySimpleName)
    {
        var projectOutput = Path.Combine(Root, "artifacts", "bin", assemblySimpleName);
        if (!Directory.Exists(projectOutput))
            return [];

        return Directory
            .EnumerateFiles(projectOutput, assemblySimpleName + ".dll", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
