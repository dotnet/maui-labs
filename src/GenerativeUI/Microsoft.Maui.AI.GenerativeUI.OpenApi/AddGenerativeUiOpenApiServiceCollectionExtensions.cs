using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// Registers the Generative UI OpenAPI server-API stack (spec cache, invoker, and the AI tools) in a
/// dependency-injection container.
/// </summary>
public static class AddGenerativeUiOpenApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GenerativeOpenApiOptions"/>, a configured <see cref="HttpClient"/>,
    /// <see cref="OpenApiCache"/>, <see cref="ApiInvoker"/>, and <see cref="OpenApiExplorerTools"/> as
    /// singletons. The OpenAPI document is fetched and reduced lazily on first tool use. Register
    /// <see cref="OpenApiExplorerTools"/> as an <c>[AIToolSource]</c> to expose the tools to a model.
    /// </summary>
    public static IServiceCollection AddGenerativeUiOpenApi(
        this IServiceCollection services,
        Action<GenerativeOpenApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new GenerativeOpenApiOptions();
        configure(options);

        if (options.BaseAddress is null)
            throw new InvalidOperationException($"{nameof(GenerativeOpenApiOptions.BaseAddress)} must be set.");

        services.AddSingleton(options);

        services.AddSingleton(_ =>
        {
            var httpClient = new HttpClient { BaseAddress = options.BaseAddress };
            options.ConfigureHttpClient?.Invoke(httpClient);
            return new GenerativeOpenApiHttpClient(httpClient);
        });

        services.AddSingleton(sp =>
            new OpenApiCache(options, sp.GetRequiredService<GenerativeOpenApiHttpClient>().Client));

        services.AddSingleton(sp =>
            new ApiInvoker(options, sp.GetRequiredService<GenerativeOpenApiHttpClient>().Client));

        services.AddSingleton<OpenApiExplorerTools>();

        return services;
    }

    // Wrapper so the stack's HttpClient can be registered/resolved without clashing with any other
    // HttpClient the host app registers.
    private sealed class GenerativeOpenApiHttpClient(HttpClient client)
    {
        public HttpClient Client { get; } = client;
    }
}
