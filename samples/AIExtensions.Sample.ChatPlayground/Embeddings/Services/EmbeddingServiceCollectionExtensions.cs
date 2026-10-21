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
        if (OperatingSystem.IsIOSVersionAtLeast(14) || OperatingSystem.IsMacCatalystVersionAtLeast(14))
        {
            using var embedding = NLEmbedding.GetSentenceEmbedding(NLLanguage.English);
            if (embedding is not null)
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(CreateAppleEmbeddingGenerator);
        }
#endif
        if (!string.IsNullOrWhiteSpace(settings.EmbeddingDeploymentName))
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(provider => CreateAzureEmbeddingGenerator(provider, settings));

        return services;
    }

#if IOS || MACCATALYST
    [SupportedOSPlatform("ios14.0")]
    [SupportedOSPlatform("maccatalyst14.0")]
    private static IEmbeddingGenerator<string, Embedding<float>> CreateAppleEmbeddingGenerator(IServiceProvider provider)
    {
        var generator = new NLEmbeddingGenerator();
        var native = ((IEmbeddingGenerator<string, Embedding<float>>)generator).GetService<NLEmbedding>()!;
        return new DescribedEmbeddingGenerator(
            generator.AsBuilder()
                .UsePlaygroundTelemetry()
                .UseLogging(provider.GetRequiredService<ILoggerFactory>())
                .Build(),
            new EmbeddingGeneratorDescriptor(
                $"apple-natural-language/en/sentence/{native.Revision}/{native.Dimension}",
                "Apple NaturalLanguage",
                $"On-device English sentence embeddings, revision {native.Revision}, {native.Dimension} dimensions."));
    }
#endif

    private static IEmbeddingGenerator<string, Embedding<float>> CreateAzureEmbeddingGenerator(IServiceProvider provider, AISettings settings) =>
        new DescribedEmbeddingGenerator(
            new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey!),
                new OpenAIClientOptions { Endpoint = settings.Endpoint! })
                .GetEmbeddingClient(settings.EmbeddingDeploymentName!)
                .AsIEmbeddingGenerator().AsBuilder()
                .UsePlaygroundTelemetry()
                .UseLogging(provider.GetRequiredService<ILoggerFactory>())
                .Build(),
            new EmbeddingGeneratorDescriptor(
                "azure-openai-embeddings",
                "Azure OpenAI",
                $"Embeddings use deployment '{settings.EmbeddingDeploymentName}'. Imported document text and search queries are sent to Azure."));
}
