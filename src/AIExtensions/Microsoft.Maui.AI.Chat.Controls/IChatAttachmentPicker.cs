namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Abstracts native file selection for a <see cref="CopilotChatView"/>.</summary>
public interface IChatAttachmentPicker : Microsoft.Maui.Chat.IChatAttachmentPicker
{
    Task<IReadOnlyList<ChatAttachment>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<Microsoft.Maui.Chat.ChatAttachment>>
        Microsoft.Maui.Chat.IChatAttachmentPicker.PickAsync(
            object? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken)
    {
        var attachments = await PickAsync(
            fileTypes as FilePickerFileType,
            maxBytesPerFile,
            cancellationToken);
        return attachments;
    }
}
