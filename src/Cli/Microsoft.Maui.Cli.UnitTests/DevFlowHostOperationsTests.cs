using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class HostOperationsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Permission_ExplicitTarget_UsesExistingAppleProvider(bool success)
    {
        var provider = new FakeAppleProvider { SetPrivacyResult = success };
        var operations = new HostOperations(() => throw new InvalidOperationException("Must not query agent"));

        var result = await operations.SetPermissionAsync(provider, "grant", "simulator-1", "com.test.app", "photos");

        Assert.Equal(success, result.Success);
        var call = Assert.Single(provider.PrivacyCalls);
        Assert.Equal(("grant", "simulator-1", "com.test.app"), (call.Action, call.Udid, call.BundleId));
        Assert.Equal(success ? null : "simctl privacy grant failed for service 'photos'.", result.Error);
    }

    [Fact]
    public async Task Permission_InvalidService_DoesNotInvokeProvider()
    {
        var provider = new FakeAppleProvider();
        var operations = new HostOperations(() => Task.FromResult<AgentStatus?>(null));

        var result = await operations.SetPermissionAsync(provider, "grant", "simulator-1", null, "invalid");

        Assert.False(result.Success);
        Assert.StartsWith("Unknown permission service 'invalid'.", result.Error);
        Assert.Empty(provider.PrivacyCalls);
    }

    [Fact]
    public async Task AlertPlatform_ExplicitTargets_BypassAgentProbe()
    {
        var operations = new HostOperations(() => throw new InvalidOperationException("Must not query agent"));

        Assert.Equal("ios-simulator", await operations.ResolveAlertPlatformAsync("simulator-1", null));
        Assert.Equal(OperatingSystem.IsWindows() ? "windows" : "maccatalyst",
            await operations.ResolveAlertPlatformAsync(null, 123));
        Assert.Equal("simulator-1", await HostOperations.ResolveUdidAsync("simulator-1"));
    }
}

[Collection("CLI")]
public sealed class BrokerOperationsTests
{
    [Fact]
    public async Task Status_FallbackHealth_ReturnsLegacyNoStateFileShape()
    {
        await using var server = new MockAgentServer { BindToLocalhost = true };
        await server.StartAsync();

        var status = await new BrokerOperations().GetStatusAsync(null, server.Port);

        Assert.True(status.Running);
        Assert.False(status.StateFile);
        Assert.Equal(2, status.Agents);
        Assert.Equal(server.Port, status.Port);
        Assert.Null(status.SourceWorkspace);
        Assert.Equal("/api/health", Assert.Single(server.RecordedRequests).Path);
    }

    [Theory]
    [InlineData("""{"status":"ok","agents":2,"sourceWorkspace":"/work/app"}""", "/work/app")]
    [InlineData("""{"status":"ok","agents":2,"sourceWorkspace":null}""", null)]
    public async Task Status_Health_ReportsSourceWorkspace(string health, string? expected)
    {
        await using var server = new MockAgentServer { BindToLocalhost = true, BrokerHealthResponse = health };
        await server.StartAsync();

        var status = await new BrokerOperations().GetStatusAsync(null, server.Port);

        Assert.True(status.Running);
        Assert.Equal(expected, status.SourceWorkspace);
    }

    [Fact]
    public async Task Status_MalformedHealth_PreservesStaleStateResult()
    {
        await using var server = new MockAgentServer
        {
            BindToLocalhost = true,
            BrokerHealthResponse = "invalid JSON"
        };
        await server.StartAsync();

        var status = await new BrokerOperations().GetStatusAsync(server.Port);

        Assert.False(status.Running);
        Assert.True(status.Stale);
        Assert.Equal(server.Port, status.Port);
        Assert.Equal("/api/health", Assert.Single(server.RecordedRequests).Path);
    }

    [Fact]
    public void LogTail_ReturnsLastFiftyLinesInOrder()
    {
        var path = Path.GetFullPath($".devflow-broker-log-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllLines(path, Enumerable.Range(0, 70).Select(index => index.ToString()));

            var lines = new BrokerOperations().ReadLogTail(path);

            Assert.Equal(50, lines.Length);
            Assert.Equal("20", lines[0]);
            Assert.Equal("69", lines[^1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[Collection("CLI")]
public sealed class AgentManagementOperationsTests
{
    [Fact]
    public async Task List_UsesBrokerAndSkipsAndroidToolingForDesktopAgent()
    {
        var agent = new AgentRegistration { Id = "desktop", Port = 12345, Platform = "MacCatalyst" };
        var operations = new AgentManagementOperations(
            () => Task.FromResult<int?>(19223),
            port =>
            {
                Assert.Equal(19223, port);
                return Task.FromResult<AgentRegistration[]?>([agent]);
            },
            () => throw new InvalidOperationException("Must not create Android forwarder"),
            () => false,
            () => Task.FromResult<int?>(19223));

        var result = await operations.ListAsync(null, check => Assert.Null(check.Report), CancellationToken.None);

        Assert.Equal(agent, Assert.Single(result.Agents!));
        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task Wait_MatchesProjectAndPlatformWithoutForwarding()
    {
        var project = Path.GetFullPath("test-app.csproj");
        var wrong = new AgentRegistration { Id = "wrong", Project = project, Platform = "Android" };
        var match = new AgentRegistration { Id = "match", Project = project, Platform = "MacCatalyst" };
        var operations = new AgentManagementOperations(
            () => Task.FromResult<int?>(19223),
            _ => Task.FromResult<AgentRegistration[]?>([wrong, match]),
            () => throw new InvalidOperationException("Must not create Android forwarder"),
            () => false,
            () => Task.FromResult<int?>(19223));

        var result = await operations.WaitAsync(1, project, "catalyst", null,
            _ => throw new InvalidOperationException("Must not check Android forwarding"), CancellationToken.None);

        Assert.True(result.BrokerAvailable);
        Assert.Same(match, result.Agent);
    }

    [Fact]
    public async Task Wait_UnavailableBroker_DoesNotPollOrPrepareDevice()
    {
        var operations = new AgentManagementOperations(
            () => Task.FromResult<int?>(null),
            _ => throw new InvalidOperationException("Must not list agents"),
            () => throw new InvalidOperationException("Must not create Android forwarder"),
            () => false,
            () => Task.FromResult<int?>(null));

        var result = await operations.WaitAsync(1, null, null, null,
            _ => throw new InvalidOperationException("Must not prepare Android"), CancellationToken.None);

        Assert.False(result.BrokerAvailable);
        Assert.Null(result.Agent);
    }

    [Fact]
    public void Scan_ExcludesBuildDirectoriesAndSortsProjectPaths()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var directory = Path.GetFullPath($".devflow-project-scan-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "obj"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "b.csproj"), "Microsoft.Maui.DevFlow.Agent");
            File.WriteAllText(Path.Combine(directory, "a.csproj"), "Redth.MauiDevFlow.Agent");
            File.WriteAllText(Path.Combine(directory, "obj", "ignored.csproj"), "Microsoft.Maui.DevFlow.Agent");
            Directory.SetCurrentDirectory(directory);

            Assert.Equal(["a.csproj", "b.csproj"], AgentManagementOperations.ScanForProjects());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(directory, recursive: true);
        }
    }
}
