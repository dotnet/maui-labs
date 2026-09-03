// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Provides extension methods for <see cref="IImageClassificationClient"/>.</summary>
public static class ImageClassificationClientExtensions
{
	/// <summary>Asks the client for an object of type <typeparamref name="TService"/>.</summary>
	/// <typeparam name="TService">The type of object to retrieve.</typeparam>
	/// <param name="client">The image classification client.</param>
	/// <param name="serviceKey">An optional key used to identify the target service.</param>
	/// <returns>The found object, otherwise <see langword="null"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
	public static TService? GetService<TService>(
		this IImageClassificationClient client,
		object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(client);

		return client.GetService(typeof(TService), serviceKey) is TService service ? service : default;
	}

	/// <summary>Classifies one in-memory image.</summary>
	/// <param name="client">The image classification client.</param>
	/// <param name="image">The encoded image and its media type.</param>
	/// <param name="options">Options that constrain the classification response.</param>
	/// <param name="cancellationToken">
	/// The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is
	/// <see cref="CancellationToken.None"/>.
	/// </param>
	/// <returns>The classification result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="client"/> or <paramref name="image"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="image"/> is empty or its media type is not an image media type.
	/// </exception>
	/// <remarks>
	/// This overload snapshots the image bytes before invoking the client. The caller retains ownership of
	/// <paramref name="image"/>; the temporary stream is disposed after the operation completes.
	/// </remarks>
	public static async Task<ImageClassificationResult> ClassifyImageAsync(
		this IImageClassificationClient client,
		DataContent image,
		ImageClassificationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(image);

		ImageClassificationOptions optionsSnapshot = options?.Clone() ?? new();

		if (!image.HasTopLevelMediaType("image"))
		{
			throw new ArgumentException("The content media type must be an image media type.", nameof(image));
		}

		if (image.Data.IsEmpty)
		{
			throw new ArgumentException("The image content must not be empty.", nameof(image));
		}

		if (image.Data.Length > optionsSnapshot.MaximumInputBytes)
		{
			throw ImageClassificationInput.CreateTooLargeException(
				optionsSnapshot.MaximumInputBytes,
				nameof(image));
		}

		using var imageStream = new MemoryStream(image.Data.ToArray(), writable: false);
		return await client
			.ClassifyImageAsync(imageStream, image.MediaType, optionsSnapshot, cancellationToken)
			.ConfigureAwait(false);
	}
}
