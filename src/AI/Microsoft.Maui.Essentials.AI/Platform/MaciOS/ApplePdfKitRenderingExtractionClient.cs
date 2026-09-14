using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using CoreGraphics;
using Foundation;
using ImageIO;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using PdfKit;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>Configures PDFKit page rendering before document extraction.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class ApplePdfKitRenderingOptions
{
	private double _dpi = 200;
	private int _maximumPixelDimension = 4096;

	/// <summary>Gets or sets the render resolution in dots per inch.</summary>
	public double Dpi
	{
		get => _dpi;
		set
		{
			if (value is < 72 or > 600)
			{
				throw new ArgumentOutOfRangeException(nameof(value), "DPI must be between 72 and 600.");
			}
			_dpi = value;
		}
	}

	/// <summary>Gets or sets the maximum rendered width or height.</summary>
	public int MaximumPixelDimension
	{
		get => _maximumPixelDimension;
		set
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, 256);
			_maximumPixelDimension = value;
		}
	}

	/// <summary>Gets or sets the PDF display box to render.</summary>
	public PdfDisplayBox DisplayBox { get; set; } = PdfDisplayBox.Crop;

	/// <summary>Gets or sets whether PDF annotations are rendered.</summary>
	public bool IncludeAnnotations { get; set; } = true;

	/// <summary>Gets or sets whether PDF copy restrictions are enforced before rendering.</summary>
	public bool RespectCopyPermissions { get; set; } = true;
}

/// <summary>Describes a PDFKit page that was rendered for document extraction.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class ApplePdfKitPageReference
{
	internal ApplePdfKitPageReference(
		int pageNumber,
		string? label,
		int rotation,
		DocumentBoundingBox bounds,
		object? innerRawRepresentation)
	{
		PageNumber = pageNumber;
		Label = label;
		Rotation = rotation;
		Bounds = bounds;
		InnerRawRepresentation = innerRawRepresentation;
	}

	/// <summary>Gets the one-based PDF page number.</summary>
	public int PageNumber { get; }

	/// <summary>Gets the PDF page label.</summary>
	public string? Label { get; }

	/// <summary>Gets the PDF page rotation in degrees.</summary>
	public int Rotation { get; }

	/// <summary>Gets the rendered PDF bounds in points.</summary>
	public DocumentBoundingBox Bounds { get; }

	/// <summary>Gets the raw representation produced by the inner image client.</summary>
	public object? InnerRawRepresentation { get; }
}

/// <summary>Renders PDFKit pages and passes each page image to an inner document extraction client.</summary>
[SupportedOSPlatform("ios26.0")]
[SupportedOSPlatform("maccatalyst26.0")]
[SupportedOSPlatform("macos26.0")]
public sealed class ApplePdfKitRenderingExtractionClient : DelegatingDocumentExtractionClient
{
	private readonly ApplePdfKitRenderingOptions _renderingOptions;

	/// <summary>Initializes a new PDFKit rendering client.</summary>
	public ApplePdfKitRenderingExtractionClient(
		IDocumentExtractionClient pageClient,
		ApplePdfKitRenderingOptions? renderingOptions = null)
		: base(pageClient)
	{
		_renderingOptions = renderingOptions ?? new ApplePdfKitRenderingOptions();
	}

	/// <inheritdoc />
	public override async Task<DocumentExtractionResult> ExtractAsync(
		Stream document,
		string mediaType,
		DocumentExtractionOptions? options = null,
		CancellationToken cancellationToken = default) =>
		await ExtractPagesAsync(document, mediaType, options, cancellationToken)
			.ToDocumentExtractionResultAsync(cancellationToken)
			.ConfigureAwait(false);

	/// <inheritdoc />
	public override async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
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
		if (!string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException(
				$"PDFKit rendering supports 'application/pdf' only. Media type '{mediaType}' is not supported.");
		}

		var bytes = await ReadAllBytesAsync(document, cancellationToken).ConfigureAwait(false);
		using var data = NSData.FromArray(bytes);
		using var pdf = new PdfDocument(data);
		if (pdf.IsLocked)
		{
			throw new NotSupportedException("Password-protected PDF documents must be unlocked before extraction.");
		}
		if (_renderingOptions.RespectCopyPermissions && !pdf.AllowsCopying)
		{
			throw new UnauthorizedAccessException("The PDF document does not allow content extraction.");
		}

