using ChatClientPlayground.Models;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.Services;

/// <summary>Resolves the application's keyed chat-client slots.</summary>
public sealed class ChatClientService
{
    private readonly IServiceProvider _services;

    /// <summary>Initializes the keyed client resolver.</summary>
    public ChatClientService(IServiceProvider services) => _services = services;

    /// <summary>Gets the selected client, or a specific reason it is unavailable.</summary>
    public ChatClientSelection GetClient(ChatClientKind kind)
    {
        var key = kind == ChatClientKind.Cloud ? ChatClientKeys.Cloud : ChatClientKeys.Local;
        try
        {
            var client = _services.GetRequiredKeyedService<IChatClient>(key);
            var descriptor = client.GetService<ChatClientDescriptor>()
                ?? throw new InvalidOperationException("The configured chat client did not expose its descriptor.");
            return new(client, descriptor);
        }
        catch (InvalidOperationException exception)
        {
            return new(null, CreateUnavailableDescriptor(kind, exception.Message));
        }
    }

    private static ChatClientDescriptor CreateUnavailableDescriptor(ChatClientKind kind, string status) =>
        kind == ChatClientKind.Cloud
            ? new("Azure OpenAI", $"Azure OpenAI is unavailable: {status}", SupportsImageInput: true)
            : new("Local provider", $"Local provider is unavailable: {status}", SupportsImageInput: false);
}
