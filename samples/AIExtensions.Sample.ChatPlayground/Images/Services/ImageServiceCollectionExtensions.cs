using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

#if WINDOWS
using System.Runtime.Versioning;
using Microsoft.Maui.Essentials.AI;
#endif

namespace AIExtensions.Sample.ChatPlayground;

internal static class ImageServiceCollectionExtensions
{
    public static IServiceCollection AddImageFeature(this IServiceCollection services, AISettings settings)
    {
        services.AddSingleton<ImageGenerationService>();
        services.AddSingleton<ImageSettingsViewModel>();
        services.AddSingleton<ImagePlaygroundViewModel>();
        services.AddTransient<Page, ImagePage>();

#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            services.AddSingleton<IImageGenerator>(_ => CreateWindowsImageGenerator());
#endif

        if (!string.IsNullOrWhiteSpace(settings.ImageDeploymentName))
            services.AddSingleton<IImageGenerator>(_ => CreateAzureImageGenerator(settings));

        return services;
    }

#if WINDOWS
    [SupportedOSPlatform("windows10.0.26100.0")]
    private static IImageGenerator CreateWindowsImageGenerator() =>
        new DescribedImageGenerator(
            new WindowsAIImageGenerator(),
            new ImageGeneratorDescriptor(
                "windows-ai-image-generation",
                "Windows AI",
                "Generates or edits images on this device. The first request checks whether the Windows image model is ready.",
                SupportsEdits: true));
#endif

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
