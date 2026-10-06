namespace AIExtensions.Sample.ChatPlayground;

public sealed class AISettings
{
    public const string SectionName = "AI";

    public Uri? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DeploymentName { get; set; }
    public string? ImageDeploymentName { get; set; }
    public string? EmbeddingDeploymentName { get; set; }
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
            string.IsNullOrWhiteSpace(EmbeddingDeploymentName))
            return;

        if (Endpoint is null)
            throw new InvalidOperationException("AI:Endpoint is required when an Azure deployment is configured.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("AI:ApiKey is required when an Azure deployment is configured.");
    }
}
