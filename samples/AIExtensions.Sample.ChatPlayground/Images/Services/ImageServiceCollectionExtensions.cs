using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            services.AddSingleton<IImageGenerator>(CreateWindowsImageGenerator);
#endif

        if (!string.IsNullOrWhiteSpace(settings.ImageDeploymentName))
            services.AddSingleton<IImageGenerator>(provider => CreateAzureImageGenerator(provider, settings));

        return services;
    }

#if WINDOWS
    [SupportedOSPlatform("windows10.0.26100.0")]
    private static IImageGenerator CreateWindowsImageGenerator(IServiceProvider provider) =>
        new DescribedImageGenerator(
            new WindowsAIImageGenerator().AsBuilder()
                .UseLogging(provider.GetRequiredService<ILoggerFactory>())
                .Build(),
            new ImageGeneratorDescriptor(
                "windows-ai-image-generation",
                "Windows AI",
                "Generates or edits images on this device. The first request checks whether the Windows AI image model is ready.",
                SupportsEdits: true));
#endif

    private static IImageGenerator CreateAzureImageGenerator(IServiceProvider provider, AISettings settings) =>
        new DescribedImageGenerator(
            new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey!),
                new OpenAIClientOptions { Endpoint = settings.Endpoint! })
                .GetImageClient(settings.ImageDeploymentName!)
                .AsIImageGenerator().AsBuilder()
                .UseLogging(provider.GetRequiredService<ILoggerFactory>())
                .Build(),
            new ImageGeneratorDescriptor(
                "azure-openai-image-generation",
                "Azure OpenAI",
                "Generates or edits images with the configured Azure deployment. Prompts and original images leave this device; requests may incur charges.",
                SupportsEdits: true));
}
