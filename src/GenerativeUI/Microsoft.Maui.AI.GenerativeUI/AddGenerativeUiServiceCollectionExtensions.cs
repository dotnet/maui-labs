using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Canvas;
using Microsoft.Maui.AI.GenerativeUI.Composition;
using Microsoft.Maui.AI.GenerativeUI.Inflation;
using Microsoft.Maui.AI.GenerativeUI.OpenApi;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.AI.GenerativeUI.Tools;
using CanvasState = Microsoft.Maui.AI.GenerativeUI.Canvas.CanvasState;

namespace Microsoft.Maui.AI.GenerativeUI;

/// <summary>
/// Registers the whole Generative UI stack — both the OpenAPI server-API half
/// (<see cref="OpenApiExplorerTools"/>) and the client-UI half (canvas, inflator, registry, and
/// <see cref="GenerativeUiTools"/>). Expose <see cref="OpenApiExplorerTools"/> and
/// <see cref="GenerativeUiTools"/> as <c>[AIToolSource]</c>s to give the model both tool families.
/// </summary>
public static class AddGenerativeUiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the combined server-API + client-UI Generative UI services as singletons.
    /// </summary>
    public static IServiceCollection AddGenerativeUi(
        this IServiceCollection services,
        Action<GenerativeUiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GenerativeUiOptions();
        configure(options);

        // Server-API half: reuse the OpenAPI registration with the same configuration.
        services.AddGenerativeUiOpenApi(o =>
        {
            o.BaseAddress = options.BaseAddress;
            o.OpenApiPath = options.OpenApiPath;
            o.ConfigureHttpClient = options.ConfigureHttpClient;
            o.AllowedHosts = options.AllowedHosts;
            o.SpecFetch = options.SpecFetch;
            o.SeedEndpointIndex = options.SeedEndpointIndex;
            o.MaxResponseBytes = options.MaxResponseBytes;
            o.MaxRequestBytes = options.MaxRequestBytes;
            o.RefResolutionDepth = options.RefResolutionDepth;
        });

        // Client-UI half.
        services.AddSingleton(options.Ui);
        services.AddSingleton<CanvasState>();
        services.AddSingleton<ComponentCandidateResolver>();
        services.AddSingleton<CompositionPlanValidator>();
        services.AddSingleton(sp => new GenUiInflator(sp.GetRequiredService<GenerativeUiRegistry>(), sp));
        services.AddSingleton<GenerativeUiTools>();

        return services;
    }
}
