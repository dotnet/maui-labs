using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Extension methods for registering the headless chat session engine.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the multi-instance chat session service.
    /// Requires <see cref="Microsoft.Extensions.AI.IChatClient"/> and
    /// <c>IEnumerable&lt;AITool&gt;</c> to be registered.
    /// </summary>
    public static IServiceCollection AddChatSession(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        services.TryAdd(new ServiceDescriptor(typeof(ChatSession), typeof(ChatSession), lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IChatSession), sp => sp.GetRequiredService<ChatSession>(), lifetime));
        return services;
    }
}
