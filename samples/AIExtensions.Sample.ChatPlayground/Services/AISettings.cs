namespace AIExtensions.Sample.ChatPlayground;

public sealed class AISettings
{
    public const string SectionName = "AI";

    public Uri? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? DeploymentName { get; set; }
    public string? ImageDeploymentName { get; set; }
    public string? EmbeddingDeploymentName { get; set; }
    public string? DocumentModelDeploymentName { get; set; }
    public Uri? FoundryEndpoint { get; set; }
    public string? FoundryApiKey { get; set; }
    public string MistralDocumentModelId { get; set; } = "mistral-ocr-4-0";

    public void Validate()
    {
        var hasOpenAI = !string.IsNullOrWhiteSpace(DeploymentName) ||
            !string.IsNullOrWhiteSpace(ImageDeploymentName) ||
            !string.IsNullOrWhiteSpace(EmbeddingDeploymentName) ||
            !string.IsNullOrWhiteSpace(DocumentModelDeploymentName);
        if (hasOpenAI)
        {
            if (Endpoint is null)
                throw new InvalidOperationException("AI:Endpoint is required when an Azure OpenAI deployment is configured.");

            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("AI:ApiKey is required when an Azure OpenAI deployment is configured.");
        }

        var hasFoundryResource = FoundryEndpoint is not null ||
            !string.IsNullOrWhiteSpace(FoundryApiKey);
        if (!hasFoundryResource)
            return;

        if (FoundryEndpoint is null)
            throw new InvalidOperationException("AI:FoundryEndpoint is required when Mistral OCR is configured.");

        if (string.IsNullOrWhiteSpace(FoundryApiKey))
            throw new InvalidOperationException("AI:FoundryApiKey is required when Mistral OCR is configured.");

        if (string.IsNullOrWhiteSpace(MistralDocumentModelId))
            throw new InvalidOperationException("AI:MistralDocumentModelId cannot be empty.");
    }
}
