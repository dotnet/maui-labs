using System.Net.Http;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientWebViewErrorTests
{
    [Theory]
    [InlineData("""{"success":false,"error":"No WebViews registered"}""", "No WebViews registered")]
    [InlineData("<html>Bridge unavailable</html>", "Bridge unavailable")]
    public async Task SendCdpCommandAsync_HttpFailure_ThrowsWithAgentError(string response, string expected)
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(response, statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendCdpCommandAsync("Runtime.evaluate"));

        Assert.Contains(expected, error.Message);
        Assert.Contains("HTTP 400", error.Message);
    }

    [Theory]
    [InlineData("""{"error":{"message":"Method unimplemented"}}""", "Method unimplemented")]
    [InlineData("""{"error":"WebView not ready"}""", "WebView not ready")]
    [InlineData("""{"error":{"message":17}}""", "17")]
    [InlineData("""{"success":false}""", "failed")]
    [InlineData("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missingVariable"}}}}""", "missingVariable")]
    [InlineData("""{"result":{"exceptionDetails":{"text":"SyntaxError: invalid selector"}}}""", "invalid selector")]
    [InlineData("""{"result":{"exceptionDetails":null}}""", "threw")]
    [InlineData("[]", "invalid CDP response")]
    public async Task SendCdpCommandAsync_CdpFailure_Throws(string response, string expected)
    {
        using var agent = FakeAgent.StartJson(response);
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendCdpCommandAsync("Runtime.evaluate"));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task SendCdpCommandAsync_InvalidJson_ThrowsWithResponseBody()
    {
        using var agent = FakeAgent.StartJson("<html>Bridge unavailable</html>");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => client.SendCdpCommandAsync("Runtime.evaluate"));

        Assert.Contains("Runtime.evaluate", error.Message);
        Assert.Contains("Bridge unavailable", error.Message);
    }

    [Fact]
    public async Task GetCdpSourceAsync_HttpFailure_ThrowsWithAgentDetails()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"ReferenceError: source failed"}""", statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCdpSourceAsync());

        Assert.Contains("ReferenceError: source failed", error.Message);
    }

    [Theory]
    [InlineData("navigate")]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("text")]
    public async Task WebViewAction_HttpFailure_ThrowsWithDetails(string action)
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"ReferenceError: missing"}""", statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => PerformActionAsync(client, action));

        Assert.Contains("ReferenceError: missing", error.Message);
        Assert.Single(agent.Requests);
    }

    [Theory]
    [InlineData("""{"success":false,"error":"Element not found"}""", "Element not found")]
    [InlineData("""{"success":false}""", "failed")]
    [InlineData("{}", "did not report success")]
    [InlineData("""{"success":"true"}""", "did not report success")]
    [InlineData("[]", "invalid CDP response")]
    public async Task WebViewAction_NonSuccessBody_Throws(string response, string expected)
    {
        using var agent = FakeAgent.StartJson(response);
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ClickWebViewAsync("#missing"));

        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("navigate")]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("text")]
    public async Task WebViewAction_Success_ReturnsTrue(string action)
    {
        using var agent = FakeAgent.StartJson("""{"success":true}""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        Assert.True(await PerformActionAsync(client, action));
    }

    private static Task<bool> PerformActionAsync(AgentClient client, string action) => action switch
    {
        "navigate" => client.NavigateWebViewAsync("/next"),
        "click" => client.ClickWebViewAsync("button"),
        "fill" => client.FillWebViewAsync("input", "hello"),
        "text" => client.InsertWebViewTextAsync("typed"),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
