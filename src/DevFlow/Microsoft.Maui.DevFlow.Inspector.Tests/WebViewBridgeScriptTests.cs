using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Maui.DevFlow.WebView;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

[Trait("Category", "BrowserFixture")]
public class WebViewBridgeScriptTests
{
    [InspectorBrowserFixtureFact]
    public async Task OpaqueOrigin_ActualEmbeddedEngine_AllowsRuntimeDomAndInputWithoutExposingStorage()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Fixture);
        Assert.Equal("about:blank", page.Url);
        Assert.True(await page.EvaluateAsync<bool>("() => { try { void window.localStorage; return false; } catch (error) { return error.name === 'SecurityError'; } }"));

        await InitializeAsync(page);
        await AssertRuntimeDomAndInputAsync(page);
        await AssertLiteralMarkersAsync(page, (method, parameters) => CommandAsync(page, method, parameters));

        Assert.True(await page.EvaluateAsync<bool>("() => { try { void window.localStorage; return false; } catch (error) { return error.name === 'SecurityError'; } }"));
        Assert.True(await page.EvaluateAsync<bool>("() => { try { void window.sessionStorage; return false; } catch (error) { return error.name === 'SecurityError'; } }"));
    }

    [InspectorBrowserFixtureFact]
    public async Task StorageGetters_ThrowIndependently_KeepEngineAndOtherNativeStorageWorking()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        foreach (var denied in new[] { "localStorage", "sessionStorage", "both", "none" })
        {
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.RouteAsync("https://devflow.test/**", route => route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = Fixture
            }));
            await page.GotoAsync("https://devflow.test/");
            await page.EvaluateAsync("""
                denied => {
                    window.__originalStorageGetters = {};
                    for (const name of ['localStorage', 'sessionStorage']) {
                        if (denied !== name && denied !== 'both') continue;
                        const get = () => { throw new DOMException(name + ' unavailable', 'SecurityError'); };
                        window.__originalStorageGetters[name] = get;
                        Object.defineProperty(window, name, { configurable: true, get });
                    }
                }
                """, denied);

            await InitializeAsync(page);
            await AssertRuntimeDomAndInputAsync(page);

            foreach (var name in new[] { "localStorage", "sessionStorage" })
            {
                if (denied == name || denied == "both")
                {
                    Assert.True(await page.EvaluateAsync<bool>("name => Object.getOwnPropertyDescriptor(window, name).get === window.__originalStorageGetters[name]", name));
                    continue;
                }
                await page.EvaluateAsync("name => window[name].setItem('native-storage', 'retained')", name);
                using var response = JsonDocument.Parse(await CommandAsync(page, "DOMStorage.getDOMStorageItems",
                    new { storageId = new { securityOrigin = "https://devflow.test", isLocalStorage = name == "localStorage" } }));
                Assert.Contains(response.RootElement.GetProperty("result").GetProperty("entries").EnumerateArray(),
                    entry => entry[0].GetString() == "native-storage" && entry[1].GetString() == "retained");
            }
        }
    }

    [InspectorBrowserFixtureFact]
    public async Task NavigationMutation_NativeBindingDeliversActualReplyDespiteDocumentReplacement()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var secondDocumentRequests = 0;
        await page.RouteAsync("https://devflow.test/**", route =>
        {
            if (route.Request.Url.EndsWith("/second", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref secondDocumentRequests);
                return route.FulfillAsync(new() { ContentType = "text/html", Body = "<title>Second document</title>" });
            }
            return route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = "<input><a id='navigate' href='/second' onclick='window.__navClicks=(window.__navClicks||0)+1'>Next</a>"
            });
        });
        await page.GotoAsync("https://devflow.test/");
        await InitializeAsync(page);
        using var channel = new NativeCdpResponseChannel();
        using var responseEvents = new NativeCdpEventSubscription(channel);
        var session = await context.NewCDPSessionAsync(page);
        EventHandler<JsonElement?> receiver = (_, message) =>
        {
            if (message is { } value) responseEvents.TryReceive(value.GetRawText());
        };
        session.Event("Runtime.bindingCalled").OnEvent += receiver;
        await session.SendAsync("Runtime.enable");
        var handshake = channel.BeginHandshake();
        try
        {
            Assert.Equal("missing", await page.EvaluateAsync<string>(handshake.Script));
            Assert.False(handshake.Received.IsCompleted);
            await session.SendAsync("Runtime.addBinding", new() { ["name"] = channel.BindingName });
            Assert.Equal("sent", await page.EvaluateAsync<string>(handshake.Script));
            await handshake.Received.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { channel.EndHandshake(handshake.Received); }
        await session.SendAsync("Runtime.addBinding", new() { ["name"] = "__application_native_binding" });
        var applicationMessage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<JsonElement?> applicationReceiver = (_, message) =>
        {
            if (message is { } value && value.GetProperty("name").GetString() == "__application_native_binding")
                applicationMessage.TrySetResult(value.GetProperty("payload").GetString()!);
        };
        session.Event("Runtime.bindingCalled").OnEvent += applicationReceiver;
        await AssertLiteralMarkersAsync(page, async (method, parameters) =>
        {
            var task = channel.Register(79);
            try
            {
                var json = JsonSerializer.Serialize(new { id = 79, method, @params = parameters });
                Assert.Equal("__cdp_pending__", await page.EvaluateAsync<string>(SendScript(json, channel.BindingName)));
                return await task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { channel.Remove(79); }
        });
        var normal = channel.Register(76);
        var readCommand = JsonSerializer.Serialize(new
        {
            id = 76, method = "Runtime.evaluate", @params = new { expression = "1 + 1", returnByValue = true }
        });
        Assert.Equal("__cdp_pending__", await page.EvaluateAsync<string>(SendScript(readCommand, channel.BindingName)));
        using var read = JsonDocument.Parse(await normal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, read.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetInt32());
        channel.Remove(76);
        var command = JsonSerializer.Serialize(new
        {
            id = 77,
            method = "Runtime.evaluate",
            @params = new
            {
                expression = "(() => { document.querySelector('#navigate').click(); return JSON.stringify({clicks:window.__navClicks,message:'clicked'}); })()",
                returnByValue = true
            }
        });
        var pending = channel.Register(77);
        var navigation = page.WaitForURLAsync("https://devflow.test/second");
        var sent = await page.EvaluateAsync<string>(SendScript(command, channel.BindingName));
        Assert.Equal("__cdp_pending__", sent);
        using var reply = JsonDocument.Parse(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(77, reply.RootElement.GetProperty("id").GetInt32());
        Assert.False(reply.RootElement.TryGetProperty("error", out _), reply.RootElement.ToString());
        Assert.False(reply.RootElement.GetProperty("result").TryGetProperty("exceptionDetails", out _), reply.RootElement.ToString());
        using var clicked = JsonDocument.Parse(reply.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString()!);
        Assert.Equal("clicked", clicked.RootElement.GetProperty("message").GetString());
        Assert.Equal(1, clicked.RootElement.GetProperty("clicks").GetInt32());
        await navigation;
        Assert.Null(await page.EvaluateAsync<string?>(Script("cdp-read-response.js")));
        Assert.Equal(1, secondDocumentRequests);
        channel.Remove(77);
        await InitializeAsync(page);
        var replacementHandshake = channel.BeginHandshake();
        try
        {
            Assert.Equal("sent", await page.EvaluateAsync<string>(replacementHandshake.Script));
            await replacementHandshake.Received.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { channel.EndHandshake(replacementHandshake.Received); }
        var failure = channel.Register(78);
        var failingCommand = JsonSerializer.Serialize(new
        {
            id = 78, method = "Runtime.evaluate",
            @params = new { expression = "(() => { throw new Error('original navigation-context error'); })()", returnByValue = true }
        });
        Assert.Equal("__cdp_pending__", await page.EvaluateAsync<string>(SendScript(failingCommand, channel.BindingName)));
        using var failed = JsonDocument.Parse(await failure.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("original navigation-context error", failed.RootElement.GetProperty("result").GetProperty("exceptionDetails").ToString());
        channel.Remove(78);
        Assert.Equal(responseEvents.MatchingBindings, responseEvents.AcceptedPayloads);
        Assert.True(responseEvents.AcceptedPayloads >= 6);
        responseEvents.Dispose();
        await session.SendAsync("Runtime.removeBinding", new() { ["name"] = channel.BindingName });
        session.Event("Runtime.bindingCalled").OnEvent -= receiver;
        await page.EvaluateAsync("() => __application_native_binding('application transport unchanged')");
        Assert.Equal("application transport unchanged", await applicationMessage.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        session.Event("Runtime.bindingCalled").OnEvent -= applicationReceiver;
        await session.SendAsync("Runtime.removeBinding", new() { ["name"] = "__application_native_binding" });
        await session.DetachAsync();
        await AssertDelayedReloadAsync(context);
        await AssertWebKitBindingNavigationAsync(context);
    }

    static async Task AssertWebKitBindingNavigationAsync(IBrowserContext context)
    {
        // Chromium supplies only the out-of-document callback for this WK message API shim.
        // The binding scripts, routing gate, channel and Chobitsu reply are production code.
        var page = await context.NewPageAsync();
        var requests = 0;
        await page.RouteAsync("https://webkit.devflow.test/**", route =>
        {
            if (route.Request.Url.EndsWith("/second", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref requests);
                return route.FulfillAsync(new() { ContentType = "text/html", Body = "<title>WebKit second document</title>" });
            }
            return route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = "<a href='/second' onclick='window.__clicks=(window.__clicks||0)+1'>Next</a>"
            });
        });
        using var channel = new NativeCdpResponseChannel();
        using var binding = new WebKitCdpResponseBinding(channel, 10, 22);
        var session = await context.NewCDPSessionAsync(page);
        var applicationMessage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<JsonElement?> receiver = (_, message) =>
        {
            if (message is not { } value || value.GetProperty("name").GetString() != "__testWKMessage") return;
            using var forwarded = JsonDocument.Parse(value.GetProperty("payload").GetString()!);
            var name = forwarded.RootElement.GetProperty("name").GetString()!;
            var payload = forwarded.RootElement.GetProperty("payload").GetString()!;
            if (name == "application") applicationMessage.TrySetResult(payload);
            else binding.TryReceive(10, 22, true, name, payload);
        };
        session.Event("Runtime.bindingCalled").OnEvent += receiver;
        await session.SendAsync("Runtime.enable");
        await session.SendAsync("Runtime.addBinding", new() { ["name"] = "__testWKMessage" });
        await page.AddInitScriptAsync($$"""
            window.__applicationUserScriptRan = true;
            window.__originalApplicationHandler = payload => __testWKMessage(JSON.stringify({name:'application',payload}));
            window.webkit = { messageHandlers: {
                application: { postMessage: window.__originalApplicationHandler },
                [{{JsonSerializer.Serialize(binding.Name)}}]: {
                    postMessage: payload => __testWKMessage(JSON.stringify({name:{{JsonSerializer.Serialize(binding.Name)}},payload}))
                }
            } };
            """);
        try
        {
            await page.GotoAsync("https://webkit.devflow.test/");
            await InitializeAsync(page);
            Assert.Equal("installed", await page.EvaluateAsync<string>(binding.InstallScript));
            Assert.Equal("bound", await page.EvaluateAsync<string>(binding.InstallScript));
            await AssertWebKitHandshakeAsync(page, channel);
            var pending = channel.Register(81);
            var command = JsonSerializer.Serialize(new
            {
                id = 81, method = "Runtime.evaluate", @params = new
                {
                    expression = "document.querySelector('a').click(); JSON.stringify({clicks:window.__clicks,value:'actual WK reply'})",
                    returnByValue = true
                }
            });
            var navigation = page.WaitForURLAsync("https://webkit.devflow.test/second");
            Assert.Equal("__cdp_pending__", await page.EvaluateAsync<string>(SendScript(command, binding.Name)));
            using var reply = JsonDocument.Parse(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            using var actual = JsonDocument.Parse(reply.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString()!);
            Assert.Equal(1, actual.RootElement.GetProperty("clicks").GetInt32());
            Assert.Equal("actual WK reply", actual.RootElement.GetProperty("value").GetString());
            await navigation;
            Assert.Equal(1, requests);
            Assert.Null(await page.EvaluateAsync<string?>(Script("cdp-read-response.js")));
            channel.Remove(81);

            Assert.Equal("installed", await page.EvaluateAsync<string>(binding.InstallScript));
            await AssertWebKitHandshakeAsync(page, channel);
            await InitializeAsync(page);
            var failure = channel.Register(82);
            var failedCommand = JsonSerializer.Serialize(new
            {
                id = 82, method = "Runtime.evaluate",
                @params = new { expression = "(() => { throw new Error('actual WK JavaScript error'); })()", returnByValue = true }
            });
            Assert.Equal("__cdp_pending__", await page.EvaluateAsync<string>(SendScript(failedCommand, binding.Name)));
            using var failed = JsonDocument.Parse(await failure.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("actual WK JavaScript error", failed.RootElement.GetProperty("result").GetProperty("exceptionDetails").ToString());
            channel.Remove(82);
            binding.Dispose();
            await page.EvaluateAsync("() => window.webkit.messageHandlers.application.postMessage('application still owns messages')");
            Assert.Equal("application still owns messages", await applicationMessage.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await page.EvaluateAsync<bool>("() => window.__applicationUserScriptRan && window.webkit.messageHandlers.application.postMessage === window.__originalApplicationHandler"));
        }
        finally
        {
            session.Event("Runtime.bindingCalled").OnEvent -= receiver;
            await session.DetachAsync();
            await page.CloseAsync();
        }
    }

    static async Task AssertWebKitHandshakeAsync(IPage page, NativeCdpResponseChannel channel)
    {
        var handshake = channel.BeginHandshake();
        try
        {
            Assert.Equal("sent", await page.EvaluateAsync<string>(handshake.Script));
            await handshake.Received.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { channel.EndHandshake(handshake.Received); }
    }

    static async Task AssertDelayedReloadAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDocument = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var phase = "initial navigation";
        await page.RouteAsync("https://reload.devflow.test/index.html", async route =>
        {
            var request = Interlocked.Increment(ref requests);
            if (request == 2)
            {
                requested.TrySetResult();
                await release.Task;
            }
            await route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = $"<title>Document {request}</title>"
            });
        });
        try
        {
            await page.GotoAsync("https://reload.devflow.test/index.html").WaitAsync(TimeSpan.FromSeconds(5));
            phase = "initial engine injection";
            await InitializeAsync(page).WaitAsync(TimeSpan.FromSeconds(5));
            var fence = new DocumentReloadFence();
            phase = "mark old document";
            Assert.Equal("marked", await page.EvaluateAsync<string>(fence.MarkCurrentDocumentScript).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await page.EvaluateAsync<bool>("""
                () => Object.getOwnPropertyNames(document)
                    .filter(name => name.startsWith('__devflow_reload_'))
                    .every(name => !Object.getOwnPropertyDescriptor(document, name).enumerable)
                """));
            await page.ExposeFunctionAsync("__recordReloadStart", (string value) => oldDocument.TrySetResult(value));
            await page.EvaluateAsync($$"""
                () => window.addEventListener('beforeunload', () => window.__recordReloadStart(JSON.stringify({
                    state: document.readyState,
                    bridgeReady: !!window.__chobitsuReady,
                    fenced: {{fence.ReadinessScript}}
                })))
                """);
            var navigation = page.ReloadAsync();
            phase = "reload request";
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // A provisional reload still exposes the fully initialized old document.
            phase = "old readiness probe";
            using var old = JsonDocument.Parse(await oldDocument.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("complete", old.RootElement.GetProperty("state").GetString());
            Assert.True(old.RootElement.GetProperty("bridgeReady").GetBoolean());
            Assert.Equal("reloading", old.RootElement.GetProperty("fenced").GetString());

            release.TrySetResult();
            phase = "replacement navigation";
            await navigation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("complete", await page.EvaluateAsync<string>(fence.ReadinessScript));
            Assert.False(await page.EvaluateAsync<bool>("() => !!window.__chobitsuReady"));
            await InitializeAsync(page);
            using var reply = JsonDocument.Parse(await CommandAsync(page, "Runtime.evaluate",
                new { expression = "window.__readQueries=(window.__readQueries||0)+1;document.title", returnByValue = true }));
            Assert.Equal("Document 2", reply.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString());
            Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__readQueries"));
            Assert.Equal(2, requests);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Delayed reload failed during {phase}; document requests={requests}.", ex);
        }
        finally
        {
            release.TrySetResult();
            await page.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    static async Task InitializeAsync(IPage page)
    {
        // Evaluate instead of adding a script tag: native hosts inject the same resource via evaluation.
        await page.EvaluateAsync(Script("chobitsu.js"));
        Assert.Equal("object", await page.EvaluateAsync<string>("() => typeof chobitsu"));
        Assert.Equal("ready", await page.EvaluateAsync<string>(Script("chobitsu-init.js")));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__chobitsuReady"));
    }

    static async Task AssertRuntimeDomAndInputAsync(IPage page)
    {
        using var runtime = JsonDocument.Parse(await CommandAsync(page, "Runtime.evaluate",
            new { expression = "JSON.stringify({ title: document.title, count: document.querySelectorAll('button').length })", returnByValue = true }));
        using var value = JsonDocument.Parse(runtime.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString()!);
        Assert.Equal("Opaque fixture", value.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, value.RootElement.GetProperty("count").GetInt32());
        using var dom = JsonDocument.Parse(await CommandAsync(page, "DOM.getDocument", new { }));
        Assert.Equal("#document", dom.RootElement.GetProperty("result").GetProperty("root").GetProperty("nodeName").GetString());
        using var click = JsonDocument.Parse(await CommandAsync(page, "Runtime.evaluate",
            new { expression = "document.querySelector('button').click(); document.querySelector('input').focus(); 'clicked'", returnByValue = true }));
        Assert.False(click.RootElement.TryGetProperty("error", out _));
        const string inputText = "hello %TEXT_LENGTH% %TEXT% %NATIVE_CDP_BINDING%";
        await page.EvaluateAsync(ScriptResources.BuildInsertTextScript(inputText, inputText.Length));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__clicks"));
        Assert.Equal(inputText, await page.Locator("input").InputValueAsync());
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__inputs"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__changes"));
    }

    static async Task<string> CommandAsync(IPage page, string method, object parameters)
    {
        var command = JsonSerializer.Serialize(new { id = 73, method, @params = parameters });
        var sent = await page.EvaluateAsync<string>(SendScript(command));
        Assert.Equal("__cdp_pending__", sent);
        await page.WaitForFunctionAsync("() => window.__cdpResponseReady", null, new() { Timeout = 5000 });
        var response = await page.EvaluateAsync<string>(Script("cdp-read-response.js"));
        using var result = JsonDocument.Parse(response);
        Assert.Equal(73, result.RootElement.GetProperty("id").GetInt32());
        Assert.False(result.RootElement.TryGetProperty("error", out _), response);
        return response;
    }

    static string SendScript(string command, string? bindingName = null)
    {
        var escaped = command.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
        return ScriptResources.BuildCdpSendScript(escaped, bindingName);
    }

    static async Task AssertLiteralMarkersAsync(IPage page, Func<string, object, Task<string>> command)
    {
        const string marker = "%NATIVE_CDP_BINDING%";
        using var literal = JsonDocument.Parse(await command("Runtime.evaluate",
            new { expression = "'" + marker + "'", returnByValue = true }));
        Assert.Equal(marker, literal.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString());
        using var text = JsonDocument.Parse(await command("Runtime.evaluate",
            new { expression = "document.querySelector('input').value = '" + marker + "'; document.querySelector('input').value", returnByValue = true }));
        Assert.Equal(marker, text.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString());
        Assert.Equal(marker, await page.Locator("input").InputValueAsync());
        using var error = JsonDocument.Parse(await command("Runtime.evaluate",
            new { expression = "(() => { throw new Error('" + marker + "'); })()", returnByValue = true }));
        Assert.Contains(marker, error.RootElement.GetProperty("result").GetProperty("exceptionDetails").ToString());
    }

    static string Script(string name) => ScriptResources.Load(name);

    const string Fixture = """
        <!doctype html><html><head><title>Opaque fixture</title></head>
        <body><button onclick="window.__clicks++">Click</button><input></body>
        <script>
        window.__clicks=0;window.__inputs=0;window.__changes=0;
        document.querySelector('input').addEventListener('input', () => window.__inputs++);
        document.querySelector('input').addEventListener('change', () => window.__changes++);
        </script></html>
        """;
}
