using Microsoft.Extensions.AI;

namespace ChatClientPlayground.Services;

/// <summary>Decorates a chat client with playground-specific service-discovery metadata.</summary>
public sealed class DescribedChatClient : DelegatingChatClient
{
    private readonly ChatClientDescriptor _descriptor;

    /// <summary>Initializes the client with metadata for its configured slot.</summary>
    public DescribedChatClient(IChatClient innerClient, ChatClientDescriptor descriptor)
        : base(innerClient)
    {
        _descriptor = descriptor;
    }

    /// <inheritdoc />
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientDescriptor) && serviceKey is null
            ? _descriptor
            : base.GetService(serviceType, serviceKey);
}
