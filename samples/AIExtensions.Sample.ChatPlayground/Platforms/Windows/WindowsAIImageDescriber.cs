using Microsoft.Extensions.AI;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.ContentSafety;
using Microsoft.Windows.AI.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

#pragma warning disable CS8305 // The Windows AI image-description API is experimental.

namespace AIExtensions.Sample.ChatPlayground.Platforms.Windows;

internal sealed class WindowsAIImageDescriber : IDisposable
{
    private readonly Lazy<Task<ImageDescriptionGenerator>> _generator = new(CreateGeneratorAsync);

    public async Task<string> DescribeAsync(DataContent image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Data.IsEmpty)
            throw new ArgumentException("Windows image descriptions require inline image bytes.", nameof(image));

        var generator = await _generator.Value.WaitAsync(cancellationToken);

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(image.Data.ToArray());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var buffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await generator.DescribeAsync(
            buffer, ImageDescriptionKind.DetailedDescription, new ContentFilterOptions());

        if (result.Status is not ImageDescriptionResultStatus.Complete)
            throw new InvalidOperationException($"Windows image description failed: {result.Status}");

        return result.Description;
    }

    private static async Task<ImageDescriptionGenerator> CreateGeneratorAsync()
    {
        var state = ImageDescriptionGenerator.GetReadyState();
        if (state is AIFeatureReadyState.NotReady)
        {
            var result = await ImageDescriptionGenerator.EnsureReadyAsync();
            if (result.Status is not AIFeatureReadyResultState.Success)
                throw new NotSupportedException(
                    $"Windows image description could not be made ready: {result.Status}: {result.ErrorDisplayText}",
                    result.ExtendedError);
        }
        else if (state is not AIFeatureReadyState.Ready)
        {
            throw new NotSupportedException($"Windows image description is not available: {state}");
        }

        state = ImageDescriptionGenerator.GetReadyState();
        if (state is not AIFeatureReadyState.Ready)
            throw new NotSupportedException($"Windows image description is not ready after setup: {state}");

        return await ImageDescriptionGenerator.CreateAsync();
    }

    public void Dispose()
    {
        if (!_generator.IsValueCreated)
            return;

        var task = _generator.Value;
        if (task.IsCompletedSuccessfully)
            task.Result.Dispose();
        else
            task.ContinueWith(
                completed => { if (completed.IsCompletedSuccessfully) completed.Result.Dispose(); },
                TaskContinuationOptions.ExecuteSynchronously);
    }
}
