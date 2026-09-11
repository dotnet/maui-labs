#if IOS || MACCATALYST

using System.Text.Json;
using CoreGraphics;
using CoreImage;
using Foundation;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DocumentExtraction;
using PdfKit;
using UIKit;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class AppleVisionDocumentExtractionTests
{
	[Fact]
	public void GetService_ReturnsMetadataAndCapabilities()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();

		var metadata = client.GetService<DocumentExtractionClientMetadata>();
		var capabilities = client.GetService<AppleVisionDocumentCapabilities>();

		Assert.NotNull(metadata);
		Assert.Equal("apple.vision", metadata.ProviderName);
		Assert.Equal("recognize-documents", metadata.DefaultModelId);
		Assert.NotNull(capabilities);
		Assert.Contains(1, capabilities.Revisions);
		Assert.NotEmpty(capabilities.RecognitionLanguages);
	}

	[Fact]
	public async Task ExtractAsync_UnsupportedMediaType_ThrowsNotSupportedException()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		using var stream = new MemoryStream([1, 2, 3]);

		await Assert.ThrowsAsync<NotSupportedException>(
			() => client.ExtractAsync(stream, "application/pdf"));
	}

	[Fact]
	public async Task ExtractAsync_SimpleTextImage_ReturnsStructuredPage()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var image = await MainThread.InvokeOnMainThreadAsync(
			() => CreateTextImage("APPLE VISION DOCUMENT\n\nThis is a document recognition test."));
		using var stream = new MemoryStream(image);

		var result = await client.ExtractAsync(stream, "image/png");

		var page = Assert.Single(result.Pages);
		Assert.True(stream.CanRead);
		Assert.Equal(1, page.PageNumber);
		Assert.Contains("APPLE", page.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(DocumentCoordinateUnit.Normalized, page.CoordinateUnit);
		Assert.Equal(DocumentCoordinateOrigin.BottomLeft, page.CoordinateOrigin);
		AssertTraversalIsBounded(page);
		var raw = Assert.IsType<AppleVisionDocumentNodeReference>(page.RawRepresentation);
		Assert.False(raw.GetRawJson().IsEmpty);
		using var rawJson = JsonDocument.Parse(raw.GetRawJson());
		Assert.Equal(JsonValueKind.Array, rawJson.RootElement.ValueKind);
		Assert.NotEmpty(rawJson.RootElement.EnumerateArray());
		var json = JsonSerializer.Serialize(result, AppleDocumentExtractionJson.CreateOptions());
		Assert.Contains("recognize-documents", json, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExtractAsync_AlreadyCancelled_ThrowsOperationCanceledException()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		using var stream = new MemoryStream([1, 2, 3]);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => client.ExtractAsync(stream, "image/png", cancellationToken: cancellation.Token));
	}

	[Fact]
	public async Task ExtractPagesAsync_CancelAfterYield_DoesNotTargetDisposedNativeToken()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var image = await MainThread.InvokeOnMainThreadAsync(
			() => CreateTextImage("YIELD THEN CANCEL"));
		using var stream = new MemoryStream(image);
		using var cancellation = new CancellationTokenSource();
		await using var enumerator = client
			.ExtractPagesAsync(stream, "image/png", cancellationToken: cancellation.Token)
			.GetAsyncEnumerator();

		Assert.True(await enumerator.MoveNextAsync());
		var exception = Record.Exception(cancellation.Cancel);

		Assert.Null(exception);
	}

	[Fact]
	public async Task ExtractAsync_NumberedList_MapsAppleListElement()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var image = await MainThread.InvokeOnMainThreadAsync(
			() => CreateTextImage("SHOPPING LIST\n\n1. Apples\n2. Bananas\n3. Coffee"));
		using var stream = new MemoryStream(image);

		var result = await client.ExtractAsync(stream, "image/png");

		var page = Assert.Single(result.Pages);
		AssertTraversalIsBounded(page);
		var list = Assert.Single(page.Elements.OfType<AppleListElement>());
		Assert.Equal(3, list.Items.Count);
		Assert.Contains(list.Items, static item => item.Text.Contains("Apples", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ExtractAsync_QrCode_MapsAppleBarcodeElement()
	{
		if (!IsSupported())
		{
			return;
		}
#if IOS
		// Vision's barcode detector is not available consistently in the iOS simulator.
		if (DeviceInfo.Current.DeviceType == DeviceType.Virtual)
		{
			return;
		}
#endif

		const string payload = "https://example.com/apple-vision";
		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var image = await MainThread.InvokeOnMainThreadAsync(() => CreateQrImage(payload));
		using var stream = new MemoryStream(image);
		var options = new DocumentExtractionOptions()
			.WithAppleBarcodeDetection(true, symbologies: ["qr"]);

		var result = await client.ExtractAsync(stream, "image/png", options);

		var barcode = Assert.Single(result.Pages[0].Elements.OfType<AppleBarcodeElement>());
		Assert.Equal("qr", barcode.Symbology);
		Assert.Equal(payload, barcode.PayloadString);
	}

	[Fact]
	public async Task ExtractAsync_Table_MapsCells()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new AppleVisionRecognizeDocumentsClient();
		var image = await MainThread.InvokeOnMainThreadAsync(CreateTableImage);
		using var stream = new MemoryStream(image);

		var result = await client.ExtractAsync(stream, "image/png");

		var page = Assert.Single(result.Pages);
		AssertTraversalIsBounded(page);
		var table = Assert.Single(page.Elements.OfType<DocumentTable>());
		Assert.True(table.RowCount >= 2);
		Assert.True(table.ColumnCount >= 2);
		Assert.NotNull(table.Cells);
		Assert.Contains(table.Cells, static cell => cell.Content.Contains("Apples", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void AppleJson_CustomElements_RoundTrip()
	{
		if (!IsSupported())
		{
			return;
		}

		var result = new DocumentExtractionResult(
		[
			new DocumentPage(1, "Item one\nhttps://example.com")
			{
				Elements =
				[
					new AppleListElement(
					[
						new AppleListItemElement("Item one")
						{
							MarkerString = "1.",
							MarkerType = "decimal",
						},
					]),
					new AppleBarcodeElement("qr")
					{
						PayloadString = "https://example.com",
						IsGs1DataCarrier = false,
					},
				],
			},
		]);
		var options = AppleDocumentExtractionJson.Default;

		var json = JsonSerializer.Serialize(result, options);
		var roundTripped = JsonSerializer.Deserialize<DocumentExtractionResult>(json, options);

		Assert.True(options.IsReadOnly);
		Assert.False(AppleDocumentExtractionJson.CreateOptions().IsReadOnly);
		Assert.NotNull(roundTripped);
		var page = Assert.Single(roundTripped.Pages);
		Assert.IsType<AppleListElement>(page.Elements[0]);
		var barcode = Assert.IsType<AppleBarcodeElement>(page.Elements[1]);
		Assert.Equal("https://example.com", barcode.PayloadString);
		Assert.Throws<NotSupportedException>(
			() => JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions));
	}

	[Fact]
	public async Task PdfWrapper_TwoPages_StreamsRenumberedPages()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new ApplePdfKitRenderingExtractionClient(
			new StubPageClient());
		var pdf = await MainThread.InvokeOnMainThreadAsync(
			() => CreatePdf(["Page one", "Page two"]));
		using var stream = new MemoryStream(pdf);
		var updates = new List<DocumentExtractionPageResult>();

		await foreach (var update in client.ExtractPagesAsync(stream, "application/pdf"))
		{
			updates.Add(update);
		}

		Assert.Equal(2, updates.Count);
		Assert.Equal([1, 2], updates.Select(static update => update.Page.PageNumber));
		Assert.All(
			updates,
			update =>
			{
				Assert.Equal(update.Page.PageNumber, update.PagesProcessed);
				Assert.Equal(2, update.TotalPages);
				var barcode = Assert.IsType<AppleBarcodeElement>(Assert.Single(update.Page.Elements));
				Assert.Equal(update.Page.PageNumber, barcode.BoundingRegion?.PageNumber);
				Assert.IsType<ApplePdfKitPageReference>(update.Page.RawRepresentation);
			});
	}

	[Fact]
	public async Task PdfWrapper_RealVision_RecognizesEveryRenderedPage()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new ApplePdfKitRenderingExtractionClient(
			new AppleVisionRecognizeDocumentsClient());
		var pdf = await MainThread.InvokeOnMainThreadAsync(
			() => CreatePdf(["ALPHA DOCUMENT", "BETA DOCUMENT"]));
		using var stream = new MemoryStream(pdf);

		var result = await client.ExtractAsync(stream, "application/pdf");

		Assert.Equal(2, result.Pages.Count);
		Assert.True(stream.CanRead);
		Assert.Contains("ALPHA", result.Pages[0].Text, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("BETA", result.Pages[1].Text, StringComparison.OrdinalIgnoreCase);
		Assert.Equal([1, 2], result.Pages.Select(static page => page.PageNumber));
		Assert.All(result.Pages, AssertTraversalIsBounded);
	}

	[Fact]
	public async Task PdfWrapper_RotatedOffsetCrop_RecognizesPageAndReportsRotatedExtent()
	{
		if (!IsSupported())
		{
			return;
		}

		using IDocumentExtractionClient client = new ApplePdfKitRenderingExtractionClient(
			new AppleVisionRecognizeDocumentsClient());
		var pdf = await MainThread.InvokeOnMainThreadAsync(CreateRotatedPdf);
		using var stream = new MemoryStream(pdf);

		var result = await client.ExtractAsync(stream, "application/pdf");

		var page = Assert.Single(result.Pages);
		Assert.Contains("ROTATED", page.Text, StringComparison.OrdinalIgnoreCase);
		var reference = Assert.IsType<ApplePdfKitPageReference>(page.RawRepresentation);
		Assert.Equal(90, reference.Rotation);
		Assert.Equal(744, reference.Bounds.Right, precision: 1);
		Assert.Equal(564, reference.Bounds.Bottom, precision: 1);
		Assert.True(
			Assert.IsType<double>(page.AdditionalProperties!["apple.pdf.effectiveRenderDpi"]) <= 200);
	}

	private static bool IsSupported() =>
		OperatingSystem.IsIOSVersionAtLeast(26) ||
		OperatingSystem.IsMacCatalystVersionAtLeast(26);

	private static void AssertTraversalIsBounded(DocumentPage page)
	{
		var properties = Assert.IsType<AdditionalPropertiesDictionary>(page.AdditionalProperties);
		Assert.InRange(
			Assert.IsType<long>(properties["apple.vision.projectedNodeCount"]),
			1,
			999);
		Assert.InRange(
			Assert.IsType<long>(properties["apple.vision.maximumTraversalDepth"]),
			0,
			64);
		Assert.IsType<long>(properties["apple.vision.repeatedContainersPruned"]);
		Assert.IsType<string[]>(properties["apple.vision.repeatedContainerExamples"]);
	}

	private static byte[] CreateTextImage(string text)
	{
		var bounds = new CGRect(0, 0, 1200, 800);
		var format = UIGraphicsImageRendererFormat.DefaultFormat;
		format.Opaque = true;
		format.Scale = 1;
		using var renderer = new UIGraphicsImageRenderer(bounds, format);
		using var image = renderer.CreateImage(context =>
		{
			UIColor.White.SetFill();
			context.FillRect(bounds);
			using var value = new NSString(text);
			using var font = UIFont.SystemFontOfSize(44, UIFontWeight.Regular);
			using var color = UIColor.Black;
			value.DrawString(
				new CGRect(60, 60, bounds.Width - 120, bounds.Height - 120),
				new UIStringAttributes
				{
					Font = font,
					ForegroundColor = color,
				});
		});
		using var data = image.AsPNG()
			?? throw new InvalidOperationException("Unable to create the test image.");
		return data.ToArray();
	}

	private static byte[] CreatePdf(IReadOnlyList<string> pages)
	{
		var bounds = new CGRect(0, 0, 612, 792);
		using var renderer = new UIGraphicsPdfRenderer(
			bounds,
			UIGraphicsPdfRendererFormat.DefaultFormat);
		using var data = renderer.CreatePdf(context =>
		{
			foreach (var page in pages)
			{
				context.BeginPage();
				using var value = new NSString(page);
				using var font = UIFont.SystemFontOfSize(36);
				value.DrawString(
					new CGRect(48, 48, bounds.Width - 96, bounds.Height - 96),
					new UIStringAttributes
					{
						Font = font,
						ForegroundColor = UIColor.Black,
					});
			}
		});
		return data.ToArray();
	}

	private static byte[] CreateRotatedPdf()
	{
		using var input = NSData.FromArray(CreatePdf(["ROTATED DOCUMENT"]));
		using var pdf = new PdfDocument(input);
		using var page = pdf.GetPage(0)
			?? throw new InvalidOperationException("Unable to load the generated PDF page.");
		page.SetBoundsForBox(new CGRect(24, 24, 564, 744), PdfDisplayBox.Crop);
		page.Rotation = 90;
		using var output = pdf.GetDataRepresentation()
			?? throw new InvalidOperationException("Unable to encode the rotated PDF.");
		return output.ToArray();
	}

	private static byte[] CreateQrImage(string payload)
	{
		using var message = NSData.FromString(payload);
		using var generator = new CIQRCodeGenerator
		{
			Message = message,
			CorrectionLevel = "M",
		};
		using var qrCode = generator.OutputImage
			?? throw new InvalidOperationException("Unable to generate the QR code.");
		using var scaledQrCode = qrCode.ImageByApplyingTransform(
			CGAffineTransform.MakeScale(20, 20));
		using var context = CIContext.FromOptions(null);
		using var cgImage = context.CreateCGImage(scaledQrCode, scaledQrCode.Extent)
			?? throw new InvalidOperationException("Unable to render the QR code.");
		using var qrImage = UIImage.FromImage(cgImage);
		var bounds = new CGRect(0, 0, 800, 800);
		var format = UIGraphicsImageRendererFormat.DefaultFormat;
		format.Opaque = true;
		format.Scale = 1;
		using var renderer = new UIGraphicsImageRenderer(bounds, format);
		using var image = renderer.CreateImage(context =>
		{
			UIColor.White.SetFill();
			context.FillRect(bounds);
			context.CGContext.InterpolationQuality = CGInterpolationQuality.None;
			qrImage.Draw(new CGRect(
				(bounds.Width - qrImage.Size.Width) / 2,
				(bounds.Height - qrImage.Size.Height) / 2,
				qrImage.Size.Width,
				qrImage.Size.Height));
		});
		using var data = image.AsPNG()
			?? throw new InvalidOperationException("Unable to encode the QR code.");
		return data.ToArray();
	}

	private static byte[] CreateTableImage()
	{
		var bounds = new CGRect(0, 0, 1200, 800);
		var format = UIGraphicsImageRendererFormat.DefaultFormat;
		format.Opaque = true;
		format.Scale = 1;
		using var renderer = new UIGraphicsImageRenderer(bounds, format);
		using var image = renderer.CreateImage(context =>
		{
			UIColor.White.SetFill();
			context.FillRect(bounds);
			var graphics = context.CGContext;
			graphics.SetStrokeColor(UIColor.Black.CGColor);
			graphics.SetLineWidth(4);
			const float left = 100;
			const float top = 140;
			const float cellWidth = 500;
			const float cellHeight = 180;
			for (var row = 0; row <= 3; row++)
			{
				var y = top + (row * cellHeight);
				graphics.MoveTo(left, y);
				graphics.AddLineToPoint(left + (cellWidth * 2), y);
			}
			for (var column = 0; column <= 2; column++)
			{
				var x = left + (column * cellWidth);
				graphics.MoveTo(x, top);
				graphics.AddLineToPoint(x, top + (cellHeight * 3));
			}
			graphics.StrokePath();

			var values = new[,]
			{
				{ "Item", "Price" },
				{ "Apples", "$4.50" },
				{ "Coffee", "$12.00" },
			};
			using var font = UIFont.SystemFontOfSize(38);
			for (var row = 0; row < 3; row++)
			{
				for (var column = 0; column < 2; column++)
				{
					using var value = new NSString(values[row, column]);
					value.DrawString(
						new CGRect(
							left + (column * cellWidth) + 30,
							top + (row * cellHeight) + 55,
							cellWidth - 60,
							cellHeight - 60),
						new UIStringAttributes
						{
							Font = font,
							ForegroundColor = UIColor.Black,
						});
				}
			}
		});
		using var data = image.AsPNG()
			?? throw new InvalidOperationException("Unable to encode the table image.");
		return data.ToArray();
	}

	private sealed class StubPageClient : IDocumentExtractionClient
	{
		public Task<DocumentExtractionResult> ExtractAsync(
			Stream document,
			string mediaType,
			DocumentExtractionOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			var barcode = new AppleBarcodeElement("qr")
			{
				PayloadString = "page",
				BoundingRegion = new DocumentBoundingRegion(
					1,
					[
						new DocumentPoint(0.1f, 0.9f),
						new DocumentPoint(0.2f, 0.9f),
						new DocumentPoint(0.2f, 0.8f),
						new DocumentPoint(0.1f, 0.8f),
					]),
			};
			return Task.FromResult(new DocumentExtractionResult(
			[
				new DocumentPage(1, "page")
				{
					Elements = [barcode],
					CoordinateUnit = DocumentCoordinateUnit.Normalized,
					CoordinateOrigin = DocumentCoordinateOrigin.BottomLeft,
				},
			]));
		}

		public async IAsyncEnumerable<DocumentExtractionPageResult> ExtractPagesAsync(
			Stream document,
			string mediaType,
			DocumentExtractionOptions? options = null,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.Yield();
			yield return new DocumentExtractionPageResult(
				(await ExtractAsync(document, mediaType, options, cancellationToken)).Pages[0]);
		}

		public object? GetService(Type serviceType, object? serviceKey = null) =>
			serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

		public void Dispose()
		{
		}
	}
}

#endif
