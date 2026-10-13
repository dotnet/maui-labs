using System.Net;
using System.Text;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

public class UiReadinessTests
{
    [Fact]
    public async Task WaitForUiSettled_ChangingRevisions_RequiresTwoConsecutiveStableCaptures()
    {
        using var handler = new CaptureHandler(
            (HttpStatusCode.OK, Capture("first", "1")),
            (HttpStatusCode.OK, Capture("second", "1")),
            (HttpStatusCode.OK, Capture("second", "2")),
            (HttpStatusCode.OK, Capture("second", "2")));
        using var http = CreateClient(handler);

        await AppFixture.WaitForUiSettledAsync(http, 5000);

        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task WaitForUiSettled_CaptureConflict_PollsWithoutReplayingMutations()
    {
        using var handler = new CaptureHandler(
            (HttpStatusCode.OK, Capture("stable", "1")),
            (HttpStatusCode.Conflict, """{"reason":"capture-changed-during-read"}"""),
            (HttpStatusCode.OK, Capture("stable", "1")),
            (HttpStatusCode.OK, Capture("stable", "1")));
        using var http = CreateClient(handler);

        await AppFixture.WaitForUiSettledAsync(http, 5000);

        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task WaitForUiSettled_TerminalFailure_FailsWithoutRetry()
    {
        using var handler = new CaptureHandler(
            (HttpStatusCode.Conflict, """{"reason":"unexpected-conflict"}"""));
        using var http = CreateClient(handler);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AppFixture.WaitForUiSettledAsync(http, 5000));

        Assert.Contains("unexpected-conflict", failure.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task WaitForUiSettled_NoSecondCapture_TimesOut()
    {
        using var handler = new CaptureHandler((HttpStatusCode.OK, Capture("first", "1")));
        using var http = CreateClient(handler);

        await Assert.ThrowsAsync<TimeoutException>(() => AppFixture.WaitForUiSettledAsync(http, 50));
        Assert.Equal(1, handler.RequestCount);
    }

    static string Capture(string revision, string inputs)
        => $$"""{"revision":"{{revision}}","diagnosticInputsRevision":"{{inputs}}"}""";

    static HttpClient CreateClient(HttpMessageHandler handler)
        => new(handler) { BaseAddress = new Uri("http://localhost:9223") };

    sealed class CaptureHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/ui/tree?envelope=true&native=false", request.RequestUri!.PathAndQuery);
            var response = responses[RequestCount++];
            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
