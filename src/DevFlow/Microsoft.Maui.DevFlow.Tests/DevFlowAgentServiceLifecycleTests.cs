using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
    public async Task RecordingStatus_IsReadableByNonOwner_ButStopIsLeaseProtected()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var owner = new AgentClient("localhost", port) { MutationLeaseId = "owner" };
        using var observer = new AgentClient("localhost", port) { MutationLeaseId = "observer" };

        service.StartServerOnly(new ImmediateDispatcher());
        await WaitForStatusAsync(owner);
        var claim = await owner.ControlMutationLeaseAsync("claim");
        Assert.True(claim.YouHold);

        var status = await observer.ControlMutationRecordingAsync("status");
        Assert.False(status.Ok);
        Assert.Contains("broker", status.Error, StringComparison.OrdinalIgnoreCase);

        var stopError = await Assert.ThrowsAsync<MutationLeaseException>(() =>
            observer.ControlMutationRecordingAsync("stop", null, null, null, null, "recording"));
        Assert.Contains("driving", stopError.Message, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(Environment.ProcessId, status.App?.ProcessId);
        Assert.NotNull(status.Extensions);
        Assert.Equal(0, status.Extensions!.Count);
        Assert.Matches("^[a-f0-9]{64}$", status.Extensions.Hash);

        var capabilities = await client.GetCapabilitiesAsync();
        var capabilityMap = capabilities.GetProperty("capabilities");
        Assert.Contains("property-descriptors", capabilityMap.GetProperty("ui.actions").GetProperty("features").EnumerateArray().Select(feature => feature.GetString()));
        Assert.Contains("subscribe", capabilityMap.GetProperty("ui.events").GetProperty("features").EnumerateArray().Select(feature => feature.GetString()));
        var jobsCapabilities = capabilityMap.GetProperty("device.jobs");
        Assert.False(jobsCapabilities.GetProperty("supported").GetBoolean());
        Assert.Empty(jobsCapabilities.GetProperty("features").EnumerateArray());

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
        var jobsCapabilities = capabilities.GetProperty("capabilities").GetProperty("device.jobs");
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

    [Fact]
    public async Task Extensions_AreDiscoverableAndCallable()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port };
        var extension = options.RegisterExtension(
            "com.example.diagnostics",
            "Diagnostics extension",
            "1.2.3",
            new[] { "build_info" });
        extension.MapTool(
            "build_info",
            "Returns build information.",
            "GET",
            "build-info",
            _ => Task.FromResult(HttpResponse.Json(new { version = "1.0.0" })),
            returns: JsonDocument.Parse("""{"type":"object","properties":{"version":{"type":"string"}}}""").RootElement.Clone(),
            annotations: new ExtensionToolAnnotations
            {
                ReadOnly = true,
                Idempotent = true,
                Category = "diagnostics"
            });

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var status = await WaitForStatusAsync(client);
        Assert.NotNull(status?.Extensions);
        Assert.Equal(1, status!.Extensions!.Count);
        Assert.Matches("^[a-f0-9]{64}$", status.Extensions.Hash);

        var extensions = await client.GetExtensionsAsync();
        var descriptor = Assert.Single(extensions);
        Assert.Equal("com.example.diagnostics", descriptor.Key);
        Assert.Equal("1.2.3", descriptor.Value.Version);
        var tool = Assert.Single(descriptor.Value.Tools);
        Assert.Equal("build_info", tool.Name);
        Assert.Equal("/api/v1/ext/com.example.diagnostics/build-info", tool.Path);
        Assert.True(tool.Annotations!.ReadOnly);

        var result = await client.CallExtensionToolAsync(tool.Method, tool.Path);
        using var resultJson = JsonDocument.Parse(result);
        Assert.Equal("1.0.0", resultJson.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task ProfileMode_AllowsReadOnlyProfilerSessionControl()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions
        {
            Port = port,
            Mode = "profile",
            ReadOnly = true,
            EnableProfiler = true
        });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());
        await WaitForStatusAsync(client);

        var started = await client.StartPerformanceSessionAsync(60_000);
        Assert.NotNull(started);
        Assert.True(started.Session.Active);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.StartPerformanceSessionAsync(60_000));
        Assert.Null(await client.StopProfilerAsync(
            started.Session.SessionId!,
            "wrong-stop-token"));
        Assert.True((await client.GetProfilerSessionAsync())!.IsActive);
        await Task.Delay(50);

        var stopped = await client.StopPerformanceSessionAsync(
            started.Session.SessionId!,
            started.Session.StopToken!);
        Assert.False(stopped.Session.Active);
        Assert.True(stopped.Session.SampleCount >= 2);
        Assert.True(stopped.Session.SampledDurationMs > 0);
    }

    [Fact]
    public void RegisterExtension_RejectsInvalidNamespace()
    {
        var options = new AgentOptions();

        Assert.Throws<ArgumentException>(() => options.RegisterExtension("diagnostics", "Invalid namespace"));
    }

    [Fact]
    public void RegisterExtension_RejectsInvalidVersion()
    {
        var options = new AgentOptions();

        Assert.Throws<ArgumentException>(() => options.RegisterExtension("com.example.diagnostics", "Diagnostics", "beta"));
    }

    [Fact]
    public async Task Extensions_WithSamePathAndDifferentMethods_GenerateUniqueToolNames()
    {
        var port = GetFreePort();
        var options = new AgentOptions { Port = port };
        var extension = options.RegisterExtension("com.example.diagnostics", "Diagnostics");
        extension.MapGet("echo", _ => Task.FromResult(HttpResponse.Json(new { method = "GET" })));
        extension.MapPost("echo", _ => Task.FromResult(HttpResponse.Json(new { method = "POST" })));

        using var service = new DevFlowAgentService(options);
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var extensions = await client.GetExtensionsAsync();
        var descriptor = Assert.Single(extensions);
        var toolNames = descriptor.Value.Tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "get_echo", "post_echo" }, toolNames);
    }

    [Fact]
    public void StartServerOnly_RejectsDuplicateExtensionNamespace()
    {
        var options = new AgentOptions { Port = GetFreePort() };
        options.RegisterExtension("com.example.diagnostics", "First");
        options.RegisterExtension("com.example.diagnostics", "Second");

        Assert.Throws<InvalidOperationException>(() => new DevFlowAgentService(options));
    }

    [Fact]
    public async Task Tap_ShellItem_NavigatesSemanticallyViaHttp()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        var shell = new Shell();
        var nativeItem = BuildShellItem("Native", "native");
        var dialogsItem = BuildShellItem("Dialogs", "dialogs");
        shell.Items.Add(nativeItem);
        shell.Items.Add(dialogsItem);
        shell.CurrentItem = nativeItem;

        var currentItemChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Shell.CurrentItem) && ReferenceEquals(shell.CurrentItem, dialogsItem))
                currentItemChanged.TrySetResult();
        };

        var app = new TestApplication([shell]);
#pragma warning disable CS0618
        app.MainPage = shell;
#pragma warning restore CS0618
        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);

        var shellItem = Assert.Single(await client.QueryAsync(type: "ShellItem", text: "Dialogs"));
        Assert.Equal("ShellItem", shellItem.Type);
        Assert.Equal("Microsoft.Maui.Controls.ShellItem", shellItem.FullType);

        var tapTask = client.TapAsync(shellItem.Id);
        await currentItemChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(tapTask.IsCompleted);
        Assert.True(await tapTask);
        Assert.Same(dialogsItem, shell.CurrentItem);
    }

    private static ShellItem BuildShellItem(string title, string route)
    {
        var item = new ShellItem { Title = title, Route = route };
        var section = new ShellSection { Title = title, Route = route };
        section.Items.Add(new ShellContent
        {
            ContentTemplate = new DataTemplate(() => new ContentPage { Title = title }),
        });
        item.Items.Add(section);
        return item;
    }

    private sealed class TestApplication(IEnumerable<IVisualTreeElement> children)
        : Application, IVisualTreeElement
    {
        private readonly IReadOnlyList<IVisualTreeElement> _children = new List<IVisualTreeElement>(children);

        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
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

    private static int GetFreePort() => TestPorts.Reserve();

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
