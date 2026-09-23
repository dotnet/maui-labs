using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Recording;

public static class RecordingChatClientBuilderExtensions
{
    public static ChatClientBuilder UseRecording(this ChatClientBuilder builder, ChatRecordingService recording) =>
        builder.Use(inner => new RecordingChatClient(inner, recording));
}
