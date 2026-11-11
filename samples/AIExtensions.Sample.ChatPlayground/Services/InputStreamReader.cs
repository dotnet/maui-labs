namespace AIExtensions.Sample.ChatPlayground;

internal static class InputStreamReader
{
    internal static async Task<byte[]> ReadBytesAsync(
        Stream input, int maximumBytes, string limitMessage, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > maximumBytes)
                throw new InvalidOperationException(limitMessage);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        return output.ToArray();
    }
}
