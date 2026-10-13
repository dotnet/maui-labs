using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

public class SampleProjectWiringTests
{
    [Theory]
    [InlineData("Linux")]
    [InlineData("MacOS")]
    [InlineData("WPF")]
    public void CustomHead_LinksEveryLocalShellPageAndControl(string head)
    {
        var root = FindRepoRoot();
        var projectPath = Path.Combine(root, "samples", $"DevFlow.Sample.{head}", $"DevFlow.Sample.{head}.csproj");
        var project = XDocument.Load(projectPath);
        var shellPath = Path.Combine(root, "samples", "DevFlow.Sample", "AppShell.xaml");
        var templates = XDocument.Load(shellPath).Descendants()
            .Attributes("ContentTemplate")
            .Select(attribute => Regex.Match(attribute.Value, @"^\{DataTemplate local:(\w+)\}$"))
            .ToArray();

        Assert.NotEmpty(templates);
        Assert.All(templates, match => Assert.True(match.Success, "Unexpected Shell ContentTemplate syntax."));

        foreach (var template in templates)
        {
            var page = template.Groups[1].Value;
            var pagePath = AssertLinkedFile(project, projectPath, "MauiXaml", $"{page}.xaml");
            AssertLinkedFile(project, projectPath, "Compile", $"{page}.xaml.cs");

            var xaml = XDocument.Load(pagePath);
            var localNamespaces = xaml.Root!.Attributes()
                .Where(attribute => attribute.IsNamespaceDeclaration && attribute.Value == "clr-namespace:DevFlow.Sample")
                .Select(attribute => attribute.Value)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var control in xaml.Descendants()
                .Where(element => localNamespaces.Contains(element.Name.NamespaceName))
                .Select(element => element.Name.LocalName)
                .Distinct())
            {
                AssertLinkedFile(project, projectPath, "Compile", $"{control}.cs");
            }
        }
    }

    [Theory]
    [InlineData("Linux")]
    [InlineData("MacOS")]
    [InlineData("WPF")]
    public void CustomHead_LinksAndRegistersPreferenceDiagnostics(string head)
    {
        var directory = Path.Combine(FindRepoRoot(), "samples", $"DevFlow.Sample.{head}");
        var projectPath = Path.Combine(directory, $"DevFlow.Sample.{head}.csproj");
        AssertLinkedFile(XDocument.Load(projectPath), projectPath, "Compile", "PreferenceDiagnostics.cs");

        var program = File.ReadAllText(Path.Combine(directory, "MauiProgram.cs"));
        Assert.Matches("""
            options\.RegisterExtension\(\s*"com\.example\.diagnostics"\s*,\s*"Sample diagnostics extension"\s*,\s*"1\.0\.0"\s*,\s*new\[\]\s*\{\s*"seed_pref"\s*\}\s*\)
            """, program);
        Assert.Contains("PreferenceDiagnostics.Register(diagnostics);", program);
    }

    [Fact]
    public void LinuxHead_RegistersThePlatformEssentialsImplementations()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "samples", "DevFlow.Sample.Linux", "MauiProgram.cs"));
        Assert.Contains(".AddLinuxGtk4Essentials()", program);
    }

    [Theory]
    [InlineData("Linux")]
    [InlineData("MacOS")]
    [InlineData("WPF")]
    public void CustomHead_EnablesTheLayoutDiagnosticsUnderTest(string head)
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "samples", $"DevFlow.Sample.{head}", "MauiProgram.cs"));
        Assert.Contains("options.EnableLayoutDiagnostics = true;", program);
    }

    [Fact]
    public void NativeAndroid_ImportsThePackagedAgentMetadataTargets()
    {
        var root = FindRepoRoot();
        var projectPath = Path.Combine(root, "samples", "DevFlow.Sample.Native", "Android", "DevFlow.Sample.Native.Android.csproj");
        var project = XDocument.Load(projectPath);
        var targetPath = Path.Combine(root, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent",
            "buildTransitive", "Microsoft.Maui.DevFlow.Agent.targets");

        Assert.Contains(project.Descendants("Import"), import =>
            ResolvePath(projectPath, (string)import.Attribute("Project")!) == targetPath);
        Assert.True(File.Exists(targetPath));

        var nativePackage = XDocument.Load(Path.Combine(root, "src", "DevFlow",
            "Microsoft.Maui.DevFlow.Agent.Native", "Microsoft.Maui.DevFlow.Agent.Native.csproj"));
        Assert.Contains(nativePackage.Descendants("None"), item =>
            ((string?)item.Attribute("Include"))?.Replace('\\', '/')
                == "../Microsoft.Maui.DevFlow.Agent/buildTransitive/Microsoft.Maui.DevFlow.Agent.targets"
            && (string?)item.Attribute("Pack") == "true");
    }

    static string AssertLinkedFile(XDocument project, string projectPath, string itemType, string filename)
    {
        var item = Assert.Single(project.Descendants(itemType),
            item => (string?)item.Attribute("Link") == filename);
        var sourcePath = ResolvePath(projectPath, (string)item.Attribute("Include")!);
        Assert.Equal(Path.Combine(FindRepoRoot(), "samples", "DevFlow.Sample", filename), sourcePath);
        Assert.True(File.Exists(sourcePath), $"Linked source does not exist: {sourcePath}");
        return sourcePath;
    }

    static string ResolvePath(string projectPath, string relativePath)
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, relativePath.Replace('\\', '/')));

    static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the maui-labs repository.");
    }
}
