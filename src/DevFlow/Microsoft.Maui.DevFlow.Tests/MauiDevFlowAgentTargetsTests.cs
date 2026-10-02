using System.Diagnostics;
using System.Security;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiDevFlowAgentTargetsTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private readonly string _projectDirectory;

    private static string CoreBuildPath => Path.Combine(
        RepoRoot, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Core", "buildTransitive");

    [Theory]
    [InlineData(true, "Debug", "")]
    [InlineData(false, "Debug", "")]
    [InlineData(true, "Debug", "false")]
    [InlineData(false, "Release", "")]
    [InlineData(false, "Release", "true")]
    public void CoreSourceMapTarget_BuildsMauiXaml_WithEitherImportOrder(
        bool importBeforeMaui, string configuration, string enabled)
    {
        var import = $$"""
            <PropertyGroup>
              <MauiSdkAtDevFlowImport>$(UsingMicrosoftMauiControlsSdk)</MauiSdkAtDevFlowImport>
            </PropertyGroup>
            <Import Project="{{SecurityElement.Escape(Path.Combine(CoreBuildPath, "Microsoft.Maui.DevFlow.Agent.Core.targets"))}}" />
            """;
        File.WriteAllText(Path.Combine(_projectDirectory, "Directory.Packages.props"), $$"""
            <Project>
              <Import Project="{{SecurityElement.Escape(Path.Combine(RepoRoot, "eng", "Versions.props"))}}" />
              <Import Project="{{SecurityElement.Escape(Path.Combine(RepoRoot, "Directory.Packages.props"))}}" />
            </Project>
            """);
        File.WriteAllText(Path.Combine(_projectDirectory, "Test.xaml"), """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         x:Class="SourceMapBuild.TestPage">
              <Button x:Name="CounterBtn" Text="Click me" />
            </ContentPage>
            """);
        File.WriteAllText(Path.Combine(_projectDirectory, "Test.xaml.cs"), """
            namespace SourceMapBuild;
            public partial class TestPage : Microsoft.Maui.Controls.ContentPage
            {
                public TestPage()
                {
                    InitializeComponent();
                    CounterBtn.Text = "Generated field";
                }
            }
            """);
        File.WriteAllText(ProjectFilePath, $$"""
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Configuration>{{configuration}}</Configuration>
                <DevFlowXamlSourceMapsEnabled>{{enabled}}</DevFlowXamlSourceMapsEnabled>
                <EnableDefaultMauiItems>false</EnableDefaultMauiItems>
                <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
                <RestoreConfigFile>{{SecurityElement.Escape(Path.Combine(RepoRoot, "NuGet.config"))}}</RestoreConfigFile>
              </PropertyGroup>
              <Import Project="{{SecurityElement.Escape(Path.Combine(CoreBuildPath, "Microsoft.Maui.DevFlow.Agent.Core.props"))}}" />
              {{(importBeforeMaui ? import : "")}}
              <ItemGroup>
                <PackageReference Include="Microsoft.Maui.Controls" />
                <MauiXaml Include="Test.xaml" Link="Pages/Test.xaml" />
              </ItemGroup>
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
              {{(importBeforeMaui ? "" : import)}}
              <Target Name="VerifySourceMaps" AfterTargets="CoreCompile">
                <Error Condition="'$(UsingMicrosoftMauiControlsSdk)' != 'true'" Text="MAUI targets not imported." />
                <Error Condition="'$(MauiSdkAtDevFlowImport)' != '{{(importBeforeMaui ? "" : "true")}}'"
                       Text="The test did not exercise the expected MAUI import order." />
                <Error Condition="'@(AdditionalFiles->Count())' != '1'" Text="Duplicate XAML AdditionalFiles." />
                <Error Condition="'%(AdditionalFiles.GenKind)' != 'Xaml'" Text="Lost GenKind." />
                <Error Condition="'%(AdditionalFiles.ManifestResourceName)' == ''" Text="Lost resource name." />
                <Error Condition="'%(AdditionalFiles.Inflator)' == ''" Text="Lost inflator." />
                <Error Condition="'$(DevFlowXamlSourceMapsEnabled)' == 'true' and '%(AdditionalFiles.DevFlowXaml)' != 'true'"
                       Text="Missing source map marker." />
                <Error Condition="'$(DevFlowXamlSourceMapsEnabled)' != 'true' and '%(AdditionalFiles.DevFlowXaml)' != ''"
                       Text="Source maps unexpectedly enabled." />
              </Target>
            </Project>
            """);

        RunMsBuildTarget("Build", "/restore");
        RunMsBuildTarget("Build");

        var generatedFiles = Directory.GetFiles(
            Path.Combine(_projectDirectory, "obj"), "*.xaml.sg.cs", SearchOption.AllDirectories);
        var generated = File.ReadAllText(Assert.Single(generatedFiles));
        Assert.Contains("InitializeComponent", generated);
        Assert.Contains("CounterBtn", generated);
        var editorConfig = File.ReadAllText(Path.Combine(
            _projectDirectory, "obj", configuration, "net10.0", "Test.GeneratedMSBuildEditorConfig.editorconfig"));
        var expectSourceMaps = enabled == "true" || (enabled == "" && configuration == "Debug");
        Assert.Equal(expectSourceMaps,
            editorConfig.Contains("build_metadata.AdditionalFiles.DevFlowXaml = true", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net10.0-windows")]
    public void CoreSourceMapTarget_ImportsWithoutPrivateMauiTargets(string targetFramework)
    {
        var targetPath = Path.Combine(
            RepoRoot,
            "src",
            "DevFlow",
            "Microsoft.Maui.DevFlow.Agent.Core",
            "buildTransitive",
            "Microsoft.Maui.DevFlow.Agent.Core.targets");
        var escapedTargetPath = SecurityElement.Escape(targetPath) ?? targetPath;
        File.WriteAllText(
            Path.Combine(_projectDirectory, "Test.xaml"),
            "<ContentPage />");
        File.WriteAllText(
            ProjectFilePath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{targetFramework}}</TargetFramework>
                <DevFlowXamlSourceMapsEnabled>true</DevFlowXamlSourceMapsEnabled>
              </PropertyGroup>
              <ItemGroup>
                <MauiXaml Include="Test.xaml" />
              </ItemGroup>
              <Import Project="{{escapedTargetPath}}" />
              <Target Name="VerifySourceMaps" DependsOnTargets="_DevFlowPromoteXamlSourceMaps">
                <Error Condition="'@(AdditionalFiles)' == ''"
                       Text="MauiXaml was not promoted to AdditionalFiles." />
                <Error Condition="'%(AdditionalFiles.DevFlowXaml)' != 'true'"
                       Text="DevFlowXaml metadata was not applied." />
              </Target>
            </Project>
            """);

        RunMsBuildTarget("VerifySourceMaps");
    }

    [Theory]
    [InlineData("net10.0", true)]
    [InlineData("net11.0", false)]
    [InlineData("net10.0-macos", true)]
    [InlineData("net11.0-macos", false)]
    [InlineData("net10.0-android", true)]
    [InlineData("net10.0-ios", false)]
    [InlineData("net10.0-maccatalyst", true)]
    [InlineData("net10.0-windows", false)]
    public void CoreSourceMapTarget_PreservesMauiPipeline_ForEveryTfm(string targetFramework, bool importBeforeMaui)
    {
        var import = $"""<Import Project="{SecurityElement.Escape(Path.Combine(CoreBuildPath, "Microsoft.Maui.DevFlow.Agent.Core.targets"))}" />""";
        // No platform SDK is needed to exercise target ordering for Apple/Android TFMs on every host.
        File.WriteAllText(ProjectFilePath, $$"""
            <Project>
              <PropertyGroup>
                <TargetFramework>{{targetFramework}}</TargetFramework>
                <DevFlowXamlSourceMapsEnabled>true</DevFlowXamlSourceMapsEnabled>
              </PropertyGroup>
              {{(importBeforeMaui ? import : "")}}
              <PropertyGroup>
                <UsingMicrosoftMauiControlsSdk>true</UsingMicrosoftMauiControlsSdk>
              </PropertyGroup>
              <ItemGroup>
                <MauiXaml Include="Test.xaml" />
              </ItemGroup>
              <Target Name="_MauiInjectXamlCssAdditionalFiles" BeforeTargets="GenerateMSBuildEditorConfigFileShouldRun">
                <ItemGroup>
                  <AdditionalFiles Include="@(MauiXaml->'%(FullPath)')" GenKind="Xaml"
                                   ManifestResourceName="Test.Test.xaml" Inflator="Runtime" />
                  <AdditionalFiles Include="style.css" GenKind="Css" />
                </ItemGroup>
              </Target>
              <Target Name="GenerateMSBuildEditorConfigFileShouldRun" />
              <Target Name="GenerateMSBuildEditorConfigFileCore"
                      DependsOnTargets="GenerateMSBuildEditorConfigFileShouldRun">
                <Error Condition="'@(AdditionalFiles->Count())' != '2'" Text="Duplicate AdditionalFiles." />
                <Error Condition="'%(AdditionalFiles.GenKind)' == 'Xaml' and '%(AdditionalFiles.DevFlowXaml)' != 'true'"
                       Text="XAML marker was not set before editorconfig generation." />
                <Error Condition="'%(AdditionalFiles.GenKind)' == 'Css' and '%(AdditionalFiles.DevFlowXaml)' != ''"
                       Text="CSS incorrectly tagged as XAML." />
              </Target>
              {{(importBeforeMaui ? "" : import)}}
            </Project>
            """);

        RunMsBuildTarget("GenerateMSBuildEditorConfigFileCore");
    }

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
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "0")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "65536")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "abc")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "0")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "65536")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "abc")]
    public void SetMauiDevFlowPort_RejectsInvalidPropertyValues(
        string relativeTargetPath,
        string port)
    {
        CreateTestProject(relativeTargetPath);

        var result = RunTarget("_SetMauiDevFlowPort", $"/p:MauiDevFlowPort={port}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("between 1 and 65535", result.Output + result.Error);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "1")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "65535")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "1")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "65535")]
    public void SetMauiDevFlowPort_AcceptsBoundaryPropertyValues(
        string relativeTargetPath,
        string port)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget($"/p:MauiDevFlowPort={port}");

        Assert.Contains(
            $"\"Microsoft.Maui.DevFlowPort\", \"{port}\"",
            File.ReadAllText(GeneratedFilePath));
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "0")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "65536")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "-1")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "9223.5")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "\"9223\"")]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets", "null")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "0")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "65536")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "-1")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "9223.5")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "\"9223\"")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets", "null")]
    public void SetMauiDevFlowPort_RejectsInvalidConfigValues(
        string relativeTargetPath,
        string port)
    {
        CreateTestProject(relativeTargetPath);
        File.WriteAllText(ConfigFilePath, $$"""
            {
              "port": {{port}}
            }
            """);

        var result = RunTarget("_SetMauiDevFlowPort");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("between 1 and 65535", result.Output + result.Error);
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
    public void SetMauiDevFlowPort_DoesNotEmbedFullProjectPath_ByDefault(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget();

        var contents = File.ReadAllText(GeneratedFilePath);
        Assert.Contains("\"Microsoft.Maui.DevFlowProject\", \"Test.csproj\"", contents);
        Assert.DoesNotContain(_projectDirectory, contents, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("build/Microsoft.Maui.DevFlow.Agent.targets")]
    [InlineData("buildTransitive/Microsoft.Maui.DevFlow.Agent.targets")]
    public void SetMauiDevFlowPort_EmbedsFullProjectPath_WhenExplicitlyEnabled(string relativeTargetPath)
    {
        CreateTestProject(relativeTargetPath);

        RunSetMauiDevFlowPortTarget("/p:MauiDevFlowIncludeProjectPath=true");

        var escapedPath = ProjectFilePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        Assert.Contains($"\"Microsoft.Maui.DevFlowProject\", \"{escapedPath}\"", File.ReadAllText(GeneratedFilePath));
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
        var result = RunTarget("_SetMauiDevFlowPort", properties);

        Assert.True(
            result.ExitCode == 0,
            $"dotnet msbuild failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}{result.Error}");
    }

    private (int ExitCode, string Output, string Error) RunTarget(
        string target,
        params string[] properties)
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
        startInfo.ArgumentList.Add($"/t:{target}");
        startInfo.ArgumentList.Add("/nologo");
        startInfo.ArgumentList.Add("/v:minimal");

        foreach (var property in properties)
            startInfo.ArgumentList.Add(property);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return (process.ExitCode, output, error);
    }

    private void RunMsBuildTarget(string target, params string[] arguments)
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
        startInfo.ArgumentList.Add($"/t:{target}");
        startInfo.ArgumentList.Add("/nologo");
        startInfo.ArgumentList.Add("/v:minimal");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet msbuild.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("dotnet msbuild timed out after two minutes.");
        }

        Assert.True(
            process.ExitCode == 0,
            $"dotnet msbuild failed with exit code {process.ExitCode}.{Environment.NewLine}{output.GetAwaiter().GetResult()}{error.GetAwaiter().GetResult()}");
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
