using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	private static AppleVisionRecognizeDocumentsCapabilities Capabilities() => new()
	{
		SupportedRecognitionLanguages = ["en-US"], SupportedBarcodeSymbologies = ["qr"], SupportedRevisions = [1, 2],
	};

	private static async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> Pages(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		for (var number = 1; number <= 2; number++)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			using var json = JsonDocument.Parse("""[{"transcript":"Page text","nodes":[{"path":"p","kind":"paragraph","text":"Text"}]}]""");
			yield return new AppleVisionRecognizeDocumentsPageSnapshot
			{
				PageNumber = number, TotalPages = 2, Snapshot = json.RootElement, SourcePixelWidth = 1000,
				SourcePixelHeight = 2000, Revision = 2,
				PdfPage = new AppleVisionRecognizeDocumentsPdfPageInfo
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
			ModelId = "RECOGNIZE-DOCUMENTS",
			AdditionalProperties = new()
			{
				[AppleVisionRecognizeDocumentsOptions.RecognitionLanguages] = new[] { "en-US" },
				[AppleVisionRecognizeDocumentsOptions.CustomWords] = new[] { "Comet" },
				[AppleVisionRecognizeDocumentsOptions.UseLanguageCorrection] = false,
				[AppleVisionRecognizeDocumentsOptions.AutomaticallyDetectLanguage] = true,
				[AppleVisionRecognizeDocumentsOptions.MaximumCandidateCount] = 3,
				[AppleVisionRecognizeDocumentsOptions.MinimumTextHeightFraction] = 0.2f,
				[AppleVisionRecognizeDocumentsOptions.BarcodeDetectionEnabled] = true,
				[AppleVisionRecognizeDocumentsOptions.BarcodeSymbologies] = new[] { "qr" },
				[AppleVisionRecognizeDocumentsOptions.CoalesceCompositeSymbologies] = false,
				[AppleVisionRecognizeDocumentsOptions.RegionOfInterest] = new[] { 0.1f, 0.2f, 0.3f, 0.4f },
				[AppleVisionRecognizeDocumentsOptions.Revision] = 2,
			},
		};
		options.AdditionalProperties["test"] = "retained";
		RecognizeDocumentsRequestOptions? forwarded = null;
		var capabilityCalls = 0;
		var client = new AppleVisionRecognizeDocumentsClient((stream, mediaType, request, token) =>
		{
			Assert.Same(source, stream);
			Assert.Equal("application/pdf", mediaType);
			forwarded = request;
			return Pages(token);
		}, () => { capabilityCalls++; return Capabilities(); });
		Assert.Equal([1, 2], client.GetCapabilities().SupportedRevisions);
		Assert.Equal(["en-US"], client.GetCapabilities().SupportedRecognitionLanguages);
		Assert.Equal(["qr"], client.GetCapabilities().SupportedBarcodeSymbologies);
		Assert.Equal(1, capabilityCalls);
		var updates = new List<DocumentExtractionPageResult>();
		await foreach (var update in client.ExtractPagesAsync(source, "application/pdf", options))
			updates.Add(update);
		Assert.NotNull(forwarded);
		Assert.Equal(["en-US"], forwarded.RecognitionLanguages!);
		Assert.NotSame(options.AdditionalProperties[AppleVisionRecognizeDocumentsOptions.RecognitionLanguages], forwarded.RecognitionLanguages);
		Assert.Equal(["Comet"], forwarded.CustomWords!);
		Assert.False(forwarded.UseLanguageCorrection);
		Assert.True(forwarded.AutomaticallyDetectLanguage);
		Assert.Equal(3, forwarded.MaximumCandidateCount);
		Assert.Equal(0.2f, forwarded.MinimumTextHeightFraction);
		Assert.True(forwarded.BarcodeDetectionEnabled);
		Assert.Equal(["qr"], forwarded.BarcodeSymbologies!);
		Assert.False(forwarded.CoalesceCompositeSymbologies);
		Assert.Equal([0.1f, 0.2f, 0.3f, 0.4f], forwarded.RegionOfInterest!);
		Assert.Equal(2, forwarded.Revision);
		Assert.Equal([1, 2], updates.Select(update => update.PagesProcessed));
		Assert.All(updates, update =>
		{
			Assert.Equal(2, update.TotalPages);
			Assert.Equal(update.PagesProcessed, update.Usage?.PagesProcessed);
			Assert.Equal("recognize-documents", update.AdditionalProperties!["apple.vision.modelId"]);
			var retained = Assert.IsType<DocumentExtractionOptions>(update.AdditionalProperties["apple.vision.options"]);
			Assert.NotSame(options, retained);
			Assert.Equal(options.ModelId, retained.ModelId);
			Assert.Equal("retained", retained.AdditionalProperties!["test"]);
			Assert.Same(forwarded.RecognitionLanguages, retained.AdditionalProperties[AppleVisionRecognizeDocumentsOptions.RecognitionLanguages]);
			Assert.Equal(1000, update.Page.AdditionalProperties!["apple.sourcePixelWidth"]);
			Assert.Equal(2000, update.Page.AdditionalProperties["apple.sourcePixelHeight"]);
			var pdf = Assert.IsType<AppleVisionRecognizeDocumentsPdfPageInfo>(update.Page.AdditionalProperties["apple.pdf.pageInfo"]);
			Assert.Equal(144d, pdf.EffectiveDpi);
			Assert.Equal(200d, pdf.RequestedDpi);
			Assert.Equal(90, pdf.Rotation);
			Assert.Equal("Crop", pdf.DisplayBox);
			Assert.Equal(500d, pdf.WidthPoints);
			Assert.Equal(1000d, pdf.HeightPoints);
			Assert.Equal("Page text", Assert.IsType<JsonElement>(update.RawRepresentation)[0].GetProperty("transcript").GetString());
		});
		Assert.Equal("Label 2", Assert.IsType<AppleVisionRecognizeDocumentsPdfPageInfo>(
			updates[1].Page.AdditionalProperties!["apple.pdf.pageInfo"]).Label);
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractAsync_DefaultOptions_AggregatesPagesAndRawSnapshots()
	{
		var client = new AppleVisionRecognizeDocumentsClient((_, _, options, token) =>
		{
			Assert.Null(options);
			return Pages(token);
		}, Capabilities);
		using var source = new MemoryStream([1]);
		var result = await client.ExtractAsync(source, "application/pdf");
		Assert.Equal([1, 2], result.Pages.Select(page => page.PageNumber));
		Assert.Equal(2, result.Usage?.PagesProcessed);
		Assert.Equal(2, result.AdditionalProperties!["apple.vision.totalPages"]);
		Assert.Equal("recognize-documents", result.AdditionalProperties["apple.vision.modelId"]);
		Assert.Equal("Page text\n\nPage text", result.Text);
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
			"candidate-low" => Options(AppleVisionRecognizeDocumentsOptions.MaximumCandidateCount, 0),
			"candidate-high" => Options(AppleVisionRecognizeDocumentsOptions.MaximumCandidateCount, 11),
			"fraction" => Options(AppleVisionRecognizeDocumentsOptions.MinimumTextHeightFraction, 1.1f),
			"fraction-nan" => Options(AppleVisionRecognizeDocumentsOptions.MinimumTextHeightFraction, float.NaN),
			"revision" => Options(AppleVisionRecognizeDocumentsOptions.Revision, 0),
			"roi-length" => Options(AppleVisionRecognizeDocumentsOptions.RegionOfInterest, new float[] { 0, 0, 1 }),
			"roi-nan" => Options(AppleVisionRecognizeDocumentsOptions.RegionOfInterest, new float[] { 0, 0, float.NaN, 1 }),
			"roi-outside" => Options(AppleVisionRecognizeDocumentsOptions.RegionOfInterest, new float[] { 0.5f, 0, 1, 1 }),
			_ => Options(AppleVisionRecognizeDocumentsOptions.RecognitionLanguages, new[] { "" }),
		};
		var called = false;
		var client = new AppleVisionRecognizeDocumentsClient((_, _, _, token) =>
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

	private static DocumentExtractionOptions Options(string key, object value) =>
		new() { AdditionalProperties = new AdditionalPropertiesDictionary { [key] = value } };

	[Fact]
	public async Task GetService_MetadataCapabilitiesSelfAndKeys_MatchContractWithoutOwningStream()
	{
		var calls = 0;
		var client = new AppleVisionRecognizeDocumentsClient((_, _, _, token) => Pages(token),
			() => { calls++; return Capabilities(); });
		IDocumentExtractionClient contract = client;
		Assert.Throws<ArgumentNullException>(() => contract.GetService(null!));
		Assert.Throws<ArgumentNullException>(() => contract.GetService(null!, "key"));
		Assert.Null(contract.GetService(typeof(string)));
		Assert.Null(contract.GetService(typeof(DocumentExtractionClientMetadata), "key"));
		Assert.Null(contract.GetService(typeof(AppleVisionRecognizeDocumentsCapabilities), "key"));
		Assert.Null(contract.GetService(typeof(IDocumentExtractionClient), "key"));
		Assert.Equal(0, calls);
		var metadata = Assert.IsType<DocumentExtractionClientMetadata>(contract.GetService(typeof(DocumentExtractionClientMetadata)));
		Assert.Same(metadata, contract.GetService(typeof(DocumentExtractionClientMetadata)));
		Assert.Equal("apple.vision", metadata.ProviderName);
		Assert.Equal("recognize-documents", metadata.DefaultModelId);
		Assert.NotNull(metadata.ProviderUri);
		var capabilities = contract.GetService(typeof(AppleVisionRecognizeDocumentsCapabilities));
		Assert.Same(capabilities, contract.GetService(typeof(AppleVisionRecognizeDocumentsCapabilities)));
		Assert.Same(capabilities, client.GetCapabilities());
		Assert.Equal(1, calls);
		Assert.Same(client, contract.GetService(typeof(AppleVisionRecognizeDocumentsClient)));
		Assert.Same(client, contract.GetService(typeof(IDocumentExtractionClient)));
		Assert.Same(client, contract.GetService(typeof(IDisposable)));
		using var source = new MemoryStream([1]);
		await contract.ExtractAsync(source, "application/pdf");
		contract.Dispose();
		contract.Dispose();
		Assert.True(source.CanRead);
	}

	[Theory]
	[InlineData(AppleVisionRecognizeDocumentsOptions.RecognitionLanguages)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.CustomWords)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.UseLanguageCorrection)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.AutomaticallyDetectLanguage)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.MaximumCandidateCount)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.MinimumTextHeightFraction)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.BarcodeDetectionEnabled)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.BarcodeSymbologies)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.CoalesceCompositeSymbologies)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.RegionOfInterest)]
	[InlineData(AppleVisionRecognizeDocumentsOptions.Revision)]
	public async Task ExtractAsync_InvalidProviderOptionTypes_ThrowBeforeRecognition(string key)
	{
		var client = new AppleVisionRecognizeDocumentsClient((_, _, _, _) =>
			throw new InvalidOperationException("Recognition must not start."), Capabilities);
		using var source = new MemoryStream([1]);
		await Assert.ThrowsAsync<ArgumentException>(() => client.ExtractAsync(source, "image/png", Options(key, new object())));
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractPagesAsync_ProviderArrays_AreCopiedBeforeAsynchronousRecognition()
	{
		string[] languages = ["en"], words = ["Comet"], symbologies = ["qr"];
		float[] roi = [0, 0, 1, 1];
		var options = new DocumentExtractionOptions
		{
			AdditionalProperties = new()
			{
				[AppleVisionRecognizeDocumentsOptions.RecognitionLanguages] = languages,
				[AppleVisionRecognizeDocumentsOptions.CustomWords] = words,
				[AppleVisionRecognizeDocumentsOptions.BarcodeSymbologies] = symbologies,
				[AppleVisionRecognizeDocumentsOptions.RegionOfInterest] = roi,
			},
		};
		var client = new AppleVisionRecognizeDocumentsClient((_, _, request, token) =>
		{
			languages[0] = words[0] = symbologies[0] = "mutated";
			roi[2] = 0;
			Assert.Equal(["en"], request!.RecognitionLanguages!);
			Assert.Equal(["Comet"], request.CustomWords!);
			Assert.Equal(["qr"], request.BarcodeSymbologies!);
			Assert.Equal([0, 0, 1, 1], request.RegionOfInterest!);
			return Pages(token);
		}, Capabilities);
		using var source = new MemoryStream([1]);
		var result = await client.ExtractAsync(source, "image/png", options);
		var retained = Assert.IsType<DocumentExtractionOptions>(result.AdditionalProperties!["apple.vision.options"]);
		Assert.Equal(["en"], Assert.IsType<string[]>(retained.AdditionalProperties![AppleVisionRecognizeDocumentsOptions.RecognitionLanguages]));
		Assert.Equal([0, 0, 1, 1], Assert.IsType<float[]>(retained.AdditionalProperties[AppleVisionRecognizeDocumentsOptions.RegionOfInterest]));
		Assert.Null(AppleVisionRecognizeDocumentsOptions.ToRequestOptions(null));
		var defaults = AppleVisionRecognizeDocumentsOptions.ToRequestOptions(new DocumentExtractionOptions())!;
		Assert.Null(defaults.RecognitionLanguages);
		Assert.Null(defaults.MaximumCandidateCount);
		Assert.Null(defaults.Revision);
	}

	[Fact]
	public async Task ExtractAsync_CancelledBeforeStart_DoesNotInvokeRecognition()
	{
		var client = new AppleVisionRecognizeDocumentsClient((_, _, _, _) =>
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
		async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> Recognize(
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
		var client = new AppleVisionRecognizeDocumentsClient((_, _, _, token) => Recognize(token), Capabilities);
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
