using System.Diagnostics;
using System.Security;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// <c>Microsoft.Maui.DevFlow.Agent.Core</c> ships two buildTransitive assets that carry the XAML
/// source-map contract to consumers: the props file decides whether source maps are on, and the
/// targets file promotes MAUI's XAML <c>AdditionalFiles</c> so the generator can see them.
///
/// <para>
/// Three things reference those exact paths — the package's own <c>Pack</c> items and the explicit
/// <c>&lt;Import&gt;</c> in each of the three DevFlow samples. If a file is missing, packing drops
/// the contract silently while every sample fails to evaluate with MSB4019. These tests pin all
/// four references to the files that actually exist on disk.
/// </para>
/// </summary>
public sealed class MauiDevFlowAgentCoreBuildTransitiveTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly string _projectDirectory;

    public MauiDevFlowAgentCoreBuildTransitiveTests()
    {
        _projectDirectory = Path.Combine(Path.GetTempPath(), $"devflow-buildtransitive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectDirectory))
            Directory.Delete(_projectDirectory, true);
    }

    private static string AgentCoreDirectory =>
        Path.Combine(RepoRoot, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Core");

    private static string BuildTransitiveFile(string name) =>
        Path.Combine(AgentCoreDirectory, "buildTransitive", name);

    [Theory]
    [InlineData("Microsoft.Maui.DevFlow.Agent.Core.props")]
    [InlineData("Microsoft.Maui.DevFlow.Agent.Core.targets")]
    public void TheBuildTransitiveAssetTheProjectPacksExistsOnDisk(string fileName)
    {
        var path = BuildTransitiveFile(fileName);
        Assert.True(File.Exists(path), $"Expected the packed buildTransitive asset at '{path}'.");

        // A packed asset that MSBuild cannot parse fails every consumer at evaluation time.
        var document = XDocument.Load(path);
        Assert.Equal("Project", document.Root?.Name.LocalName);

        var csproj = XDocument.Load(
            Path.Combine(AgentCoreDirectory, "Microsoft.Maui.DevFlow.Agent.Core.csproj"));
        var packed = csproj.Descendants()
            .Where(element => element.Name.LocalName == "None")
            .Any(element =>
                (element.Attribute("Include")?.Value ?? string.Empty)
                    .Replace('\\', '/')
                    .EndsWith($"buildTransitive/{fileName}", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase) &&
                (element.Attribute("PackagePath")?.Value ?? string.Empty)
                    .Replace('\\', '/')
                    .StartsWith("buildTransitive", StringComparison.Ordinal));
        Assert.True(packed, $"Agent.Core.csproj must pack buildTransitive/{fileName} into buildTransitive/.");
    }

    /// <summary>
    /// A ProjectReference build does not import a referenced package's buildTransitive assets, so
    /// each sample imports them explicitly. Every one of those import paths must resolve.
    /// </summary>
    [Theory]
    [InlineData("samples/DevFlow.Sample/DevFlow.Sample.csproj")]
    [InlineData("samples/DevFlow.Sample.Linux/DevFlow.Sample.Linux.csproj")]
    [InlineData("samples/DevFlow.Sample.MacOS/DevFlow.Sample.MacOS.csproj")]
    public void EverySampleImportOfTheAgentCoreBuildTransitiveAssetsResolves(string relativeProjectPath)
    {
        var projectPath = Path.Combine(RepoRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(projectPath), projectPath);

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var imports = XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "Import")
            .Select(element => element.Attribute("Project")?.Value ?? string.Empty)
            .Where(value => value.Replace('\\', '/').Contains("Agent.Core/buildTransitive/", StringComparison.Ordinal))
            .ToArray();

        // Both halves matter: the props file decides the gate, the targets file acts on it.
        Assert.Equal(2, imports.Length);
        foreach (var import in imports)
        {
            var resolved = Path.GetFullPath(
                Path.Combine(projectDirectory, import.Replace('\\', Path.DirectorySeparatorChar)));
            Assert.True(File.Exists(resolved), $"{relativeProjectPath} imports a missing file: {resolved}");
        }
    }

    /// <summary>
    /// Evaluating the two assets together must produce the documented gate: on for a Debug build,
    /// off for Release so shipping builds never embed XAML text or developer-machine paths.
    /// </summary>
    [Theory]
    [InlineData("Debug", "true")]
    [InlineData("Release", "false")]
    public void EvaluatingBothAssetsGatesSourceMapsOnConfiguration(string configuration, string expected)
    {
        var projectPath = CreateImportingProject();

        var value = EvaluateProperty(projectPath, "DevFlowXamlSourceMapsEnabled", configuration);

        Assert.Equal(expected, value);
    }

    /// <summary>
    /// The generator can only distinguish the XAML it should map from any other
    /// <c>AdditionalFiles</c> if the targets file makes the <c>DevFlowXaml</c> metadata compiler
    /// visible. Losing this line makes the source map silently empty rather than failing the build.
    /// </summary>
    [Fact]
    public void TheTargetsFileMakesTheDevFlowXamlMetadataCompilerVisible()
    {
        var targets = XDocument.Load(BuildTransitiveFile("Microsoft.Maui.DevFlow.Agent.Core.targets"));

        var visible = targets.Descendants()
            .Where(element => element.Name.LocalName == "CompilerVisibleItemMetadata")
            .Any(element =>
                element.Attribute("Include")?.Value == "AdditionalFiles" &&
                element.Attribute("MetadataName")?.Value == "DevFlowXaml");
        Assert.True(visible, "The targets file must expose AdditionalFiles/DevFlowXaml to the generator.");

        var promotes = targets.Descendants()
            .Any(element => element.Name.LocalName == "Target" &&
                element.Attribute("Name")?.Value == "_DevFlowPromoteXamlSourceMaps");
        Assert.True(promotes, "The targets file must promote MAUI's XAML AdditionalFiles entries.");
    }

    private string CreateImportingProject()
    {
        var props = SecurityElement.Escape(BuildTransitiveFile("Microsoft.Maui.DevFlow.Agent.Core.props"))!;
        var targets = SecurityElement.Escape(BuildTransitiveFile("Microsoft.Maui.DevFlow.Agent.Core.targets"))!;
        var projectPath = Path.Combine(_projectDirectory, "Consumer.csproj");
        File.WriteAllText(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="{props}" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Import Project="{targets}" />
            </Project>
            """);
        return projectPath;
    }

    private string EvaluateProperty(string projectPath, string property, string configuration)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _projectDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add($"/getProperty:{property}");
        startInfo.ArgumentList.Add($"/p:Configuration={configuration}");
        startInfo.ArgumentList.Add("/nologo");

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"dotnet msbuild failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{error}");
        return output.Trim();
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
