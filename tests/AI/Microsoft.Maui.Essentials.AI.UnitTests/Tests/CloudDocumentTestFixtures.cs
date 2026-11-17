using System.Net;
using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

internal static class CloudDocumentTestFixtures
{
    internal static readonly Uri OcrEndpoint = new("https://example.invalid/resource/providers/mistral/azure/ocr");

    internal static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new Handler(respond));

    internal static HttpClient Http(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) =>
        Http((request, token) => Task.FromResult(respond(request, token)));

    internal static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    internal static DocumentExtractionClient Mistral(HttpClient http, string deployment = "deployment") =>
        new MistralDocumentExtractionClient(
            new() { OcrEndpoint = OcrEndpoint, DefaultModel = deployment, IncludeImages = true }, "test-key", http);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            respond(request, token);
    }

    internal sealed class TrackingContent(string value) : StringContent(value)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
