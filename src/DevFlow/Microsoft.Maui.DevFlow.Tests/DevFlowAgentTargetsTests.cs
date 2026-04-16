using System.Diagnostics;
using System.Security;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class DevFlowAgentTargetsTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly string _projectDirectory;

    public DevFlowAgentTargetsTests()
    {
        _projectDirectory = Path.Combine(Path.GetTempPath(), $"mauidevflow-msbuild-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectDirectory))
            Directory.Delete(_projectDirectory, true);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetDevFlowPort_DoesNotRewriteGeneratedFile_WhenInputsAreUnchanged(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetDevFlowPortTarget("/p:DevFlowPort=9225");

        Assert.True(File.Exists(GeneratedFilePath), $"Expected generated file at '{GeneratedFilePath}'.");
        Assert.Contains("\"DevFlowPort\", \"9225\"", File.ReadAllText(GeneratedFilePath));

        File.SetLastWriteTimeUtc(GeneratedFilePath, SentinelTimestampUtc);

        RunSetDevFlowPortTarget("/p:DevFlowPort=9225");

        Assert.Equal(SentinelTimestampUtc, File.GetLastWriteTimeUtc(GeneratedFilePath));
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetDevFlowPort_RewritesGeneratedFile_WhenPortPropertyChanges(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetDevFlowPortTarget("/p:DevFlowPort=9225");
        File.SetLastWriteTimeUtc(GeneratedFilePath, SentinelTimestampUtc);

        RunSetDevFlowPortTarget("/p:DevFlowPort=9333");

        Assert.NotEqual(SentinelTimestampUtc, File.GetLastWriteTimeUtc(GeneratedFilePath));

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"DevFlowPort\", \"9333\"", contents);
        Assert.DoesNotContain("\"DevFlowPort\", \"9225\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetDevFlowPort_EmitsBothLegacyAndNewAssemblyMetadata(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetDevFlowPortTarget("/p:DevFlowPort=9225");

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"DevFlowPort\", \"9225\"", contents);
        Assert.Contains("\"Microsoft.Maui.DevFlowPort\", \"9225\"", contents);
        Assert.Contains("\"DevFlowEnabled\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetDevFlowPort_HonorsLegacyMauiDevFlowPortProperty(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetDevFlowPortTarget("/p:MauiDevFlowPort=9226");

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"DevFlowPort\", \"9226\"", contents);
        Assert.Contains("\"Microsoft.Maui.DevFlowPort\", \"9226\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void ReadDevFlowConfig_PrefersNewDevFlowFilename(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);
        File.WriteAllText(NewConfigFilePath, """{"port": 9441}""");
        File.WriteAllText(LegacyConfigFilePath, """{"port": 9442}""");

        RunSetDevFlowPortTarget();

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"DevFlowPort\", \"9441\"", contents);
        Assert.DoesNotContain("\"DevFlowPort\", \"9442\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void ReadDevFlowConfig_FallsBackToLegacyMauiDevFlowFile(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);
        File.WriteAllText(LegacyConfigFilePath, """{"port": 9442}""");

        var (exitCode, stdout, _) = RunDotNetMsbuild("/t:_SetDevFlowPort");
        Assert.Equal(0, exitCode);

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"DevFlowPort\", \"9442\"", contents);
        Assert.Contains("is a legacy config filename", stdout);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void EnableDevFlow_AddsDevFlowSymbolToDefineConstants(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        var (exitCode, stdout, _) = RunDotNetMsbuild(
            "/t:_AddDevFlowCompilerSymbol",
            "/p:EnableDevFlow=true",
            "/getProperty:DefineConstants");

        Assert.Equal(0, exitCode);
        Assert.Contains("DEVFLOW", stdout);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void EnableDevFlow_HonorsCustomConstant(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        var (exitCode, stdout, _) = RunDotNetMsbuild(
            "/t:_AddDevFlowCompilerSymbol",
            "/p:EnableDevFlow=true",
            "/p:DevFlowConstant=MY_DEVFLOW",
            "/getProperty:DefineConstants");

        Assert.Equal(0, exitCode);
        Assert.Contains("MY_DEVFLOW", stdout);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void EnableDevFlow_DefaultsOffOutsideDebug(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        var (exitCode, stdout, _) = RunDotNetMsbuild(
            "/t:_AddDevFlowCompilerSymbol",
            "/p:Configuration=Release",
            "/getProperty:DefineConstants");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("DEVFLOW", stdout);
    }

    private string ProjectFilePath => Path.Combine(_projectDirectory, "Test.csproj");

    private string NewConfigFilePath => Path.Combine(_projectDirectory, ".devflow");

    private string LegacyConfigFilePath => Path.Combine(_projectDirectory, ".mauidevflow");

    private string GeneratedFilePath => Path.Combine(_projectDirectory, "obj", "Debug", "net10.0", "Microsoft.Maui.DevFlowPort.g.cs");

    private static DateTime SentinelTimestampUtc { get; } = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private void CreateTestProject(string relativeTargetPath)
    {
        var targetFilePath = Path.Combine(
            RepoRoot,
            "src",
            "DevFlow",
            "Microsoft.Maui.DevFlow.Agent",
            relativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

        var escapedTargetFilePath = SecurityElement.Escape(targetFilePath) ?? targetFilePath;

        File.WriteAllText(ProjectFilePath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Import Project="{{escapedTargetFilePath}}" />
            </Project>
            """);
    }

    private void RunSetDevFlowPortTarget(params string[] properties)
    {
        var args = new List<string> { "/t:_SetDevFlowPort" };
        args.AddRange(properties);
        var (exitCode, stdout, stderr) = RunDotNetMsbuild(args.ToArray());
        Assert.True(
            exitCode == 0,
            $"dotnet msbuild failed with exit code {exitCode}.{Environment.NewLine}{stdout}{stderr}");
    }

    private (int ExitCode, string Stdout, string Stderr) RunDotNetMsbuild(params string[] extraArgs)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _projectDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(ProjectFilePath);
        startInfo.ArgumentList.Add("/nologo");
        startInfo.ArgumentList.Add("/v:minimal");

        foreach (var arg in extraArgs)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return (process.ExitCode, output, error);
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
