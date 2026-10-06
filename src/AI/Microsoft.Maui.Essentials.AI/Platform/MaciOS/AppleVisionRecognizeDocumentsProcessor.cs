using System.Runtime.CompilerServices;
using System.Text.Json;
using CoreGraphics;
using CoreImage;
using Foundation;
using ImageIO;
using PdfKit;

namespace Microsoft.Maui.Essentials.AI;

internal sealed class AppleVisionRecognizeDocumentsProcessor
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

	internal RecognizeDocumentsRequestCapabilities GetCapabilities()
	{
		using var native = RecognizeDocumentsRequestNative.GetCapabilities();
		return new RecognizeDocumentsRequestCapabilities(
			native.SupportedRecognitionLanguages,
			native.SupportedBarcodeSymbologies,
			[.. native.SupportedRevisions.Select(static revision => revision.Int32Value)]);
	}

	internal async IAsyncEnumerable<RecognizeDocumentsRequestPageSnapshot> RecognizePagesAsync(
		Stream source,
		string mediaType,
		RecognizeDocumentsRequestOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (!source.CanRead)
		{
			throw new ArgumentException("The document stream must be readable.", nameof(source));
		}

		var isPdf = string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);
		if (!isPdf && !s_supportedImageMediaTypes.Contains(mediaType))
		{
			throw new NotSupportedException(
				$"Apple Vision document recognition supports PNG, JPEG, HEIC, TIFF, and PDF streams. Media type '{mediaType}' is not supported.");
		}

		var bytes = await ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
		if (!isPdf)
		{
			yield return await RecognizeImageAsync(bytes, pageNumber: 1, totalPages: 1, options, pdfPage: null, cancellationToken)
				.ConfigureAwait(false);
			yield break;
		}

		using var data = NSData.FromArray(bytes);
		using var pdf = OpenPdf(data);
		if (pdf.IsLocked)
		{
			throw new NotSupportedException("Password-protected PDF documents must be unlocked before recognition.");
		}
		if (!pdf.AllowsCopying)
		{
			throw new UnauthorizedAccessException("The PDF document does not allow content extraction.");
		}

		var pageCount = checked((int)pdf.PageCount);
		if (pageCount == 0)
		{
			throw new InvalidDataException("The PDF document contains no readable pages.");
		}

		for (var index = 0; index < pageCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using var page = pdf.GetPage(index)
				?? throw new InvalidDataException($"PDFKit could not load page {index + 1}.");
			page.DisplaysAnnotations = true;

			var bounds = page.GetBoundsForBox(PdfDisplayBox.Crop);
			var rendered = RenderPdfPage(page, bounds);
			var pageInfo = new RecognizeDocumentsRequestPdfPageInfo(
				page.Label,
				checked((int)page.Rotation),
				PdfDisplayBox.Crop.ToString(),
				PdfRenderDpi,
				rendered.EffectiveDpi,
				rendered.WidthPoints,
				rendered.HeightPoints);

			yield return await RecognizeImageAsync(
				rendered.Data,
				index + 1,
				pageCount,
				options,
				pageInfo,
				cancellationToken).ConfigureAwait(false);
		}
	}

	private static PdfDocument OpenPdf(NSData data)
	{
		try
		{
			return new PdfDocument(data);
		}
		catch (Exception exception)
		{
			throw new InvalidDataException("The stream is not a valid PDF document.", exception);
		}
	}

	private static async Task<RecognizeDocumentsRequestPageSnapshot> RecognizeImageAsync(
		byte[] bytes,
		int pageNumber,
		int totalPages,
		RecognizeDocumentsRequestOptions? options,
		RecognizeDocumentsRequestPdfPageInfo? pdfPage,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var imageData = NSData.FromArray(bytes);
		var imageInfo = GetImageInfo(imageData);
		using var nativeRecognizer = new RecognizeDocumentsRequestNative();
		using var nativeOptions = ToNative(options);

		var tokenSync = new object();
		CancellationTokenNative? nativeToken = null;
		var completion = new TaskCompletionSource<RecognizeDocumentsRequestSnapshotNative>(
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

			var createdToken = nativeRecognizer.Perform(
				imageData,
				imageInfo.Orientation,
				nativeOptions,
				(result, error) =>
				{
					if (error is not null)
					{
						if (cancellationToken.IsCancellationRequested ||
							(error.Domain == nameof(RecognizeDocumentsRequestNative) &&
								error.Code == (nint)RecognizeDocumentsRequestErrorNative.Cancelled))
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
						completion.TrySetException(new InvalidDataException("Apple Vision returned no document result."));
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
			if (json.RootElement.ValueKind != JsonValueKind.Array)
			{
				throw new InvalidDataException("Apple Vision returned an invalid document snapshot.");
			}
			cancellationToken.ThrowIfCancellationRequested();

			return new RecognizeDocumentsRequestPageSnapshot(
				pageNumber,
				totalPages,
				json.RootElement.Clone(),
				imageInfo.Width,
				imageInfo.Height,
				options?.Revision ?? 1,
				pdfPage);
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

	private static RenderedPdfPage RenderPdfPage(PdfPage page, CGRect bounds)
	{
		if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
		{
			throw new InvalidDataException("The PDF page has invalid render bounds.");
		}

		var rotation = ((checked((int)page.Rotation) % 360) + 360) % 360;
		var swapsDimensions = rotation is 90 or 270;
		var widthPoints = swapsDimensions ? bounds.Height : bounds.Width;
		var heightPoints = swapsDimensions ? bounds.Width : bounds.Height;
		var scale = Math.Min(PdfRenderDpi / 72d, PdfMaximumPixelDimension / Math.Max(widthPoints, heightPoints));
		var width = Math.Max(1, (int)Math.Ceiling(widthPoints * scale));
		var height = Math.Max(1, (int)Math.Ceiling(heightPoints * scale));

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
			?? throw new InvalidDataException("PDFKit could not render the page image.");
		using var output = new NSMutableData();
		using var destination = CGImageDestination.Create(output, "public.png", 1)
			?? throw new InvalidDataException("ImageIO could not create a PNG destination.");
		destination.AddImage(image);
		if (!destination.Close())
		{
			throw new InvalidDataException("ImageIO could not encode the rendered PDF page.");
		}

		var effectiveDpi = Math.Min(PdfRenderDpi, Math.Min(width / widthPoints * 72d, height / heightPoints * 72d));
		return new RenderedPdfPage(output.ToArray(), widthPoints, heightPoints, effectiveDpi);
	}

	private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
	{
		using var memory = new MemoryStream();
		await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
		return memory.ToArray();
	}

	private static ImageInfo GetImageInfo(NSData imageData)
	{
		using var source = CGImageSource.FromData(imageData)
			?? throw new InvalidDataException("The stream does not contain a supported image.");
		if (source.ImageCount == 0)
		{
			throw new InvalidDataException("The stream does not contain a supported image.");
		}

		var properties = source.GetProperties(0);
		var orientation = properties?.Orientation is { } value
			? (nint)(int)value
			: (nint)(int)CIImageOrientation.TopLeft;
		int? width = properties?.PixelWidth is { } pixelWidth ? checked((int)pixelWidth) : null;
		int? height = properties?.PixelHeight is { } pixelHeight ? checked((int)pixelHeight) : null;
		if (orientation is >= 5 and <= 8)
		{
			(width, height) = (height, width);
		}

		return new ImageInfo(orientation, width, height);
	}

	private static RecognizeDocumentsRequestOptionsNative? ToNative(RecognizeDocumentsRequestOptions? options)
	{
		if (options is null)
		{
			return null;
		}

		return new RecognizeDocumentsRequestOptionsNative
		{
			RecognitionLanguages = options.RecognitionLanguages,
			CustomWords = options.CustomWords,
			UseLanguageCorrection = ToNSNumber(options.UseLanguageCorrection),
			AutomaticallyDetectLanguage = ToNSNumber(options.AutomaticallyDetectLanguage),
			MaximumCandidateCount = ToNSNumber(options.MaximumCandidateCount),
			MinimumTextHeightFraction = ToNSNumber(options.MinimumTextHeightFraction),
			BarcodeDetectionEnabled = ToNSNumber(options.BarcodeDetectionEnabled),
			BarcodeSymbologies = options.BarcodeSymbologies,
			CoalesceCompositeSymbologies = ToNSNumber(options.CoalesceCompositeSymbologies),
			RegionOfInterest = options.RegionOfInterest?.Select(static value => NSNumber.FromFloat(value)).ToArray(),
			Revision = ToNSNumber(options.Revision),
		};
	}

	private static NSNumber? ToNSNumber(bool? value) =>
		value is { } actual ? NSNumber.FromBoolean(actual) : null;

	private static NSNumber? ToNSNumber(int? value) =>
		value is { } actual ? NSNumber.FromInt32(actual) : null;

	private static NSNumber? ToNSNumber(float? value) =>
		value is { } actual ? NSNumber.FromFloat(actual) : null;

	private readonly record struct ImageInfo(nint Orientation, int? Width, int? Height);

	private readonly record struct RenderedPdfPage(byte[] Data, double WidthPoints, double HeightPoints, double EffectiveDpi);
}

internal sealed record RecognizeDocumentsRequestCapabilities(
	IReadOnlyList<string> SupportedRecognitionLanguages,
	IReadOnlyList<string> SupportedBarcodeSymbologies,
	IReadOnlyList<int> SupportedRevisions);

internal sealed record RecognizeDocumentsRequestPageSnapshot(
	int PageNumber,
	int TotalPages,
	JsonElement Snapshot,
	int? SourcePixelWidth,
	int? SourcePixelHeight,
	int Revision,
	RecognizeDocumentsRequestPdfPageInfo? PdfPage);

internal sealed record RecognizeDocumentsRequestPdfPageInfo(
	string? Label,
	int Rotation,
	string DisplayBox,
	double RequestedDpi,
	double EffectiveDpi,
	double WidthPoints,
	double HeightPoints);
