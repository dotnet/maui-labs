using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class AppleVisionRecognizeDocumentsClientTests
{
	private static async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> Pages(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		for (var number = 1; number <= 2; number++)
		{
			await Task.Yield();
			cancellationToken.ThrowIfCancellationRequested();
			using var json = JsonDocument.Parse("""[{"transcript":"Page text","nodes":[{"path":"p","kind":"paragraph","text":"Text"}]}]""");
			yield return new(number, 2, json.RootElement);
		}
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_UsesOriginalFinalizerAndKeepsCallerStreamOpen(bool capture)
	{
		using var source = new MemoryStream([1]);
		using var client = new AppleVisionRecognizeDocumentsClient((stream, media, options, token) =>
		{
			Assert.Same(source, stream);
			Assert.Equal("application/pdf", media);
			Assert.Null(options);
			return Pages(token);
		});
		using var result = await client.ExtractAsync(source, "input", "application/pdf", new() { CaptureRawOutput = capture });
		Assert.Equal([1, 2], result.Pages.Select(page => page.PageNumber));
		Assert.Equal("input", result.Document.Identifier);
		Assert.Null(result.Document.StoredMarkdown);
		Assert.All(result.Pages, page => Assert.Null(page.Markdown));
		Assert.Equal("Text" + Environment.NewLine + "Text", result.Document.ToMarkdown());
		Assert.All(result.Document.Sections, section => Assert.Equal("Page text", section.Annotations[0].Value));
		Assert.All(result.Document.SourceOutputs, output =>
			Assert.Equal(capture ? DocumentRawContentStatus.Retained : DocumentRawContentStatus.Disabled, output.NativeStatus));
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractPagesAsync_SnapshotsSupportedSettingsBeforeEnumerationWithoutChangingDefaults()
	{
		RecognizeDocumentsRequestOptions? native = null;
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, options, token) => { native = options; return Pages(token); });
		using var input = new MemoryStream([1]);
		var languages = new[] { "en" };
		var options = new DocumentExtractionOptions();
		options.AdditionalProperties["apple.vision.recognitionLanguages"] = languages;
		options.AdditionalProperties["apple.vision.barcodeDetectionEnabled"] = false;
		var pages = client.ExtractPagesAsync(input, "input", "image/png", options);
		options.CaptureRawOutput = false;
		options.AdditionalProperties.Clear();
		languages[0] = "fr";
		using var result = await pages.ToDocumentExtractionResultAsync("input");
		Assert.Equal(["en"], native!.RecognitionLanguages!);
		Assert.False(native.BarcodeDetectionEnabled);
		Assert.Null(native.UseLanguageCorrection);
		Assert.Null(native.Revision);
		Assert.Equal(DocumentRawContentStatus.Retained, result.Document.SourceOutputs[0].NativeStatus);
		using var defaults = await client.ExtractAsync(input, "other", "image/png");
		Assert.Null(native);
	}

	[Theory]
	[InlineData("caller.option", 42)]
	[InlineData("apple.vision.revision", 0)]
	[InlineData("apple.vision.maximumCandidateCount", 11)]
	[InlineData("apple.vision.useLanguageCorrection", "true")]
	public async Task ExtractAsync_RejectsUnsupportedOrInvalidOptionsBeforeNativeCall(string key, object value)
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		var options = new DocumentExtractionOptions();
		options.AdditionalProperties[key] = value;
		if (key == "caller.option")
			await Assert.ThrowsAsync<NotSupportedException>(async () => await client.ExtractAsync(source, "input", "image/png", options));
		else
			await Assert.ThrowsAnyAsync<ArgumentException>(async () => await client.ExtractAsync(source, "input", "image/png", options));
		Assert.Equal(0, source.Position);
		Assert.Same(value, options.AdditionalProperties[key]);
	}

	[Fact]
	public async Task ExtractAsync_UnsupportedModelMediaAndCancellationNeverInvokeVision()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		await Assert.ThrowsAsync<NotSupportedException>(async () => await client.ExtractAsync(source, "input", "image/png", new() { ModelId = "other" }));
		await Assert.ThrowsAsync<NotSupportedException>(async () => await client.ExtractAsync(source, "input", "text/plain"));
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await client.ExtractAsync(source, "input", "image/png", cancellationToken: cancellation.Token));
		Assert.True(source.CanRead);
		Assert.Equal(0, source.Position);
		Assert.Same(client, client.GetService(typeof(IDocumentExtractionClient)));
		Assert.Null(client.GetService(typeof(string)));
		Assert.Null(client.GetService(typeof(IDocumentExtractionClient), "key"));
		client.Dispose();
		Assert.Throws<ObjectDisposedException>(() => client.GetService(typeof(IDocumentExtractionClient)));
	}

	[Fact]
	public async Task ConcurrentCalls_KeepOptionsAndOutputsIndependent()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, token) => Pages(token));
		using var first = new MemoryStream([1]);
		using var second = new MemoryStream([2]);
		var calls = await Task.WhenAll(client.ExtractAsync(first, "first", "image/png"),
			client.ExtractAsync(second, "second", "image/png", new() { CaptureRawOutput = false }));
		using var a = calls[0];
		using var b = calls[1];
		Assert.NotSame(a.Document.SourceOutputs[0], b.Document.SourceOutputs[0]);
		Assert.Equal(DocumentRawContentStatus.Retained, a.Document.SourceOutputs[0].NativeStatus);
		Assert.Equal(DocumentRawContentStatus.Disabled, b.Document.SourceOutputs[0].NativeStatus);
		Assert.StartsWith("first/", a.Pages[0].Observations[0].ProviderId);
		Assert.StartsWith("second/", b.Pages[0].Observations[0].ProviderId);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractPagesAsync_CancelledOrStopped_DisposesEnumeratorNotSource(bool cancel)
	{
		using var source = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		var disposed = false;
		async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> Recognize([EnumeratorCancellation] CancellationToken token)
		{
			try { await foreach (var page in Pages(token)) yield return page; }
			finally { disposed = true; }
		}
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, token) => Recognize(token));
		await using (var enumerator = client.ExtractPagesAsync(source, "input", "application/pdf",
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
