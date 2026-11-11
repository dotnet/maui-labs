namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Lets the user pick files to attach to the next message.</summary>
/// <remarks>
/// <see cref="AppBuilderExtensions.UseAIChat"/> registers a default implementation that uses the platform
/// file picker. <see cref="AIChatView"/> also falls back to that default when nothing is registered.
/// </remarks>
public interface IChatAttachmentPicker
{
    /// <summary>Shows a picker and returns the chosen files as in-memory content.</summary>
    /// <param name="fileTypes">The file types to offer, or <see langword="null"/> for any file.</param>
    /// <param name="maxBytesPerFile">The largest file, in bytes, the caller accepts.</param>
    /// <param name="cancellationToken">Cancels the pick.</param>
    /// <returns>
    /// The picked files, each with <see cref="DataContent.Name"/> and <see cref="DataContent.MediaType"/> set.
    /// Empty when the user dismisses the picker.
    /// </returns>
    Task<IReadOnlyList<DataContent>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default);
}
