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

    public void Validate()
    {
        var hasDeployment = !string.IsNullOrWhiteSpace(DeploymentName) ||
            !string.IsNullOrWhiteSpace(ImageDeploymentName) ||
            !string.IsNullOrWhiteSpace(EmbeddingDeploymentName) ||
            !string.IsNullOrWhiteSpace(DocumentDeploymentName);
        if (!hasDeployment)
            return;

        if (Endpoint is null)
            throw new InvalidOperationException("AI:Endpoint is required when an Azure deployment is configured.");

        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("AI:ApiKey is required when an Azure deployment is configured.");

        if (!string.IsNullOrWhiteSpace(DocumentDeploymentName))
            _ = GetFoundryResourceEndpoint();
    }

    internal Uri GetFoundryResourceEndpoint()
    {
        var endpoint = Endpoint ?? throw new InvalidOperationException("AI:Endpoint is required.");
        var builder = new UriBuilder(endpoint)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty,
        };

        const string openAiSuffix = ".openai.azure.com";
        const string foundrySuffix = ".services.ai.azure.com";

        if (builder.Host.EndsWith(openAiSuffix, StringComparison.OrdinalIgnoreCase))
        {
            builder.Host = builder.Host[..^openAiSuffix.Length] + foundrySuffix;
        }
        else if (!builder.Host.EndsWith(foundrySuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AI:Endpoint must use an Azure OpenAI or Microsoft Foundry resource host when AI:DocumentDeploymentName is configured.");
        }

        return builder.Uri;
    }
}
