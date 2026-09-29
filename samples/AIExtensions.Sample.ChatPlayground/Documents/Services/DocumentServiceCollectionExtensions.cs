using Microsoft.Extensions.DependencyInjection;

namespace AIExtensions.Sample.ChatPlayground;

internal static class DocumentServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentFeature(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentExtractionProvider, AppleDocumentExtractionProvider>();
        services.AddSingleton<DocumentExtractionRunner>();
        services.AddSingleton<DocumentInputService>();
        services.AddSingleton<DocumentSettingsViewModel>();
        services.AddSingleton<DocumentPlaygroundViewModel>();
        services.AddTransient<Page, DocumentPage>();
        return services;
    }
}
