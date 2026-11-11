namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>The default <see cref="IChatAttachmentPicker"/>: the platform file picker, read into memory.</summary>
/// <remarks>
/// Files are copied into memory so an attachment can be previewed and sent without holding an open stream.
/// A file larger than the limit is rejected while copying, before the whole file is read.
/// </remarks>
internal sealed class FileChatAttachmentPicker : IChatAttachmentPicker
{
    public static FileChatAttachmentPicker Default { get; } = new();

    public async Task<IReadOnlyList<DataContent>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytesPerFile);

        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions { FileTypes = fileTypes });
        if (results is null)
            return [];

        var attachments = new List<DataContent>();
        foreach (var file in results)
        {
            if (file is null)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = await file.OpenReadAsync();
            var bytes = await StreamLimits.ReadAsync(stream, maxBytesPerFile, cancellationToken)
                ?? throw new AttachmentTooLargeException(file.FileName);

            var mediaType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;
            attachments.Add(new DataContent(bytes, mediaType) { Name = file.FileName });
        }

        return attachments;
    }
}

/// <summary>Thrown by the default picker when a file exceeds the per-file limit.</summary>
internal sealed class AttachmentTooLargeException(string fileName)
    : Exception($"'{fileName}' is larger than the attachment limit.")
{
}

internal static class StreamLimits
{
    /// <summary>Copies a stream into memory, or returns <see langword="null"/> once it exceeds the limit.</summary>
    public static async Task<byte[]?> ReadAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                return buffer.ToArray();

            total += read;
            if (total > maxBytes)
                return null;

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }
}
