using System.Text.Json;
using Microsoft.Maui.DevFlow.Blazor.Gtk;

namespace Microsoft.Maui.DevFlow.Agent.Gtk.Tests;

public class GtkWebViewScreenshotTests
{
    static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task CaptureScreenshot_ReplacementContext_UsesItsNativeSnapshotAndPreservesCommandId()
    {
        using var agent = new TestAgent();
        using var service = new GtkBlazorWebViewDebugService();
        service.WireToAgent(agent);
        var firstCaptures = 0;
        var secondCaptures = 0;
        var first = CreateBridge(service, "BlazorWebView", _ =>
        {
            firstCaptures++;
            return Task.FromResult(Png);
        });
        var second = CreateBridge(service, "BlazorWebView", _ =>
        {
            secondCaptures++;
            return Task.FromResult(Png);
        });
        service.AddBridge(first);
        service.AddBridge(second);
        await Task.WhenAll(first.InitializeAsync(), second.InitializeAsync());

        using var response = JsonDocument.Parse(await agent.SendToAsync("BlazorWebView",
            """{"id":71,"method":"Page.captureScreenshot","params":{"format":"png"}}"""));

        Assert.Equal(71, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(Png, Convert.FromBase64String(response.RootElement.GetProperty("result").GetProperty("data").GetString()!));
        Assert.Equal(0, firstCaptures);
        Assert.Equal(1, secondCaptures);
    }

    [Fact]
    public async Task CaptureScreenshot_EmptyNativeResult_IsAnErrorNotAnEmptySuccess()
    {
        using var service = new GtkBlazorWebViewDebugService();
        var bridge = CreateBridge(service, "empty", _ => Task.FromResult(Array.Empty<byte>()));
        service.AddBridge(bridge);
        await bridge.InitializeAsync();

        using var response = JsonDocument.Parse(await bridge.SendCdpCommandAsync(
            """{"id":72,"method":"Page.captureScreenshot"}"""));

        Assert.Contains("no screenshot PNG data", response.RootElement.GetProperty("error").GetString());
        Assert.False(response.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task CaptureScreenshot_UnsupportedOptions_DoNotReturnAViewportImage()
    {
        using var service = new GtkBlazorWebViewDebugService();
        var captures = 0;
        var bridge = CreateBridge(service, "unsupported", _ =>
        {
            captures++;
            return Task.FromResult(Png);
        });
        service.AddBridge(bridge);
        await bridge.InitializeAsync();

        foreach (var parameters in new[]
        {
            """{"format":"jpeg"}""",
            """{"clip":{"x":0,"y":0,"width":10,"height":10,"scale":1}}""",
            """{"captureBeyondViewport":true}"""
        })
        {
            using var response = JsonDocument.Parse(await bridge.SendCdpCommandAsync(
                $$"""{"id":74,"method":"Page.captureScreenshot","params":{{parameters}}}"""));
            Assert.True(response.RootElement.TryGetProperty("error", out _));
            Assert.False(response.RootElement.TryGetProperty("result", out _));
        }
        Assert.Equal(0, captures);
    }

    [Fact]
    public async Task CaptureScreenshot_DetachedAsSnapshotCompletes_DiscardsItsImage()
    {
        using var service = new GtkBlazorWebViewDebugService();
        GtkBlazorWebViewDebugService.GtkWebViewBridge? bridge = null;
        bridge = CreateBridge(service, "completing", _ =>
        {
            service.RemoveBridge(bridge!);
            return Task.FromResult(Png);
        });
        service.AddBridge(bridge);
        await bridge.InitializeAsync();

        using var response = JsonDocument.Parse(await bridge.SendCdpCommandAsync(
            """{"id":75,"method":"Page.captureScreenshot"}"""));

        Assert.True(response.RootElement.TryGetProperty("error", out _));
        Assert.False(response.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task CaptureScreenshot_DetachedDuringCapture_CancelsWithoutReturningAnImage()
    {
        using var service = new GtkBlazorWebViewDebugService();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = CreateBridge(service, "detached", token =>
        {
            started.TrySetResult(token);
            return completed.Task;
        });
        service.AddBridge(bridge);
        await bridge.InitializeAsync();
        var capture = bridge.SendCdpCommandAsync("""{"id":73,"method":"Page.captureScreenshot"}""");
        try
        {
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.RemoveBridge(bridge);
            using var response = JsonDocument.Parse(await capture.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.True(token.IsCancellationRequested);
            Assert.True(response.RootElement.TryGetProperty("error", out _));
            Assert.False(response.RootElement.TryGetProperty("result", out _));
        }
        finally
        {
            completed.TrySetResult(Png);
        }
    }

    static GtkBlazorWebViewDebugService.GtkWebViewBridge CreateBridge(
        GtkBlazorWebViewDebugService service, string id, Func<CancellationToken, Task<byte[]>> capture)
        => new(service, null!, id, script =>
        {
            Assert.DoesNotContain("Page.captureScreenshot", script);
            return Task.FromResult<string?>(script.Contains("typeof chobitsu") ? "loaded" : "{}");
        }, capture);

    sealed class TestAgent : Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService
    {
        public Task<string> SendToAsync(string id, string command)
            => GetCdpWebViewsSnapshot().Single(view => view.AutomationId == id).CommandHandler(command);
    }
}
