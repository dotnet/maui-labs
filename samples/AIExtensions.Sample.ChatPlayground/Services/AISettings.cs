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
    public Uri? FoundryEndpoint { get; set; }
    public string? FoundryApiKey { get; set; }

    public void Validate()
    {
        var hasOpenAI = !string.IsNullOrWhiteSpace(DeploymentName) ||
            !string.IsNullOrWhiteSpace(ImageDeploymentName) ||
            !string.IsNullOrWhiteSpace(EmbeddingDeploymentName);
        if (hasOpenAI)
        {
            if (Endpoint is null)
                throw new InvalidOperationException("AI:Endpoint is required when an Azure OpenAI deployment is configured.");

            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("AI:ApiKey is required when an Azure OpenAI deployment is configured.");
        }

        var hasFoundryResource = FoundryEndpoint is not null ||
            !string.IsNullOrWhiteSpace(FoundryApiKey) ||
            !string.IsNullOrWhiteSpace(DocumentDeploymentName);
        if (!hasFoundryResource)
            return;

        if (FoundryEndpoint is null)
            throw new InvalidOperationException("AI:FoundryEndpoint is required when a document deployment is configured.");

        if (string.IsNullOrWhiteSpace(FoundryApiKey))
            throw new InvalidOperationException("AI:FoundryApiKey is required when a document deployment is configured.");

        if (string.IsNullOrWhiteSpace(DocumentDeploymentName))
            throw new InvalidOperationException("AI:DocumentDeploymentName is required when Foundry document credentials are configured.");
    }
}
