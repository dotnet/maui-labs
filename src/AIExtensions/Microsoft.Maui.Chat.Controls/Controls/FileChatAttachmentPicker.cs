namespace Microsoft.Maui.Chat.Controls;

/// <summary>The default <see cref="IChatAttachmentPicker"/>: the platform file picker, read into reusable buffers.</summary>
/// <remarks>
/// Files are copied into memory so an attachment can be previewed and sent repeatedly without holding an
/// open stream. A file larger than the limit is rejected while copying, before the whole file is read.
/// </remarks>
public sealed class FileChatAttachmentPicker : IChatAttachmentPicker
{
    /// <summary>Gets the shared instance used when a <see cref="ChatView"/> has no explicit picker.</summary>
    public static FileChatAttachmentPicker Default { get; } = new();

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytesPerFile"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">A picked file is larger than <paramref name="maxBytesPerFile"/>.</exception>
    public async Task<IReadOnlyList<ChatAttachment>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytesPerFile);

        var results = await FilePicker.Default
            .PickMultipleAsync(new PickOptions { FileTypes = fileTypes })
            .ConfigureAwait(true);

        if (results is null)
            return [];

        var attachments = new List<ChatAttachment>();

        foreach (var file in results)
        {
            if (file is null)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
            using var buffer = new MemoryStream();
            await CopyWithLimitAsync(stream, buffer, maxBytesPerFile, file.FileName, cancellationToken)
                .ConfigureAwait(true);

            var mediaType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            attachments.Add(new ChatAttachment(file.FileName, mediaType, buffer.ToArray()));
        }

        return attachments;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(true);
            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
                throw new InvalidOperationException($"'{fileName}' is larger than the {maxBytes} byte limit.");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);
        }
    }
}
