using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// The chat's attachment picker: choose an image, or attach the bundled sample image for a repeatable image test.
/// </summary>
/// <remarks>Registered in the service container, so the chat view's attach button uses it.</remarks>
public sealed class PlaygroundAttachmentPicker(ImageInputService images) : IChatAttachmentPicker
{
    internal const string ChooseImage = "Choose image\u2026";
    internal const string UseSampleImage = "Use sample image";

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataContent>> PickAsync(
        FilePickerFileType? fileTypes,
        long maxBytesPerFile,
        CancellationToken cancellationToken = default)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        var choice = page is null
            ? ChooseImage
            : await page.DisplayActionSheetAsync("Add an image", "Cancel", null, ChooseImage, UseSampleImage);
        cancellationToken.ThrowIfCancellationRequested();

        var image = choice switch
        {
            ChooseImage => await images.PickImageAsync(cancellationToken),
            UseSampleImage => await images.LoadSampleImageAsync(cancellationToken),
            _ => null,
        };

        // The chat view checks the size against its limit and explains a file that is too large.
        return image is null ? [] : [new DataContent(image.Bytes, image.MediaType) { Name = image.FileName }];
    }
}
