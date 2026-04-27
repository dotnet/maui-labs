using System.Diagnostics;
using System.Security;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiDevFlowAgentTargetsTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly string _projectDirectory;

    public MauiDevFlowAgentTargetsTests()
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
    public void SetMauiDevFlowPort_DoesNotRewriteGeneratedFile_WhenInputsAreUnchanged(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget("/p:MauiDevFlowPort=9225");

        Assert.True(File.Exists(GeneratedFilePath), $"Expected generated file at '{GeneratedFilePath}'.");
        Assert.Contains("\"Microsoft.Maui.DevFlowPort\", \"9225\"", File.ReadAllText(GeneratedFilePath));

        File.SetLastWriteTimeUtc(GeneratedFilePath, SentinelTimestampUtc);

        RunSetMauiDevFlowPortTarget("/p:MauiDevFlowPort=9225");

        Assert.Equal(SentinelTimestampUtc, File.GetLastWriteTimeUtc(GeneratedFilePath));
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetMauiDevFlowPort_RewritesGeneratedFile_WhenPortPropertyChanges(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget("/p:MauiDevFlowPort=9225");
        File.SetLastWriteTimeUtc(GeneratedFilePath, SentinelTimestampUtc);

        RunSetMauiDevFlowPortTarget("/p:MauiDevFlowPort=9333");

        Assert.NotEqual(SentinelTimestampUtc, File.GetLastWriteTimeUtc(GeneratedFilePath));

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"Microsoft.Maui.DevFlowPort\", \"9333\"", contents);
        Assert.DoesNotContain("\"Microsoft.Maui.DevFlowPort\", \"9225\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void ReadMauiDevFlowConfig_RewritesGeneratedFile_WhenConfigChanges(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);
        File.WriteAllText(ConfigFilePath, """
            {
              "port": 9225
            }
            """);

        RunSetMauiDevFlowPortTarget();
        File.SetLastWriteTimeUtc(GeneratedFilePath, SentinelTimestampUtc);

        File.WriteAllText(ConfigFilePath, """
            {
              "port": 9333
            }
            """);

        RunSetMauiDevFlowPortTarget();

        Assert.NotEqual(SentinelTimestampUtc, File.GetLastWriteTimeUtc(GeneratedFilePath));

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"Microsoft.Maui.DevFlowPort\", \"9333\"", contents);
        Assert.DoesNotContain("\"Microsoft.Maui.DevFlowPort\", \"9225\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetMauiDevFlowPort_EmitsSessionId_DerivedFromProjectPath(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget();

        var contents = File.ReadAllText(GeneratedFilePath);
        var expectedSessionId = ComputeExpectedSessionId(ProjectFilePath);
        Assert.Contains($"\"Microsoft.Maui.DevFlowSessionId\", \"{expectedSessionId}\"", contents);
        Assert.DoesNotContain("Microsoft.Maui.DevFlowBaseApplicationId", contents);
        Assert.DoesNotContain("Microsoft.Maui.DevFlowApplicationId", contents);
        Assert.DoesNotContain("Microsoft.Maui.DevFlowDebugAppIdentitySuffix", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetMauiDevFlowPort_UsesExplicitSessionId_WhenProvided(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget("/p:MauiDevFlowSessionId=my-custom-session");

        var contents = File.ReadAllText(GeneratedFilePath);
        // Explicit values are sanitized (lowercase, alphanumeric only) for XML safety
        Assert.Contains("\"Microsoft.Maui.DevFlowSessionId\", \"mycustomsession\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetMauiDevFlowPort_EmitsSessionId_ForReleaseBuilds(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget("/p:Configuration=Release");

        var contents = File.ReadAllText(GetGeneratedFilePath("Release"));
        var expectedSessionId = ComputeExpectedSessionId(ProjectFilePath);
        Assert.Contains($"\"Microsoft.Maui.DevFlowSessionId\", \"{expectedSessionId}\"", contents);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetMauiDevFlowPort_DoesNotRewriteApplicationId(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath, """
            <ApplicationId>com.example.myapp</ApplicationId>
            """);

        RunSetMauiDevFlowPortTarget();

        var contents = File.ReadAllText(GeneratedFilePath);
        // Session ID should be present
        Assert.Contains("Microsoft.Maui.DevFlowSessionId", contents);
        // ApplicationId must NOT be rewritten — no identity isolation metadata
        Assert.DoesNotContain("Microsoft.Maui.DevFlowBaseApplicationId", contents);
        Assert.DoesNotContain("Microsoft.Maui.DevFlowApplicationId", contents);
    }

    private string ProjectFilePath => Path.Combine(_projectDirectory, "Test.csproj");

    private string ConfigFilePath => Path.Combine(_projectDirectory, ".mauidevflow");

    private string GeneratedFilePath => GetGeneratedFilePath("Debug");

    private static DateTime SentinelTimestampUtc { get; } = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private string GetGeneratedFilePath(string configuration) =>
        Path.Combine(_projectDirectory, "obj", configuration, "net10.0", "Microsoft.Maui.DevFlowPort.g.cs");

    private void CreateTestProject(string relativeTargetPath, string? additionalProperties = null)
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
            {{additionalProperties}}
              </PropertyGroup>
              <Import Project="{{escapedTargetFilePath}}" />
            </Project>
            """);
    }

    private static string ComputeExpectedSessionId(string projectFilePath)
    {
        var sanitized = new string(
            projectFilePath
                .ToLowerInvariant()
                .Where(static ch => char.IsAsciiLetterOrDigit(ch))
                .ToArray());

        if (sanitized.Length > 24)
            sanitized = sanitized[^24..];

        return $"dw{sanitized}";
    }

    private void RunSetMauiDevFlowPortTarget(params string[] properties)
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
        startInfo.ArgumentList.Add("/t:_SetMauiDevFlowPort");
        startInfo.ArgumentList.Add("/nologo");
        startInfo.ArgumentList.Add("/v:minimal");

        foreach (var property in properties)
            startInfo.ArgumentList.Add(property);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"dotnet msbuild failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{error}");
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
