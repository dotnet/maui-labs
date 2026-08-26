// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

/// <summary>Represents a client that classifies encoded images.</summary>
/// <remarks>
/// <para>
/// Unless otherwise specified, all members are thread-safe for concurrent use. Implementations are expected
/// to support multiple concurrent requests.
/// </para>
/// <para>
/// The input stream contains one encoded image, beginning at its current position. The caller retains ownership:
/// implementations must not dispose the stream or retain it after the returned task completes.
/// </para>
/// <para>
/// The media type supplied to <see cref="ClassifyAsync"/> must identify an image media type, such as <c>image/jpeg</c> or
/// <c>image/png</c>. Implementations should throw <see cref="ArgumentException"/> for malformed input and
/// <see cref="NotSupportedException"/> when the media type is valid but unsupported.
/// </para>
/// </remarks>
public interface IImageClassificationClient : IDisposable
{
	/// <summary>Classifies an encoded image.</summary>
	/// <param name="imageStream">A readable stream containing one encoded image.</param>
	/// <param name="imageMediaType">The image media type (also known as MIME type).</param>
	/// <param name="options">Options that constrain the classification response.</param>
	/// <param name="cancellationToken">
	/// The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is
	/// <see cref="CancellationToken.None"/>.
	/// </param>
	/// <returns>The classification result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="imageStream"/> or <paramref name="imageMediaType"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="imageStream"/> is unreadable or empty, or <paramref name="imageMediaType"/> is not an
	/// image media type.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// The image media type is valid but unsupported by the implementation.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	Task<ImageClassificationResult> ClassifyAsync(
		Stream imageStream,
		string imageMediaType,
		ImageClassificationOptions? options = null,
		CancellationToken cancellationToken = default);

	/// <summary>Asks the client for an object of the specified type.</summary>
	/// <param name="serviceType">The type of object being requested.</param>
	/// <param name="serviceKey">An optional key used to identify the target service.</param>
	/// <returns>The found object, otherwise <see langword="null"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// This method enables strongly typed access to services provided or wrapped by the client, including the
	/// client itself. Provider-specific capabilities belong behind this escape hatch rather than in the common
	/// classification options.
	/// </remarks>
	object? GetService(Type serviceType, object? serviceKey = null);
}
