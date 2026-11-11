namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Contains an input file independent of the picker, packaged asset or caller-owned stream it came from.</summary>
public sealed record InputFile(
    string FileName,
    string MediaType,
    byte[] Bytes)
{
    public static async Task<InputFile> ReadAsync(
        Stream input, string fileName, string? contentType = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!input.CanRead)
            throw new ArgumentException("The input stream must be readable.", nameof(input));
        cancellationToken.ThrowIfCancellationRequested();
        var reported = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        var mediaType = reported is "application/pdf" or "image/jpeg" or "image/png" or "image/gif" or "image/webp" or
            "image/heic" or "image/tiff" ? reported : Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".heic" => "image/heic",
                ".tif" or ".tiff" => "image/tiff",
                _ => throw new NotSupportedException("The selected file type is not supported."),
            };
        var bytes = await InputStreamReader.ReadBytesAsync(
            input, 20 * 1024 * 1024, "Files must be 20 MB or smaller.", cancellationToken).ConfigureAwait(false);
        return new(fileName, mediaType, bytes);
    }
}
