// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Essentials.AI;

internal static class ImageClassificationInput
{
	private const int StreamCopyBufferSize = 81920;

	public static ArgumentException CreateTooLargeException(long maximumInputBytes, string parameterName) =>
		new(
			$"The image stream exceeds the configured maximum of {maximumInputBytes} bytes. " +
			$"Reduce the image size or increase {nameof(ImageClassificationOptions)}.{nameof(ImageClassificationOptions.MaximumInputBytes)}.",
			parameterName);

	public static async Task<byte[]> ReadBytesAsync(
		Stream imageStream,
		long maximumInputBytes,
		CancellationToken cancellationToken,
		string parameterName)
	{
		if (imageStream.CanSeek)
		{
			long length = imageStream.Length;
			long position = imageStream.Position;
			long remainingLength = position >= length ? 0 : length - position;
			if (remainingLength > maximumInputBytes)
			{
				throw CreateTooLargeException(maximumInputBytes, parameterName);
			}
		}

		using var imageBuffer = new MemoryStream();
		var buffer = new byte[StreamCopyBufferSize];
		long totalBytesRead = 0;

		while (true)
		{
			long remainingAllowance = maximumInputBytes - totalBytesRead;
			int requestedBytes = remainingAllowance >= buffer.Length
				? buffer.Length
				: (int)remainingAllowance + 1;
			int bytesRead = await imageStream
				.ReadAsync(buffer, 0, requestedBytes, cancellationToken)
				.ConfigureAwait(false);

			if (bytesRead == 0)
			{
				return imageBuffer.ToArray();
			}

			totalBytesRead += bytesRead;
			if (totalBytesRead > maximumInputBytes)
			{
				throw CreateTooLargeException(maximumInputBytes, parameterName);
			}

			imageBuffer.Write(buffer, 0, bytesRead);
		}
	}
}
