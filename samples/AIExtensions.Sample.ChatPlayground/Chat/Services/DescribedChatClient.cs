using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Exposes the descriptor through the IChatClient service-discovery API.</summary>
public sealed class DescribedChatClient(IChatClient innerClient, ChatClientDescriptor descriptor)
    : DelegatingChatClient(innerClient)
{
    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientDescriptor) && serviceKey is null
            ? descriptor
            : base.GetService(serviceType, serviceKey);
}
