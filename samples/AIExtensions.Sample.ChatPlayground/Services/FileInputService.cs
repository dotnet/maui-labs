namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Loads caller-selected file types or packaged samples into a shared input value.</summary>
public sealed class FileInputService
{
    public static FilePickerFileType DocumentFiles { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.iOS] = ["com.adobe.pdf", "public.png", "public.jpeg", "public.heic", "public.tiff"],
        [DevicePlatform.MacCatalyst] = ["com.adobe.pdf", "public.png", "public.jpeg", "public.heic", "public.tiff"],
        [DevicePlatform.Android] = ["application/pdf", "image/png", "image/jpeg", "image/heic", "image/tiff"],
        [DevicePlatform.WinUI] = [".pdf", ".png", ".jpg", ".jpeg", ".heic", ".tif", ".tiff"],
    });

    public static FilePickerFileType ImageFiles { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.iOS] = ["public.png", "public.jpeg", "com.compuserve.gif", "org.webmproject.webp"],
        [DevicePlatform.MacCatalyst] = ["public.png", "public.jpeg", "com.compuserve.gif", "org.webmproject.webp"],
        [DevicePlatform.Android] = ["image/png", "image/jpeg", "image/gif", "image/webp"],
        [DevicePlatform.WinUI] = [".png", ".jpg", ".jpeg", ".gif", ".webp"],
    });

    public async Task<InputFile?> PickAsync(PickOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        FileResult? file;
        try
        {
            file = await FilePicker.Default.PickAsync(options);
        }
        catch (PermissionException exception)
        {
            throw new InvalidOperationException(
                "File access was denied. Allow file access in the platform settings and try again.", exception);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (file is null)
            return null;

        await using var input = await file.OpenReadAsync();
        return await InputFile.ReadAsync(input, file.FileName, file.ContentType, cancellationToken);
    }

    public async Task<InputFile> LoadSampleAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var input = await FileSystem.OpenAppPackageFileAsync(fileName);
        return await InputFile.ReadAsync(input, fileName, cancellationToken: cancellationToken);
    }
}
