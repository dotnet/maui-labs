using System.Diagnostics;
using System.Text;

namespace Microsoft.Maui.Build.AppProjectReference.Tests;

public sealed class BuildTargetsTests
{
    private static readonly TimeSpan DotNetCommandTimeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ProjectReferenceMarkedAsMauiAppProjectReference_BuildsAppAndExposesArtifactItem()
    {
        using var workspace = TestWorkspace.Create();

        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(workspace, expectedName: "App");
    }

    [Fact]
    public async Task MauiAppProjectReference_OneLine_BuildsAppAndExposesArtifactItem()
    {
        using var workspace = TestWorkspace.Create();

        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <MauiAppProjectReference Include="..\App\App.csproj"
                                     TargetFramework="net10.0" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(workspace, expectedName: "App");
    }

    [Fact]
    public async Task MauiAppProjectReference_DefaultOutputRoot_IsRelativeToHostProject()
    {
        using var workspace = TestWorkspace.Create();

        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <MauiAppProjectReference Include="..\App\App.csproj"
                                     TargetFramework="net10.0" />
            """,
            setOutputRoot: false);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(workspace, expectedName: "App");

        var artifactPath = Path.GetFullPath(GetSingleArtifactPath(workspace));
        var expectedRoot = Path.GetFullPath(Path.Combine(workspace.TestProjectDirectory, "obj", "maui-app-refs", "App", "net10.0")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(expectedRoot, artifactPath, PathComparison);
    }

    [Fact]
    public async Task MauiAppProjectReference_ExplicitOutputRootWithoutTrailingSlash_AddsSeparator()
    {
        using var workspace = TestWorkspace.Create();
        var outputRoot = Path.Combine(workspace.Root, "explicit-output");

        var result = await BuildWorkspaceAsync(
            workspace,
            $$"""
            <MauiAppProjectReference Include="..\App\App.csproj"
                                     TargetFramework="net10.0"
                                     OutputRoot="{{TestWorkspace.XmlEscape(outputRoot)}}" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(workspace, expectedName: "App");

        var artifactPath = Path.GetFullPath(GetSingleArtifactPath(workspace));
        var expectedRoot = Path.GetFullPath(outputRoot) + Path.DirectorySeparatorChar;
        Assert.StartsWith(expectedRoot, artifactPath, PathComparison);
        Assert.False(Directory.Exists(outputRoot + "bin"), "OutputRoot should not be concatenated directly with platform output subdirectories.");
    }

    [Fact]
    public async Task MauiAppProjectReference_AppearsInProjectGraphAsProjectReference()
    {
        using var workspace = TestWorkspace.Create();

        // Use a full build (not just evaluation) and assert the App project actually
        // got referenced/built. NuGet restore creating App's project.assets.json proves
        // the synthesized ProjectReference edge was visible to the restore graph.
        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <MauiAppProjectReference Include="..\App\App.csproj"
                                     TargetFramework="net10.0" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);

