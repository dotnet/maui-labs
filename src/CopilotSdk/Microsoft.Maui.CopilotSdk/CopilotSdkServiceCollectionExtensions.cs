using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// Extension methods for registering a <see cref="CopilotSdkChatClient"/> as the default <see cref="IChatClient"/>.
/// </summary>
public static class CopilotSdkServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="CopilotSdkChatClient"/> as the default <see cref="IChatClient"/>
    /// using the GitHub Copilot SDK backend.
    /// </summary>
    public static IServiceCollection AddCopilotSdkChatClient(
        this IServiceCollection services,
        Action<CopilotSdkConfiguration>? configure = null)
    {
        var config = new CopilotSdkConfiguration();
        configure?.Invoke(config);

        services.AddSingleton(config);
        services.AddSingleton<IChatClient>(sp =>
        {
            var cfg = sp.GetRequiredService<CopilotSdkConfiguration>();
            return new CopilotSdkChatClient(cfg);
        });

        return services;
    }
}
