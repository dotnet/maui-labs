#nullable enable
using System;
using System.IO;
using System.Threading;

namespace CometBaristaNotes.Services;

internal sealed class PhotoByteOwnership : IDisposable
{
    byte[]? _bytes;

    public PhotoByteOwnership(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
    }

    public bool HasBytes => Volatile.Read(ref _bytes) is { Length: > 0 };

    public Stream OpenRead()
    {
        var bytes = Volatile.Read(ref _bytes)
            ?? throw new ObjectDisposedException(nameof(PhotoByteOwnership));
        return new MemoryStream(bytes, writable: false);
    }

    public PhotoByteOwnership? Transfer()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        return bytes is null ? null : new PhotoByteOwnership(bytes);
    }

    public void Dispose() => Interlocked.Exchange(ref _bytes, null);
}
