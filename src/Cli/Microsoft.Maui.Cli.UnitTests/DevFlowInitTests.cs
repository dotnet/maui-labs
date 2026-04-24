using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Init;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
[RequiresUnreferencedCode("DevFlow init tests exercise MSBuild evaluation which uses reflection-heavy APIs.")]
[RequiresDynamicCode("DevFlow init tests exercise MSBuild evaluation which uses reflection-heavy APIs.")]
public sealed class DevFlowInitTests
{
    static readonly SemaphoreSlim s_currentDirectoryGate = new(1, 1);

    [Fact]
    public void ManifestLoader_LoadsEmbeddedManifest()
    {
        var manifest = DevFlowInitManifestLoader.Load();

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("Microsoft.Maui.DevFlow.Agent", manifest.Packages.Agent.PackageId);
        Assert.Equal("Microsoft.Maui.DevFlow.Blazor", manifest.Packages.Blazor.PackageId);
        Assert.Contains(manifest.Hosts, host => host.Id == "claude");
        Assert.Contains(manifest.Hosts, host => host.Id == "copilot");
    }

    [Fact]
    public void ProjectScanner_DescribeProject_DetectsBlazorProject()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateMauiProject("BlazorApp", blazor: true);

        var candidate = DevFlowProjectScanner.DescribeProject(workspace.RootPath, projectPath);

