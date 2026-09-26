using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

#if IOS || MACCATALYST
using System.Runtime.Versioning;
using Microsoft.Maui.Essentials.AI;
using NaturalLanguage;
#endif

namespace AIExtensions.Sample.ChatPlayground;

internal static class EmbeddingServiceCollectionExtensions
{
    public static IServiceCollection AddEmbeddingFeature(this IServiceCollection services, AISettings settings)
    {
        services.AddSingleton(_ => new DocumentStore(FileSystem.AppDataDirectory));
        services.AddSingleton(serviceProvider => new DocumentSearchService(
            serviceProvider.GetRequiredService<DocumentStore>(),
            serviceProvider.GetRequiredService<ILogger<DocumentSearchService>>(),
            FileSystem.AppDataDirectory));
        services.AddSingleton<EmbeddingSettingsViewModel>();
        services.AddSingleton<EmbeddingPlaygroundViewModel>();
        services.AddTransient<Page, EmbeddingPage>();

#if IOS || MACCATALYST
        if (OperatingSystem.IsIOSVersionAtLeast(13) || OperatingSystem.IsMacCatalystVersionAtLeast(13, 1))
        {
            using var embedding = NLEmbedding.GetSentenceEmbedding(NLLanguage.English);
            if (embedding is not null)
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ => CreateAppleEmbeddingGenerator());
        }
#endif
        if (!string.IsNullOrWhiteSpace(settings.EmbeddingDeploymentName))
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ => CreateAzureEmbeddingGenerator(settings));

        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios13.0")]
    [SupportedOSPlatform("maccatalyst13.1")]
    private static IEmbeddingGenerator<string, Embedding<float>> CreateAppleEmbeddingGenerator() =>
        new DescribedEmbeddingGenerator(
            new NLEmbeddingGenerator(),
            new EmbeddingGeneratorDescriptor(
                "apple-natural-language",
                "Apple NaturalLanguage",
                "On-device English sentence embeddings from Apple's NaturalLanguage framework."));
#endif

    private static IEmbeddingGenerator<string, Embedding<float>> CreateAzureEmbeddingGenerator(AISettings settings) =>
        new DescribedEmbeddingGenerator(
            new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey!),
                new OpenAIClientOptions { Endpoint = settings.Endpoint! })
                .GetEmbeddingClient(settings.EmbeddingDeploymentName!)
                .AsIEmbeddingGenerator(),
            new EmbeddingGeneratorDescriptor(
                "azure-openai-embeddings",
                "Azure OpenAI",
                $"Embeddings use deployment '{settings.EmbeddingDeploymentName}'. Imported document text and search queries are sent to Azure."));
}
