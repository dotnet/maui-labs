using System.Net;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Network;

namespace Microsoft.Maui.DevFlow.Tests;

public class NetworkCaptureTests
{
    [Fact]
    public void ProfileMode_DisablesNetworkMonitoring()
    {
        var options = new AgentOptions();

        options.ApplyBuildMetadata("true", "profile");

        Assert.False(options.EnableNetworkMonitoring);
        Assert.Equal(0, options.MaxNetworkBodySize);
        Assert.True(options.ReadOnly);
        Assert.True(options.EnableProfiler);
    }

    [Fact]
    public void DisabledBuildMetadata_RejectsAgentRegistration()
    {
        var options = new AgentOptions();

        var error = Assert.Throws<InvalidOperationException>(
            () => options.ApplyBuildMetadata("false", "disabled"));

        Assert.Contains("disabled for this build", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void AgentOptions_RejectInvalidPorts(int port)
    {
        var options = new AgentOptions { Port = port };

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            options.ValidateForRegistration);

        Assert.Contains("between 1 and 65535", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void AgentOptions_AcceptBoundaryPorts(int port)
    {
        var options = new AgentOptions { Port = port };

        options.ValidateForRegistration();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void AgentOptions_RejectInvalidPortMetadata(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AgentOptions.ParsePortMetadata(value));

        Assert.Contains("between 1 and 65535", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    public void AgentOptions_ParseValidPortMetadata(string? value, int? expected)
        => Assert.Equal(expected, AgentOptions.ParsePortMetadata(value));

    [Fact]
    public void AgentOptions_AppliesMetadataPortBeforeBrokerRegistration()
    {
        var options = new AgentOptions();

        options.ApplyPortMetadata(19333);

        Assert.Equal(19333, options.Port);
    }

    [Fact]
    public void AgentOptions_RuntimeCustomPortWinsOverMetadata()
    {
        var options = new AgentOptions { Port = 19444 };

        options.ApplyPortMetadata(19333);

        Assert.Equal(19444, options.Port);
    }

    [Fact]
    public async Task ZeroBodyLimit_DoesNotReadRequestOrStreamingResponseBodies()
    {
        var store = new NetworkRequestStore();
        using var handler = new DevFlowHttpHandler(
            store,
            new StreamingResponseHandler(),
            maxBodySize: 0);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/stream")
        {
            Content = new ThrowIfReadContent()
        };

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(store.GetRecent());
        var captured = store.GetRecent()[0];
        Assert.Null(captured.RequestBody);
        Assert.Null(captured.ResponseBody);
    }

    private sealed class StreamingResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowIfReadContent()
            });
    }

    private sealed class ThrowIfReadContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
            => throw new InvalidOperationException("The body must not be buffered.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
