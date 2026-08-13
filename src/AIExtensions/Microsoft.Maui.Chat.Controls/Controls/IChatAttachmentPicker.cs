namespace Microsoft.Maui.Chat.Controls;

/// <summary>Picks files for the <see cref="ChatView"/> composer.</summary>
/// <remarks>
/// The default implementation wraps the platform file picker. Replace it to source attachments from
/// anywhere else, or to make picking deterministic in tests.
/// </remarks>
public interface IChatAttachmentPicker
{
    /// <summary>Prompts the user to pick one or more files.</summary>
    /// <param name="fileTypes">The file types to allow, or <see langword="null"/> for any.</param>
    /// <param name="maxBytesPerFile">The largest accepted file size in bytes.</param>
    /// <param name="cancellationToken">Cancels the pick.</param>
    /// <returns>The picked attachments; empty when the user cancelled.</returns>
    Task<IReadOnlyList<ChatAttachment>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default);
}
