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

        var result = MauiProgramPatcher.EnsureRegistration(mauiProgramPath, includeBlazor: true, isGtk: false, dryRun: false);

        Assert.Equal(DevFlowInitStatus.Success, result.Status);

        var updated = File.ReadAllText(mauiProgramPath);
        Assert.Contains("using Microsoft.Maui.DevFlow.Agent;", updated);
        Assert.Contains("using Microsoft.Maui.DevFlow.Blazor;", updated);
        Assert.Contains("#if DEBUG", updated);
        Assert.Contains("builder.AddMauiDevFlowAgent();", updated);
        Assert.Contains("builder.AddMauiBlazorDevFlowTools();", updated);

        var secondPass = MauiProgramPatcher.EnsureRegistration(mauiProgramPath, includeBlazor: true, isGtk: false, dryRun: false);
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

    [Fact]
    public void ManifestLoader_ContainsGtkPackages()
    {
        var manifest = DevFlowInitManifestLoader.Load();

        Assert.Equal("Microsoft.Maui.DevFlow.Agent.Gtk", manifest.Packages.AgentGtk.PackageId);
        Assert.Equal("Microsoft.Maui.DevFlow.Blazor.Gtk", manifest.Packages.BlazorGtk.PackageId);
        Assert.NotEmpty(manifest.Packages.AgentGtk.Version);
        Assert.NotEmpty(manifest.Packages.BlazorGtk.Version);
    }

    [Fact]
    public void ProjectScanner_DescribeProject_DetectsGtkProject()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateGtkProject("GtkApp");

        var candidate = DevFlowProjectScanner.DescribeProject(workspace.RootPath, projectPath);

        Assert.NotNull(candidate);
        Assert.Equal("gtk", candidate!.Flavor);
        Assert.True(candidate.IsSupported);
        Assert.False(candidate.IsAlreadyIntegrated);
    }

    [Fact]
    public void ProjectScanner_DescribeProject_DetectsGtkBlazorProject()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateGtkProject("GtkBlazorApp", blazor: true);

        var candidate = DevFlowProjectScanner.DescribeProject(workspace.RootPath, projectPath);

        Assert.NotNull(candidate);
        Assert.Equal("gtk-blazor", candidate!.Flavor);
        Assert.True(candidate.IsSupported);
        Assert.True(candidate.NeedsBlazor);
    }

    [Fact]
    public void ProjectUpdater_Apply_GtkProject_UsesGtkPackages()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateGtkProject("GtkApp");
        var candidate = DevFlowProjectScanner.DescribeProject(workspace.RootPath, projectPath);
        Assert.NotNull(candidate);

        var result = DevFlowProjectUpdater.Apply(candidate!, DevFlowInitManifestLoader.Load(), dryRun: false);

        Assert.Equal(DevFlowInitStatus.Success, result.OverallStatus);
        var projectText = File.ReadAllText(projectPath);
        Assert.Contains("Microsoft.Maui.DevFlow.Agent.Gtk", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Microsoft.Maui.DevFlow.Agent\"", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void MauiProgramPatcher_GtkProject_UsesGtkNamespaces()
    {
        using var workspace = new TempWorkspace();
        var mauiProgramPath = workspace.WriteFile("MauiProgram.cs", """
using Microsoft.Extensions.DependencyInjection;

namespace GtkApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        return builder.Build();
    }
}
""");

        var result = MauiProgramPatcher.EnsureRegistration(mauiProgramPath, includeBlazor: false, isGtk: true, dryRun: false);

        Assert.Equal(DevFlowInitStatus.Success, result.Status);

        var updated = File.ReadAllText(mauiProgramPath);
        Assert.Contains("using Microsoft.Maui.DevFlow.Agent.Gtk;", updated);
        Assert.DoesNotContain("using Microsoft.Maui.DevFlow.Agent;", updated);
        Assert.Contains("builder.AddMauiDevFlowAgent();", updated);
    }

    [Fact]
    public void MauiProgramPatcher_GtkBlazorProject_UsesGtkBlazorNamespaces()
    {
        using var workspace = new TempWorkspace();
        var mauiProgramPath = workspace.WriteFile("MauiProgram.cs", """
using Microsoft.Extensions.DependencyInjection;

namespace GtkBlazorApp;

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

        var result = MauiProgramPatcher.EnsureRegistration(mauiProgramPath, includeBlazor: true, isGtk: true, dryRun: false);

        Assert.Equal(DevFlowInitStatus.Success, result.Status);

        var updated = File.ReadAllText(mauiProgramPath);
        Assert.Contains("using Microsoft.Maui.DevFlow.Agent.Gtk;", updated);
        Assert.Contains("using Microsoft.Maui.DevFlow.Blazor.Gtk;", updated);
        Assert.DoesNotContain("using Microsoft.Maui.DevFlow.Agent;", updated);
        Assert.DoesNotContain("using Microsoft.Maui.DevFlow.Blazor;", updated);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyOnboarded_ReportsAlreadyPresent()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateMauiProject("AlreadyDone");
        var mauiProgramDir = Path.GetDirectoryName(projectPath)!;

        // Manually add DevFlow package and registration to simulate already-onboarded state
        var csproj = File.ReadAllText(projectPath);
        csproj = csproj.Replace("</Project>", """
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview.5" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(projectPath, csproj);

        var mauiProgramText = File.ReadAllText(Path.Combine(mauiProgramDir, "MauiProgram.cs"));
        mauiProgramText = mauiProgramText.Replace(
            "return builder.Build();",
            """
#if DEBUG
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
""");
        mauiProgramText = "using Microsoft.Maui.DevFlow.Agent;\n" + mauiProgramText;
        File.WriteAllText(Path.Combine(mauiProgramDir, "MauiProgram.cs"), mauiProgramText);

        var output = new TestOutputWriter();
        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                var success = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions { NoAi = true },
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
        Assert.Equal(DevFlowInitStatus.AlreadyPresent, report.OverallStatus);
        Assert.Contains(report.Projects, p => p.OverallStatus == DevFlowInitStatus.AlreadyPresent);
        // Phase 3: already-onboarded projects include verification commands
        var alreadyProject = report.Projects.First(p => p.OverallStatus == DevFlowInitStatus.AlreadyPresent);
        Assert.Contains(alreadyProject.VerificationCommands, cmd => cmd.Contains("dotnet build", StringComparison.Ordinal));
        Assert.Contains(alreadyProject.VerificationCommands, cmd => cmd.Contains("maui devflow wait", StringComparison.Ordinal));
        // Phase 3: already-onboarded includes suggestion to use --force
        Assert.Contains(alreadyProject.ManualSteps, step => step.Contains("--force", StringComparison.Ordinal));
        // Phase 3: NextSteps populated for already-onboarded workspace
        Assert.NotEmpty(report.NextSteps);
        Assert.Contains(report.NextSteps, s => s.Contains("already integrated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_JsonSidecar_WrittenAlongsideMd()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateMauiProject("JsonTest");
        var output = new TestOutputWriter();

        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                var success = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions { NoAi = true },
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
        // JSON sidecar is written alongside the markdown report
        Assert.True(File.Exists(report.JsonReportPath), $"JSON sidecar not found at {report.JsonReportPath}");
        Assert.True(File.Exists(report.ReportPath), $"Markdown report not found at {report.ReportPath}");

        // JSON sidecar is valid JSON containing expected fields
        var jsonContent = File.ReadAllText(report.JsonReportPath);
        var doc = JsonDocument.Parse(jsonContent);
        Assert.Equal(report.WorkspacePath, doc.RootElement.GetProperty("workspacePath").GetString());
        Assert.Equal(report.OverallStatus, doc.RootElement.GetProperty("overallStatus").GetString());
        Assert.True(doc.RootElement.TryGetProperty("nextSteps", out var nextSteps));
        Assert.Equal(JsonValueKind.Array, nextSteps.ValueKind);
    }

    [Fact]
    public async Task ExecuteAsync_Force_ReappliesAlreadyOnboarded()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateMauiProject("ForceApp");
        var mauiProgramDir = Path.GetDirectoryName(projectPath)!;

        // Simulate already-onboarded with old package version
        var csproj = File.ReadAllText(projectPath);
        csproj = csproj.Replace("</Project>", """
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="0.0.1-old" />
  </ItemGroup>
</Project>
""");
        File.WriteAllText(projectPath, csproj);

        var mauiProgramText = File.ReadAllText(Path.Combine(mauiProgramDir, "MauiProgram.cs"));
        mauiProgramText = mauiProgramText.Replace(
            "return builder.Build();",
            """
#if DEBUG
        builder.AddMauiDevFlowAgent();
#endif

        return builder.Build();
""");
        mauiProgramText = "using Microsoft.Maui.DevFlow.Agent;\n" + mauiProgramText;
        File.WriteAllText(Path.Combine(mauiProgramDir, "MauiProgram.cs"), mauiProgramText);

        var output = new TestOutputWriter();
        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                // Without --force: reports already present
                var successWithout = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions { NoAi = true },
                    output);
                Assert.True(successWithout);
                var reportWithout = Assert.IsType<DevFlowInitReport>(output.LastResult);
                Assert.Equal(DevFlowInitStatus.AlreadyPresent, reportWithout.OverallStatus);

                // With --force: re-processes the project
                var successWith = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions { NoAi = true, Force = true },
                    output);
                Assert.True(successWith);
                var reportWith = Assert.IsType<DevFlowInitReport>(output.LastResult);
                // With force, the project should be processed (success or already_present for the operations)
                Assert.Contains(reportWith.Projects, p =>
                    p.OverallStatus != DevFlowInitStatus.Skipped);
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
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulInit_PopulatesNextSteps()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateMauiProject("NextStepsApp");
        var output = new TestOutputWriter();

        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                var success = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions { NoAi = true },
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
        Assert.NotEmpty(report.NextSteps);
        Assert.Contains(report.NextSteps, s => s.Contains("maui devflow wait", StringComparison.Ordinal));
        Assert.Contains(report.NextSteps, s => s.Contains("maui devflow tree", StringComparison.Ordinal));

        // Per-project verification commands populated for successful projects
        var project = report.Projects.First(p => p.OverallStatus == DevFlowInitStatus.Success);
        Assert.NotEmpty(project.VerificationCommands);
        Assert.Contains(project.VerificationCommands, cmd => cmd.Contains("dotnet build", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BlazorProject_AddsBlazorPackageAndRegistration()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.CreateMauiProject("BlazorE2E", blazor: true);
        var mauiProgramDir = Path.GetDirectoryName(projectPath)!;
        var output = new TestOutputWriter();

        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                var success = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions { NoAi = true },
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

        var projectText = File.ReadAllText(projectPath);
        Assert.Contains("Microsoft.Maui.DevFlow.Agent", projectText, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Maui.DevFlow.Blazor", projectText, StringComparison.Ordinal);

        var mauiProgramText = File.ReadAllText(Path.Combine(mauiProgramDir, "MauiProgram.cs"));
        Assert.Contains("builder.AddMauiDevFlowAgent();", mauiProgramText);
        Assert.Contains("builder.AddMauiBlazorDevFlowTools();", mauiProgramText);
        Assert.Contains("using Microsoft.Maui.DevFlow.Agent;", mauiProgramText);
        Assert.Contains("using Microsoft.Maui.DevFlow.Blazor;", mauiProgramText);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyWorkspace_ReportsNoProjects()
    {
        using var workspace = new TempWorkspace();
        var output = new TestOutputWriter();

        await s_currentDirectoryGate.WaitAsync();
        try
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(workspace.RootPath);
            try
            {
                var success = await DevFlowInitCommand.ExecuteAsync(
                    new DevFlowInitOptions { NoAi = true },
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
        Assert.Equal(DevFlowInitStatus.ManualRequired, report.OverallStatus);
        Assert.Contains(report.Notes, n => n.Contains("No MAUI projects", StringComparison.Ordinal));
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

        public string CreateGtkProject(string name, bool blazor = false)
        {
            var projectDirectory = Path.Combine(RootPath, name);
            Directory.CreateDirectory(projectDirectory);

            WriteFile(Path.Combine(name, $"{name}.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <UseMaui>true</UseMaui>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Maui.Gtk" Version="0.1.0" />
{{(blazor ? """
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" Version="10.0.0-preview.1" />
""" : "")}}
  </ItemGroup>
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
