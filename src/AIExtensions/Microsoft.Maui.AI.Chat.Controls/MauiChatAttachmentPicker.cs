using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

internal sealed class MauiChatAttachmentPicker : IChatAttachmentPicker
{
    internal static MauiChatAttachmentPicker Default { get; } = new();

    public async Task<IReadOnlyList<ChatAttachment>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default)
    {
        if (maxBytesPerFile <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerFile));

        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            FileTypes = fileTypes,
        });
        var attachments = new List<ChatAttachment>();
        foreach (var file in results)
        {
            if (file is null)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await CopyWithLimitAsync(
                stream,
                buffer,
                maxBytesPerFile,
                file.FileName,
                cancellationToken);
            var mediaType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;
            attachments.Add(new(
                file.FileName,
                new DataContent(buffer.ToArray(), mediaType)));
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
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Attachment '{fileName}' exceeds the {maxBytes} byte limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
