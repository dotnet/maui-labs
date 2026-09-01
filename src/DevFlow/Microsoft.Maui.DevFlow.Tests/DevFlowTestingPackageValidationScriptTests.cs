namespace Microsoft.Maui.DevFlow.Tests;

public sealed class DevFlowTestingPackageValidationScriptTests
{
    [Fact]
    public void PackageDiscoverySearchesNestedArcadeOutputDirectories()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tests",
            "DevFlow",
            "PackageConsumer",
            "Validate-TestingPackage.ps1"));

        Assert.Contains(
            "Get-ChildItem -Path $Directory -File -Recurse -Filter '*.nupkg'",
            script,
            StringComparison.Ordinal);

        var packages = System.Xml.Linq.XDocument.Load(Path.Combine(
            repositoryRoot,
            "Directory.Packages.props"));
        var testingPackage = Assert.Single(
            packages.Descendants(),
            element => element.Name.LocalName == "PackageVersion" &&
                string.Equals(
                    element.Attribute("Include")?.Value,
                    "Microsoft.Maui.DevFlow.Testing",
                    StringComparison.Ordinal));

        Assert.Equal(
            "$(DevFlowTestingPackageVersion)",
            testingPackage.Attribute("Version")?.Value);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
