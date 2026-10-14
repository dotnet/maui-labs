using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewReadinessTests
{
    [Fact]
    public async Task MainReadiness_OlderReadyMainAndRight_DoNotSatisfyNewestPendingMain()
    {
        var reads = 0;
        var probes = new List<(string Target, string Expression)>();
        var target = await WebViewReadiness.WaitForMainContextAsync(
            () => Task.FromResult(Contexts(ready: ++reads > 1)),
            (id, expression) =>
            {
                Assert.True(reads >= 2);
                probes.Add((id, expression));
                return Task.FromResult(ProbeResult(expression));
            });

        Assert.Equal("3", target);
        Assert.Equal(2, probes.Count);
        Assert.All(probes, probe => Assert.Equal("3", probe.Target));
        Assert.Equal("1 + 1", probes[0].Expression);
        Assert.Contains(".todo-container", probes[1].Expression);
    }

    [Fact]
    public async Task MainReadiness_CanonicalIdAndTransientReadOnlyError_StayPinnedToSameTarget()
    {
        var probes = 0;
        var target = await WebViewReadiness.WaitForMainContextAsync(
            () => Task.FromResult(Contexts(ready: true, canonical: true)),
            (id, expression) =>
            {
                Assert.Equal("webview-3", id);
                if (++probes == 1)
                    throw new InvalidOperationException("Runtime.evaluate failed: WebView not ready");
                return Task.FromResult(ProbeResult(expression));
            });

        Assert.Equal("webview-3", target);
        Assert.Equal(3, probes);
    }

    [Fact]
    public async Task MainReadiness_ComponentNotRendered_WaitsWithoutMutatingIt()
    {
        var renderProbes = 0;
        var target = await WebViewReadiness.WaitForMainContextAsync(
            () => Task.FromResult(Contexts(ready: true)),
            (id, expression) =>
            {
                Assert.Equal("3", id);
                return Task.FromResult(expression == "1 + 1" || ++renderProbes > 1
                    ? ProbeResult(expression)
                    : Json("""{"result":{"result":{"value":"waiting"}}}"""));
            });

        Assert.Equal("3", target);
        Assert.Equal(2, renderProbes);
    }

    [Fact]
    public async Task MainReadiness_ContextReplacedDuringProbes_DoesNotReturnOldRegistration()
    {
        var reads = 0;
        var targets = new List<string>();
        var target = await WebViewReadiness.WaitForMainContextAsync(
            () => Task.FromResult(++reads == 1
                ? Contexts(ready: true)
                : Contexts(ready: reads > 2, newestIndex: 4)),
            (id, expression) =>
            {
                targets.Add(id);
                return Task.FromResult(ProbeResult(expression));
            });

        Assert.Equal("4", target);
        Assert.Equal(new[] { "3", "3", "4", "4" }, targets);
    }

    [Fact]
    public async Task MainReadiness_PendingTargetTimeout_ReportsTargetAndFullContexts()
    {
        var payload = Contexts(ready: false);
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            WebViewReadiness.WaitForMainContextAsync(
                () => Task.FromResult(payload),
                (_, _) => throw new InvalidOperationException("Pending target must not be probed."),
                timeoutMs: 50));

        Assert.Contains("Target: 3", error.Message);
        Assert.Contains("newest active Main registration is not ready", error.Message);
        Assert.Contains(payload.ToString(), error.Message);
    }

    [Fact]
    public async Task MainReadiness_OnlyReadyRightAndInactiveMain_TimeoutsWithoutProbing()
    {
        var payload = Json("""
            {"webviews":[
                {"index":0,"automationId":"BlazorWebView","active":false,"ready":true},
                {"index":2,"automationId":"BlazorRight","active":true,"ready":true}
            ]}
            """);
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            WebViewReadiness.WaitForMainContextAsync(
                () => Task.FromResult(payload),
                (_, _) => throw new InvalidOperationException("Unrelated contexts must not be probed."),
                timeoutMs: 50));

        Assert.Contains("Target: (none)", error.Message);
        Assert.Contains(payload.ToString(), error.Message);
    }

    [Fact]
    public async Task MainReadiness_NonTransientReadOnlyFailure_IsNotIgnored()
    {
        var probes = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WebViewReadiness.WaitForMainContextAsync(
                () => Task.FromResult(Contexts(ready: true)),
                (_, _) =>
                {
                    probes++;
                    throw new InvalidOperationException("ReferenceError: broken diagnostic");
                }));

        Assert.Contains("ReferenceError", error.Message);
        Assert.Equal(1, probes);
    }

    static JsonElement Contexts(bool ready, bool canonical = false, int newestIndex = 3)
        => Json($$"""
            {"webviews":[
                {"index":0,"automationId":"BlazorWebView","active":true,"ready":true},
                {"index":2,"automationId":"BlazorRight","active":false,"ready":true},
                {"index":{{newestIndex}},{{(canonical ? "\"id\":\"webview-" + newestIndex + "\"," : "")}}
                 "automationId":"BlazorWebView","active":true,"ready":{{(ready ? "true" : "false")}}}
            ]}
            """);

    static JsonElement ProbeResult(string expression)
        => Json(expression == "1 + 1"
            ? """{"result":{"result":{"value":2}}}"""
            : """{"result":{"result":{"value":"ready"}}}""");

    static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
