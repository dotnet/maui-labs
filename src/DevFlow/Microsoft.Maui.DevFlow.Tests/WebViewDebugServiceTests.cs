using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DevFlow.Blazor;
using Microsoft.Maui.DevFlow.WebView;
using Microsoft.Maui.Hosting;
using AgentService = Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewDebugServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_GenericAndBlazorInEitherOrder_IsLazyAndFirstExplicitOptionsWin(bool blazorFirst)
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        var genericConfigurations = 0;
        var blazorConfigurations = 0;
        void ConfigureGeneric(WebViewDebugOptions options)
        {
            genericConfigurations++;
            options.EnableLogging = false;
            options.EnableWebViewInspection = false;
        }
        void ConfigureBlazor(BlazorWebViewDebugOptions options)
        {
            blazorConfigurations++;
            options.EnableLogging = true;
            options.EnableWebViewInspection = true;
        }
        if (blazorFirst) builder.AddMauiBlazorDevFlowTools(ConfigureBlazor);
        builder.AddMauiWebViewDevFlowTools(ConfigureGeneric);
        if (!blazorFirst) builder.AddMauiBlazorDevFlowTools(ConfigureBlazor);
        builder.AddMauiWebViewDevFlowTools(_ => genericConfigurations++);
        builder.AddMauiBlazorDevFlowTools(_ => blazorConfigurations++);
        builder.AddMauiWebViewDevFlowTools();

        var genericDescriptor = Assert.Single(builder.Services, d => d.ServiceType == typeof(WebViewDebugService));
        var blazorDescriptor = Assert.Single(builder.Services, d => d.ServiceType == typeof(BlazorWebViewDebugService));
        Assert.Null(genericDescriptor.ImplementationInstance);
        Assert.Null(blazorDescriptor.ImplementationInstance);
        Assert.NotNull(genericDescriptor.ImplementationFactory);
        Assert.NotNull(blazorDescriptor.ImplementationFactory);
        using var provider = builder.Services.BuildServiceProvider();
        var generic = provider.GetRequiredService<WebViewDebugService>();
        var blazor = provider.GetRequiredService<BlazorWebViewDebugService>();
        Assert.Same(generic, provider.GetRequiredService<WebViewDebugServiceBase>());
        Assert.Same(blazor, provider.GetRequiredService<BlazorWebViewDebugServiceBase>());
        Assert.False(generic.Options.EnableLogging);
        Assert.False(generic.Options.EnableWebViewInspection);
        Assert.True(blazor.Options.EnableLogging);
        Assert.True(blazor.Options.EnableWebViewInspection);
        Assert.Equal(1, genericConfigurations);
        Assert.Equal(1, blazorConfigurations);
    }

    [Fact]
    public void Registration_PreexistingServiceFactory_IsNotInvokedOrReplacedByExtensions()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        var creations = 0;
        var options = new WebViewDebugOptions { EnableLogging = false, EnableWebViewInspection = false };
        builder.Services.AddSingleton<WebViewDebugService>(_ =>
        {
            creations++;
            return new(options);
        });
        builder.AddMauiWebViewDevFlowTools(_ => throw new InvalidOperationException("Must preserve user registration."));
        builder.AddMauiBlazorDevFlowTools();
        builder.AddMauiWebViewDevFlowTools();
        Assert.Equal(0, creations);
        Assert.Single(builder.Services, d => d.ServiceType == typeof(WebViewDebugService));
        using var provider = builder.Services.BuildServiceProvider();
        var service = provider.GetRequiredService<WebViewDebugService>();
        Assert.Same(options, service.Options);
        Assert.Same(service, provider.GetRequiredService<WebViewDebugServiceBase>());
        Assert.Equal(1, creations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_ExplicitlyDisabledGeneric_RemainsDisabledWhenBlazorIsAdded(bool defaultFirst)
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        if (defaultFirst)
        {
            builder.AddMauiWebViewDevFlowTools();
            builder.AddMauiWebViewDevFlowTools();
        }
        builder.AddMauiWebViewDevFlowTools(options => options.Enabled = false);
        builder.AddMauiBlazorDevFlowTools();
        builder.AddMauiWebViewDevFlowTools(options => options.Enabled = true);
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Null(provider.GetService<WebViewDebugService>());
        Assert.Null(provider.GetService<WebViewDebugServiceBase>());
        Assert.NotNull(provider.GetService<BlazorWebViewDebugService>());
    }

    [Fact]
    public void Registration_ExplicitlyDisabledBlazor_RepeatedCallsDoNotEnableIt()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        builder.AddMauiBlazorDevFlowTools(options => options.Enabled = false);
        builder.AddMauiBlazorDevFlowTools(options => options.Enabled = true);
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Null(provider.GetService<BlazorWebViewDebugService>());
        Assert.Null(provider.GetService<WebViewDebugService>());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("null", null)]
    [InlineData("<null>", null)]
    [InlineData("undefined", null)]
    [InlineData("\"complete\"", "complete")]
    [InlineData("\"{\\\"id\\\":2}\"", "{\"id\":2}")]
    [InlineData("{\"id\":2}", "{\"id\":2}")]
    public void NormalizeEvaluationResult_NativeFormats_DecodesOnlyEnvelope(string? native, string? expected)
        => Assert.Equal(expected, WebViewDebugServiceBase.WebViewBridge.NormalizeEvaluationResult(native));

    [Fact]
    public async Task Initialize_OrdinaryDocument_UsesEmbeddedEngineWithoutBlazorOrHostedAsset()
    {
        var scripts = new ConcurrentQueue<string>();
        var loaded = false;
        using var service = new TestService(script =>
        {
            scripts.Enqueue(script);
            if (script == "document.readyState") return Task.FromResult<string?>("complete");
            if (script.StartsWith("typeof chobitsu")) return Task.FromResult<string?>(loaded ? "loaded" : "waiting");
            if (script.Contains("chobitsu v")) loaded = true;
            if (script.StartsWith("if (typeof chobitsu")) loaded = true;
            if (script.Contains("chobitsu_not_found")) return Task.FromResult<string?>("ready");
            if (script.Contains("window.__chobitsuReady ?")) return Task.FromResult<string?>("ready");
            return Task.FromResult<string?>(null);
        });
        await service.InitializeOne();
        Assert.True(service.IsReady);
        Assert.Contains(scripts, s => s.StartsWith("if (typeof chobitsu"));
        Assert.DoesNotContain(scripts, s => s.Contains("querySelector('#app')") || s.Contains("_content/"));
        Assert.True(service.DispatchCount > 0);
    }

    [Fact]
    public async Task SendCdpCommand_ConcurrentRequests_SerializesSingleSlotMailbox()
    {
        var firstSend = NewCompletion();
        var release = NewCompletion();
        var sends = 0;
        long pendingWireId = 0;
        using var service = new TestService(async script =>
        {
            if (script.Contains("sendRawMessage"))
            {
                pendingWireId = ReadSentWireId(script);
                if (Interlocked.Increment(ref sends) == 1)
                {
                    firstSend.TrySetResult();
                    await release.Task;
                }
                return "__cdp_pending__";
            }
            if (script.Contains("var r = window.__cdpResponse"))
                return $"{{\"id\":{pendingWireId},\"result\":{{}}}}";
            return ReadyResult(script);
        });
        var first = service.SendCdpCommandAsync("{\"id\":1,\"method\":\"Runtime.evaluate\"}");
        await firstSend.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.SendCdpCommandAsync("{\"id\":2,\"method\":\"Runtime.evaluate\"}");
        Assert.Equal(1, Volatile.Read(ref sends));
        release.TrySetResult();
        Assert.Equal(1, JsonDocument.Parse(await first).RootElement.GetProperty("id").GetInt32());
        Assert.Equal(2, JsonDocument.Parse(await second).RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task SendCdpCommand_UnsolicitedOrMismatchedReply_WaitsForOwnId()
    {
        var reads = 0;
        long wireId = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("sendRawMessage"))
            {
                wireId = ReadSentWireId(script);
                return Task.FromResult<string?>("__cdp_pending__");
            }
            return Task.FromResult(script.Contains("var r = window.__cdpResponse") ? Interlocked.Increment(ref reads) switch
            {
                1 => "{\"method\":\"Runtime.consoleAPICalled\"}",
                2 => $"{{\"id\":{wireId + 1},\"result\":{{}}}}",
                _ => $"{{\"id\":{wireId},\"result\":{{}}}}"
            } : ReadyResult(script));
        });
        var response = await service.SendCdpCommandAsync("{\"id\":7,\"method\":\"Runtime.evaluate\"}");
        Assert.Equal(7, JsonDocument.Parse(response).RootElement.GetProperty("id").GetInt32());
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task SendCdpCommand_ActualScriptsLateResponseAfterTimeout_ReusedCallerIdCannotSatisfyNextCommand()
    {
        using var scriptHost = new JavaScriptHost();
        using var service = new TestService(script =>
            script.Contains("sendRawMessage") || script.Contains("var r = window.__cdpResponse")
                ? scriptHost.EvaluateAsync(script) : Task.FromResult(ReadyResult(script)),
            responseBudget: 1);
        const string command = "{\"id\":99999,\"method\":\"Runtime.evaluate\"}";
        using var timedOut = JsonDocument.Parse(await service.SendCdpCommandAsync(command));
        Assert.Equal(99999, timedOut.RootElement.GetProperty("id").GetInt32());
        Assert.Contains("CDP response timeout", timedOut.RootElement.GetProperty("error").GetProperty("message").GetString());

        using var next = JsonDocument.Parse(await service.SendCdpCommandAsync(command));
        Assert.Equal(99999, next.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("fresh", next.RootElement.GetProperty("result").GetProperty("value").GetString());
        using var sentIds = JsonDocument.Parse((await scriptHost.EvaluateAsync("JSON.stringify(window.__sentIds)"))!);
        Assert.Equal(2, sentIds.RootElement.GetArrayLength());
        Assert.NotEqual(sentIds.RootElement[0].GetInt64(), sentIds.RootElement[1].GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendCdpCommand_WireResponse_RemapsCallerIdAndPreservesResultOrError(bool protocolError)
    {
        long wireId = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("sendRawMessage"))
            {
                wireId = ReadSentWireId(script);
                return Task.FromResult<string?>("__cdp_pending__");
            }
            return Task.FromResult(script.Contains("var r = window.__cdpResponse")
                ? $"{{\"id\":{wireId},{(protocolError ? "\"error\":{\"code\":-32601,\"message\":\"unsupported\"}" : "\"result\":{\"value\":\"result\"}")}}}"
                : ReadyResult(script));
        });
        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync("{\"id\":99999,\"method\":\"Runtime.evaluate\"}"));
        Assert.Equal(99999, response.RootElement.GetProperty("id").GetInt32());
        if (protocolError) Assert.Equal(-32601, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        else Assert.Equal("result", response.RootElement.GetProperty("result").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Deactivate_ActiveAndQueuedCommands_CancelsBothAndCleansUpOnce()
    {
        var entered = NewCompletion();
        var never = NewCompletion();
        var cleanupCount = 0;
        using var service = new TestService(async script =>
        {
            if (!script.Contains("sendRawMessage")) return ReadyResult(script);
            entered.TrySetResult();
            await never.Task;
            return "__cdp_pending__";
        }, cleanup: () => cleanupCount++);
        var first = service.SendCdpCommandAsync("{\"id\":1,\"method\":\"Runtime.evaluate\"}");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = service.SendCdpCommandAsync("{\"id\":2,\"method\":\"Runtime.evaluate\"}");
        service.Detach();
        service.Detach();
        Assert.Contains("no longer available", await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("no longer available", await queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(service.IsReady);
        Assert.Equal(1, cleanupCount);
        never.TrySetResult();
    }

    [Fact]
    public async Task SendCdpCommand_FailedInitialization_DoesNotReportSuccess()
    {
        using var service = new TestService(script => Task.FromResult<string?>(
            script.Contains("chobitsu_not_found") ? "chobitsu_not_found" : ReadyResult(script)));
        var response = await service.SendCdpCommandAsync("{\"id\":42,\"method\":\"DOM.getDocument\"}");
        Assert.True(JsonDocument.Parse(response).RootElement.TryGetProperty("error", out _));
        Assert.False(service.IsReady);
    }

    [Fact]
    public async Task Initialize_TransientFailure_DocumentMonitorRecoversWithoutCdpRequest()
    {
        var injections = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("chobitsu_not_found") && Interlocked.Increment(ref injections) == 1)
                throw new InvalidOperationException("The document was replaced.");
            return Task.FromResult(ReadyResult(script));
        });
        await service.InitializeOne();
        Assert.False(service.IsReady);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!service.IsReady && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(service.IsReady);
        Assert.Equal(2, injections);
        Assert.Null(service.Bridges[0].LastError);
    }

    [Fact]
    public async Task Initialize_DisabledRoutineLogging_StillTracesFailureAndRetainsDetails()
    {
        using var trace = new ErrorTraceListener();
        using var service = new TestService(script =>
        {
            if (script.Contains("chobitsu_not_found")) throw new InvalidOperationException("injection-test-failure");
            return Task.FromResult(ReadyResult(script));
        });
        Assert.Null(service.LogCallback);
        await service.InitializeOne();
        Assert.Contains(trace.Messages, m => m.Contains("Initialization failed") && m.Contains("injection-test-failure"));
        Assert.Contains("injection-test-failure", service.Bridges[0].LastError);
        var response = await service.SendCdpCommandAsync("{\"id\":17,\"method\":\"DOM.getDocument\"}");
        Assert.Contains("injection-test-failure", JsonDocument.Parse(response).RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Initialize_ReadinessProbeFailure_ReportsNativeCauseInsteadOfGenericNotReady()
    {
        using var trace = new ErrorTraceListener();
        using var service = new TestService(_ => throw new InvalidOperationException("native-probe-failure"), loadBudget: 1);
        var response = await service.SendCdpCommandAsync("{\"id\":18,\"method\":\"DOM.getDocument\"}");
        Assert.Contains("native-probe-failure", JsonDocument.Parse(response).RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Contains(trace.Messages, m => m.Contains("native-probe-failure"));
    }

    [Fact]
    public async Task Monitor_DisabledRoutineLogging_StillTracesErrors()
    {
        using var trace = new ErrorTraceListener();
        var probes = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("window.__chobitsuReady ?") && Interlocked.Increment(ref probes) > 1)
                throw new InvalidOperationException("monitor-test-failure");
            return Task.FromResult(ReadyResult(script));
        });
        await service.InitializeOne();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!trace.Messages.Any(m => m.Contains("monitor-test-failure")) && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        Assert.Contains(trace.Messages, m => m.Contains("Document/console monitor failed") && m.Contains("monitor-test-failure"));
    }

    [Fact]
    public void Cleanup_DisabledRoutineLogging_StillTracesErrors()
    {
        using var trace = new ErrorTraceListener();
        using var service = new TestService(script => Task.FromResult(ReadyResult(script)),
            cleanup: () => throw new InvalidOperationException("cleanup-test-failure"));
        service.Detach();
        Assert.Contains(trace.Messages, m => m.Contains("Attachment cleanup failed") && m.Contains("cleanup-test-failure"));
    }

    [Fact]
    public async Task Monitor_ConsoleEntries_RoutesStructuredLogs()
    {
        var log = new TaskCompletionSource<(string Level, string Message, string? Exception)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new TestService(script => Task.FromResult(
            script.Contains("var logs = JSON.stringify(window.__webviewLogs)")
                ? "[{\"l\":\"error\",\"m\":\"console failure\",\"e\":\"stack\"}]" : ReadyResult(script)));
        service.WebViewLogCallback = (level, message, exception) => log.TrySetResult((level, message, exception));
        await service.InitializeOne();
        var entry = await log.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("Error", "console failure", "stack"), entry);
    }

    [Fact]
    public async Task SendCdpCommand_UnsupportedBrowserMethod_ReturnsProtocolError()
    {
        using var service = new TestService(script => Task.FromResult(ReadyResult(script)));
        var response = await service.SendCdpCommandAsync("{\"id\":8,\"method\":\"Browser.close\"}");
        Assert.Equal(-32601, JsonDocument.Parse(response).RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData("next.html")]
    [InlineData("/next.html")]
    [InlineData("//example.org/next")]
    public async Task Navigate_RelativeGenericUrl_DoesNotUseNativeNavigationOrBlazorRouter(string url)
    {
        var scripts = new List<string>();
        using var service = new TestService(script =>
        {
            scripts.Add(script);
            return Task.FromResult(ReadyResult(script));
        }, navigate: _ => throw new InvalidOperationException("Browser-relative URL must not be passed unresolved to native navigation."));
        var result = await service.SendCdpCommandAsync(JsonSerializer.Serialize(new { id = 9, method = "Page.navigate", @params = new { url } }));
        Assert.False(JsonDocument.Parse(result).RootElement.TryGetProperty("error", out _));
        Assert.Single(scripts);
        Assert.Equal($"location.href = {JsonSerializer.Serialize(url)};", scripts[0]);
    }

    [Theory]
    [InlineData(false, "next.html", "https://origin.example/dir/next.html")]
    [InlineData(false, "/next.html", "https://origin.example/next.html")]
    [InlineData(false, "//example.org/next", "https://example.org/next")]
    [InlineData(true, "next.html", "https://origin.example/dir/next.html")]
    [InlineData(true, "/next.html", "https://origin.example/next.html")]
    [InlineData(true, "//example.org/next", "https://example.org/next")]
    public async Task Navigate_GenericAndBlazorFallback_PreservesBrowserRelativeResolution(bool blazor, string url, string expected)
    {
        using var scriptHost = new JavaScriptHost();
        Action<string> native = _ => throw new InvalidOperationException("Unexpected native navigation.");
        using WebViewDebugServiceBase service = blazor
            ? new TestBlazorService(scriptHost.EvaluateAsync, native)
            : new TestService(scriptHost.EvaluateAsync, navigate: native);
        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            JsonSerializer.Serialize(new { id = 19, method = "Page.navigate", @params = new { url } })));
        Assert.False(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal(expected, await scriptHost.EvaluateAsync("location.href"));
    }

    [Theory]
    [InlineData(false, "https://example.org/next")]
    [InlineData(false, "app://0.0.0.1/next")]
    [InlineData(false, "file:///dir/next.html")]
    [InlineData(true, "https://example.org/next")]
    [InlineData(true, "app://0.0.0.1/next")]
    [InlineData(true, "file:///dir/next.html")]
    public async Task Navigate_ExplicitScheme_PreservesNativeNavigation(bool blazor, string url)
    {
        string? navigated = null;
        Func<string, Task<string?>> evaluate = _ => throw new InvalidOperationException("Unexpected script navigation.");
        using WebViewDebugServiceBase service = blazor
            ? new TestBlazorService(evaluate, target => navigated = target)
            : new TestService(evaluate, navigate: target => navigated = target);
        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            JsonSerializer.Serialize(new { id = 20, method = "Page.navigate", @params = new { url } })));
        Assert.False(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal(url, navigated);
    }

    [Fact]
    public void ConnectAgent_ReplacementThenStaleCleanup_DoesNotRemoveNewBridge()
    {
        using var agent = new TestAgent();
        var owner = new object();
        using var oldService = new TestService(script => Task.FromResult(ReadyResult(script)), owner: owner);
        using var replacement = new TestService(script => Task.FromResult(ReadyResult(script)), owner: owner);
        oldService.ConnectAgent(agent);
        oldService.ConnectAgent(agent);
        replacement.ConnectAgent(agent);
        Assert.Single(agent.Snapshot);
        oldService.Detach();
        var registered = Assert.Single(agent.Snapshot);
        Assert.Equal("hybrid", registered.HostKind);
        Assert.True(registered.Owner!.TryGetTarget(out var target));
        Assert.Same(owner, target);
        replacement.Detach();
        Assert.Empty(agent.Snapshot);
    }

    private static string? ReadyResult(string script)
        => script == "document.readyState" ? "complete" :
           script.StartsWith("typeof chobitsu") ? "loaded" :
           script.Contains("chobitsu_not_found") || script.Contains("window.__chobitsuReady ?") ? "ready" : null;

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static long ReadSentWireId(string script)
    {
        var encoded = Regex.Match(script, @"JSON\.parse\('((?:\\.|[^'])*)'\)").Groups[1].Value;
        using var command = JsonDocument.Parse(Regex.Unescape(encoded));
        return command.RootElement.GetProperty("id").GetInt64();
    }

    private sealed class TestAgent : AgentService
    {
        public Microsoft.Maui.DevFlow.Agent.Core.CdpWebViewInfo[] Snapshot => GetCdpWebViewsSnapshot();
    }

    private sealed class TestService : WebViewDebugServiceBase
    {
        private readonly int _index;
        private readonly int _loadBudget;
        private readonly int _responseBudget;
        public int DispatchCount { get; private set; }
        public TestService(Func<string, Task<string?>> evaluate, Action? cleanup = null, object? owner = null,
            int loadBudget = 2000, int responseBudget = 10000, Action<string>? navigate = null)
        {
            _loadBudget = loadBudget;
            _responseBudget = responseBudget;
            _index = AddWebViewBridge(evaluate, () => { }, navigate ?? (_ => { }), null, cleanup, "hybrid", owner);
        }
        public override void ConfigureHandler() { }
        protected override int GetWebViewLoadDelayMs() => _loadBudget;
        protected override int GetCdpResponseTimeoutMs() => _responseBudget;
        public Task InitializeOne() => InitializeBridgeAsync(_index);
        public void Detach() => DeactivateWebViewBridge(_index);
        protected override Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func) { DispatchCount++; return func(); }
        protected override Task<T> RunOnMainThreadAsync<T>(Func<T> func) { DispatchCount++; return Task.FromResult(func()); }
        protected override Task RunOnMainThreadAsync(Func<Task> func) { DispatchCount++; return func(); }
        protected override Task RunOnMainThreadAsync(Action action) { DispatchCount++; action(); return Task.CompletedTask; }
        protected override void PostToMainThread(Action action) => action();
    }

    private sealed class TestBlazorService : BlazorWebViewDebugServiceBase
    {
        public TestBlazorService(Func<string, Task<string?>> evaluate, Action<string> navigate)
            => AddWebViewBridge(evaluate, () => { }, navigate);
        public override void ConfigureHandler() { }
        protected override Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func) => func();
        protected override Task<T> RunOnMainThreadAsync<T>(Func<T> func) => Task.FromResult(func());
        protected override Task RunOnMainThreadAsync(Func<Task> func) => func();
        protected override Task RunOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
        protected override void PostToMainThread(Action action) => action();
    }

    private sealed class JavaScriptHost : IDisposable
    {
        private readonly Process _process;
        public JavaScriptHost()
        {
            var start = new ProcessStartInfo("node")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("""
                const vm = require('node:vm');
                const readline = require('node:readline');
                let href = 'https://origin.example/dir/start.html';
                const location = {};
                Object.defineProperty(location, 'href', {
                    get: () => href, set: value => href = new URL(value, href).href
                });
                const window = { location, __sentIds: [] };
                let receiver = () => {};
                const chobitsu = {
                    onMessage: receiver,
                    setOnMessage(callback) { this.onMessage = receiver = callback; },
                    sendRawMessage(json) {
                        const id = JSON.parse(json).id;
                        window.__sentIds.push(id);
                        if (window.__sentIds.length === 2) {
                            queueMicrotask(() => {
                                receiver(JSON.stringify({id: window.__sentIds[0], result: {value: 'stale'}}));
                                receiver(JSON.stringify({id, result: {value: 'fresh'}}));
                            });
                        }
                    }
                };
                const context = vm.createContext({ window, location, chobitsu });
                readline.createInterface({ input: process.stdin }).on('line', line => {
                    try {
                        const value = vm.runInContext(JSON.parse(line), context);
                        process.stdout.write(JSON.stringify(value === undefined ? null : value) + '\n');
                    } catch (error) {
                        process.stdout.write(JSON.stringify({error: error.message}) + '\n');
                    }
                });
                """);
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node script evaluator.");
        }

        public async Task<string?> EvaluateAsync(string script)
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(script));
            var response = await _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))
                ?? throw new InvalidOperationException("Node script evaluator exited.");
            using var result = JsonDocument.Parse(response);
            if (result.RootElement.ValueKind == JsonValueKind.Object)
                throw new InvalidOperationException(result.RootElement.GetProperty("error").GetString());
            return result.RootElement.ValueKind == JsonValueKind.Null ? null : result.RootElement.GetString();
        }

        public void Dispose()
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(5000)) _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }
    }

    private sealed class ErrorTraceListener : TraceListener
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ErrorTraceListener() => Trace.Listeners.Add(this);
        public override void Write(string? message) { if (message is not null) Messages.Enqueue(message); }
        public override void WriteLine(string? message) => Write(message);
        protected override void Dispose(bool disposing)
        {
            if (disposing) Trace.Listeners.Remove(this);
            base.Dispose(disposing);
        }
    }
}
