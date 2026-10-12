namespace AIExtensions.Sample.ChatPlayground;

public sealed class AISettings
{
    public const string SectionName = "AI";

    public Uri? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DeploymentName { get; set; }
    public string? ImageDeploymentName { get; set; }
    public string? EmbeddingDeploymentName { get; set; }

    public void Validate()
    {
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
