using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class DevFlowAgentServiceLifecycleTests
{
    [Fact]
    public async Task StartServerOnly_AllowsLateAppBinding()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var beforeBind = await WaitForStatusAsync(client);
        Assert.NotNull(beforeBind);
        Assert.False(beforeBind!.Running);
        Assert.Equal("unknown", beforeBind.AppName);

        var app = new Application();
        service.BindApp(app);

        var afterBind = await WaitForStatusAsync(client);
        Assert.NotNull(afterBind);
        Assert.True(afterBind!.Running);
        Assert.Equal(app.GetType().Assembly.GetName().Name, afterBind.AppName);
    }

    [Fact]
    public async Task BaseAgent_ReportsJobsUnsupportedConsistently()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var status = await WaitForStatusAsync(client);
        Assert.NotNull(status);
        Assert.NotNull(status!.Capabilities);
        Assert.False(status.Capabilities!.Jobs);

        var capabilities = await client.GetCapabilitiesAsync();
        Assert.False(capabilities.GetProperty("jobs").GetProperty("supported").GetBoolean());
        Assert.Empty(capabilities.GetProperty("jobs").GetProperty("features").EnumerateArray());

        var jobs = await client.GetJobsAsync();
        Assert.False(jobs.GetProperty("supported").GetBoolean());
        Assert.Empty(jobs.GetProperty("jobs").EnumerateArray());

        var run = await client.RunJobAsync("missing-job");
        Assert.False(run.GetProperty("success").GetBoolean());
        Assert.Contains("not supported", run.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capabilities_WhenJobsRunUnsupported_DoesNotAdvertiseRunFeature()
    {
        var port = GetFreePort();
        using var service = new ListOnlyJobsAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var status = await WaitForStatusAsync(client);
        Assert.NotNull(status);
        Assert.NotNull(status!.Capabilities);
        Assert.True(status.Capabilities!.Jobs);

        var capabilities = await client.GetCapabilitiesAsync();
        var jobsCapabilities = capabilities.GetProperty("jobs");
        Assert.True(jobsCapabilities.GetProperty("supported").GetBoolean());

        var features = jobsCapabilities.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).ToArray();
        Assert.Equal(new[] { "list" }, features);
    }

    [Fact]
    public async Task DispatchAsync_WhenDispatcherDoesNotRequireDispatchButMainThreadDoes_UsesMainThreadFallback()
    {
        using var service = new DispatchProbeAgentService(new ImmediateDispatcher(), mainThreadDispatchRequired: true);

        var result = await service.RunDispatchAsync(() => service.IsInsideMainThreadFallback ? "main-thread" : "direct");

        Assert.Equal("main-thread", result);
        Assert.Equal(1, service.MainThreadFallbackCallCount);
    }

    [Fact]
    public async Task DispatchAsync_AsyncFunc_WhenDispatcherDoesNotRequireDispatchButMainThreadDoes_UsesMainThreadFallback()
    {
        using var service = new DispatchProbeAgentService(new ImmediateDispatcher(), mainThreadDispatchRequired: true);

        var result = await service.RunDispatchAsync(async () =>
        {
            await Task.Yield();
            return service.IsInsideMainThreadFallback ? "main-thread" : "direct";
        });

        Assert.Equal("main-thread", result);
        Assert.Equal(1, service.MainThreadFallbackCallCount);
    }

    private static async Task<AgentStatus?> WaitForStatusAsync(AgentClient client)
    {
        for (int i = 0; i < 10; i++)
        {
            var status = await client.GetStatusAsync();
            if (status != null)
                return status;

            await Task.Delay(100);
        }

        return null;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class ListOnlyJobsAgentService(AgentOptions options) : DevFlowAgentService(options)
    {
        protected override bool IsJobsSupported => true;

        protected override bool IsJobRunSupported => false;
    }

    private sealed class DispatchProbeAgentService : DevFlowAgentService
    {
        private readonly bool _mainThreadDispatchRequired;

        public DispatchProbeAgentService(IDispatcher dispatcher, bool mainThreadDispatchRequired)
        {
            _dispatcher = dispatcher;
            _mainThreadDispatchRequired = mainThreadDispatchRequired;
        }

        public int MainThreadFallbackCallCount { get; private set; }

        public bool IsInsideMainThreadFallback { get; private set; }

        public Task<string> RunDispatchAsync(Func<string> func) => DispatchAsync(func);

        public Task<string?> RunDispatchAsync(Func<Task<string?>> func) => DispatchAsync(func);

        protected override bool IsMainThreadDispatchRequired() => _mainThreadDispatchRequired;

        protected override Task<T> DispatchViaMainThreadAsync<T>(Func<T> func)
        {
            MainThreadFallbackCallCount++;
            return RunInsideMainThreadFallbackAsync(func);
        }

        protected override Task<T?> DispatchViaMainThreadAsync<T>(Func<Task<T?>> func) where T : class
        {
            MainThreadFallbackCallCount++;
            return RunInsideMainThreadFallbackAsync(func);
        }

        private Task<T> RunInsideMainThreadFallbackAsync<T>(Func<T> func)
        {
            var wasInsideMainThreadFallback = IsInsideMainThreadFallback;
            IsInsideMainThreadFallback = true;
            try
            {
                return Task.FromResult(func());
            }
            finally
            {
                IsInsideMainThreadFallback = wasInsideMainThreadFallback;
            }
        }

        private async Task<T?> RunInsideMainThreadFallbackAsync<T>(Func<Task<T?>> func) where T : class
        {
            var wasInsideMainThreadFallback = IsInsideMainThreadFallback;
            IsInsideMainThreadFallback = true;
            try
            {
                return await func();
            }
            finally
            {
                IsInsideMainThreadFallback = wasInsideMainThreadFallback;
            }
        }
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            action();
            return true;
        }

        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class ImmediateDispatcherTimer : IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick
        {
            add { }
            remove { }
        }

        public void Start()
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}
