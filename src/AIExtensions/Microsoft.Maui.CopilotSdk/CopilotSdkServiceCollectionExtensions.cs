using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// Dependency injection extensions for registering a <see cref="CopilotSdkChatClient"/>.
/// </summary>
public static class CopilotSdkServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="CopilotSdkChatClient"/> as the singleton <see cref="IChatClient"/> (and as the
    /// concrete <see cref="CopilotSdkChatClient"/>) backed by the GitHub Copilot SDK.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback to configure the <see cref="CopilotSdkConfiguration"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddCopilotSdkChatClient(
        this IServiceCollection services,
        Action<CopilotSdkConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(serviceProvider =>
        {
            var configuration = new CopilotSdkConfiguration();
            configure?.Invoke(configuration);
            return configuration;
        });

        services.AddSingleton(serviceProvider =>
            new CopilotSdkChatClient(serviceProvider.GetRequiredService<CopilotSdkConfiguration>()));

        services.AddSingleton<IChatClient>(serviceProvider =>
            serviceProvider.GetRequiredService<CopilotSdkChatClient>());

        return services;
    }
}
