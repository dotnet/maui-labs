using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using CoreGraphics;
using Foundation;
using ImageIO;
using Microsoft.Extensions.DataIngestion;
using PdfKit;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Reads images and PDF documents on-device using Apple Vision document recognition.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class AppleVisionDocumentReader : IngestionDocumentReader
{
	private const double PdfDpi = 200;
	private const int MaximumPdfDimension = 4096;

	/// <inheritdoc />
	public override async Task<IngestionDocument> ReadAsync(
		Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(identifier);
		ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
		if (!source.CanRead)
			throw new ArgumentException("The document stream must be readable.", nameof(source));

		var isPdf = mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
		if (!isPdf && !new[] { "image/png", "image/jpeg", "image/jpg", "image/heic", "image/tiff" }
			.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
			throw new NotSupportedException($"Unsupported document media type: {mediaType}.");

		cancellationToken.ThrowIfCancellationRequested();
		using var memory = new MemoryStream();
		await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
		var document = new IngestionDocument(identifier);
		using var data = NSData.FromArray(memory.ToArray());
		if (isPdf)
		{
			using var pdf = OpenPdf(data);
			if (pdf.IsLocked)
				throw new NotSupportedException("Password-protected PDFs cannot be read.");
			if (!pdf.AllowsCopying)
				throw new UnauthorizedAccessException("The PDF does not permit copying its content.");
			if (pdf.PageCount == 0)
				throw new InvalidDataException("The PDF contains no readable pages.");

			for (var i = 0; i < checked((int)pdf.PageCount); i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				using var page = pdf.GetPage(i)
					?? throw new InvalidDataException($"Cannot load PDF page {i + 1}.");
				using var image = Render(page);
				document.Sections.Add(await RecognizeAsync(image, i + 1, cancellationToken).ConfigureAwait(false));
			}
		}
		else
		{
			using var imageSource = CGImageSource.FromData(data)
				?? throw new InvalidDataException("The stream is not a valid image.");
			if (imageSource.ImageCount == 0)
				throw new InvalidDataException("The stream is not a valid image.");
			var orientation = imageSource.GetProperties(0)?.Orientation is { } value ? (nint)(int)value : 1;
			document.Sections.Add(await RecognizeAsync(data, 1, cancellationToken, orientation).ConfigureAwait(false));
		}
		return document;
	}

	private static PdfDocument OpenPdf(NSData data)
	{
		try
		{
			return new PdfDocument(data);
		}
		catch (Exception exception)
		{
			throw new InvalidDataException("The stream is not a valid PDF.", exception);
		}
	}

	private static NSData Render(PdfPage page)
	{
		var bounds = page.GetBoundsForBox(PdfDisplayBox.Crop);
		if (bounds.Width <= 0 || bounds.Height <= 0)
			throw new InvalidDataException("The PDF page has invalid bounds.");
		var rotated = (checked((int)page.Rotation) % 180) != 0;
		var widthPoints = rotated ? bounds.Height : bounds.Width;
		var heightPoints = rotated ? bounds.Width : bounds.Height;
		var scale = Math.Min(PdfDpi / 72, MaximumPdfDimension / Math.Max(widthPoints, heightPoints));
		var width = Math.Max(1, (int)Math.Ceiling(widthPoints * scale));
		var height = Math.Max(1, (int)Math.Ceiling(heightPoints * scale));
		using var colorSpace = CGColorSpace.CreateDeviceRGB();
		using var context = new CGBitmapContext(null, width, height, 8, width * 4,
			colorSpace, CGBitmapFlags.PremultipliedLast);
		context.SetFillColor(1, 1, 1, 1);
		context.FillRect(new CGRect(0, 0, width, height));
		context.SaveState();
		context.ScaleCTM(width / widthPoints, height / heightPoints);
		page.Draw(PdfDisplayBox.Crop, context);
		context.RestoreState();
		using var cgImage = context.ToImage()
			?? throw new InvalidDataException("PDFKit could not render the page.");
		var output = new NSMutableData();
		using var destination = CGImageDestination.Create(output, "public.png", 1)
			?? throw new InvalidDataException("ImageIO could not encode the page.");
		destination.AddImage(cgImage);
		if (!destination.Close())
		{
			output.Dispose();
			throw new InvalidDataException("ImageIO could not encode the page.");
		}
		return output;
	}

	private static async Task<IngestionDocumentSection> RecognizeAsync(
		NSData image, int pageNumber, CancellationToken cancellationToken, nint orientation = 1)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using var native = new VisionDocumentReaderNative();
		var completion = new TaskCompletionSource<NSData>(TaskCreationOptions.RunContinuationsAsynchronously);
		var gate = new object();
		CancellationTokenNative? nativeToken = null;
		using var registration = cancellationToken.Register(() =>
		{
			lock (gate)
				nativeToken?.Cancel();
		});
		try
		{
			var token = native.Recognize(image, orientation, (result, error) =>
			{
				if (cancellationToken.IsCancellationRequested)
					completion.TrySetCanceled(cancellationToken);
				else if (error is not null)
					completion.TrySetException(new NSErrorException(error));
				else if (result is not null)
					completion.TrySetResult(result);
				else
					completion.TrySetException(new InvalidDataException("Vision returned no document result."));
			});
			lock (gate)
			{
				nativeToken = token;
				if (cancellationToken.IsCancellationRequested)
					token.Cancel();
			}
			using var response = await completion.Task.ConfigureAwait(false);
			using var json = JsonDocument.Parse(response.ToArray());
			var section = new IngestionDocumentSection { PageNumber = pageNumber };
			foreach (var element in json.RootElement.EnumerateArray())
			{
				var kind = element.GetProperty("kind").GetString();
				var text = element.GetProperty("text").GetString() ?? "";
				switch (kind)
				{
					case "title" when !string.IsNullOrWhiteSpace(text):
						section.Elements.Add(new IngestionDocumentHeader(text) { Level = 1, Text = text, PageNumber = pageNumber });
						break;
					case "paragraph" when !string.IsNullOrWhiteSpace(text):
						section.Elements.Add(new IngestionDocumentParagraph(text) { Text = text, PageNumber = pageNumber });
						break;
					case "table":
						section.Elements.Add(ReadTable(element, pageNumber));
						break;
				}
			}
			return section;
		}
		finally
		{
			// Unregister before disposing the native token so cancellation cannot race it.
			registration.Dispose();
			lock (gate)
			{
				nativeToken?.Dispose();
				nativeToken = null;
			}
		}
	}

	private static IngestionDocumentTable ReadTable(JsonElement table, int pageNumber)
	{
		var rowCount = table.GetProperty("rows").GetInt32();
		var columnCount = table.GetProperty("columns").GetInt32();
		if (rowCount < 0 || columnCount < 0 || (long)rowCount * columnCount > 20_000)
			throw new InvalidDataException("Vision returned invalid table dimensions.");
		var cells = new IngestionDocumentElement?[rowCount, columnCount];
		foreach (var cell in table.GetProperty("cells").EnumerateArray())
		{
			var row = cell.GetProperty("row").GetInt32();
			var column = cell.GetProperty("column").GetInt32();
			var rowSpan = cell.GetProperty("rowSpan").GetInt32();
			var columnSpan = cell.GetProperty("columnSpan").GetInt32();
			if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1 ||
				(long)row + rowSpan > rowCount || (long)column + columnSpan > columnCount)
				throw new InvalidDataException("Vision returned a table cell outside its grid.");
			var text = cell.GetProperty("text").GetString();
			if (!string.IsNullOrWhiteSpace(text))
				cells[row, column] = new IngestionDocumentParagraph(text) { Text = text, PageNumber = pageNumber };
		}
		var markdown = new StringBuilder();
		for (var r = 0; r < rowCount; r++)
		{
			markdown.Append('|');
			for (var c = 0; c < columnCount; c++)
			{
				var text = cells[r, c]?.GetMarkdown();
				markdown.Append(' ').Append((text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")).Append(" |");
			}
			markdown.AppendLine();
			if (r == 0)
			{
				markdown.Append('|');
				for (var c = 0; c < columnCount; c++)
					markdown.Append(" --- |");
				markdown.AppendLine();
			}
		}
		return new IngestionDocumentTable(markdown.ToString().TrimEnd(), cells) { PageNumber = pageNumber };
	}
}
