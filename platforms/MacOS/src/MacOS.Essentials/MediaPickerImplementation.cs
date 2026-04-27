using AppKit;
using Foundation;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;
using UniformTypeIdentifiers;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

#pragma warning disable CS0618 // Obsolete members required by interface
class MediaPickerImplementation : IMediaPicker
{
    public bool IsCaptureSupported => false;

    public async Task<FileResult?> PickPhotoAsync(MediaPickerOptions? options = null)
    {
        var result = await PickFileWithTypes(GetImageTypes());
        return result;
    }

    public async Task<List<FileResult>> PickPhotosAsync(MediaPickerOptions? options = null)
    {
        var results = await PickFilesWithTypes(GetImageTypes(), options?.SelectionLimit ?? 0);
        return results;
    }

    public Task<FileResult?> CapturePhotoAsync(MediaPickerOptions? options = null) =>
        throw new NotSupportedException("Camera capture is not supported on macOS");

    public async Task<FileResult?> PickVideoAsync(MediaPickerOptions? options = null)
    {
        var result = await PickFileWithTypes(GetVideoTypes());
        return result;
    }

    public async Task<List<FileResult>> PickVideosAsync(MediaPickerOptions? options = null)
    {
        var results = await PickFilesWithTypes(GetVideoTypes(), options?.SelectionLimit ?? 0);
        return results;
    }

    public Task<FileResult?> CaptureVideoAsync(MediaPickerOptions? options = null) =>
        throw new NotSupportedException("Video capture is not supported on macOS");

    static Task<FileResult?> PickFileWithTypes(UTType[] allowedTypes)
    {
        var tcs = new TaskCompletionSource<FileResult?>();

        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.CanChooseFiles = true;
            panel.CanChooseDirectories = false;
            panel.AllowsMultipleSelection = false;
            panel.AllowedContentTypes = allowedTypes;

            var result = panel.RunModal();
            if (result == 1 && panel.Url?.Path is not null)
                tcs.TrySetResult(new FileResult(panel.Url.Path));
            else
                tcs.TrySetResult(null);
        });

        return tcs.Task;
    }

    static Task<List<FileResult>> PickFilesWithTypes(UTType[] allowedTypes, int selectionLimit)
    {
        var tcs = new TaskCompletionSource<List<FileResult>>();

        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.CanChooseFiles = true;
            panel.CanChooseDirectories = false;
            panel.AllowsMultipleSelection = selectionLimit != 1;
            panel.AllowedContentTypes = allowedTypes;

            var result = panel.RunModal();
            if (result == 1 && panel.Urls?.Length > 0)
            {
                var results = panel.Urls
                    .Where(u => u.Path is not null)
                    .Select(u => new FileResult(u.Path!))
                    .ToList();
                tcs.TrySetResult(results);
            }
            else
            {
                tcs.TrySetResult(new List<FileResult>());
            }
        });

        return tcs.Task;
    }

    static UTType[] GetImageTypes() => new[]
    {
        UTType.CreateFromIdentifier("public.image")!
    };

    static UTType[] GetVideoTypes() => new[]
    {
        UTType.CreateFromIdentifier("public.movie")!
    };
}
#pragma warning restore CS0618
