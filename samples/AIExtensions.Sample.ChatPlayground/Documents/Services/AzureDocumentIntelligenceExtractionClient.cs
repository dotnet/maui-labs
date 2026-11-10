using Azure.AI.DocumentIntelligence;
using CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;

namespace AIExtensions.Sample.ChatPlayground;

internal sealed class AzureDocumentIntelligenceExtractionClient(DocumentIntelligenceClient client)
    : CloudDocumentExtractionPolicy(client.AsDocumentExtractionClient(), TimeSpan.FromSeconds(100))
{
}
