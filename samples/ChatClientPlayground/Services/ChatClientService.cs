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
        if (kind == ChatClientKind.Recording)
            return new(kind, new ReplayChatClient(_recording), new(
                "Recording",
                _recording.HasReplayRemaining
                    ? $"Replay {_recording.ReplayPosition + 1}/{_recording.InteractionCount}; no model will be invoked."
                    : _recording.InteractionCount == 0
                        ? "The recording is empty. Record a response or choose a recording file."
                        : "Playback complete. Rewind to replay or clear the recording.",
                SupportsImageInput: true));

        if (kind is not (ChatClientKind.Local or ChatClientKind.Cloud))
            throw new ArgumentOutOfRangeException(nameof(kind));
        try
        {
            var client = _services.GetRequiredKeyedService<IChatClient>(kind);
            var descriptor = client.GetService<ChatClientDescriptor>()
                ?? throw new InvalidOperationException("The configured chat client did not expose its descriptor.");
            return new(kind, client, descriptor);
        }
        catch (InvalidOperationException exception)
        {
            return new(kind, null, CreateUnavailableDescriptor(kind, exception.Message));
        }
    }

    /// <summary>Records either real provider when enabled, or returns a provider-free replay client.</summary>
    public IChatClient GetRequestClient(ChatClientSelection selection) => selection.Kind switch
    {
        ChatClientKind.Recording => selection.Client
            ?? throw new InvalidOperationException("The recording client is unavailable."),
        _ when _recording.IsRecordingEnabled => new RecordingChatClient(selection.Client
            ?? throw new InvalidOperationException("A real provider is required to record."), _recording),
        _ => selection.Client ?? throw new InvalidOperationException("The selected provider is unavailable."),
    };

    private static ChatClientDescriptor CreateUnavailableDescriptor(ChatClientKind kind, string status) =>
        kind == ChatClientKind.Cloud
            ? new("Azure OpenAI", $"Azure OpenAI is unavailable: {status}", SupportsImageInput: true)
            : new("Local provider", $"Local provider is unavailable: {status}", SupportsImageInput: false);
}
