using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Services;

/// <summary>Metadata exposed by a configured chat-client slot.</summary>
public sealed record ChatClientDescriptor(
    string DisplayName,
    string Status,
    bool SupportsImageInput,
    bool SupportsReasoningSummary = false,
    bool SupportsImageGeneration = false,
    bool IsReplay = false);

/// <summary>Exposes the descriptor through the IChatClient service-discovery API.</summary>
public sealed class DescribedChatClient : DelegatingChatClient
{
    private readonly ChatClientDescriptor _descriptor;

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
