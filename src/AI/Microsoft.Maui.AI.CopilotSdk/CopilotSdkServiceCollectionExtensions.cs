using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.CopilotSdk;

/// <summary>
/// Extension methods for registering the Copilot SDK as the <see cref="IChatClient"/> backend.
/// </summary>
public static class CopilotSdkServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="CopilotSdkChatClient"/> as the default <see cref="IChatClient"/>
    /// using the GitHub Copilot SDK backend.
    /// </summary>
    public static IServiceCollection AddCopilotChatWithCopilotSdk(
        this IServiceCollection services,
        Action<CopilotChatConfiguration>? configure = null)
    {
        var config = new CopilotChatConfiguration();
        configure?.Invoke(config);

        services.AddSingleton(config);
        services.AddSingleton<IChatClient>(sp =>
        {
            var cfg = sp.GetRequiredService<CopilotChatConfiguration>();
            return new CopilotSdkChatClient(cfg);
        });

        return services;
    }
}
