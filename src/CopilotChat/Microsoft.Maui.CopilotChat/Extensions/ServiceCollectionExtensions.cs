using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.CopilotChat.Controls;

namespace Microsoft.Maui.CopilotChat;

/// <summary>
/// Extension methods for registering the Copilot Chat control services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="CopilotSdkChatClient"/> as the default <see cref="IChatClient"/>
    /// using the Copilot SDK backend.
    /// </summary>
    public static IServiceCollection AddCopilotChat(
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

    /// <summary>
    /// Registers an existing <see cref="IChatClient"/> for use by <see cref="CopilotChatView"/>.
    /// Use this when you want to provide your own chat client (e.g. Azure OpenAI, Ollama).
    /// </summary>
    public static IServiceCollection AddCopilotChat(
        this IServiceCollection services,
        IChatClient chatClient)
    {
        services.AddSingleton(chatClient);
        return services;
    }
}
