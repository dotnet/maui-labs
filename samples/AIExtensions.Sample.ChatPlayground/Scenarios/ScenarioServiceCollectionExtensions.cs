using Microsoft.Extensions.DependencyInjection;

namespace AIExtensions.Sample.ChatPlayground;

internal static class ScenarioServiceCollectionExtensions
{
    /// <summary>Adds the Agentic tab: the agentic UI scenarios, on the chat clients the chat feature registers.</summary>
    public static IServiceCollection AddScenarioFeature(this IServiceCollection services)
    {
        services.AddSingleton<ScenariosViewModel>();
        services.AddTransient<Page, ScenariosPage>();
        return services;
    }
}
