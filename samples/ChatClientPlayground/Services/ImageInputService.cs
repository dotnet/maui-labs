using ChatClientPlayground.Models;

namespace ChatClientPlayground.Services;

/// <summary>Loads image input from the system picker or the packaged sample image.</summary>
public sealed class ImageInputService
{
    private const int MaximumImageBytes = 20 * 1024 * 1024;

    /// <summary>Lets the user choose an image and copies its temporary stream into memory.</summary>
    public async Task<ImageAttachment?> PickImageAsync(CancellationToken cancellationToken = default)
    {
        FileResult? result;
        try
        {
            result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select an image",
                FileTypes = FilePickerFileType.Images,
            });
        }
        catch (PermissionException exception)
        {
            throw new InvalidOperationException(
                "Image access was denied. Allow file access in the platform settings and try again.",
                exception);
        }

        if (result is null)
            return null;

        var mediaType = GetImageMediaType(result);
        if (mediaType is null)
            throw new InvalidOperationException("The selected file is not a supported image type.");

        await using var input = await result.OpenReadAsync();
        return new(result.FileName, mediaType, await ReadBytesAsync(input, cancellationToken));
    }

    /// <summary>Loads the packaged .NET bot image for repeatable image-message testing.</summary>
    public async Task<ImageAttachment> LoadSampleImageAsync(CancellationToken cancellationToken = default)
    {
        await using var input = await FileSystem.OpenAppPackageFileAsync("playground_sample.png");
        return new(
            "playground_sample.png",
            "image/png",
            await ReadBytesAsync(input, cancellationToken));
    }

    private static async Task<byte[]> ReadBytesAsync(Stream input, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + bytesRead > MaximumImageBytes)
                throw new InvalidOperationException("Images must be 20 MB or smaller.");

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return output.ToArray();
    }

    private static string? GetImageMediaType(FileResult result)
    {
        var reportedMediaType = result.ContentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (reportedMediaType is "image/jpeg" or "image/png" or "image/gif" or "image/webp")
        {
            return reportedMediaType;
        }

        return Path.GetExtension(result.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => null,
        };
    }
}
