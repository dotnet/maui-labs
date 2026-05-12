using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.CopilotChat;

/// <summary>
/// Extension methods for registering chat client services for <see cref="Controls.CopilotChatView"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an existing <see cref="IChatClient"/> for use by <see cref="Controls.CopilotChatView"/>.
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
