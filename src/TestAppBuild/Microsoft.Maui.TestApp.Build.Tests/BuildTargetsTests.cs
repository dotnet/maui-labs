using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.TestApp.Build.Tests;

public sealed class BuildTargetsTests
{
    [Fact]
    public async Task ProjectReferenceMarkedAsMauiTestApp_BuildsAppAndWritesManifest()
    {
        using var workspace = TestWorkspace.Create();

        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiTestApp="true"
                              TargetFramework="net10.0" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertManifest(workspace, expectedName: "App");
    }

    [Fact]
    public async Task ProjectReferenceWithOutputItemType_BuildsAppAndWritesManifest()
    {
        using var workspace = TestWorkspace.Create();

        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              OutputItemType="MauiTestAppReference"
                              TargetFramework="net10.0"
                              ReferenceName="OutputItemTypeApp" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertManifest(workspace, expectedName: "OutputItemTypeApp");
    }

    private static async Task<ProcessResult> BuildWorkspaceAsync(TestWorkspace workspace, string projectReferenceXml)
    {
        workspace.WriteProjects(projectReferenceXml);

        return await RunDotNetAsync(
            workspace.Root,
            "build",
            workspace.TestProjectPath,
            "-v:minimal",
            "-p:RestorePackagesPath=" + Path.Combine(workspace.Root, "packages"));
    }

    private static void AssertManifest(TestWorkspace workspace, string expectedName)
    {
        var manifestPath = Path.Combine(workspace.TestProjectDirectory, "bin", "Debug", "net10.0", "maui-test-apps.json");
        Assert.True(File.Exists(manifestPath), "Expected manifest at " + manifestPath);

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var apps = document.RootElement.GetProperty("apps");
        var app = Assert.Single(apps.EnumerateArray());

        Assert.Equal(expectedName, app.GetProperty("name").GetString());
        Assert.Equal("net10.0", app.GetProperty("targetFramework").GetString());

        var artifactPath = app.GetProperty("path").GetString();
        Assert.False(string.IsNullOrWhiteSpace(artifactPath));
        Assert.True(File.Exists(artifactPath), "Expected app artifact at " + artifactPath);

        var projectPath = app.GetProperty("projectPath").GetString();
        Assert.Equal(Path.GetFullPath(workspace.AppProjectPath), Path.GetFullPath(projectPath!));
    }

    private static async Task<ProcessResult> RunDotNetAsync(string workingDirectory, params string[] arguments)
    {
        var output = new StringBuilder();
        using var process = new Process();
        process.StartInfo.FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, output.ToString());
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root)
        {
            Root = root;
            AppProjectDirectory = Path.Combine(root, "App");
            TestProjectDirectory = Path.Combine(root, "Tests");
            AppProjectPath = Path.Combine(AppProjectDirectory, "App.csproj");
            TestProjectPath = Path.Combine(TestProjectDirectory, "Tests.csproj");
        }

        public string Root { get; }

        public string AppProjectDirectory { get; }

        public string TestProjectDirectory { get; }

        public string AppProjectPath { get; }

        public string TestProjectPath { get; }

        public static TestWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "maui-test-app-build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestWorkspace(root);
        }

        public void WriteProjects(string projectReferenceXml)
        {
            Directory.CreateDirectory(AppProjectDirectory);
            Directory.CreateDirectory(TestProjectDirectory);

            File.WriteAllText(
                AppProjectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <ApplicationId>com.example.testapp</ApplicationId>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(
                Path.Combine(AppProjectDirectory, "Program.cs"),
                """
                System.Console.WriteLine("Hello from test app.");
                """);

            var repoRoot = FindRepoRoot();
            var propsPath = Path.Combine(repoRoot, "src", "TestAppBuild", "Microsoft.Maui.TestApp.Build", "build", "Microsoft.Maui.TestApp.Build.props");
            var targetsPath = Path.Combine(repoRoot, "src", "TestAppBuild", "Microsoft.Maui.TestApp.Build", "build", "Microsoft.Maui.TestApp.Build.targets");
            var outputRoot = Path.Combine(Root, "test-app-output") + Path.DirectorySeparatorChar;

            File.WriteAllText(
                TestProjectPath,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{{XmlEscape(propsPath)}}" />

                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <MauiTestAppOutputRoot>{{XmlEscape(outputRoot)}}</MauiTestAppOutputRoot>
                    <MauiTestAppGeneratedSourceNamespace>TestAppBuild.Generated</MauiTestAppGeneratedSourceNamespace>
                  </PropertyGroup>

                  <ItemGroup>
                {{Indent(projectReferenceXml, 4)}}
                  </ItemGroup>

                  <Import Project="{{XmlEscape(targetsPath)}}" />
                </Project>
                """);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
        }

        private static string Indent(string value, int spaces)
        {
            var prefix = new string(' ', spaces);
            return string.Join(Environment.NewLine, value.Split(["\r\n", "\n"], StringSplitOptions.None).Select(line => prefix + line));
        }

        private static string XmlEscape(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
