using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CoreImage;
using Foundation;
using ImageIO;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Extracts structured document content using Apple Vision's RecognizeDocumentsRequest.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionRecognizeDocumentsClient : IDocumentExtractionClient
{
	private static readonly HashSet<string> s_supportedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/jpeg",
		"image/jpg",
		"image/png",
		"image/heic",
		"image/tiff",
	};

	private DocumentExtractionClientMetadata? _metadata;
	private AppleVisionDocumentCapabilities? _capabilities;

	/// <inheritdoc />
	public async Task<DocumentExtractionResult> ExtractAsync(
		Stream document,
		string mediaType,
		DocumentExtractionOptions? options = null,
		CancellationToken cancellationToken = default) =>
		await ExtractPagesAsync(document, mediaType, options, cancellationToken)
			.ToDocumentExtractionResultAsync(cancellationToken)
			.ConfigureAwait(false);

	/// <inheritdoc />
	public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
		Stream document,
		string mediaType,
		DocumentExtractionOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (!document.CanRead)
		{
			throw new ArgumentException("The document stream must be readable.", nameof(document));
		}
		if (!s_supportedMediaTypes.Contains(mediaType))
		{
			throw new NotSupportedException(
				$"Apple Vision document recognition supports image streams only. Media type '{mediaType}' is not supported.");
		}
		if (options?.ModelId is not null &&
			!string.Equals(options.ModelId, "recognize-documents", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException(
				$"Model ID '{options.ModelId}' is not supported. Use 'recognize-documents'.");
		}

		var bytes = await ReadAllBytesAsync(document, cancellationToken).ConfigureAwait(false);
		using var imageData = NSData.FromArray(bytes);
		var imageInfo = GetImageInfo(imageData);
		using var nativeClient = new VisionRecognizeDocumentsClientNative();
		using var nativeOptions = ToNative(options);

		var tokenSync = new object();
		CancellationTokenNative? nativeToken = null;
		var completion = new TaskCompletionSource<VisionDocumentResultNative>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		VisionDocumentResultNative nativeResult;
		CancellationTokenRegistration registration = default;
		try
		{
			registration = cancellationToken.Register(() =>
			{
				lock (tokenSync)
				{
					nativeToken?.Cancel();
				}
			});

			var createdToken = nativeClient.RecognizeDocument(
				imageData,
				imageInfo.Orientation,
				nativeOptions,
				(result, error) =>
				{
					if (error is not null)
					{
						if (cancellationToken.IsCancellationRequested ||
							(error.Domain == nameof(VisionRecognizeDocumentsClientNative) &&
								error.Code == (nint)VisionDocumentClientErrorNative.Cancelled))
						{
							completion.TrySetCanceled(cancellationToken);
						}
						else
						{
							completion.TrySetException(new NSErrorException(error));
						}
					}
					else if (result is null)
					{
						completion.TrySetException(
							new InvalidOperationException("Apple Vision returned no document result."));
					}
					else
					{
						completion.TrySetResult(result);
					}
				});

			lock (tokenSync)
			{
				nativeToken = createdToken;
				if (cancellationToken.IsCancellationRequested)
				{
					nativeToken?.Cancel();
				}
			}

			nativeResult = await completion.Task.ConfigureAwait(false);
		}
		finally
		{
			await registration.DisposeAsync().ConfigureAwait(false);
			lock (tokenSync)
			{
				nativeToken?.Dispose();
				nativeToken = null;
			}
		}

		var revision = GetOption(options, AppleVisionDocumentOptionsExtensions.RevisionKey, 1);
		var page = AppleVisionDocumentMapper.ToPage(
			nativeResult,
			pageNumber: 1,
			imageInfo.Width,
			imageInfo.Height,
			revision);
		yield return new DocumentExtractionPageResult(page)
		{
			PagesProcessed = 1,
			TotalPages = 1,
			AdditionalProperties = new AdditionalPropertiesDictionary
			{
				["apple.vision.request"] = "recognize-documents",
				["apple.vision.revision"] = revision,
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
		if (serviceType == typeof(DocumentExtractionClientMetadata))
		{
			return _metadata ??= new DocumentExtractionClientMetadata(
				providerName: "apple.vision",
				defaultModelId: "recognize-documents");
		}
		if (serviceType == typeof(AppleVisionDocumentCapabilities))
		{
			return _capabilities ??= CreateCapabilities();
		}
		return serviceType.IsInstanceOfType(this) ? this : null;
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}

	private static AppleVisionDocumentCapabilities CreateCapabilities()
	{
		using var native = VisionRecognizeDocumentsClientNative.GetCapabilities();
		return new AppleVisionDocumentCapabilities(native);
	}

	private static async Task<byte[]> ReadAllBytesAsync(
		Stream stream,
		CancellationToken cancellationToken)
	{
		using var memory = new MemoryStream();
		await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
		return memory.ToArray();
	}

	private static ImageInfo GetImageInfo(NSData imageData)
	{
		using var source = CGImageSource.FromData(imageData)
			?? throw new ArgumentException("The stream does not contain a supported image.", nameof(imageData));
		var properties = source.GetProperties(0);
		var orientation = properties.Orientation is { } value
			? (nint)(int)value
			: (nint)(int)CIImageOrientation.TopLeft;
		int? width = properties.PixelWidth is { } pixelWidth ? checked((int)pixelWidth) : null;
		int? height = properties.PixelHeight is { } pixelHeight ? checked((int)pixelHeight) : null;
		if (orientation is >= 5 and <= 8)
		{
			(width, height) = (height, width);
		}
		return new ImageInfo(
			orientation,
			width,
			height);
	}

	private static VisionDocumentOptionsNative? ToNative(DocumentExtractionOptions? options)
	{
		if (options?.AdditionalProperties is not { } properties)
		{
			return null;
		}

		return new VisionDocumentOptionsNative
		{
			RecognitionLanguages = GetOption<string[]>(properties, AppleVisionDocumentOptionsExtensions.RecognitionLanguagesKey),
			CustomWords = GetOption<string[]>(properties, AppleVisionDocumentOptionsExtensions.CustomWordsKey),
			UseLanguageCorrection = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptionsExtensions.UseLanguageCorrectionKey)),
			AutomaticallyDetectLanguage = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptionsExtensions.AutomaticallyDetectLanguageKey)),
			MaximumCandidateCount = ToNSNumber(GetOption<int?>(properties, AppleVisionDocumentOptionsExtensions.MaximumCandidateCountKey)),
			MinimumTextHeightFraction = ToNSNumber(GetOption<float?>(properties, AppleVisionDocumentOptionsExtensions.MinimumTextHeightFractionKey)),
			BarcodeDetectionEnabled = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptionsExtensions.BarcodeDetectionEnabledKey)),
			BarcodeSymbologies = GetOption<string[]>(properties, AppleVisionDocumentOptionsExtensions.BarcodeSymbologiesKey),
			CoalesceCompositeSymbologies = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptionsExtensions.CoalesceCompositeSymbologiesKey)),
			RegionOfInterest = GetOption<float[]>(properties, AppleVisionDocumentOptionsExtensions.RegionOfInterestKey)?
				.Select(static value => NSNumber.FromFloat(value))
				.ToArray(),
			Revision = ToNSNumber(GetOption<int?>(properties, AppleVisionDocumentOptionsExtensions.RevisionKey)),
		};
	}

	private static T? GetOption<T>(
		AdditionalPropertiesDictionary properties,
		string key) =>
		properties.TryGetValue(key, out var value) && value is T typed ? typed : default;

	private static T GetOption<T>(
		DocumentExtractionOptions? options,
		string key,
		T defaultValue) =>
		options?.AdditionalProperties is { } properties &&
		properties.TryGetValue(key, out var value) &&
		value is T typed
			? typed
			: defaultValue;

	private static NSNumber? ToNSNumber(bool? value) =>
		value is { } actual ? NSNumber.FromBoolean(actual) : null;

	private static NSNumber? ToNSNumber(int? value) =>
		value is { } actual ? NSNumber.FromInt32(actual) : null;

	private static NSNumber? ToNSNumber(float? value) =>
		value is { } actual ? NSNumber.FromFloat(actual) : null;

	private readonly record struct ImageInfo(
		nint Orientation,
		int? Width,
		int? Height);
}
