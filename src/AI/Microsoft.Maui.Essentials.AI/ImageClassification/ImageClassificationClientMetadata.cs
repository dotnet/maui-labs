// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Extensions.AI;

/// <summary>Provides metadata about an <see cref="IImageClassificationClient"/>.</summary>
public sealed class ImageClassificationClientMetadata
{
	/// <summary>Initializes a new instance of the <see cref="ImageClassificationClientMetadata"/> class.</summary>
	/// <param name="providerName">The provider name, if applicable.</param>
	/// <param name="providerUri">The URI for accessing the provider, if applicable.</param>
	/// <param name="defaultModelId">The model identifier used by default, if applicable.</param>
	public ImageClassificationClientMetadata(
		string? providerName = null,
		Uri? providerUri = null,
		string? defaultModelId = null)
	{
		ProviderName = providerName;
		ProviderUri = providerUri;
		DefaultModelId = defaultModelId;
	}

	/// <summary>Gets the provider name.</summary>
	public string? ProviderName { get; }

	/// <summary>Gets the URI for accessing the provider.</summary>
	public Uri? ProviderUri { get; }

	/// <summary>Gets the model identifier used by default.</summary>
	/// <remarks>
	/// This value can be <see langword="null"/> when the model is unknown or when the client can select among
	/// multiple models.
	/// </remarks>
	public string? DefaultModelId { get; }
}
