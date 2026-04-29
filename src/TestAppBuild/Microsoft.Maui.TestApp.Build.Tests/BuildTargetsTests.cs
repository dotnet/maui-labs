using System.Diagnostics;
using System.Text;

namespace Microsoft.Maui.TestApp.Build.Tests;

public sealed class BuildTargetsTests
{
    [Fact]
    public async Task ProjectReferenceMarkedAsMauiTestApp_BuildsAppAndExposesArtifactItem()
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
        AssertArtifactItem(workspace, expectedName: "App");
    }

    [Fact]
    public async Task ProjectReferenceWithOutputItemType_BuildsAppAndExposesArtifactItem()
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
        AssertArtifactItem(workspace, expectedName: "OutputItemTypeApp");
    }

    [Fact]
    public async Task ProjectReferenceWithAppBundleDirectory_ExposesAppArtifactItem()
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
                              TargetFramework="net10.0"
                              ReferenceName="IosStyleApp"
                              Properties="MauiTestAppSimulateAppBundle=true" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(
            workspace,
            expectedName: "IosStyleApp",
            expectedArtifactType: "app",
            expectSingleArtifact: false,
            expectedArtifactIsDirectory: true);
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

    private static void AssertArtifactItem(
        TestWorkspace workspace,
        string expectedName,
        string expectedArtifactType = "dll",
        bool expectSingleArtifact = true,
        bool expectedArtifactIsDirectory = false)
    {
        var artifactsPath = Path.Combine(workspace.TestProjectDirectory, "maui-test-app-artifacts.txt");
        Assert.True(File.Exists(artifactsPath), "Expected artifact capture at " + artifactsPath);

        var lines = File.ReadAllLines(artifactsPath);
        if (expectSingleArtifact)
            Assert.Single(lines);

        var line = Assert.Single(lines, line =>
        {
            var parts = line.Split('|');
            return parts.Length == 6 && parts[0] == expectedName && parts[4] == expectedArtifactType;
        });
        var parts = line.Split('|');

        Assert.Equal(6, parts.Length);
        Assert.Equal(expectedName, parts[0]);
        if (expectedArtifactIsDirectory)
            Assert.True(Directory.Exists(parts[1]), "Expected app artifact directory at " + parts[1]);
        else
            Assert.True(File.Exists(parts[1]), "Expected app artifact file at " + parts[1]);

        Assert.Equal(Path.GetFullPath(workspace.AppProjectPath), Path.GetFullPath(parts[2]));
        Assert.Equal("net10.0", parts[3]);
        Assert.Equal(expectedArtifactType, parts[4]);
        Assert.Equal("com.example.testapp", parts[5]);

        var artifactPathsFile = Path.Combine(workspace.TestProjectDirectory, "maui-test-app-artifact-paths.txt");
        Assert.True(File.Exists(artifactPathsFile), "Expected artifact paths capture at " + artifactPathsFile);
        Assert.Contains(Path.GetFullPath(parts[1]), File.ReadAllText(artifactPathsFile), StringComparison.Ordinal);

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

                  <Target Name="CreateFakeAppBundle"
                          AfterTargets="Build"
                          Condition="'$(MauiTestAppSimulateAppBundle)' == 'true' and '$(AppBundleDir)' != ''">
                    <MakeDir Directories="$(AppBundleDir)" />
                    <WriteLinesToFile File="$([System.IO.Path]::Combine('$(AppBundleDir)', 'Info.plist'))"
                                      Lines="Fake bundle for tests."
                                      Overwrite="true" />
                  </Target>
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
                  </PropertyGroup>

                  <ItemGroup>
                {{Indent(projectReferenceXml, 4)}}
                  </ItemGroup>

                  <Target Name="CaptureMauiTestAppArtifacts"
                          AfterTargets="BuildMauiTestApps"
                          Condition="'@(MauiTestAppArtifact)' != ''">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)\maui-test-app-artifacts.txt"
                                      Lines="@(MauiTestAppArtifact->'%(ReferenceName)|%(Identity)|%(ProjectPath)|%(TargetFramework)|%(ArtifactType)|%(ApplicationId)')"
                                      Overwrite="true" />
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)\maui-test-app-artifact-paths.txt"
                                      Lines="$(MauiTestAppArtifactPaths)"
                                      Overwrite="true" />
                  </Target>

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
