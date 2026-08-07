namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Abstracts native file selection for a <see cref="CopilotChatView"/>.</summary>
public interface IChatAttachmentPicker
{
    Task<IReadOnlyList<ChatAttachment>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default);
}