        Assert.NotNull(candidate);
        Assert.Equal("standard-maui-blazor", candidate!.Flavor);
        Assert.True(candidate.IsSupported);
        Assert.True(candidate.NeedsBlazor);
        Assert.False(candidate.IsAlreadyIntegrated);
    }

    [Fact]
    public void MauiProgramPatcher_EnsureRegistration_AddsDevFlowCallsAndUsings()
    {
        using var workspace = new TempWorkspace();
        var mauiProgramPath = workspace.WriteFile("MauiProgram.cs", """
using Microsoft.Extensions.DependencyInjection;

namespace SampleApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddMauiBlazorWebView();

        return builder.Build();
    }
}
""");

        var result = MauiProgramPatcher.EnsureRegistration(mauiProgramPath, includeBlazor: true, dryRun: false);

        Assert.Equal(DevFlowInitStatus.Success, result.Status);

        var updated = File.ReadAllText(mauiProgramPath);
        Assert.Contains("using Microsoft.Maui.DevFlow.Agent;", updated);
        Assert.Contains("using Microsoft.Maui.DevFlow.Blazor;", updated);
        Assert.Contains("#if DEBUG", updated);
        Assert.Contains("builder.AddMauiDevFlowAgent();", updated);
        Assert.Contains("builder.AddMauiBlazorDevFlowTools();", updated);

        var secondPass = MauiProgramPatcher.EnsureRegistration(mauiProgramPath, includeBlazor: true, dryRun: false);
        Assert.Equal(DevFlowInitStatus.AlreadyPresent, secondPass.Status);
    }

    [Fact]
    public void ProjectUpdater_Apply_WithCentralPackageManagement_WritesPackageVersionToDirectoryPackagesProps()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("Directory.Packages.props", """
<Project>
  <ItemGroup />
</Project>
""");
        var projectPath = workspace.CreateMauiProject("CpmApp");
        var candidate = DevFlowProjectScanner.DescribeProject(workspace.RootPath, projectPath);
        Assert.NotNull(candidate);

        var result = DevFlowProjectUpdater.Apply(candidate!, DevFlowInitManifestLoader.Load(), dryRun: false);

        Assert.Equal(DevFlowInitStatus.Success, result.OverallStatus);

        var projectText = File.ReadAllText(projectPath);
        Assert.Contains("PackageReference Include=\"Microsoft.Maui.DevFlow.Agent\"", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference Include=\"Microsoft.Maui.DevFlow.Agent\" Version=", projectText, StringComparison.Ordinal);

        var packagesText = File.ReadAllText(Path.Combine(workspace.RootPath, "Directory.Packages.props"));
        Assert.Contains("PackageVersion Include=\"Microsoft.Maui.DevFlow.Agent\"", packagesText, StringComparison.Ordinal);
        Assert.Contains(DevFlowInitManifestLoader.Load().Packages.Agent.Version, packagesText);
    }

    [Fact]
    public async Task ExecuteAsync_NoAi_WritesReportAndUpdatesProject()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateMauiProject("SampleApp");
        var mauiProgramPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "MauiProgram.cs");
        var reportPath = Path.Combine(workspace.RootPath, "MAUI-DEVFLOW-INIT-REPORT.md");
        var output = new TestOutputWriter();

        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                var success = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions
                    {
                        NoAi = true
                    },
                    output);

                Assert.True(success);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }
        finally
        {
            s_currentDirectoryGate.Release();
        }

        var report = Assert.IsType<DevFlowInitReport>(output.LastResult);
        Assert.Equal(DevFlowInitStatus.Success, report.OverallStatus);
        Assert.True(File.Exists(reportPath));
        Assert.Contains("Microsoft.Maui.DevFlow.Agent", File.ReadAllText(projectPath));
        Assert.Contains("builder.AddMauiDevFlowAgent();", File.ReadAllText(mauiProgramPath));
        Assert.Contains("`disabled`", File.ReadAllText(reportPath));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleProjectsInCiMode_RequiresExplicitSelection()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateMauiProject("AppOne");
        workspace.CreateMauiProject("AppTwo");
        var reportPath = Path.Combine(workspace.RootPath, "MAUI-DEVFLOW-INIT-REPORT.md");
        var output = new TestOutputWriter();

        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                var success = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions
                    {
                        NoAi = true,
                        Ci = true
                    },
                    output);

                Assert.False(success);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDirectory);
            }
        }
        finally
        {
            s_currentDirectoryGate.Release();
        }

        var report = Assert.IsType<DevFlowInitReport>(output.LastResult);
        Assert.Equal(DevFlowInitStatus.Failed, report.OverallStatus);
        Assert.Contains(report.Notes, note => note.Contains("Multiple eligible MAUI projects were found.", StringComparison.Ordinal));
        Assert.True(File.Exists(reportPath));
    }

    sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "maui-cli-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateMauiProject(string name, bool blazor = false)
        {
            var projectDirectory = Path.Combine(RootPath, name);
            Directory.CreateDirectory(projectDirectory);

            WriteFile(Path.Combine(name, $"{name}.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>
    <UseMaui>true</UseMaui>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
{{(blazor ? """
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" Version="10.0.0-preview.1" />
  </ItemGroup>
""" : "")}}
</Project>
""");

            WriteFile(Path.Combine(name, "MauiProgram.cs"), $$"""
using Microsoft.Extensions.DependencyInjection;

namespace {{name}};

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
{{(blazor ? "        builder.Services.AddMauiBlazorWebView();\n" : "")}}
        return builder.Build();
    }
}
""");

            return Path.Combine(projectDirectory, $"{name}.csproj");
        }

        public string WriteFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, contents.ReplaceLineEndings(Environment.NewLine));
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Best effort cleanup for temp test data.
            }
        }
    }

    sealed class TestOutputWriter : IDevFlowOutputWriter
    {
        public object? LastResult { get; private set; }
        public string? LastError { get; private set; }

        public bool ResolveJsonMode(bool jsonFlag, bool noJsonFlag) => jsonFlag && !noJsonFlag;
        public void WriteResult<T>(T data, bool json, Action<T>? humanFormatter = null) => LastResult = data;
        public void WriteRawJson(string jsonString) => LastResult = jsonString;
        public void WriteJsonElement(JsonElement element, bool json) => LastResult = element.Clone();
        public void WriteActionResult(bool success, string action, string? elementId, bool json, string? humanMessage = null) => LastResult = success;
        public void WriteError(string message, bool json, string errorType = "RuntimeError", bool retryable = false, string[]? suggestions = null) => LastError = message;
        public void WriteJsonLine<T>(T data) => LastResult = data;
        public string FormatJson<T>(T data) => JsonSerializer.Serialize(data);
    }
}