        var appAssets = Path.Combine(workspace.AppProjectDirectory, "obj", "project.assets.json");
        Assert.True(File.Exists(appAssets), $"NuGet restore did not produce assets file for App, indicating the synthesized ProjectReference was not visible to the restore graph. Build output:\n{result.Output}");
    }

    [Fact]
    public async Task MauiAppProjectReference_UserMetadataOverridesDefaults()
    {
        using var workspace = TestWorkspace.Create();

        // User overrides the implicit default of PrivateAssets=all with PrivateAssets=none.
        // Build still has to succeed and the artifact still has to be discovered.
        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <MauiAppProjectReference Include="..\App\App.csproj"
                                     TargetFramework="net10.0"
                                     PrivateAssets="none"
                                     ReferenceName="OverrideApp" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(workspace, expectedName: "OverrideApp");
    }

    [Fact]
    public async Task ProjectReferenceWithoutTargetFramework_UsesAppProjectTargetFramework()
    {
        using var workspace = TestWorkspace.Create();

        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(workspace, expectedName: "App");

        var artifactsText = File.ReadAllText(Path.Combine(workspace.TestProjectDirectory, "maui-test-app-artifacts.txt"));
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}bin", artifactsText, StringComparison.Ordinal);
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
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0"
                              ReferenceName="IosStyleApp"
                              Properties="MauiAppRefSimulateAppBundle=true" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(
            workspace,
            expectedName: "IosStyleApp",
            expectedArtifactType: "app",
            expectedInstallable: true,
            expectedLaunchable: true,
            expectSingleArtifact: false,
            expectedArtifactIsDirectory: true);
    }

    [Fact]
    public async Task ProjectReferenceWithTrailingSlashAppBundleDirectory_ExposesAppArtifactItem()
    {
        using var workspace = TestWorkspace.Create();
        var appBundleDir = TestWorkspace.XmlEscape(Path.Combine(workspace.Root, "custom-output", "TrailingSlashApp.app") + Path.DirectorySeparatorChar);

        var result = await BuildWorkspaceAsync(
            workspace,
            $$"""
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0"
                              ReferenceName="TrailingSlashApp"
                              SetPlatformOutputPaths="false"
                              Properties="MauiAppRefSimulateAppBundle=true;AppBundleDir={{appBundleDir}}" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(
            workspace,
            expectedName: "TrailingSlashApp",
            expectedArtifactType: "app",
            expectedInstallable: true,
            expectedLaunchable: true,
            expectSingleArtifact: false,
            expectedArtifactIsDirectory: true);
    }

    [Fact]
    public async Task ProjectReferenceWithTrailingSlashPublishDirectory_ExposesPublishDirectoryArtifactItem()
    {
        using var workspace = TestWorkspace.Create();
        var customAfterTargetsPath = TestWorkspace.XmlEscape(workspace.CustomAfterTargetsPath);

        var result = await BuildWorkspaceAsync(
            workspace,
            $$"""
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0"
                              ReferenceName="PublishDirApp"
                              Properties="CustomAfterMicrosoftCommonTargets={{customAfterTargetsPath}}" />
            """,
            customAfterTargetsXml:
            """
            <Project>
              <Target Name="CreateFakePublishDirectory"
                      AfterTargets="Build"
                      Condition="'$(PublishDir)' != ''">
                <MakeDir Directories="$(PublishDir)" />
              </Target>
            </Project>
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(
            workspace,
            expectedName: "PublishDirApp",
            expectedArtifactType: "publish-directory",
            expectSingleArtifact: false,
            expectedArtifactIsDirectory: true);
    }

    [Fact]
    public async Task ProjectReferenceWithAppInstaller_ExposesAppInstallerArtifactType()
    {
        using var workspace = TestWorkspace.Create();

        var result = await BuildWorkspaceAsync(
            workspace,
            """
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0"
                              ReferenceName="WindowsStyleApp"
                              Properties="MauiAppRefSimulateAppInstaller=true" />
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(
            workspace,
            expectedName: "WindowsStyleApp",
            expectedArtifactType: "appinstaller",
            expectSingleArtifact: false);
    }

    [Fact]
    public async Task ProjectReferenceWithCustomAfterTargets_PreservesCustomTargetAndInjectsArtifactTarget()
    {
        using var workspace = TestWorkspace.Create();
        var customAfterTargetsPath = TestWorkspace.XmlEscape(workspace.CustomAfterTargetsPath);

        var result = await BuildWorkspaceAsync(
            workspace,
            $$"""
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0"
                              Properties="CustomAfterMicrosoftCommonTargets={{customAfterTargetsPath}};MauiAppRefAppTargetsPath=missing.targets" />
            """,
            customAfterTargetsXml:
            """
            <Project>
              <Target Name="RecordCustomAfterImport" BeforeTargets="Build">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)\custom-after-imported.txt"
                                  Lines="imported"
                                  Overwrite="true" />
              </Target>
            </Project>
            """);

        Assert.True(result.ExitCode == 0, result.Output);
        AssertArtifactItem(workspace, expectedName: "App");
        Assert.True(File.Exists(Path.Combine(workspace.AppProjectDirectory, "custom-after-imported.txt")), result.Output);
    }

    [Fact]
    public async Task CleanAppProjectReferenceArtifacts_SkipsOutputRootOutsideBaseIntermediatePath()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteProjects(
            """
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0" />
            """);

        var outsideOutputRoot = Path.Combine(workspace.Root, "outside-output") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(outsideOutputRoot);
        File.WriteAllText(Path.Combine(outsideOutputRoot, "keep.txt"), "keep");

        var result = await RunDotNetAsync(
            workspace.Root,
            "msbuild",
            workspace.TestProjectPath,
            "-t:Clean",
            "-v:minimal",
            "-p:MauiAppRefOutputRoot=" + outsideOutputRoot,
            "-p:RestorePackagesPath=" + Path.Combine(workspace.Root, "packages"));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(Path.Combine(outsideOutputRoot, "keep.txt")), result.Output);
        Assert.Contains("Skipping MAUI app reference artifact clean", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanAppProjectReferenceArtifacts_SkipsDifferentCasedOutputRootOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var workspace = TestWorkspace.Create();
        workspace.WriteProjects(
            """
            <ProjectReference Include="..\App\App.csproj"
                              ReferenceOutputAssembly="false"
                              BuildReference="false"
                              PrivateAssets="all"
                              MauiAppProjectReference="true"
                              TargetFramework="net10.0" />
            """);

        var baseIntermediateOutputPath = Path.Combine(workspace.Root, "obj") + Path.DirectorySeparatorChar;
        var differentCasedOutputRoot = Path.Combine(workspace.Root, "OBJ", "case-output") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(differentCasedOutputRoot);
        File.WriteAllText(Path.Combine(differentCasedOutputRoot, "keep.txt"), "keep");

        var result = await RunDotNetAsync(
            workspace.Root,
            "msbuild",
            workspace.TestProjectPath,
            "-t:Clean",
            "-v:minimal",
            "-p:BaseIntermediateOutputPath=" + baseIntermediateOutputPath,
            "-p:MauiAppRefOutputRoot=" + differentCasedOutputRoot,
            "-p:RestorePackagesPath=" + Path.Combine(workspace.Root, "packages"));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(Path.Combine(differentCasedOutputRoot, "keep.txt")), result.Output);
        Assert.Contains("Skipping MAUI app reference artifact clean", result.Output, StringComparison.Ordinal);
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static async Task<ProcessResult> BuildWorkspaceAsync(TestWorkspace workspace, string projectReferenceXml, bool setOutputRoot = true)
        => await BuildWorkspaceAsync(workspace, projectReferenceXml, customAfterTargetsXml: null, setOutputRoot: setOutputRoot);

    private static async Task<ProcessResult> BuildWorkspaceAsync(
        TestWorkspace workspace,
        string projectReferenceXml,
        string? customAfterTargetsXml,
        bool setOutputRoot = true)
    {
        workspace.WriteProjects(projectReferenceXml, customAfterTargetsXml, setOutputRoot);

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
        bool expectedInstallable = false,
        bool expectedLaunchable = false,
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
            return parts.Length == 8 && parts[0] == expectedName && parts[4] == expectedArtifactType;
        });
        var parts = line.Split('|');

        Assert.Equal(8, parts.Length);
        Assert.Equal(expectedName, parts[0]);
        if (expectedArtifactIsDirectory)
            Assert.True(Directory.Exists(parts[1]), "Expected app artifact directory at " + parts[1]);
        else
            Assert.True(File.Exists(parts[1]), "Expected app artifact file at " + parts[1]);

        Assert.Equal(Path.GetFullPath(workspace.AppProjectPath), Path.GetFullPath(parts[2]));
        Assert.Equal("net10.0", parts[3]);
        Assert.Equal(expectedArtifactType, parts[4]);
        Assert.Equal("com.example.testapp", parts[5]);
        Assert.Equal(expectedInstallable.ToString().ToLowerInvariant(), parts[6]);
        Assert.Equal(expectedLaunchable.ToString().ToLowerInvariant(), parts[7]);

        var artifactPathsFile = Path.Combine(workspace.TestProjectDirectory, "maui-test-app-artifact-paths.txt");
        Assert.True(File.Exists(artifactPathsFile), "Expected artifact paths capture at " + artifactPathsFile);
        Assert.Contains(Path.GetFullPath(parts[1]), File.ReadAllText(artifactPathsFile), StringComparison.Ordinal);

    }

    private static string GetSingleArtifactPath(TestWorkspace workspace)
    {
        var artifactsPath = Path.Combine(workspace.TestProjectDirectory, "maui-test-app-artifacts.txt");
        var line = Assert.Single(File.ReadAllLines(artifactsPath));
        var parts = line.Split('|');
        Assert.Equal(8, parts.Length);
        return parts[1];
    }

    private static async Task<ProcessResult> RunDotNetAsync(string workingDirectory, params string[] arguments)
    {
        var output = new StringBuilder();
        var outputLock = new object();
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
            {
                lock (outputLock)
                    output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                    output.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(DotNetCommandTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            lock (outputLock)
            {
                output.AppendLine();
                output.AppendLine($"Command timed out after {DotNetCommandTimeout.TotalMinutes:0} minutes: {process.StartInfo.FileName} {string.Join(" ", arguments)}");
            }

            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await process.WaitForExitAsync();
        }

        lock (outputLock)
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
            CustomAfterTargetsPath = Path.Combine(root, "custom-after.targets");
        }

        public string Root { get; }

        public string AppProjectDirectory { get; }

        public string TestProjectDirectory { get; }

        public string AppProjectPath { get; }

        public string TestProjectPath { get; }

        public string CustomAfterTargetsPath { get; }

        public static TestWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "maui-test-app-build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestWorkspace(root);
        }

        public void WriteProjects(string projectReferenceXml, string? customAfterTargetsXml = null, bool setOutputRoot = true)
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
                          Condition="'$(MauiAppRefSimulateAppBundle)' == 'true' and '$(AppBundleDir)' != ''">
                    <MakeDir Directories="$(AppBundleDir)" />
                    <WriteLinesToFile File="$([System.IO.Path]::Combine('$(AppBundleDir)', 'Info.plist'))"
                                      Lines="Fake bundle for tests."
                                      Overwrite="true" />
                  </Target>

                  <Target Name="CreateFakeAppInstaller"
                          AfterTargets="Build"
                          Condition="'$(MauiAppRefSimulateAppInstaller)' == 'true' and '$(MauiAppRefOutputRoot)' != ''">
                    <MakeDir Directories="$(MauiAppRefOutputRoot)" />
                    <WriteLinesToFile File="$([System.IO.Path]::Combine('$(MauiAppRefOutputRoot)', '$(MSBuildProjectName).appinstaller'))"
                                      Lines="Fake appinstaller for tests."
                                      Overwrite="true" />
                  </Target>
                </Project>
                """);

            File.WriteAllText(
                Path.Combine(AppProjectDirectory, "Program.cs"),
                """
                System.Console.WriteLine("Hello from test app.");
                """);

            if (customAfterTargetsXml is not null)
                File.WriteAllText(CustomAfterTargetsPath, customAfterTargetsXml);

            var repoRoot = FindRepoRoot();
            var propsPath = Path.Combine(repoRoot, "src", "AppProjectReference", "Microsoft.Maui.Build.AppProjectReference", "build", "Microsoft.Maui.Build.AppProjectReference.props");
            var targetsPath = Path.Combine(repoRoot, "src", "AppProjectReference", "Microsoft.Maui.Build.AppProjectReference", "build", "Microsoft.Maui.Build.AppProjectReference.targets");
            var outputRoot = Path.Combine(Root, "test-app-output") + Path.DirectorySeparatorChar;
            var outputRootProperty = setOutputRoot
                ? $"    <MauiAppRefOutputRoot>{XmlEscape(outputRoot)}</MauiAppRefOutputRoot>"
                : "";

            File.WriteAllText(
                TestProjectPath,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{{XmlEscape(propsPath)}}" />

                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                {{outputRootProperty}}
                  </PropertyGroup>

                  <ItemGroup>
                {{Indent(projectReferenceXml, 4)}}
                  </ItemGroup>

                  <Target Name="CaptureMauiAppArtifacts"
                          AfterTargets="BuildAppProjectReferences"
                          Condition="'@(MauiAppArtifact)' != ''">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)\maui-test-app-artifacts.txt"
                                      Lines="@(MauiAppArtifact->'%(ReferenceName)|%(Identity)|%(ProjectPath)|%(TargetFramework)|%(ArtifactType)|%(ApplicationId)|%(Installable)|%(Launchable)')"
                                      Overwrite="true" />
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)\maui-test-app-artifact-paths.txt"
                                      Lines="$(MauiAppArtifactPaths)"
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

        public static string XmlEscape(string value)
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