		var pageCount = checked((int)pdf.PageCount);
		for (var index = 0; index < pageCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using var pdfPage = pdf.GetPage(index)
				?? throw new InvalidOperationException($"PDFKit could not load page {index + 1}.");
			pdfPage.DisplaysAnnotations = _renderingOptions.IncludeAnnotations;
			var bounds = pdfPage.GetBoundsForBox(_renderingOptions.DisplayBox);
			var renderedPage = RenderPage(pdfPage, bounds);
			using var imageStream = new MemoryStream(renderedPage.Data, writable: false);
			var innerResult = await InnerClient
				.ExtractAsync(imageStream, "image/png", options, cancellationToken)
				.ConfigureAwait(false);
			if (innerResult.Pages.Count != 1)
			{
				throw new InvalidOperationException(
					$"The PDF page client must return exactly one page, but returned {innerResult.Pages.Count}.");
			}

			var pageNumber = index + 1;
			var page = AppleDocumentPageRenumberer.Renumber(
				innerResult.Pages[0],
				pageNumber,
				new ApplePdfKitPageReference(
					pageNumber,
					pdfPage.Label,
					checked((int)pdfPage.Rotation),
					new DocumentBoundingBox(
						0,
						0,
						(float)renderedPage.WidthPoints,
						(float)renderedPage.HeightPoints),
					innerResult.Pages[0].RawRepresentation),
				renderedPage,
				_renderingOptions);
			yield return new DocumentExtractionPageResult(page)
			{
				PagesProcessed = pageNumber,
				TotalPages = pageCount,
				AdditionalProperties = new AdditionalPropertiesDictionary
				{
					["apple.pdf.pageNumber"] = pageNumber,
					["apple.pdf.totalPages"] = pageCount,
					["apple.pdf.renderDpi"] = _renderingOptions.Dpi,
					["apple.pdf.effectiveRenderDpi"] = renderedPage.EffectiveDpi,
				},
			};
		}
	}

	private RenderedPdfPage RenderPage(PdfPage page, CGRect bounds)
	{
		if (bounds.Width <= 0 || bounds.Height <= 0)
		{
			throw new InvalidOperationException("The PDF page has invalid render bounds.");
		}

		var rotation = ((checked((int)page.Rotation) % 360) + 360) % 360;
		var swapsDimensions = rotation is 90 or 270;
		var widthPoints = swapsDimensions ? bounds.Height : bounds.Width;
		var heightPoints = swapsDimensions ? bounds.Width : bounds.Height;
		var scale = _renderingOptions.Dpi / 72d;
		var width = Math.Max(1, (int)Math.Ceiling(widthPoints * scale));
		var height = Math.Max(1, (int)Math.Ceiling(heightPoints * scale));
		var largest = Math.Max(width, height);
		if (largest > _renderingOptions.MaximumPixelDimension)
		{
			var clampScale = (double)_renderingOptions.MaximumPixelDimension / largest;
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
		page.Draw(_renderingOptions.DisplayBox, context);
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
			_renderingOptions.Dpi,
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

	internal readonly record struct RenderedPdfPage(
		byte[] Data,
		double WidthPoints,
		double HeightPoints,
		double EffectiveDpi);
}

internal static class AppleDocumentPageRenumberer
{
	internal static DocumentPage Renumber(
		DocumentPage source,
		int pageNumber,
		ApplePdfKitPageReference rawReference,
		ApplePdfKitRenderingExtractionClient.RenderedPdfPage renderedPage,
		ApplePdfKitRenderingOptions options)
	{
		var properties = source.AdditionalProperties is null
			? new AdditionalPropertiesDictionary()
			: new AdditionalPropertiesDictionary(source.AdditionalProperties);
		properties["apple.pdf.pageLabel"] = rawReference.Label;
		properties["apple.pdf.rotation"] = rawReference.Rotation;
		properties["apple.pdf.displayBox"] = options.DisplayBox.ToString();
		properties["apple.pdf.renderDpi"] = options.Dpi;
		properties["apple.pdf.effectiveRenderDpi"] = renderedPage.EffectiveDpi;
		properties["apple.pdf.widthPoints"] = renderedPage.WidthPoints;
		properties["apple.pdf.heightPoints"] = renderedPage.HeightPoints;

		return new DocumentPage(pageNumber, source.Text)
		{
			Elements = [.. source.Elements.Select(element => Renumber(element, pageNumber))],
			Dimensions = source.Dimensions,
			CoordinateUnit = source.CoordinateUnit,
			CoordinateOrigin = source.CoordinateOrigin,
			RawRepresentation = rawReference,
			AdditionalProperties = properties,
		};
	}

	private static DocumentElement Renumber(DocumentElement source, int pageNumber) =>
		source switch
		{
			DocumentBlock block => new DocumentBlock(block.Text)
			{
				Kind = block.Kind,
				BoundingRegion = Renumber(block.BoundingRegion, pageNumber),
				Confidence = block.Confidence,
				RawRepresentation = block.RawRepresentation,
				AdditionalProperties = Clone(block.AdditionalProperties),
			},
			DocumentTable table => new DocumentTable(
				table.RowCount,
				table.ColumnCount,
				table.Cells?.Select(cell => Renumber(cell, pageNumber)).ToArray(),
				table.MarkdownRepresentation)
			{
				BoundingRegion = Renumber(table.BoundingRegion, pageNumber),
				Confidence = table.Confidence,
				RawRepresentation = table.RawRepresentation,
				AdditionalProperties = Clone(table.AdditionalProperties),
			},
			DocumentImage image => new DocumentImage
			{
				Content = image.Content,
				Caption = image.Caption,
				BoundingRegion = Renumber(image.BoundingRegion, pageNumber),
				Confidence = image.Confidence,
				RawRepresentation = image.RawRepresentation,
				AdditionalProperties = Clone(image.AdditionalProperties),
			},
			AppleBarcodeElement barcode => new AppleBarcodeElement(barcode.Symbology)
			{
				PayloadString = barcode.PayloadString,
				PayloadData = barcode.PayloadData,
				IsGs1DataCarrier = barcode.IsGs1DataCarrier,
				IsColorInverted = barcode.IsColorInverted,
				SupplementalPayloadString = barcode.SupplementalPayloadString,
				SupplementalPayloadData = barcode.SupplementalPayloadData,
				SupplementalCompositeType = barcode.SupplementalCompositeType,
				BoundingRegion = Renumber(barcode.BoundingRegion, pageNumber),
				Confidence = barcode.Confidence,
				RawRepresentation = barcode.RawRepresentation,
				AdditionalProperties = Clone(barcode.AdditionalProperties),
			},
			AppleListElement list => new AppleListElement(
				[.. list.Items.Select(item => (AppleListItemElement)Renumber(item, pageNumber))])
			{
				BoundingRegion = Renumber(list.BoundingRegion, pageNumber),
				Confidence = list.Confidence,
				RawRepresentation = list.RawRepresentation,
				AdditionalProperties = Clone(list.AdditionalProperties),
			},
			AppleListItemElement item => new AppleListItemElement(item.Text)
			{
				ItemString = item.ItemString,
				MarkerString = item.MarkerString,
				MarkerType = item.MarkerType,
				Elements = [.. item.Elements.Select(element => Renumber(element, pageNumber))],
				BoundingRegion = Renumber(item.BoundingRegion, pageNumber),
				Confidence = item.Confidence,
				RawRepresentation = item.RawRepresentation,
				AdditionalProperties = Clone(item.AdditionalProperties),
			},
			_ => RenumberUnknown(source, pageNumber),
		};

	private static DocumentElement RenumberUnknown(DocumentElement source, int pageNumber)
	{
		source.BoundingRegion = Renumber(source.BoundingRegion, pageNumber);
		return source;
	}

	private static DocumentTableCell Renumber(DocumentTableCell source, int pageNumber) =>
		new(source.RowIndex, source.ColumnIndex, source.Content)
		{
			Kind = source.Kind,
			RowSpan = source.RowSpan,
			ColumnSpan = source.ColumnSpan,
			Elements = source.Elements?.Select(element => Renumber(element, pageNumber)).ToArray(),
			BoundingRegion = Renumber(source.BoundingRegion, pageNumber),
			Confidence = source.Confidence,
			RawRepresentation = source.RawRepresentation,
			AdditionalProperties = Clone(source.AdditionalProperties),
		};

	private static DocumentBoundingRegion? Renumber(
		DocumentBoundingRegion? region,
		int pageNumber) =>
		region is null ? null : new DocumentBoundingRegion(pageNumber, region.Polygon);

	private static AdditionalPropertiesDictionary? Clone(
		AdditionalPropertiesDictionary? properties) =>
		properties is null ? null : new AdditionalPropertiesDictionary(properties);
}
