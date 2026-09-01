using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The reviewed C# source analysis runs Roslyn against the developer's real project, which needs an
/// <c>MSBuildWorkspace</c>, which in turn needs MSBuild located exactly once per process before the
/// first workspace is created. Registering twice throws; registering late or not at all makes every
/// C# proposal fail with a confusing "semantic model unavailable".
///
/// <para>
/// These are build-contract assertions, not behaviour tests: they keep the three package references
/// and the single registration site in step so neither can be dropped without the other.
/// </para>
/// </summary>
public sealed class CSharpWorkspaceMSBuildRegistrationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string CliDirectory => Path.Combine(RepoRoot, "src", "Cli", "Microsoft.Maui.Cli");

    private static string ProposalServicePath => Path.Combine(
        CliDirectory, "DevFlow", "Inspector", "CSharpAutomationIdProposalService.cs");

    [Theory]
    [InlineData("Microsoft.CodeAnalysis.CSharp.Workspaces")]
    [InlineData("Microsoft.CodeAnalysis.Workspaces.MSBuild")]
    [InlineData("Microsoft.Build.Locator")]
    public void TheCliReferencesTheRoslynWorkspacePackagesTheCSharpProposalServiceNeeds(string package)
    {
        var csproj = XDocument.Load(Path.Combine(CliDirectory, "Microsoft.Maui.Cli.csproj"));
        var referenced = csproj.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Any(element => string.Equals(
                element.Attribute("Include")?.Value,
                package,
                StringComparison.Ordinal));

        Assert.True(referenced, $"Microsoft.Maui.Cli.csproj must reference {package}.");
    }

    /// <summary>
    /// Exactly one registration, guarded by <c>IsRegistered</c>, and reached before the first
    /// <c>MSBuildWorkspace.Create()</c>. A second unguarded call throws
    /// <see cref="InvalidOperationException"/> the moment two proposals overlap.
    /// </summary>
    [Fact]
    public void MSBuildIsLocatedExactlyOnceAndBeforeTheFirstWorkspaceIsCreated()
    {
        var source = File.ReadAllText(ProposalServicePath);

        Assert.Single(Regex.Matches(source, @"MSBuildLocator\.RegisterDefaults\(\)"));
        Assert.Single(Regex.Matches(source, @"MSBuildLocator\.IsRegistered"));

        var guard = source.IndexOf("MSBuildLocator.IsRegistered", StringComparison.Ordinal);
        var register = source.IndexOf("MSBuildLocator.RegisterDefaults()", StringComparison.Ordinal);
        Assert.InRange(guard, 0, register);

        var firstWorkspace = source.IndexOf("MSBuildWorkspace.Create()", StringComparison.Ordinal);
        Assert.True(firstWorkspace >= 0, "The C# proposal service must create an MSBuildWorkspace.");
        Assert.Contains("TryRegisterMSBuild(out var registrationError)", source, StringComparison.Ordinal);
        Assert.InRange(
            source.IndexOf("TryRegisterMSBuild(out var registrationError)", StringComparison.Ordinal),
            0,
            firstWorkspace);

        // Concurrent proposals must not race the one-shot registration.
        Assert.Contains("lock (WorkspaceRegistrationGate)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// No other CLI file may register MSBuild — a second site would be the double-registration bug
    /// in waiting.
    /// </summary>
    [Fact]
    public void NoOtherCliFileRegistersMSBuild()
    {
        var offenders = Directory
            .EnumerateFiles(CliDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, ProposalServicePath, StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("MSBuildLocator", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(CliDirectory, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FindRepoRoot()
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
