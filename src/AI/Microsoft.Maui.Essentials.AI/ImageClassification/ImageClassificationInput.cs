// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Essentials.AI;

internal static class ImageClassificationInput
{
	public static ArgumentException CreateTooLargeException(long maximumInputBytes, string parameterName) =>
		new(
			$"The image stream exceeds the configured maximum of {maximumInputBytes} bytes. " +
			$"Reduce the image size or increase {nameof(ImageClassificationOptions)}.{nameof(ImageClassificationOptions.MaximumInputBytes)}.",
			parameterName);
}
