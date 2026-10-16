namespace AIExtensions.Sample.ChatPlayground;

public sealed class AISettings
{
    public const string SectionName = "AI";

    public Uri? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DeploymentName { get; set; }
    public string? ImageDeploymentName { get; set; }
    public string? EmbeddingDeploymentName { get; set; }
    public string? DocumentDeploymentName { get; set; }
    public Uri? DocumentIntelligenceEndpoint { get; set; }
    public string? DocumentIntelligenceKey { get; set; }

    public void Validate()
    {
        if (DocumentIntelligenceEndpoint is not null || !string.IsNullOrWhiteSpace(DocumentIntelligenceKey))
        {
            if (DocumentIntelligenceEndpoint is null)
                throw new InvalidOperationException("AI:DocumentIntelligenceEndpoint is required when Document Intelligence is configured.");
            if (string.IsNullOrWhiteSpace(DocumentIntelligenceKey))
                throw new InvalidOperationException("AI:DocumentIntelligenceKey is required when Document Intelligence is configured.");
        }

        if (string.IsNullOrWhiteSpace(DeploymentName) &&
            string.IsNullOrWhiteSpace(ImageDeploymentName) &&
            string.IsNullOrWhiteSpace(EmbeddingDeploymentName) &&
            string.IsNullOrWhiteSpace(DocumentDeploymentName))
            return;

        if (Endpoint is null)
            throw new InvalidOperationException("AI:Endpoint is required when an Azure deployment is configured.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("AI:ApiKey is required when an Azure deployment is configured.");

        if (!string.IsNullOrWhiteSpace(DocumentDeploymentName))
            _ = GetFoundryDocumentEndpoint();
    }

    internal Uri GetFoundryDocumentEndpoint()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttps ||
            Endpoint.UserInfo.Length != 0 || Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("AI:Endpoint must be an HTTPS resource URL without credentials, query, or fragment.");

        var host = Endpoint.Host;
        const string openAiSuffix = ".openai.azure.com";
        if (host.EndsWith(openAiSuffix, StringComparison.OrdinalIgnoreCase))
            host = host[..^openAiSuffix.Length] + ".services.ai.azure.com";

        return new UriBuilder(Endpoint) { Host = host, Path = "/", Query = "", Fragment = "" }.Uri;
    }
}
