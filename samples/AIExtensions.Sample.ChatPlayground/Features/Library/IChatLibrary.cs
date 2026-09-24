using AIExtensions.Sample.ChatPlayground.Features.Recording;

namespace AIExtensions.Sample.ChatPlayground.Features.Library;

/// <summary>Read-only access to saved chats for independent indexers.</summary>
public interface IChatLibrary
{
    IReadOnlyList<SavedChat> ListChats();
    ChatRecording ReadChat(string id);
}
