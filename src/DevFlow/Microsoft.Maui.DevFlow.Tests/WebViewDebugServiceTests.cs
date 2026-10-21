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
    [Fact]
    public void PublicApi_DoesNotRetainCompatibilityOnlyWebViewWrappers()
    {
        Assert.Null(typeof(AgentService).GetProperty("CdpCommandHandler"));
        Assert.Null(typeof(AgentService).GetProperty("CdpReadyCheck"));
        Assert.Single(typeof(AgentService).GetMethods(), m => m.Name == "RegisterCdpWebView");
        Assert.Single(typeof(AgentService).GetMethods(), m => m.Name == "UnregisterCdpWebView");
        Assert.Null(typeof(BlazorWebViewDebugService).Assembly.GetType("Microsoft.Maui.DevFlow.Blazor.BlazorWebViewDebugServiceBase"));
        Assert.Null(typeof(BlazorWebViewDebugService).Assembly.GetType("Microsoft.Maui.DevFlow.Blazor.ChobitsuDebugScript"));
        Assert.Null(typeof(WebViewDebugService).GetMethod("ConfigureHandler"));
        Assert.All(typeof(WebViewDebugServiceBase).GetMethods().Where(m => m.Name == "SendCdpCommandAsync"),
            method => Assert.Equal(2, method.GetParameters().Length));
    }

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
        Assert.Null(provider.GetService<WebViewDebugServiceBase>());
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
        Assert.Null(provider.GetService<WebViewDebugServiceBase>());
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

    [Fact]
    public void Registration_PreexistingBlazorFactory_RemainsLazyAndPreservesOptions()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        var creations = 0;
        var options = new BlazorWebViewDebugOptions { Enabled = false };
        builder.Services.AddSingleton<BlazorWebViewDebugService>(_ =>
        {
            creations++;
            return new(options);
        });
        builder.AddMauiBlazorDevFlowTools(_ => throw new InvalidOperationException("Must preserve user registration."));
        builder.AddMauiBlazorDevFlowTools();
        Assert.Equal(0, creations);
        Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(BlazorWebViewDebugService));
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Same(options, provider.GetRequiredService<BlazorWebViewDebugService>().Options);
        Assert.NotNull(provider.GetService<WebViewDebugService>());
        Assert.Equal(1, creations);
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
    public async Task Reload_OldCompleteDocumentCannotBecomeReadyOrReceiveQuery_BeforeReplacement()
    {
        using var host = new JavaScriptHost();
        await host.EvaluateAsync("document = { readyState: 'complete' }; null");
        var reloads = 0;
        var sends = 0;
        string? reply = null;
        using var service = new TestService(script =>
        {
            if (script == "document.readyState" ||
                script.Contains("Object.defineProperty(document,") ||
                script.Contains("Object.prototype.hasOwnProperty.call(document,"))
                return host.EvaluateAsync(script);
            if (script.Contains("sendRawMessage"))
            {
                sends++;
                reply = JsonSerializer.Serialize(new { id = ReadSentWireId(script), result = new { value = "new document" } });
                return Task.FromResult<string?>("__cdp_pending__");
            }
            if (script.Contains("var r = window.__cdpResponse")) return Task.FromResult(reply);
            return Task.FromResult(ReadyResult(script));
        }, loadBudget: 1, reload: () => reloads++);
        await service.InitializeOne();
        Assert.True(service.IsReady);

        using var reload = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":81,"method":"Page.reload"}""", 0));
        Assert.False(reload.RootElement.TryGetProperty("error", out _));
        Assert.Equal(1, reloads);
        Assert.False(service.IsReady);
        using var oldQuery = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":82,"method":"Runtime.evaluate","params":{"expression":"document.title"}}""", 0));
        Assert.Contains("Document readiness timed out", oldQuery.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.False(service.IsReady);
        Assert.Equal(0, sends);

        await host.EvaluateAsync("document = { readyState: 'complete' }; null");
        await service.InitializeOne();
        Assert.True(service.IsReady);
        using var newQuery = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":83,"method":"Runtime.evaluate","params":{"expression":"document.title"}}""", 0));
        Assert.Equal("new document", newQuery.RootElement.GetProperty("result").GetProperty("value").GetString());
        Assert.Equal(1, sends);
        Assert.Equal(1, reloads);
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
        var first = service.SendCdpCommandAsync("{\"id\":1,\"method\":\"Runtime.evaluate\"}", 0);
        await firstSend.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.SendCdpCommandAsync("{\"id\":2,\"method\":\"Runtime.evaluate\"}", 0);
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
        var response = await service.SendCdpCommandAsync("{\"id\":7,\"method\":\"Runtime.evaluate\"}", 0);
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
        using var timedOut = JsonDocument.Parse(await service.SendCdpCommandAsync(command, 0));
        Assert.Equal(99999, timedOut.RootElement.GetProperty("id").GetInt32());
        Assert.Contains("CDP response timeout", timedOut.RootElement.GetProperty("error").GetProperty("message").GetString());

        using var next = JsonDocument.Parse(await service.SendCdpCommandAsync(command, 0));
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
        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync("{\"id\":99999,\"method\":\"Runtime.evaluate\"}", 0));
        Assert.Equal(99999, response.RootElement.GetProperty("id").GetInt32());
        if (protocolError) Assert.Equal(-32601, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        else Assert.Equal("result", response.RootElement.GetProperty("result").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendCdpCommand_LiteralTemplateMarker_PreservesExpressionAndTextOnBothTransports(bool native)
    {
        const string marker = "%NATIVE_CDP_BINDING%";
        const string expression = "'%NATIVE_CDP_BINDING%'";
        NativeCdpResponseChannel? channel = null;
        string? reply = null;
        var sends = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("sendRawMessage"))
            {
                sends++;
                var encoded = Regex.Match(script, @"JSON\.parse\('((?:\\.|[^'])*)'\)").Groups[1].Value;
                using var command = JsonDocument.Parse(Regex.Unescape(encoded));
                var parameters = command.RootElement.GetProperty("params");
                Assert.Equal(expression, parameters.GetProperty("expression").GetString());
                Assert.Equal(marker, parameters.GetProperty("text").GetString());
                var wireId = command.RootElement.GetProperty("id").GetInt64();
                reply = JsonSerializer.Serialize(new { id = wireId, result = new { result = new { value = marker } } });
                if (native)
                    Assert.True(channel!.TryReceive(JsonSerializer.Serialize(new { channel = channel.BindingName, response = reply })));
                return Task.FromResult<string?>("__cdp_pending__");
            }
            if (script.Contains("var r = window.__cdpResponse")) return Task.FromResult(reply);
            return Task.FromResult(ReadyResult(script));
        });
        if (native) channel = service.EnableNativeCdpResponses(0);

        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            JsonSerializer.Serialize(new { id = 731, method = "Runtime.evaluate", @params = new { expression, text = marker } }), 0));

        Assert.Equal(731, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(marker, response.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString());
        Assert.Equal(1, sends);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendCdpCommand_InputTextWithLiteralMarkers_PreservesTextAndFiresEventsOnce(bool native)
    {
        const string text = "%TEXT_LENGTH% %TEXT% %NATIVE_CDP_BINDING%";
        using var host = new JavaScriptHost();
        await host.EvaluateAsync("""
            window.__inputEvents = [];
            Event = class { constructor(type) { this.type = type; } };
            document = { activeElement: {
                tagName: 'INPUT', value: '', selectionStart: 0, selectionEnd: 0,
                dispatchEvent: event => window.__inputEvents.push(event.type)
            }};
            null
            """);
        var insertions = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("var el = document.activeElement"))
            {
                insertions++;
                return host.EvaluateAsync(script);
            }
            return Task.FromResult(ReadyResult(script));
        });
        if (native) service.EnableNativeCdpResponses(0);

        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(JsonSerializer.Serialize(
            new { id = 732, method = "Input.insertText", @params = new { text } }), 0));

        Assert.False(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal(732, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(text, await host.EvaluateAsync("document.activeElement.value"));
        using var state = JsonDocument.Parse((await host.EvaluateAsync(
            "JSON.stringify({caret:document.activeElement.selectionStart, events:window.__inputEvents})"))!);
        Assert.Equal(text.Length, state.RootElement.GetProperty("caret").GetInt32());
        Assert.Equal(new[] { "input", "change" },
            state.RootElement.GetProperty("events").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(1, insertions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendCdpCommand_NativeReplySurvivesDestroyedEvaluation_UsesActualResponseOnce(bool evaluationThrows)
    {
        NativeCdpResponseChannel? channel = null;
        var sends = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("sendRawMessage"))
            {
                sends++;
                var wireId = ReadSentWireId(script);
                Assert.True(channel!.TryReceive(JsonSerializer.Serialize(new
                {
                    channel = channel.BindingName,
                    response = JsonSerializer.Serialize(new { id = wireId, result = new { value = "actual reply" } })
                })));
                if (evaluationThrows) throw new InvalidOperationException("The old evaluation context was destroyed.");
                return Task.FromResult<string?>(null);
            }
            Assert.DoesNotContain("var r = window.__cdpResponse", script);
            return Task.FromResult(ReadyResult(script));
        });
        channel = service.EnableNativeCdpResponses(0);

        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":73,"method":"Runtime.evaluate","params":{"expression":"button.click()"}}""", 0));

        Assert.Equal(73, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("actual reply", response.RootElement.GetProperty("result").GetProperty("value").GetString());
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task SendCdpCommand_NativeReplyAbsentAfterDispatchFailure_ReportsOriginalErrorWithoutReplay()
    {
        var sends = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("sendRawMessage"))
            {
                sends++;
                throw new InvalidOperationException("native dispatch context destroyed");
            }
            return Task.FromResult(ReadyResult(script));
        }, responseBudget: 1);
        service.EnableNativeCdpResponses(0);

        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":74,"method":"Runtime.evaluate","params":{"expression":"button.click()"}}""", 0));

        Assert.Contains("CDP response timeout", response.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Contains("native dispatch context destroyed", response.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task SendCdpCommand_NativePendingAndQueuedCommands_DetachmentCancelsBoth()
    {
        var dispatched = NewCompletion();
        var sends = 0;
        using var service = new TestService(script =>
        {
            if (script.Contains("sendRawMessage"))
            {
                sends++;
                dispatched.TrySetResult();
                return Task.FromResult<string?>("__cdp_pending__");
            }
            return Task.FromResult(ReadyResult(script));
        });
        service.EnableNativeCdpResponses(0);
        var first = service.SendCdpCommandAsync("""{"id":1,"method":"Runtime.evaluate"}""", 0);
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = service.SendCdpCommandAsync("""{"id":2,"method":"Runtime.evaluate"}""", 0);

        service.Detach();

        Assert.Contains("no longer available", await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("no longer available", await queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, sends);
        Assert.False(service.IsReady);
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
        var first = service.SendCdpCommandAsync("{\"id\":1,\"method\":\"Runtime.evaluate\"}", 0);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = service.SendCdpCommandAsync("{\"id\":2,\"method\":\"Runtime.evaluate\"}", 0);
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
        var response = await service.SendCdpCommandAsync("{\"id\":42,\"method\":\"DOM.getDocument\"}", 0);
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
        var response = await service.SendCdpCommandAsync("{\"id\":17,\"method\":\"DOM.getDocument\"}", 0);
        Assert.Contains("injection-test-failure", JsonDocument.Parse(response).RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Initialize_ReadinessProbeFailure_ReportsNativeCauseInsteadOfGenericNotReady()
    {
        using var trace = new ErrorTraceListener();
        using var service = new TestService(_ => throw new InvalidOperationException("native-probe-failure"), loadBudget: 1);
        var response = await service.SendCdpCommandAsync("{\"id\":18,\"method\":\"DOM.getDocument\"}", 0);
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
        var response = await service.SendCdpCommandAsync("{\"id\":8,\"method\":\"Browser.close\"}", 0);
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
        var result = await service.SendCdpCommandAsync(JsonSerializer.Serialize(new { id = 9, method = "Page.navigate", @params = new { url } }), 0);
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
            JsonSerializer.Serialize(new { id = 19, method = "Page.navigate", @params = new { url } }), 0));
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
            JsonSerializer.Serialize(new { id = 20, method = "Page.navigate", @params = new { url } }), 0));
        Assert.False(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal(url, navigated);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"children":[],"textContent":""}""")]
    [InlineData("""{"children":[],"textContent":"Loading..."}""")]
    public async Task BlazorReadiness_UnrenderedApp_WaitsDespiteCompleteDocument(string app)
    {
        using var host = new JavaScriptHost();
        await host.EvaluateAsync($$"""
            document = { readyState: 'complete', querySelector: selector => selector === '#app' ? {{app}} : null };
            null
            """);
        using var service = new TestBlazorService(host.EvaluateAsync, _ => { });
        var probes = 0;
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.HostReady(async script =>
        {
            var result = await host.EvaluateAsync(script);
            Assert.Equal("waiting", result);
            probes++;
            cancellation.Cancel();
            return result;
        }, cancellation.Token));
        Assert.Equal(1, probes);
    }

    [Theory]
    [InlineData("""{"children":[{"tagName":"H1"},{"tagName":"P"},{"tagName":"BUTTON"}],"textContent":"CounterCurrent count: 0Click me"}""")]
    [InlineData("""{"children":[{"tagName":"H1"},{"tagName":"P","children":[{"tagName":"EM"}]}],"textContent":"WeatherLoading..."}""")]
    public async Task BlazorReadiness_RenderedAppIncludingNestedLoadingText_AllowsBridgeInitialization(string app)
    {
        using var host = new JavaScriptHost();
        await host.EvaluateAsync($$"""
            document = { readyState: 'complete', querySelector: selector => selector === '#app' ? {{app}} : null };
            null
            """);
        var hostProbes = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var service = new TestBlazorService(async script =>
        {
            if (!script.Contains("querySelector('#app')"))
                return ReadyResult(script);

            hostProbes++;
            var result = await host.EvaluateAsync(script);
            Assert.Equal("ready", result);
            return result;
        }, _ => { });

        await service.InitializeOne().WaitAsync(cancellation.Token);

        Assert.True(service.IsReady);
        Assert.Equal(1, hostProbes);
        Assert.Null(service.Bridges[0].LastError);
    }

    [Fact]
    public async Task BlazorRelativeRoute_UsesRouterWithoutNativeDocumentNavigation()
    {
        using var host = new JavaScriptHost();
        await host.EvaluateAsync("window.Blazor = {navigateTo: target => window.__route = target}; null");
        using var service = new TestBlazorService(host.EvaluateAsync, _ => throw new InvalidOperationException("Native route navigation"));
        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":23,"method":"Page.navigate","params":{"url":"/counter"}}""", 0));
        Assert.False(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal("/counter", await host.EvaluateAsync("window.__route"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlazorRelativeRoute_MissingOrThrowingRouter_UsesHistoryAndPopState(bool throwingRouter)
    {
        using var host = new JavaScriptHost();
        await host.EvaluateAsync("""
            history = { pushState: (_, __, target) => window.__historyRoute = target };
            PopStateEvent = class { constructor(type) { this.type = type; } };
            window.dispatchEvent = event => window.__event = event.type;
            null
            """);
        if (throwingRouter)
            await host.EvaluateAsync("window.Blazor = { navigateTo: () => { throw new Error('router unavailable'); } }; null");
        using var service = new TestBlazorService(host.EvaluateAsync,
            _ => throw new InvalidOperationException("Unexpected native route navigation."));

        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":24,"method":"Page.navigate","params":{"url":"/counter?value=1#details"}}""", 0));

        Assert.False(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal("/counter?value=1#details", await host.EvaluateAsync("window.__historyRoute"));
        Assert.Equal("popstate", await host.EvaluateAsync("window.__event"));
        Assert.Equal("https://origin.example/dir/start.html", await host.EvaluateAsync("location.href"));
    }

    [Fact]
    public async Task BlazorRelativeRoute_RouterAndHistoryThrow_FallsBackToDocumentNavigation()
    {
        using var host = new JavaScriptHost();
        await host.EvaluateAsync("""
            window.Blazor = { navigateTo: () => { throw new Error('router unavailable'); } };
            history = { pushState: () => { throw new Error('history unavailable'); } };
            null
            """);
        using var service = new TestBlazorService(host.EvaluateAsync,
            _ => throw new InvalidOperationException("Unexpected native route navigation."));

        using var response = JsonDocument.Parse(await service.SendCdpCommandAsync(
            """{"id":25,"method":"Page.navigate","params":{"url":"/counter"}}""", 0));

        Assert.False(response.RootElement.TryGetProperty("error", out _));
        Assert.Equal("https://origin.example/counter", await host.EvaluateAsync("location.href"));
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
            int loadBudget = 2000, int responseBudget = 10000, Action<string>? navigate = null, Action? reload = null)
        {
            _loadBudget = loadBudget;
            _responseBudget = responseBudget;
            _index = AddWebViewBridge(evaluate, reload ?? (() => { }), navigate ?? (_ => { }), null, cleanup, "hybrid", owner);
        }
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

    private sealed class TestBlazorService : BlazorWebViewDebugService
    {
        public Task InitializeOne() => InitializeBridgeAsync(0);
        public Task<bool> HostReady(Func<string, Task<string?>> evaluate, CancellationToken token)
            => IsHostReadyAsync(evaluate, token);
        public TestBlazorService(Func<string, Task<string?>> evaluate, Action<string> navigate)
            => AddWebViewBridge(evaluate, () => { }, navigate);
        protected override Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func) => func();
        protected override Task<T> RunOnMainThreadAsync<T>(Func<T> func) => Task.FromResult(func());
        protected override Task RunOnMainThreadAsync(Func<Task> func) => func();
        protected override Task RunOnMainThreadAsync(Action action) { action(); return Task.CompletedTask; }
        protected override void PostToMainThread(Action action) => action();
    }

    [Fact]
    public async Task JavaScriptHost_StartupHandshake_PrecedesFirstCommandResponse()
    {
        using var host = new JavaScriptHost(beforeReady: """
            const deadline = Date.now() + 5100;
            while (Date.now() < deadline) {}
            """);

        Assert.Equal("initialized", await host.EvaluateAsync("window.__fixture = 'initialized'"));
        Assert.Equal("initialized", await host.EvaluateAsync("window.__fixture"));
    }

    [Theory]
    [InlineData("process.stderr.write('startup-test-error\\n'); process.exit(7);", "exit=7", "startup-test-error")]
    [InlineData("process.stdout.write('unexpected\\n');", "Unexpected startup acknowledgement", "unexpected")]
    public async Task JavaScriptHost_StartupFailure_ReportsPhaseAndStopsFurtherCommands(
        string beforeReady, string expected, string detail)
    {
        using var host = new JavaScriptHost(beforeReady);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.EvaluateAsync("'unused'"));
        Assert.Contains("startup", error.Message);
        Assert.Contains("pid=", error.Message);
        Assert.Contains(expected, error.Message);
        Assert.Contains(detail, error.Message);
        Assert.True(host.HasExited);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.EvaluateAsync("'must not be sent'"));
    }

    [Fact]
    public async Task JavaScriptHost_StartupTimeout_CancelsReadAndTerminatesOwnedProcess()
    {
        using var host = new JavaScriptHost(beforeReady: "while (true) {}",
            startupTimeout: TimeSpan.FromMilliseconds(250));

        var error = await Assert.ThrowsAsync<TimeoutException>(() => host.EvaluateAsync("'unused'"));

        Assert.Contains("startup", error.Message);
        Assert.Contains("pid=", error.Message);
        Assert.Contains("exit=", error.Message);
        Assert.True(host.HasExited);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.EvaluateAsync("'must not be sent'"));
    }

    [Fact]
    public async Task JavaScriptHost_CommandExit_ReportsCommandPhaseAndStderr()
    {
        using var host = new JavaScriptHost(beforeReady: """
            context.__exit = () => { process.stderr.write('command-test-error\n'); process.exit(9); };
            """);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.EvaluateAsync("__exit()"));

        Assert.Contains("command", error.Message);
        Assert.Contains("pid=", error.Message);
        Assert.Contains("exit=9", error.Message);
        Assert.Contains("command-test-error", error.Message);
        Assert.True(host.HasExited);
    }

    [Fact]
    public async Task JavaScriptHost_CommandTimeout_CancelsReadBeforeAnyFurtherCommand()
    {
        using var host = new JavaScriptHost();

        var error = await Assert.ThrowsAsync<TimeoutException>(() => host.EvaluateAsync("while (true) {}"));

        Assert.Contains("command", error.Message);
        Assert.Contains("5 seconds", error.Message);
        Assert.True(host.HasExited);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.EvaluateAsync("'must not be sent'"));
    }

    private sealed class JavaScriptHost : IDisposable
    {
        private const string ReadyMessage = "__devflow_test_ready__";
        private readonly Process _process;
        private readonly Task _stderrDrain;
        private readonly TimeSpan _startupTimeout;
        private readonly object _stderrGate = new();
        private readonly System.Text.StringBuilder _stderr = new();
        private Task? _startup;
        private bool _failed;
        public bool HasExited => _process.HasExited;

        public JavaScriptHost(string? beforeReady = null, TimeSpan? startupTimeout = null)
        {
            // Node startup is external-process work, not part of the five-second command budget.
            _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(30);
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
                Object.defineProperty(context, 'Blazor', {get: () => window.Blazor});
                readline.createInterface({ input: process.stdin }).on('line', line => {
                    try {
                        const value = vm.runInContext(JSON.parse(line), context);
                        process.stdout.write(JSON.stringify(value === undefined ? null : value) + '\n');
                    } catch (error) {
                        process.stdout.write(JSON.stringify({error: error.message}) + '\n');
                    }
                });
                """ + "\n" + beforeReady + "\n" +
                $"process.stdout.write('{ReadyMessage}\\n');");
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Node script evaluator.");
            _stderrDrain = DrainStderrAsync();
        }

        public async Task<string?> EvaluateAsync(string script)
        {
            if (_failed)
                throw new InvalidOperationException(Diagnostics("command", "Node evaluator is unavailable after a transport failure."));
            await (_startup ??= WaitForStartupAsync());
            using var commandTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            string response;
            try
            {
                await _process.StandardInput.WriteLineAsync(
                    JsonSerializer.Serialize(script).AsMemory(), commandTimeout.Token);
                await _process.StandardInput.FlushAsync(commandTimeout.Token);
                response = await ReadResponseAsync("command", commandTimeout.Token);
            }
            catch (OperationCanceledException ex) when (commandTimeout.IsCancellationRequested)
            {
                await StopFailedProcessAsync();
                throw new TimeoutException(Diagnostics("command", "Response timed out after 5 seconds."), ex);
            }
            catch (IOException ex)
            {
                await StopFailedProcessAsync();
                throw new InvalidOperationException(Diagnostics("command", "Node pipe failed."), ex);
            }
            using var result = JsonDocument.Parse(response);
            if (result.RootElement.ValueKind == JsonValueKind.Object)
                throw new InvalidOperationException(Diagnostics("command", result.RootElement.GetProperty("error").GetString()!));
            return result.RootElement.ValueKind == JsonValueKind.Null ? null : result.RootElement.GetString();
        }

        private async Task WaitForStartupAsync()
        {
            using var timeout = new CancellationTokenSource(_startupTimeout);
            try
            {
                var ready = await ReadResponseAsync("startup", timeout.Token);
                if (ready != ReadyMessage)
                {
                    await StopFailedProcessAsync();
                    throw new InvalidOperationException(Diagnostics("startup", $"Unexpected startup acknowledgement: {ready}"));
                }
            }
            catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
            {
                await StopFailedProcessAsync();
                throw new TimeoutException(Diagnostics("startup", $"Ready handshake timed out after {_startupTimeout}."), ex);
            }
            catch (IOException ex)
            {
                await StopFailedProcessAsync();
                throw new InvalidOperationException(Diagnostics("startup", "Node pipe failed."), ex);
            }
        }

        private async Task<string> ReadResponseAsync(string phase, CancellationToken token)
        {
            var response = await _process.StandardOutput.ReadLineAsync(token);
            if (response is not null)
                return response;
            await StopFailedProcessAsync();
            throw new InvalidOperationException(Diagnostics(phase, "Node stdout closed before a response."));
        }

        private async Task DrainStderrAsync()
        {
            var buffer = new char[1024];
            int read;
            while ((read = await _process.StandardError.ReadAsync(buffer)) > 0)
            {
                lock (_stderrGate)
                {
                    _stderr.Append(buffer, 0, read);
                    if (_stderr.Length > 4096)
                        _stderr.Remove(0, _stderr.Length - 4096);
                }
            }
        }

        private async Task StopFailedProcessAsync()
        {
            _failed = true;
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (_process.HasExited) { }
            }
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await _stderrDrain.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private string Diagnostics(string phase, string message)
        {
            lock (_stderrGate)
                return $"Node {phase}: {message} pid={_process.Id}; " +
                    $"exit={(_process.HasExited ? _process.ExitCode.ToString() : "(running)")}; stderr={_stderr}";
        }

        public void Dispose()
        {
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000))
                    StopFailedProcessAsync().GetAwaiter().GetResult();
                _stderrDrain.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            finally { _process.Dispose(); }
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
