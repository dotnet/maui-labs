using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.Json;
using CoreGraphics;
using CoreImage;
using Foundation;
using ImageIO;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using PdfKit;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Extracts structured image and PDF content using Apple Vision's RecognizeDocumentsRequest.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionDocumentExtractionClient : IDocumentExtractionClient
{
	private const double PdfRenderDpi = 200;
	private const int PdfMaximumPixelDimension = 4096;

	private static readonly HashSet<string> s_supportedImageMediaTypes = new(StringComparer.OrdinalIgnoreCase)
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
		if (options?.ModelId is not null &&
			!string.Equals(options.ModelId, "recognize-documents", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException(
				$"Model ID '{options.ModelId}' is not supported. Use 'recognize-documents'.");
		}

		var isPdf = string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);
		if (!isPdf && !s_supportedImageMediaTypes.Contains(mediaType))
		{
			throw new NotSupportedException(
				$"Apple Vision document extraction supports PNG, JPEG, HEIC, TIFF, and PDF streams. Media type '{mediaType}' is not supported.");
		}

		var bytes = await ReadAllBytesAsync(document, cancellationToken).ConfigureAwait(false);
		if (isPdf)
		{
			await foreach (var update in ExtractPdfPagesAsync(bytes, options, cancellationToken)
				.ConfigureAwait(false))
			{
				yield return update;
			}
			yield break;
		}

		var page = await RecognizeImageAsync(bytes, pageNumber: 1, options, cancellationToken)
			.ConfigureAwait(false);
		yield return new DocumentExtractionPageResult(page)
		{
			PagesProcessed = 1,
			TotalPages = 1,
			AdditionalProperties = CreateRequestProperties(options),
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

	private static async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPdfPagesAsync(
		byte[] bytes,
		DocumentExtractionOptions? options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var data = NSData.FromArray(bytes);
		using var pdf = new PdfDocument(data);
		if (pdf.IsLocked)
		{
			throw new NotSupportedException("Password-protected PDF documents must be unlocked before extraction.");
		}
		if (!pdf.AllowsCopying)
		{
			throw new UnauthorizedAccessException("The PDF document does not allow content extraction.");
		}

		var pageCount = checked((int)pdf.PageCount);
		for (var index = 0; index < pageCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using var pdfPage = pdf.GetPage(index)
				?? throw new InvalidOperationException($"PDFKit could not load page {index + 1}.");
			pdfPage.DisplaysAnnotations = true;
			var bounds = pdfPage.GetBoundsForBox(PdfDisplayBox.Crop);
			var rendered = RenderPdfPage(pdfPage, bounds);
			var pageNumber = index + 1;
			var page = await RecognizeImageAsync(
				rendered.Data,
				pageNumber,
				options,
				cancellationToken).ConfigureAwait(false);
			var properties = page.AdditionalProperties ??= new AdditionalPropertiesDictionary();
			properties["apple.pdf.pageLabel"] = pdfPage.Label;
			properties["apple.pdf.rotation"] = checked((int)pdfPage.Rotation);
			properties["apple.pdf.displayBox"] = PdfDisplayBox.Crop.ToString();
			properties["apple.pdf.renderDpi"] = PdfRenderDpi;
			properties["apple.pdf.effectiveRenderDpi"] = rendered.EffectiveDpi;
			properties["apple.pdf.widthPoints"] = rendered.WidthPoints;
			properties["apple.pdf.heightPoints"] = rendered.HeightPoints;

			var updateProperties = CreateRequestProperties(options);
			updateProperties["apple.pdf.pageNumber"] = pageNumber;
			updateProperties["apple.pdf.totalPages"] = pageCount;
			updateProperties["apple.pdf.renderDpi"] = PdfRenderDpi;
			updateProperties["apple.pdf.effectiveRenderDpi"] = rendered.EffectiveDpi;
			yield return new DocumentExtractionPageResult(page)
			{
				PagesProcessed = pageNumber,
				TotalPages = pageCount,
				AdditionalProperties = updateProperties,
			};
		}
	}

	private static async Task<DocumentPage> RecognizeImageAsync(
		byte[] bytes,
		int pageNumber,
		DocumentExtractionOptions? options,
		CancellationToken cancellationToken)
	{
		using var imageData = NSData.FromArray(bytes);
		var imageInfo = GetImageInfo(imageData);
		using var nativeClient = new VisionRecognizeDocumentsClientNative();
		using var nativeOptions = ToNative(options);

		var tokenSync = new object();
		CancellationTokenNative? nativeToken = null;
		var completion = new TaskCompletionSource<VisionDocumentResultNative>(
			TaskCreationOptions.RunContinuationsAsynchronously);
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

			using var nativeResult = await completion.Task.ConfigureAwait(false);
			using var json = JsonDocument.Parse(nativeResult.JsonData.ToArray());
			var revision = GetOption(options, AppleVisionDocumentOptions.RevisionKey, 1);
			return AppleVisionDocumentMapper.ToPage(
				json.RootElement.Clone(),
				pageNumber,
				imageInfo.Width,
				imageInfo.Height,
				revision);
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
	}

	private static AppleVisionDocumentCapabilities CreateCapabilities()
	{
		using var native = VisionRecognizeDocumentsClientNative.GetCapabilities();
		return new AppleVisionDocumentCapabilities(native);
	}

	private static AdditionalPropertiesDictionary CreateRequestProperties(
		DocumentExtractionOptions? options) =>
		new()
		{
			["apple.vision.request"] = "recognize-documents",
			["apple.vision.revision"] = GetOption(options, AppleVisionDocumentOptions.RevisionKey, 1),
		};

	private static RenderedPdfPage RenderPdfPage(PdfPage page, CGRect bounds)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			throw new InvalidOperationException("The PDF page has invalid render bounds.");
		}

		var rotation = ((checked((int)page.Rotation) % 360) + 360) % 360;
		var swapsDimensions = rotation is 90 or 270;
		var widthPoints = swapsDimensions ? bounds.Height : bounds.Width;
		var heightPoints = swapsDimensions ? bounds.Width : bounds.Height;
		var scale = PdfRenderDpi / 72d;
		var width = Math.Max(1, (int)Math.Ceiling(widthPoints * scale));
		var height = Math.Max(1, (int)Math.Ceiling(heightPoints * scale));
		var largest = Math.Max(width, height);
		if (largest > PdfMaximumPixelDimension)
		{
			var clampScale = (double)PdfMaximumPixelDimension / largest;
			width = Math.Max(1, (int)Math.Floor(width * clampScale));
			height = Math.Max(1, (int)Math.Floor(height * clampScale));
		}

		using var colorSpace = CGColorSpace.CreateDeviceRGB();
		using var context = new CGBitmapContext(
			data: null,
			width,
			height,
			bitsPerComponent: 8,
			bytesPerRow: width * 4,
			colorSpace,
			CGBitmapFlags.PremultipliedLast);
		context.SetFillColor(1, 1, 1, 1);
		context.FillRect(new CGRect(0, 0, width, height));
		context.SaveState();
		context.ScaleCTM(width / widthPoints, height / heightPoints);
		page.Draw(PdfDisplayBox.Crop, context);
		context.RestoreState();

		using var image = context.ToImage()
			?? throw new InvalidOperationException("PDFKit could not render the page image.");
		using var output = new NSMutableData();
		using var destination = CGImageDestination.Create(output, "public.png", 1)
			?? throw new InvalidOperationException("ImageIO could not create a PNG destination.");
		destination.AddImage(image);
		if (!destination.Close())
		{
			throw new InvalidOperationException("ImageIO could not encode the rendered PDF page.");
		}
		var effectiveDpi = Math.Min(
			PdfRenderDpi,
			Math.Min(
				width / widthPoints * 72d,
				height / heightPoints * 72d));
		return new RenderedPdfPage(
			output.ToArray(),
			widthPoints,
			heightPoints,
			effectiveDpi);
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
		return new ImageInfo(orientation, width, height);
	}

	private static VisionDocumentOptionsNative? ToNative(DocumentExtractionOptions? options)
	{
		if (options?.AdditionalProperties is not { } properties)
		{
			return null;
		}

		return new VisionDocumentOptionsNative
		{
			RecognitionLanguages = GetOption<string[]>(properties, AppleVisionDocumentOptions.RecognitionLanguagesKey),
			CustomWords = GetOption<string[]>(properties, AppleVisionDocumentOptions.CustomWordsKey),
			UseLanguageCorrection = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptions.UseLanguageCorrectionKey)),
			AutomaticallyDetectLanguage = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptions.AutomaticallyDetectLanguageKey)),
			MaximumCandidateCount = ToNSNumber(GetOption<int?>(properties, AppleVisionDocumentOptions.MaximumCandidateCountKey)),
			MinimumTextHeightFraction = ToNSNumber(GetOption<float?>(properties, AppleVisionDocumentOptions.MinimumTextHeightFractionKey)),
			BarcodeDetectionEnabled = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptions.BarcodeDetectionEnabledKey)),
			BarcodeSymbologies = GetOption<string[]>(properties, AppleVisionDocumentOptions.BarcodeSymbologiesKey),
			CoalesceCompositeSymbologies = ToNSNumber(GetOption<bool?>(properties, AppleVisionDocumentOptions.CoalesceCompositeSymbologiesKey)),
			RegionOfInterest = GetOption<float[]>(properties, AppleVisionDocumentOptions.RegionOfInterestKey)?
				.Select(static value => NSNumber.FromFloat(value))
				.ToArray(),
			Revision = ToNSNumber(GetOption<int?>(properties, AppleVisionDocumentOptions.RevisionKey)),
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

	private readonly record struct RenderedPdfPage(
		byte[] Data,
		double WidthPoints,
		double HeightPoints,
		double EffectiveDpi);
}
