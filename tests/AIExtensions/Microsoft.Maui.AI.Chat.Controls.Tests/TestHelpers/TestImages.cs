namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>Web images for tests, served without a network.</summary>
internal static class TestImages
{
    /// <summary>The start of a PNG file: enough for its size to be read from the header.</summary>
    public static byte[] Png(int width, int height) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, .. Be32(width), .. Be32(height), 8, 6, 0, 0, 0];

    /// <summary>A loader that answers every address with <paramref name="bytes"/> (<see langword="null"/>: the download failed).</summary>
    public static RemoteImageLoader Loader(byte[]? bytes, List<Uri>? requests = null) =>
        Loader(_ => bytes, requests);

    /// <summary>A loader that answers each address with what <paramref name="serve"/> returns for it, recording the requests.</summary>
    public static RemoteImageLoader Loader(Func<Uri, byte[]?> serve, List<Uri>? requests = null) =>
        new((uri, _) =>
        {
            requests?.Add(uri);
            return Task.FromResult(serve(uri));
        });

    /// <summary>A loader whose downloads never finish, as on a slow network.</summary>
    public static RemoteImageLoader Pending(List<Uri>? requests = null) =>
        new((uri, _) =>
        {
            requests?.Add(uri);
            return new TaskCompletionSource<byte[]?>().Task;
        });

    private static byte[] Be32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
