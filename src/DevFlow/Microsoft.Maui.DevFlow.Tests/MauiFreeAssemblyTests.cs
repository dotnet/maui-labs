using Microsoft.Maui.DevFlow.Logging;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Regression fence for the framework-neutral half of DevFlow.
///
/// DevFlow must run inside plain .NET Android / iOS / Mac Catalyst / macOS apps that have no MAUI
/// reference at all. These tests fail the build the moment a MAUI type sneaks back into an assembly
/// that is supposed to stay neutral.
/// </summary>
public class MauiFreeAssemblyTests
{
    [Fact]
    public void Logging_DoesNotReferenceMaui()
        => AssemblyReferenceGuard.AssertMauiFree(typeof(FileLogWriter).Assembly.Location);

    [Fact]
    public void Logging_ProjectDeclaresNoMaui()
        => AssemblyReferenceGuard.AssertProjectDeclaresNoMaui(Path.Combine(
            TestRepo.Root,
            "src", "DevFlow", "Microsoft.Maui.DevFlow.Logging", "Microsoft.Maui.DevFlow.Logging.csproj"));

    [Fact]
    public void Abstractions_DoesNotReferenceMaui()
        => AssemblyReferenceGuard.AssertMauiFree(typeof(AgentOptions).Assembly.Location);

    [Fact]
    public void Abstractions_ProjectDeclaresNoMaui()
        => AssemblyReferenceGuard.AssertProjectDeclaresNoMaui(Path.Combine(
            TestRepo.Root,
            "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Abstractions",
            "Microsoft.Maui.DevFlow.Agent.Abstractions.csproj"));

    [Fact]
    public void Abstractions_HostsTheNeutralAgentService()
    {
        // DevFlowAgentService is the routing core every backend derives from. If it drifts back
        // into the MAUI assembly, native apps lose the ability to host an agent at all.
        var neutral = typeof(DevFlowAgentService).Assembly.GetName().Name;

        Assert.Equal("Microsoft.Maui.DevFlow.Agent.Abstractions", neutral);
        Assert.True(
            typeof(DevFlowAgentService).IsAssignableFrom(typeof(MauiDevFlowAgentService)),
            "MauiDevFlowAgentService must remain a DevFlowAgentService backend.");
    }

    [Fact]
    public void Native_ProjectDeclaresNoMaui()
        => AssemblyReferenceGuard.AssertProjectDeclaresNoMaui(Path.Combine(
            TestRepo.Root,
            "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Native",
            "Microsoft.Maui.DevFlow.Agent.Native.csproj"));

    [Fact]
    public void Native_DoesNotReferenceMaui()
    {
        // The native agent is the whole point of the split: it must be hostable from a plain
        // .NET Android/iOS/Mac Catalyst/macOS app that never restores a MAUI package.
        var assemblies = TestRepo.FindBuiltAssemblies("Microsoft.Maui.DevFlow.Agent.Native");

        foreach (var assembly in assemblies)
            AssemblyReferenceGuard.AssertMauiFree(assembly);
    }

    [Fact]
    public void NativeEssentials_DoesNotReferenceMauiControls()
    {
        // The add-on is allowed to depend on Essentials — that is its whole job — but pulling in
        // Controls would drag the consuming plain .NET app into MAUI, which defeats the point.
        var assemblies = TestRepo.FindBuiltAssemblies("Microsoft.Maui.DevFlow.Agent.Native.Essentials");

        foreach (var assembly in assemblies)
            AssemblyReferenceGuard.AssertControlsFree(assembly);
    }

    [Fact]
    public void NativeEssentials_SharesTheMauiAgentEndpointImplementations()
    {
        // Preferences, secure storage, device and sensor endpoints have to behave identically on
        // both agents. They do so because both compile the same source, not because someone kept
        // two copies in sync — this asserts that arrangement is still in place.
        var shared = Path.Combine(TestRepo.Root, "src", "DevFlow", "Shared.Essentials");

        Assert.True(Directory.Exists(shared), $"Shared Essentials sources are missing: {shared}");

        foreach (var project in new[]
        {
            Path.Combine(TestRepo.Root, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Core",
                "Microsoft.Maui.DevFlow.Agent.Core.csproj"),
            Path.Combine(TestRepo.Root, "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent.Native.Essentials",
                "Microsoft.Maui.DevFlow.Agent.Native.Essentials.csproj"),
        })
        {
            Assert.Contains(
                @"..\Shared.Essentials\**\*.cs",
                File.ReadAllText(project));
        }
    }

    [Fact]
    public void NativeSamples_DeclareNoMaui()
    {
        // The samples are the executable proof that a plain .NET app can host the agent. If one
        // of them picks up a MAUI reference the whole scenario stops being validated.
        string[] projects =
        [
            Path.Combine("Android", "DevFlow.Sample.Native.Android.csproj"),
            Path.Combine("iOS", "DevFlow.Sample.Native.iOS.csproj"),
            Path.Combine("MacCatalyst", "DevFlow.Sample.Native.MacCatalyst.csproj"),
            Path.Combine("MacOS", "DevFlow.Sample.Native.MacOS.csproj"),
        ];

        foreach (var project in projects)
        {
            var path = Path.Combine(TestRepo.Root, "samples", "DevFlow.Sample.Native", project);

            Assert.True(File.Exists(path), $"Missing native sample project: {path}");
            AssemblyReferenceGuard.AssertProjectDeclaresNoMaui(path);
            Assert.Contains("<UseMaui>false</UseMaui>", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NativeSamples_ShareTheMauiSampleAutomationIds()
    {
        // Integration assertions are shared between the MAUI and native samples, so every id the
        // tests query for has to exist in all three native heads too.
        string[] heads =
        [
            Path.Combine("Android", "MainActivity.cs"),
            Path.Combine("Apple", "SampleViewController.cs"),
            Path.Combine("MacOS", "AppDelegate.cs"),
        ];

        string[] sharedIds =
        [
            "HeaderLabel", "CountLabel", "StatusLabel",
            "NewTodoEntry", "NewDescriptionEntry", "AddButton",
            "TodoList", "TodoCheckBox", "DeleteButton",
            "TestButton", "TestSwitch", "GetPostsButton",
        ];

        foreach (var head in heads)
        {
            var path = Path.Combine(TestRepo.Root, "samples", "DevFlow.Sample.Native", head);
            Assert.True(File.Exists(path), $"Missing native sample head: {path}");

            var source = File.ReadAllText(path);

            foreach (var id in sharedIds)
                Assert.Contains($"\"{id}\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Guard_DetectsMauiReferences_InAMauiAssembly()
    {
        // Sanity check: the guard must actually be capable of failing. Agent.Core is the MAUI
        // backend, so it is expected to reference MAUI — if this comes back empty the guard is
        // silently passing everywhere.
        var agentCore = Path.Combine(AppContext.BaseDirectory, "Microsoft.Maui.DevFlow.Agent.Core.dll");

        Assert.True(File.Exists(agentCore), $"Expected Agent.Core next to the tests: {agentCore}");
        Assert.NotEmpty(AssemblyReferenceGuard.GetMauiReferences(agentCore));
    }

    [Fact]
    public void Guard_TreatsDevFlowAssembliesAsNeutral()
    {
        // Microsoft.Maui.DevFlow.* shares the product prefix but is not MAUI itself.
        var logging = typeof(FileLogWriter).Assembly.Location;

        Assert.Contains(
            AssemblyReferenceGuard.GetReferencedAssemblyNames(logging),
            name => name.StartsWith("System.", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
        Assert.Empty(AssemblyReferenceGuard.GetMauiReferences(logging));
    }
}
