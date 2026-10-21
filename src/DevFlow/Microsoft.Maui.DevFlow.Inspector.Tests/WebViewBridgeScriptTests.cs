using System.Reflection;
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
                Body = "<a id='navigate' href='/second' onclick='window.__navClicks=(window.__navClicks||0)+1'>Next</a>"
            });
        });
        await page.GotoAsync("https://devflow.test/");
        await InitializeAsync(page);
        using var channel = new NativeCdpResponseChannel();
        var session = await context.NewCDPSessionAsync(page);
        EventHandler<JsonElement?> receiver = (_, message) =>
        {
            if (message is { } value && value.GetProperty("name").GetString() == channel.BindingName)
                channel.TryReceive(value.GetProperty("payload").GetString()!);
        };
        session.Event("Runtime.bindingCalled").OnEvent += receiver;
        await session.SendAsync("Runtime.enable");
        await session.SendAsync("Runtime.addBinding", new() { ["name"] = channel.BindingName });
        await session.SendAsync("Runtime.addBinding", new() { ["name"] = "__application_native_binding" });
        var applicationMessage = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<JsonElement?> applicationReceiver = (_, message) =>
        {
            if (message is { } value && value.GetProperty("name").GetString() == "__application_native_binding")
                applicationMessage.TrySetResult(value.GetProperty("payload").GetString()!);
        };
        session.Event("Runtime.bindingCalled").OnEvent += applicationReceiver;
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
        await session.SendAsync("Runtime.removeBinding", new() { ["name"] = channel.BindingName });
        session.Event("Runtime.bindingCalled").OnEvent -= receiver;
        await page.EvaluateAsync("() => __application_native_binding('application transport unchanged')");
        Assert.Equal("application transport unchanged", await applicationMessage.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        session.Event("Runtime.bindingCalled").OnEvent -= applicationReceiver;
        await session.SendAsync("Runtime.removeBinding", new() { ["name"] = "__application_native_binding" });
        await session.DetachAsync();
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
        await page.EvaluateAsync(Script("insert-text.js").Replace("%TEXT%", "hello").Replace("%TEXT_LENGTH%", "5"));
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.__clicks"));
        Assert.Equal("hello", await page.Locator("input").InputValueAsync());
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
        return Script("cdp-send-receive.js").Replace("%CDP_MESSAGE%", escaped)
            .Replace("%NATIVE_CDP_BINDING%", JsonSerializer.Serialize(bindingName));
    }

    static string Script(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WebViewScripts." + name)
            ?? throw new InvalidOperationException($"Missing actual WebView script: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

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
