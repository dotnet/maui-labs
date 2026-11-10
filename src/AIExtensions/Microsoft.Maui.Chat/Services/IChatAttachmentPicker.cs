namespace Microsoft.Maui.Chat;

/// <summary>Picks files for the chat composer.</summary>
/// <remarks>
/// The default implementation wraps the platform file picker. Replace it to source attachments from
/// anywhere else, or to make picking deterministic in tests.
/// </remarks>
public interface IChatAttachmentPicker
{
    /// <summary>Prompts the user to pick one or more files.</summary>
    /// <param name="fileTypes">
    /// An optional file type filter, typically a <c>FilePickerFileType</c>, or <see langword="null"/> for any.
    /// </param>
    /// <param name="maxBytesPerFile">The largest accepted file size in bytes.</param>
    /// <param name="cancellationToken">Cancels the pick.</param>
    /// <returns>The picked attachments; empty when the user cancelled.</returns>
    Task<IReadOnlyList<ChatAttachment>> PickAsync(
        object? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default);
}
