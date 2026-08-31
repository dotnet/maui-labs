using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Thin experimental AppKit lifecycle adapter. It delegates all semantic execution and report
/// construction to the one canonical <see cref="MauiFlowRunner"/> owned by <see cref="AppleFlowTestHost"/>.
/// </summary>
internal sealed class MacOSFlowTestHost : IAsyncDisposable
{
    readonly AppleFlowTestHost _host;
    readonly AgentClient? _agentClient;

    internal MacOSFlowTestHost(MacOSFixture fixture)
        : this(
            fixture ?? throw new ArgumentNullException(nameof(fixture)),
            new AgentClientMauiFlowDriver(fixture.Client),
            fixture.Client)
    {
    }

    internal MacOSFlowTestHost(
        IPlatformFlowTestLifecycle lifecycle,
        IMauiFlowDriver driver,
        AgentClient? agentClient = null)
    {
        _agentClient = agentClient;
        _host = new AppleFlowTestHost(
            MacOSFlowCapabilities.PlatformKey,
            lifecycle,
            driver,
            MacOSFlowCapabilities.ValidateStatic,
            MacOSFlowCapabilities.ValidateRuntime);
    }

    internal async Task<AppleFlowRunResult> RunAsync(
        MauiFlow flow,
        MauiTestPlan plan,
        AppleFlowRunRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        using var retries = new NoMutatingRetryScope(_agentClient);
        return await _host.RunAsync(flow, plan, request, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    sealed class NoMutatingRetryScope : IDisposable
    {
        readonly AgentClient? _client;
        readonly bool _previous;

        public NoMutatingRetryScope(AgentClient? client)
        {
            _client = client;
            _previous = client?.RetryMutatingRequests ?? false;
            if (_client is not null)
                _client.RetryMutatingRequests = false;
        }

        public void Dispose()
        {
            if (_client is not null)
                _client.RetryMutatingRequests = _previous;
        }
    }
}
