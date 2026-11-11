// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Downloads the web images that Markdown and rich text show when <see cref="AIChatView.AllowRemoteImages"/> is on,
/// within limits, and keeps recent ones so a view built again shows them at once.
/// </summary>
/// <remarks>
/// A download stops at <see cref="MaxBytes"/> or after the timeout (<see cref="DefaultTimeout"/>). An image is only
/// shown when its header gives its size (PNG, JPEG, GIF, BMP, WebP) and it has at most <see cref="MaxPixels"/> pixels:
/// decoding takes memory in proportion to the pixels, and a small file can claim a huge image.
/// </remarks>
internal sealed class RemoteImageLoader
{
    /// <summary>The most bytes downloaded for one image.</summary>
    internal const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>The most pixels an image has and is still decoded (16 megapixels).</summary>
    internal const long MaxPixels = 16L * 1024 * 1024;

    /// <summary>How long a download may take.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    // Recent images stay in memory up to this many bytes in all.
    private const long KeptBytes = 32L * 1024 * 1024;

    // No cookies: an image request does not identify the user to the site that serves it.
    private static readonly Lazy<HttpClient> s_client = new(static () => new HttpClient(new HttpClientHandler { UseCookies = false }));

    private readonly Func<Uri, CancellationToken, Task<byte[]?>> _download;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<RemoteImage?>> _images = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, int Bytes)> _kept = new();
    private long _keptBytes;

    /// <param name="download">Downloads an image, or returns <see langword="null"/> when it cannot.</param>
    /// <param name="timeout">How long a download may take; <see cref="DefaultTimeout"/> when not given.</param>
    internal RemoteImageLoader(Func<Uri, CancellationToken, Task<byte[]?>> download, TimeSpan? timeout = null)
    {
        _download = download;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>The loader of every view that has no other, which downloads with one shared <see cref="HttpClient"/>.</summary>
    internal static RemoteImageLoader Shared { get; } =
        new(static (uri, cancellationToken) => DownloadAsync(s_client.Value, uri, MaxBytes, cancellationToken));

    /// <summary>Gets the image at <paramref name="uri"/>, or <see langword="null"/> when it cannot or may not be shown.</summary>
    /// <remarks>Each address is downloaded once while its image is kept; a failed one is tried again next time.</remarks>
    internal Task<RemoteImage?> LoadAsync(Uri uri)
    {
        var key = uri.AbsoluteUri;
        Task<RemoteImage?> load;
        lock (_gate)
        {
            if (_images.TryGetValue(key, out var known))
                return known;

            load = LoadCoreAsync(uri);
            _images[key] = load;
        }

        _ = load.ContinueWith(
            loaded => Keep(key, loaded.Result),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return load;
    }

    /// <summary>Downloads <paramref name="uri"/>, or returns <see langword="null"/> when it fails or exceeds <paramref name="maxBytes"/>.</summary>
    internal static async Task<byte[]?> DownloadAsync(HttpClient client, Uri uri, int maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
                return null;

            // The length header is optional, so the limit also holds while the bytes arrive.
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + read > maxBytes)
                    return null;
                bytes.Write(buffer, 0, read);
            }

            return bytes.ToArray();
        }
        catch (Exception)
        {
            // Offline, refused, timed out, or cut off: there is no image, and the chip stays.
            return null;
        }
    }

    /// <summary>Gets the image in <paramref name="bytes"/> when it is safe to decode, or <see langword="null"/>.</summary>
    internal static RemoteImage? Accept(byte[] bytes) =>
        ImageHeader.ReadSize(bytes) is { } size && size.Width * size.Height <= MaxPixels
            ? new RemoteImage(bytes, size)
            : null;

    private async Task<RemoteImage?> LoadCoreAsync(Uri uri)
    {
        try
        {
            using var timeout = new CancellationTokenSource(_timeout);
            var bytes = await _download(uri, timeout.Token).ConfigureAwait(false);
            return bytes is null ? null : Accept(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Keep(string key, RemoteImage? image)
    {
        lock (_gate)
        {
            if (image is null)
            {
                _images.Remove(key);
                return;
            }

            _kept.Enqueue((key, image.Bytes.Length));
            _keptBytes += image.Bytes.Length;
            while (_keptBytes > KeptBytes && _kept.Count > 1)
            {
                var (oldest, bytes) = _kept.Dequeue();
                _images.Remove(oldest);
                _keptBytes -= bytes;
            }
        }
    }
}

/// <summary>A downloaded image and its size in pixels, read from its header.</summary>
internal sealed record RemoteImage(byte[] Bytes, Size Size);
