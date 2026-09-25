using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Recording;

public static class RecordingChatClientBuilderExtensions
{
    public static ChatClientBuilder UseRecording(this ChatClientBuilder builder, IChatRecordingSession recording) =>
        builder.Use(inner => new RecordingChatClient(inner, recording));
}
