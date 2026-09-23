using ChatClientPlayground.Models;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.Services;

/// <summary>Resolves the application's keyed chat-client slots.</summary>
public sealed class ChatClientService
{
    private readonly IServiceProvider _services;
    private readonly ChatRecordingService _recording;

    /// <summary>Initializes the keyed client resolver.</summary>
    public ChatClientService(IServiceProvider services, ChatRecordingService recording)
    {
        _services = services;
        _recording = recording;
    }

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

    /// <summary>Applies the current recording mode to either selected keyed provider.</summary>
    public IChatClient GetRequestClient(ChatClientSelection selection) => _recording.Mode switch
    {
        RecordingMode.Replay => new ReplayChatClient(_recording),
        RecordingMode.Record => new RecordingChatClient(selection.Client
            ?? throw new InvalidOperationException("A real provider is required to record."), _recording),
        _ => selection.Client ?? throw new InvalidOperationException("The selected provider is unavailable."),
    };

    private static ChatClientDescriptor CreateUnavailableDescriptor(ChatClientKind kind, string status) =>
        kind == ChatClientKind.Cloud
            ? new("Azure OpenAI", $"Azure OpenAI is unavailable: {status}", SupportsImageInput: true)
            : new("Local provider", $"Local provider is unavailable: {status}", SupportsImageInput: false);
}
