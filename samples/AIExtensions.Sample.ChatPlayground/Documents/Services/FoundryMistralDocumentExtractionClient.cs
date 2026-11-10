using CommunityToolkit.DocumentExtraction.Mistral;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class FoundryMistralDocumentExtractionClient : CloudDocumentExtractionPolicy
{
    private readonly HttpClient _boundedClient;

    public FoundryMistralDocumentExtractionClient(
        HttpClient httpClient, Uri ocrEndpoint, string apiKey, string deploymentName)
        : this(new HttpClient(new BoundedDocumentResponseHandler(httpClient)) { Timeout = Timeout.InfiniteTimeSpan },
            new MistralDocumentExtractionClientOptions
            {
                OcrEndpoint = ocrEndpoint,
                DefaultModel = deploymentName,
                IncludeImages = true,
            }, apiKey)
    {
    }

    private FoundryMistralDocumentExtractionClient(
        HttpClient boundedClient, MistralDocumentExtractionClientOptions options, string apiKey)
        : base(new MistralDocumentExtractionClient(options, apiKey, boundedClient), TimeSpan.FromMinutes(5))
        => _boundedClient = boundedClient;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _boundedClient.Dispose();
    }
}
