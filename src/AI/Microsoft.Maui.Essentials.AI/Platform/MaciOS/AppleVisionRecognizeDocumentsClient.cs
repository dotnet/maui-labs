using System.Runtime.CompilerServices;
using System.Text.Json;
using CoreGraphics;
using CoreImage;
using Foundation;
using ImageIO;
using PdfKit;

namespace Microsoft.Maui.Essentials.AI;

public sealed partial class AppleVisionRecognizeDocumentsClient
{
	private const double PdfRenderDpi = 200;
	private const int PdfMaximumPixelDimension = 4096;

	/// <summary>Initializes an on-device RecognizeDocumentsRequest client.</summary>
	public AppleVisionRecognizeDocumentsClient() : this(RecognizePagesAsync)
	{
	}

	// Images are recognized as a single page. PDFs are rendered page by page with PDFKit,
	// because Vision only accepts images.
	private static async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> RecognizePagesAsync(
		Stream source,
		string mediaType,
		RecognizeDocumentsRequestOptions? options,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		byte[] bytes;
		using (var memory = new MemoryStream())
		{
			await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
			bytes = memory.ToArray();
		}

		if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
		{
			yield return new(1, await RecognizeImageAsync(bytes, options, cancellationToken).ConfigureAwait(false));
			yield break;
		}

		using var data = NSData.FromArray(bytes);
		using var pdf = OpenPdf(data);
		if (pdf.IsLocked)
			throw new NotSupportedException("Password-protected PDF documents must be unlocked before recognition.");
		if (!pdf.AllowsCopying)
			throw new UnauthorizedAccessException("The PDF document does not allow content extraction.");

		var pageCount = checked((int)pdf.PageCount);
		if (pageCount == 0)
			throw new InvalidDataException("The PDF document contains no readable pages.");

		for (var index = 0; index < pageCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using var page = pdf.GetPage(index)
				?? throw new InvalidDataException($"PDFKit could not load page {index + 1}.");
			page.DisplaysAnnotations = true;
			var rendered = RenderPdfPage(page);
			yield return new(index + 1, await RecognizeImageAsync(rendered, options, cancellationToken).ConfigureAwait(false));
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

	private static async Task<JsonElement> RecognizeImageAsync(
		byte[] bytes, RecognizeDocumentsRequestOptions? options, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var imageData = NSData.FromArray(bytes);
		var orientation = GetOrientation(imageData);
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
					nativeToken?.Cancel();
			});

			var createdToken = nativeRecognizer.Perform(imageData, orientation, nativeOptions, (result, error) =>
			{
				if (error is not null)
				{
					if (cancellationToken.IsCancellationRequested ||
						(error.Domain == nameof(RecognizeDocumentsRequestNative) &&
							error.Code == (nint)RecognizeDocumentsRequestErrorNative.Cancelled))
						completion.TrySetCanceled(cancellationToken);
					else
						completion.TrySetException(new NSErrorException(error));
				}
				else if (result is null)
					completion.TrySetException(new InvalidDataException("Apple Vision returned no document result."));
				else
					completion.TrySetResult(result);
			});

			lock (tokenSync)
			{
				nativeToken = createdToken;
				if (cancellationToken.IsCancellationRequested)
					nativeToken?.Cancel();
			}

			using var nativeResult = await completion.Task.ConfigureAwait(false);
			using var json = JsonDocument.Parse(nativeResult.JsonData.ToArray());
			if (json.RootElement.ValueKind != JsonValueKind.Array)
				throw new InvalidDataException("Apple Vision returned an invalid document snapshot.");
			cancellationToken.ThrowIfCancellationRequested();
			return json.RootElement.Clone();
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

	private static byte[] RenderPdfPage(PdfPage page)
	{
		var bounds = page.GetBoundsForBox(PdfDisplayBox.Crop);
		if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
			throw new InvalidDataException("The PDF page has invalid render bounds.");

		var rotation = ((checked((int)page.Rotation) % 360) + 360) % 360;
		var swapsDimensions = rotation is 90 or 270;
		var widthPoints = swapsDimensions ? bounds.Height : bounds.Width;
		var heightPoints = swapsDimensions ? bounds.Width : bounds.Height;
		var scale = Math.Min(PdfRenderDpi / 72d, PdfMaximumPixelDimension / Math.Max(widthPoints, heightPoints));
		var width = Math.Max(1, (int)Math.Ceiling(widthPoints * scale));
		var height = Math.Max(1, (int)Math.Ceiling(heightPoints * scale));

		using var colorSpace = CGColorSpace.CreateDeviceRGB();
		using var context = new CGBitmapContext(
			data: null, width, height, bitsPerComponent: 8, bytesPerRow: width * 4, colorSpace, CGBitmapFlags.PremultipliedLast);
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
			throw new InvalidDataException("ImageIO could not encode the rendered PDF page.");
		return output.ToArray();
	}

	private static nint GetOrientation(NSData imageData)
	{
		using var source = CGImageSource.FromData(imageData)
			?? throw new InvalidDataException("The stream does not contain a supported image.");
		if (source.ImageCount == 0)
			throw new InvalidDataException("The stream does not contain a supported image.");
		return source.GetProperties(0)?.Orientation is { } value
			? (nint)(int)value
			: (nint)(int)CIImageOrientation.TopLeft;
	}

	private static RecognizeDocumentsRequestOptionsNative? ToNative(RecognizeDocumentsRequestOptions? options) =>
		options is null ? null : new RecognizeDocumentsRequestOptionsNative
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

	private static NSNumber? ToNSNumber(bool? value) => value is { } actual ? NSNumber.FromBoolean(actual) : null;

	private static NSNumber? ToNSNumber(int? value) => value is { } actual ? NSNumber.FromInt32(actual) : null;

	private static NSNumber? ToNSNumber(float? value) => value is { } actual ? NSNumber.FromFloat(actual) : null;
}
