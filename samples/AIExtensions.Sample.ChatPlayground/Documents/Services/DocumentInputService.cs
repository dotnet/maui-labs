namespace AIExtensions.Sample.ChatPlayground;

public sealed record SelectedDocument(string FileName, string MediaType, byte[] Bytes);

public sealed class DocumentInputService
{
    private const int MaximumBytes = 20 * 1024 * 1024;
    private static readonly FilePickerFileType SupportedFiles = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.iOS] = ["com.adobe.pdf", "public.png", "public.jpeg", "public.heic", "public.tiff"],
        [DevicePlatform.MacCatalyst] = ["com.adobe.pdf", "public.png", "public.jpeg", "public.heic", "public.tiff"],
        [DevicePlatform.Android] = ["application/pdf", "image/png", "image/jpeg", "image/heic", "image/tiff"],
        [DevicePlatform.WinUI] = [".pdf", ".png", ".jpg", ".jpeg", ".heic", ".tif", ".tiff"],
    });

    public async Task<SelectedDocument?> PickAsync(CancellationToken cancellationToken = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a PDF, PNG, JPEG, HEIC, or TIFF document",
            FileTypes = SupportedFiles,
        });
        if (file is null)
            return null;

        var mediaType = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".heic" => "image/heic",
            ".tif" or ".tiff" => "image/tiff",
            _ => throw new NotSupportedException("Select a PDF, PNG, JPEG, HEIC, or TIFF file."),
        };
        await using var input = await file.OpenReadAsync();
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > MaximumBytes)
                throw new InvalidOperationException("Documents must be 20 MB or smaller.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return new SelectedDocument(file.FileName, mediaType, output.ToArray());
    }
}
