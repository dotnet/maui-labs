using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI;

/// <summary>
/// Registers the adaptive whole-component composition runtime without exposing any AI tools.
/// </summary>
public static class AddAdaptiveGenerativeUiServiceCollectionExtensions
{
    public static IServiceCollection AddAdaptiveGenerativeUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<AdaptiveComponentCatalogBuilder>();
        services.AddSingleton<ComponentLayoutValidator>();
        services.AddSingleton<IAdaptiveLayoutCache, AdaptiveLayoutCache>();
        services.AddSingleton<IAdaptiveSurfaceSessionFactory, AdaptiveSurfaceSessionFactory>();
        services.AddSingleton<AdaptiveStateProjector>();
        services.AddSingleton<IAdaptiveLayoutGenerator, AdaptiveLayoutGenerator>();
        services.AddSingleton<AdaptiveSurfaceComposer>();
        services.AddSingleton<AdaptiveRegionRenderer>();
        return services;
    }

    public static IServiceCollection AddAdaptiveGenerativeUi(
        this IServiceCollection services,
        GenerativeUiRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        services.AddSingleton(registry);
        return services.AddAdaptiveGenerativeUi();
    }
}
