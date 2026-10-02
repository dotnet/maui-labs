using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace AIExtensions.Sample.ChatPlayground;

internal static class ImageServiceCollectionExtensions
{
    public static IServiceCollection AddImageFeature(this IServiceCollection services, AISettings settings)
    {
        services.AddSingleton<ImageGenerationService>();
        services.AddSingleton<ImageSettingsViewModel>();
        services.AddSingleton<ImagePlaygroundViewModel>();
        services.AddTransient<Page, ImagePage>();

        if (!string.IsNullOrWhiteSpace(settings.ImageDeploymentName))
            services.AddSingleton<IImageGenerator>(_ => CreateAzureImageGenerator(settings));

        return services;
    }

    private static IImageGenerator CreateAzureImageGenerator(AISettings settings) =>
        new DescribedImageGenerator(
            new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey!),
                new OpenAIClientOptions { Endpoint = settings.Endpoint! })
                .GetImageClient(settings.ImageDeploymentName!)
                .AsIImageGenerator(),
            new ImageGeneratorDescriptor(
                "azure-openai-image-generation",
                "Azure OpenAI",
                "Generates or edits images with the configured Azure deployment. Prompts and original images leave this device; requests may incur charges.",
                SupportsEdits: true));
}
