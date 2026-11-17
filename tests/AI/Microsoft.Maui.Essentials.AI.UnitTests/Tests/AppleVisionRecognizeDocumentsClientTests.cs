using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
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

	[Fact]
	public async Task ExtractAsync_ReturnsDetachedDocumentAndKeepsCallerStreamOpen()
	{
		using var source = new MemoryStream([1]);
		using var client = new AppleVisionRecognizeDocumentsClient((stream, media, options, token) =>
		{
			Assert.Same(source, stream);
			Assert.Equal("application/pdf", media);
			Assert.Null(options);
			return Pages(token);
		});
		var result = await client.ExtractAsync(source, "input", "application/pdf");
		client.Dispose();
		Assert.Equal([1, 2], result.SourcePages.Select(page => page.PageNumber));
		Assert.Equal("input", result.Identifier);
		Assert.All(result.Sections, section =>
		{
			Assert.Null(section.Content);
			Assert.Equal("Text", Assert.IsType<TextContent>(Assert.Single(section.Elements).Content).Text);
			Assert.Equal("Page text", Assert.IsType<JsonElement>(section.Sources[0].GetRawRepresentation())[0].GetProperty("transcript").GetString());
		});
		Assert.Equal(2, Assert.IsType<JsonElement[]>(result.RawRepresentation).Length);
		Assert.True(source.CanRead);
	}

	[Fact]
	public async Task ExtractAsync_SnapshotsSupportedSettingsBeforeAsyncWorkWithoutChangingDefaults()
	{
		RecognizeDocumentsRequestOptions? native = null;
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, options, token) => { native = options; return Pages(token); });
		using var input = new MemoryStream([1]);
		var languages = new[] { "en" };
		var options = new DocumentExtractionOptions();
		options.AdditionalProperties["apple.vision.recognitionLanguages"] = languages;
		options.AdditionalProperties["apple.vision.barcodeDetectionEnabled"] = false;
		var extraction = client.ExtractAsync(input, "input", "image/png", options);
		options.AdditionalProperties.Clear();
		languages[0] = "fr";
		await extraction;
		Assert.Equal(["en"], native!.RecognitionLanguages!);
		Assert.False(native.BarcodeDetectionEnabled);
		Assert.Null(native.UseLanguageCorrection);
		Assert.Null(native.Revision);
		await client.ExtractAsync(input, "other", "image/png");
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
	public async Task ExtractAsync_UnsupportedExtractorMediaAndCancellationNeverInvokeVision()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, _) =>
			throw new InvalidOperationException("Recognition must not start."));
		using var source = new MemoryStream([1]);
		await Assert.ThrowsAsync<NotSupportedException>(async () => await client.ExtractAsync(source, "input", "image/png", new() { ExtractorName = "other" }));
		await Assert.ThrowsAsync<NotSupportedException>(async () => await client.ExtractAsync(source, "input", "text/plain"));
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await client.ExtractAsync(source, "input", "image/png", cancellationToken: cancellation.Token));
		Assert.True(source.CanRead);
		Assert.Equal(0, source.Position);
		Assert.Same(client, client.GetService(typeof(DocumentExtractionClient)));
		Assert.Null(client.GetService(typeof(string)));
		Assert.Null(client.GetService(typeof(DocumentExtractionClient), "key"));
		client.Dispose();
		Assert.Throws<ObjectDisposedException>(() => client.GetService(typeof(DocumentExtractionClient)));
		await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ExtractAsync(source, "input", "image/png"));
	}

	[Fact]
	public async Task ConcurrentCalls_KeepOptionsAndOutputsIndependent()
	{
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, token) => Pages(token));
		using var first = new MemoryStream([1]);
		using var second = new MemoryStream([2]);
		var calls = await Task.WhenAll(client.ExtractAsync(first, "first", "image/png"),
			client.ExtractAsync(second, "second", "image/png"));
		Assert.NotSame(calls[0].RawRepresentation, calls[1].RawRepresentation);
		Assert.StartsWith("first/", calls[0].SourcePages[0].Identifier);
		Assert.StartsWith("second/", calls[1].SourcePages[0].Identifier);
	}

	[Fact]
	public async Task ExtractAsync_DuplicateSourcePage_RejectsDocumentAndDisposesEnumeratorNotInput()
	{
		using var input = new MemoryStream([1]);
		var disposed = false;
		async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> DuplicatePages()
		{
			try
			{
				await Task.Yield();
				using var snapshot = JsonDocument.Parse("[]");
				yield return new(1, 2, snapshot.RootElement);
				yield return new(1, 2, snapshot.RootElement);
			}
			finally { disposed = true; }
		}
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, _) => DuplicatePages());
		var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
			client.ExtractAsync(input, "input", "application/pdf"));
		Assert.Equal("Vision returned duplicate source page number '1'.", error.Message);
		Assert.True(disposed);
		Assert.True(input.CanRead);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_CancellationOrFailure_DisposesEnumeratorNotSource(bool cancel)
	{
		using var source = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		var disposed = false;
		async IAsyncEnumerable<AppleVisionRecognizeDocumentsPageSnapshot> Recognize([EnumeratorCancellation] CancellationToken token)
		{
			try
			{
				await foreach (var page in Pages(token))
				{
					yield return page;
					if (cancel) cancellation.Cancel();
					else throw new InvalidDataException("Native failure");
				}
			}
			finally { disposed = true; }
		}
		using var client = new AppleVisionRecognizeDocumentsClient((_, _, _, token) => Recognize(token));
		if (cancel)
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExtractAsync(source, "input", "application/pdf", cancellationToken: cancellation.Token));
		else
			await Assert.ThrowsAsync<InvalidDataException>(() => client.ExtractAsync(source, "input", "application/pdf"));
		Assert.True(disposed);
		Assert.True(source.CanRead);
	}
}
