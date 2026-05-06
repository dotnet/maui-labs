using AppKit;
using Foundation;
using Microsoft.Maui.Storage;
using UniformTypeIdentifiers;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class FilePickerImplementation : IFilePicker
{
    public Task<FileResult?> PickAsync(PickOptions? options = null)
    {
        var tcs = new TaskCompletionSource<FileResult?>();

        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.CanChooseFiles = true;
            panel.CanChooseDirectories = false;
            panel.AllowsMultipleSelection = false;

            if (options?.PickerTitle is not null)
                panel.Title = options.PickerTitle;

            ConfigureAllowedTypes(panel, options);

            var result = panel.RunModal();
            if (result == 1 && panel.Url?.Path is not null)
                tcs.TrySetResult(new FileResult(panel.Url.Path));
            else
                tcs.TrySetResult(null);
        });

        return tcs.Task;
    }

    public Task<IEnumerable<FileResult?>> PickMultipleAsync(PickOptions? options = null)
    {
        var tcs = new TaskCompletionSource<IEnumerable<FileResult?>>();

        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.CanChooseFiles = true;
            panel.CanChooseDirectories = false;
            panel.AllowsMultipleSelection = true;

            if (options?.PickerTitle is not null)
                panel.Title = options.PickerTitle;

            ConfigureAllowedTypes(panel, options);

            var result = panel.RunModal();
            if (result == 1 && panel.Urls?.Length > 0)
            {
                var results = panel.Urls
                    .Where(u => u.Path is not null)
                    .Select(u => (FileResult?)new FileResult(u.Path!))
                    .ToList();
                tcs.TrySetResult(results);
            }
            else
            {
                tcs.TrySetResult(Enumerable.Empty<FileResult?>());
            }
        });

        return tcs.Task;
    }

    static void ConfigureAllowedTypes(NSOpenPanel panel, PickOptions? options)
    {
        if (options?.FileTypes?.Value is null)
        {
            panel.AllowedContentTypes = Array.Empty<UTType>();
            return;
        }

        var types = new List<UTType>();
        foreach (var type in options.FileTypes.Value)
        {
            var utType = UTType.CreateFromIdentifier(type);
            if (utType is not null)
                types.Add(utType);
        }

        if (types.Count > 0)
            panel.AllowedContentTypes = types.ToArray();
    }
}
