namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Abstracts native file selection for a <see cref="CopilotChatView"/>.</summary>
public interface IChatAttachmentPicker : Microsoft.Maui.Chat.Controls.IChatAttachmentPicker
{
    new Task<IReadOnlyList<ChatAttachment>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyList<Microsoft.Maui.Chat.Controls.ChatAttachment>>
        Microsoft.Maui.Chat.Controls.IChatAttachmentPicker.PickAsync(
            FilePickerFileType? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken)
    {
        var attachments = await PickAsync(
            fileTypes,
            maxBytesPerFile,
            cancellationToken);
        return attachments;
    }
}
