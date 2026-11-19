using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>The record/replay protocol, independent of where chats are stored.</summary>
public interface IChatRecordingSession
{
    void AddResponse(JsonObject request, ChatResponse response);
    RecordedInteraction BeginStreaming(JsonObject request);
    void AddUpdate(RecordedInteraction interaction, JsonObject update);
    void CompleteStreaming(RecordedInteraction interaction);
    RecordedInteraction PeekNext();
    void CompleteReplay(RecordedInteraction interaction);
}
