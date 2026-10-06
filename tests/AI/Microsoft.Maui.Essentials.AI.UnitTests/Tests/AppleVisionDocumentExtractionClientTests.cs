using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionDocumentExtractionClientTests
{
	private static AppleVisionDocumentCapabilities Capabilities() => new()
	{
		RecognitionLanguages = ["en-US"], BarcodeSymbologies = ["qr"], Revisions = [1, 2],
	};

	private static async IAsyncEnumerable<AppleVisionSourcePage> Pages(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		for (var number = 1; number <= 2; number++)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			using var json = JsonDocument.Parse("""[{"transcript":"Page text","nodes":[{"path":"p","kind":"paragraph","text":"Text"}]}]""");
			yield return new AppleVisionSourcePage
			{
				PageNumber = number, TotalPages = 2, Snapshot = json.RootElement, SourcePixelWidth = 1000,
				SourcePixelHeight = 2000, Revision = 2,
				PdfPage = new DocumentPdfPageInfo
				{
					Label = $"Label {number}", Rotation = 90, DisplayBox = "Crop", RequestedDpi = 200,
					EffectiveDpi = 144, WidthPoints = 500, HeightPoints = 1000,
				},
			};
		}
	}

	[Fact]
	public async Task ExtractPagesAsync_OptionsProgressAndPdfFacts_AreRetainedWithoutOwningSource()
	{
		using var source = new MemoryStream([1, 2, 3]);
		var options = new DocumentExtractionOptions
		{
			ModelId = "RECOGNIZE-DOCUMENTS", RecognitionLanguages = ["en-US"], CustomWords = ["Comet"],
			UseLanguageCorrection = false, AutomaticallyDetectLanguage = true, MaximumCandidateCount = 3,
			MinimumTextHeightFraction = 0.2f, BarcodeDetectionEnabled = true, BarcodeSymbologies = ["qr"],
			CoalesceCompositeSymbologies = false, RegionOfInterest = [0.1f, 0.2f, 0.3f, 0.4f], Revision = 2,
		};
		options.AdditionalProperties["test"] = "retained";
		DocumentExtractionOptions? forwarded = null;
		var capabilityCalls = 0;
		var client = new AppleVisionDocumentExtractionClient((stream, mediaType, request, token) =>
		{
			Assert.Same(source, stream);
			Assert.Equal("application/pdf", mediaType);
			forwarded = request;
			return Pages(token);
		}, () => { capabilityCalls++; return Capabilities(); });
		Assert.Equal([1, 2], client.GetCapabilities().Revisions);
		Assert.Equal(["en-US"], client.GetCapabilities().RecognitionLanguages);
		Assert.Equal(["qr"], client.GetCapabilities().BarcodeSymbologies);
		Assert.Equal(1, capabilityCalls);
		var updates = new List<DocumentExtractionPageResult>();
		await foreach (var update in client.ExtractPagesAsync(source, "application/pdf", options))
			updates.Add(update);
		Assert.NotNull(forwarded);
		Assert.NotSame(options, forwarded);
		Assert.Equal(options.ModelId, forwarded.ModelId);
		Assert.Equal(options.RecognitionLanguages, forwarded.RecognitionLanguages);
		Assert.NotSame(options.RecognitionLanguages, forwarded.RecognitionLanguages);
		Assert.Equal(options.CustomWords, forwarded.CustomWords);
		Assert.Equal(options.UseLanguageCorrection, forwarded.UseLanguageCorrection);
		Assert.Equal(options.AutomaticallyDetectLanguage, forwarded.AutomaticallyDetectLanguage);
		Assert.Equal(options.MaximumCandidateCount, forwarded.MaximumCandidateCount);
		Assert.Equal(options.MinimumTextHeightFraction, forwarded.MinimumTextHeightFraction);
		Assert.Equal(options.BarcodeDetectionEnabled, forwarded.BarcodeDetectionEnabled);
		Assert.Equal(options.BarcodeSymbologies, forwarded.BarcodeSymbologies);
		Assert.Equal(options.CoalesceCompositeSymbologies, forwarded.CoalesceCompositeSymbologies);
		Assert.Equal(options.RegionOfInterest, forwarded.RegionOfInterest);
		Assert.Equal(options.Revision, forwarded.Revision);
		Assert.Equal("retained", forwarded.AdditionalProperties["test"]);
		Assert.Equal([1, 2], updates.Select(update => update.Usage.PagesProcessed));
		Assert.All(updates, update =>
		{
			Assert.Equal(2, update.Usage.TotalPages);
			Assert.Equal("recognize-documents", update.ModelId);
			Assert.Same(forwarded, update.AdditionalProperties["apple.vision.options"]);
			Assert.Equal(1000, update.Page.AdditionalProperties["apple.sourcePixelWidth"]);
			Assert.Equal(2000, update.Page.AdditionalProperties["apple.sourcePixelHeight"]);
			Assert.Equal(144d, update.Page.PdfPage?.EffectiveDpi);
			Assert.Equal(200d, update.Page.PdfPage?.RequestedDpi);
			Assert.Equal(90, update.Page.PdfPage?.Rotation);
			Assert.Equal("Crop", update.Page.PdfPage?.DisplayBox);
			Assert.Equal(500d, update.Page.PdfPage?.WidthPoints);
			Assert.Equal(1000d, update.Page.PdfPage?.HeightPoints);
			Assert.Equal("Page text", Assert.IsType<JsonElement>(update.RawRepresentation)[0].GetProperty("transcript").GetString());
		});
		Assert.Equal("Label 2", updates[1].Page.PdfPage?.Label);
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractAsync_DefaultOptions_AggregatesPagesAndRawSnapshots()
	{
		var client = new AppleVisionDocumentExtractionClient((_, _, options, token) =>
		{
			Assert.Null(options);
			return Pages(token);
		}, Capabilities);
		using var source = new MemoryStream([1]);
		var result = await client.ExtractAsync(source, "application/pdf");
		Assert.Equal([1, 2], result.Pages.Select(page => page.PageNumber));
		Assert.Equal(2, result.Usage.PagesProcessed);
		Assert.Equal(2, result.Usage.TotalPages);
		Assert.Equal("recognize-documents", result.ModelId);
		var raw = Assert.IsType<object[]>(result.RawRepresentation);
		Assert.Equal(2, raw.Length);
		Assert.Equal("Page text", Assert.IsType<JsonElement>(raw[0])[0].GetProperty("transcript").GetString());
		Assert.True(source.CanRead);
	}

	[Theory]
	[InlineData("model")]
	[InlineData("candidate-low")]
	[InlineData("candidate-high")]
	[InlineData("fraction")]
	[InlineData("fraction-nan")]
	[InlineData("revision")]
	[InlineData("roi-length")]
	[InlineData("roi-nan")]
	[InlineData("roi-outside")]
	[InlineData("language")]
	public async Task ExtractAsync_InvalidOptions_FailsBeforeRecognition(string invalid)
	{
		var options = invalid switch
		{
			"model" => new DocumentExtractionOptions { ModelId = "other" },
			"candidate-low" => new DocumentExtractionOptions { MaximumCandidateCount = 0 },
			"candidate-high" => new DocumentExtractionOptions { MaximumCandidateCount = 11 },
			"fraction" => new DocumentExtractionOptions { MinimumTextHeightFraction = 1.1f },
			"fraction-nan" => new DocumentExtractionOptions { MinimumTextHeightFraction = float.NaN },
			"revision" => new DocumentExtractionOptions { Revision = 0 },
			"roi-length" => new DocumentExtractionOptions { RegionOfInterest = [0, 0, 1] },
			"roi-nan" => new DocumentExtractionOptions { RegionOfInterest = [0, 0, float.NaN, 1] },
			"roi-outside" => new DocumentExtractionOptions { RegionOfInterest = [0.5f, 0, 1, 1] },
			_ => new DocumentExtractionOptions { RecognitionLanguages = [""] },
		};
		var called = false;
		var client = new AppleVisionDocumentExtractionClient((_, _, _, token) =>
		{
			called = true;
			return Pages(token);
		}, Capabilities);
		using var source = new MemoryStream([1]);
		if (invalid == "model")
			await Assert.ThrowsAsync<NotSupportedException>(() => client.ExtractAsync(source, "image/png", options));
		else
			await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ExtractAsync(source, "image/png", options));
		Assert.False(called);
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractAsync_CancelledBeforeStart_DoesNotInvokeRecognition()
	{
		var client = new AppleVisionDocumentExtractionClient((_, _, _, _) =>
			throw new InvalidOperationException("Recognition must not start."), Capabilities);
		using var source = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			client.ExtractAsync(source, "image/png", cancellationToken: cancellation.Token));
		Assert.Equal(0, source.Position);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractPagesAsync_CancelledOrStopped_DisposesEnumeratorButNotSource(bool cancel)
	{
		using var source = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		var disposed = false;
		async IAsyncEnumerable<AppleVisionSourcePage> Recognize(
			[EnumeratorCancellation] CancellationToken token)
		{
			Assert.Equal(cancellation.Token, token);
			try
			{
				await foreach (var page in Pages(token))
					yield return page;
			}
			finally
			{
				disposed = true;
			}
		}
		var client = new AppleVisionDocumentExtractionClient((_, _, _, token) => Recognize(token), Capabilities);
		await using (var enumerator = client.ExtractPagesAsync(source, "application/pdf",
			cancellationToken: cancellation.Token).GetAsyncEnumerator())
		{
			Assert.True(await enumerator.MoveNextAsync());
			if (cancel)
			{
				cancellation.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
			}
		}
		Assert.True(disposed);
		Assert.True(source.CanRead);
	}
}
