using System.Text.Json;
using Microsoft.Maui.DevFlow.Blazor.Gtk;

namespace Microsoft.Maui.DevFlow.Agent.Gtk.Tests;

[Collection("GTK diagnostic output")]
public class GtkCdpCommandTests
{
    [Fact]
    public async Task Initialize_Diagnostics_UseOnlyTheExplicitCallback()
    {
        var stdout = Console.Out;
        var stderr = Console.Error;
        using var capturedOutput = new StringWriter();
        using var capturedError = new StringWriter();
        var diagnostics = new List<string>();
        try
        {
            Console.SetOut(capturedOutput);
            Console.SetError(capturedError);
            using var service = new GtkBlazorWebViewDebugService();
            var bridge = new GtkBlazorWebViewDebugService.GtkWebViewBridge(
                service, null!, "quiet", script =>
                    Task.FromResult<string?>(script.Contains("typeof chobitsu") ? "loaded" : "{}"));
            service.AddBridge(bridge);
            await bridge.InitializeAsync();
            Assert.Empty(diagnostics);

            service.LogCallback = diagnostics.Add;
            await bridge.InitializeAsync();

            Assert.NotEmpty(diagnostics);
            Assert.Equal("", capturedOutput.ToString());
            Assert.Equal("", capturedError.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }

    [Fact]
    public void EnsureCommandId_MissingId_AssignsUniqueIdsAndPreservesPayload()
    {
        var sequence = 0;
        const string command = """{"method":"Runtime.evaluate","params":{"expression":"1 + 1"}}""";
        using var first = JsonDocument.Parse(GtkBlazorWebViewDebugService.GtkWebViewBridge.EnsureCommandId(command, ref sequence));
        using var second = JsonDocument.Parse(GtkBlazorWebViewDebugService.GtkWebViewBridge.EnsureCommandId(command, ref sequence));

        Assert.Equal(1, first.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(2, second.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Runtime.evaluate", first.RootElement.GetProperty("method").GetString());
        Assert.Equal("1 + 1", first.RootElement.GetProperty("params").GetProperty("expression").GetString());
    }

    [CollectionDefinition("GTK diagnostic output", DisableParallelization = true)]
    public sealed class GtkDiagnosticOutputCollection;

    [Fact]
    public void EnsureCommandId_ExplicitId_PreservesCallerId()
    {
        var sequence = 0;
        using var command = JsonDocument.Parse(GtkBlazorWebViewDebugService.GtkWebViewBridge.EnsureCommandId(
            """{"id":42,"method":"Runtime.evaluate"}""", ref sequence));

        Assert.Equal(42, command.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(0, sequence);
    }

    [Fact]
    public void RemoveBridge_DetachedView_UnregistersItsContext()
    {
        using var agent = new TestAgent();
        using var service = new GtkBlazorWebViewDebugService();
        service.WireToAgent(agent);
        var old = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, "BlazorWebView");
        service.AddBridge(old);
        Assert.Equal(1, agent.WebViewCount);

        service.RemoveBridge(old);
        service.AddBridge(new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, "BlazorWebView"));

        Assert.Equal(1, agent.WebViewCount);
        Assert.DoesNotContain(old, service.Bridges);
        Assert.False(old.IsReady);
    }

    sealed class TestAgent : Microsoft.Maui.DevFlow.Agent.Core.DevFlowAgentService
    {
        public int WebViewCount => GetCdpWebViewsSnapshot().Length;
        public object? CommandTarget => Assert.Single(GetCdpWebViewsSnapshot()).CommandHandler.Target;
    }

    [Fact]
    public void RemoveBridge_ReplacementCapturedBeforeDetachment_PreservesReplacementContext()
    {
        using var agent = new TestAgent();
        using var service = new GtkBlazorWebViewDebugService();
        service.WireToAgent(agent);
        var old = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, "BlazorWebView");
        var replacement = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, "BlazorWebView");
        service.AddBridge(old);
        service.AddBridge(replacement);

        service.RemoveBridge(old);

        Assert.Equal(1, agent.WebViewCount);
        Assert.Same(replacement, agent.CommandTarget);
        Assert.Single(service.Bridges);
    }

    [Fact]
    public async Task RemoveBridge_DuringFinalInitializationEvaluation_DoesNotStartLogDrain()
    {
        using var service = new GtkBlazorWebViewDebugService();
        var evaluatingConsole = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishConsole = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consoleScript = ScriptResources.Load("console-intercept.js");
        var bridge = new GtkBlazorWebViewDebugService.GtkWebViewBridge(service, null!, "BlazorWebView", script =>
        {
            if (script == consoleScript)
            {
                evaluatingConsole.TrySetResult();
                return finishConsole.Task;
            }
            return Task.FromResult<string?>(script.Contains("typeof chobitsu") ? "loaded" : "{}");
        });
        service.AddBridge(bridge);
        var initialization = bridge.InitializeAsync();
        try
        {
            await evaluatingConsole.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(bridge.IsReady);
            service.RemoveBridge(bridge);
            finishConsole.TrySetResult("{}");
            await initialization.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(bridge.IsReady);
            Assert.Null(bridge.LogDrainTask);
        }
        finally
        {
            finishConsole.TrySetResult("{}");
        }
    }
}
