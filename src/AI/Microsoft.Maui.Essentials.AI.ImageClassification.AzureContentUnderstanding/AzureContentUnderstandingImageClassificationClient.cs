// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net.Http.Headers;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.ImageClassification.AzureContentUnderstanding;

/// <summary>
/// Classifies images by invoking a whole-image classifier analyzer in Azure Content Understanding.
/// </summary>
/// <remarks>
/// The supplied credential, injected client, and image streams remain caller-owned. Disposing this adapter
/// does not dispose them. The analyzer must be configured with <c>EnableSegment=false</c> and produce exactly
/// one content-level category.
/// </remarks>
public sealed class AzureContentUnderstandingImageClassificationClient : IImageClassificationClient
{
	private const string ProviderName = "Azure Content Understanding";
	private const string AnalyzerIdPropertyName = "analyzerId";

	private readonly ContentUnderstandingClient _client;
	private readonly string _analyzerId;
	private readonly ImageClassificationClientMetadata _metadata;

	/// <summary>
	/// Initializes a client for an Azure Content Understanding classifier analyzer.
	/// </summary>
	/// <param name="endpoint">The Azure Content Understanding resource endpoint.</param>
	/// <param name="credential">The caller-owned token credential used to authenticate requests.</param>
	/// <param name="options">Provider configuration containing the analyzer identifier.</param>
	public AzureContentUnderstandingImageClassificationClient(
		Uri endpoint,
		TokenCredential credential,
		AzureContentUnderstandingImageClassificationOptions options)
	{
		ArgumentNullException.ThrowIfNull(endpoint);
		ArgumentNullException.ThrowIfNull(credential);
		ArgumentNullException.ThrowIfNull(options);

		_analyzerId = GetAnalyzerId(options);
		_client = new ContentUnderstandingClient(endpoint, credential);
		_metadata = new ImageClassificationClientMetadata(ProviderName, endpoint, _analyzerId);
	}

	internal AzureContentUnderstandingImageClassificationClient(
		ContentUnderstandingClient client,
		AzureContentUnderstandingImageClassificationOptions options,
		ImageClassificationClientMetadata? metadata = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(options);

		_analyzerId = GetAnalyzerId(options);
		_client = client;
		_metadata = metadata ?? new ImageClassificationClientMetadata(ProviderName, defaultModelId: _analyzerId);
	}

	/// <inheritdoc />
	public async Task<ImageClassificationResult> ClassifyImageAsync(
		Stream image,
		string imageMediaType,
		ImageClassificationOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(image);
		ArgumentException.ThrowIfNullOrWhiteSpace(imageMediaType);

		ImageClassificationOptions optionsSnapshot = options?.Clone() ?? new ImageClassificationOptions();

		if (optionsSnapshot.MinimumConfidence is not null)
		{
			throw new NotSupportedException(
				$"{nameof(ImageClassificationOptions.MinimumConfidence)} is not supported because Azure Content Understanding whole-image classifier categories do not include confidence values.");
		}

		if (!image.CanRead)
		{
			throw new ArgumentException("The image stream must be readable.", nameof(image));
		}

		if (!MediaTypeHeaderValue.TryParse(imageMediaType, out MediaTypeHeaderValue? parsedMediaType) ||
			parsedMediaType.MediaType is null ||
			!parsedMediaType.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("The media type must identify image content.", nameof(imageMediaType));
		}

		if (!IsSupportedImageMediaType(parsedMediaType.MediaType))
		{
			throw new NotSupportedException(
				$"Azure Content Understanding does not support the image media type '{parsedMediaType.MediaType}'. Supported media types are image/jpeg, image/png, image/bmp, image/heif, and image/heic.");
		}

		byte[] imageBytes = await ImageClassificationInput.ReadBytesAsync(
			image,
			optionsSnapshot.MaximumInputBytes,
			cancellationToken,
			nameof(image)).ConfigureAwait(false);

		if (imageBytes.Length == 0)
		{
			throw new ArgumentException("The image stream must not be empty.", nameof(image));
		}

		Operation<AnalysisResult> operation = await _client.AnalyzeBinaryAsync(
			WaitUntil.Completed,
			_analyzerId,
			BinaryData.FromBytes(imageBytes),
			contentRange: null,
			contentType: imageMediaType,
			processingLocation: null,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		AnalysisResult analysisResult = operation.Value;
		IList<AnalysisContent>? contents = analysisResult.Contents;
		if (contents is null || contents.Count != 1)
		{
			throw new InvalidOperationException(
				$"Azure Content Understanding analyzer '{_analyzerId}' returned {contents?.Count ?? 0} content results; exactly one is required for whole-image classification.");
		}

		string? category = contents[0].Category;
		if (string.IsNullOrWhiteSpace(category))
		{
			throw new InvalidOperationException(
				$"Azure Content Understanding analyzer '{_analyzerId}' did not return a non-empty content-level category.");
		}

		return new ImageClassificationResult(
			[new ImageClassificationPrediction(category, confidence: null)])
		{
			ModelId = _analyzerId,
			RawRepresentation = analysisResult,
			AdditionalProperties = new AdditionalPropertiesDictionary
			{
				[AnalyzerIdPropertyName] = _analyzerId,
			},
		};
	}

	/// <inheritdoc />
	public object? GetService(Type serviceType, object? serviceKey = null)
	{
		ArgumentNullException.ThrowIfNull(serviceType);

		if (serviceKey is not null)
		{
			return null;
		}

		if (serviceType.IsInstanceOfType(this))
		{
			return this;
		}

		if (serviceType.IsInstanceOfType(_client))
		{
			return _client;
		}

		if (serviceType.IsInstanceOfType(_metadata))
		{
			return _metadata;
		}

		return null;
	}

	/// <summary>
	/// Releases adapter resources without disposing the caller-owned credential, injected client, or image streams.
	/// </summary>
	public void Dispose()
	{
	}

	private static string GetAnalyzerId(AzureContentUnderstandingImageClassificationOptions options)
	{
		string? analyzerId = options.AnalyzerId;
		if (string.IsNullOrWhiteSpace(analyzerId))
		{
			throw new ArgumentException("The analyzer identifier must not be empty.", nameof(options));
		}

		return analyzerId;
	}

	private static bool IsSupportedImageMediaType(string mediaType) =>
		mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
		mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
		mediaType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase) ||
		mediaType.Equals("image/heif", StringComparison.OrdinalIgnoreCase) ||
		mediaType.Equals("image/heic", StringComparison.OrdinalIgnoreCase);
}
