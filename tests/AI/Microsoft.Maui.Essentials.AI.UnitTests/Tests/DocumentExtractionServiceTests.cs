using System.Runtime.CompilerServices;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class DocumentExtractionServiceTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_OwnsResultWithoutReadingRawUntilExplicitInspection(bool stream)
	{
		using var client = new Client();
		var progress = new SynchronousProgress();
		using var result = await new DocumentExtractionService().ExtractAsync(client,
			new SelectedDocument("input.png", "image/png", [1]), stream, progress: progress);
		Assert.Equal(2, result.PageCount);
		Assert.Contains("DocumentTableElement", result.Output);
		Assert.Contains("\"IsCovered\": true", result.Output);
		Assert.Contains("\"State\": \"covered\"", result.Output);
		Assert.Contains("\"State\": \"empty\"", result.Output);
		Assert.Contains("\"RowSpan\": 2", result.Output);
		Assert.Contains("\"ColumnSpan\": 2", result.Output);
		Assert.Contains("actual-role", result.Output);
		Assert.Contains("0.123456789012345", result.Output);
		Assert.DoesNotContain("nativeOnly", result.Output);
		Assert.DoesNotContain("wireOnly", result.Output);
		Assert.True(result.HasRawOutput);
		Assert.False(client.Artifact!.IsDisposed);
		Assert.Equal(0, client.NativeReads);
		Assert.Equal(0, client.Body!.ReadCalls);
		Assert.False(client.Input!.CanRead);
		Assert.Equal(stream ? new[] { 1, 2 } : [], progress.Pages);
		var inspected = await result.InspectRawAsync();
		Assert.Contains("nativeOnly", inspected);
		Assert.Contains("wireOnly", inspected);
		Assert.Equal(1, client.NativeReads);
		Assert.True(client.Body.ReadCalls > 0);
		var reads = client.Body.ReadCalls;
		Assert.Equal(inspected, await result.InspectRawAsync());
		Assert.Equal(reads, client.Body.ReadCalls);
		using var raw = JsonDocument.Parse(inspected);
		Assert.Equal(1, raw.RootElement.GetArrayLength());
		result.Dispose();
		Assert.True(client.Artifact.IsDisposed);
		Assert.Equal(1, client.Body.DisposeCount);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ExtractAsync_CaptureOptOutDoesNotInspectRawAndLeaksNoSourceObjects(bool stream)
	{
		using var client = new Client();
		using var result = await new DocumentExtractionService().ExtractAsync(client,
			new SelectedDocument("input.png", "image/png", [1]), stream, new() { CaptureRawOutput = false });
		Assert.Null(client.Artifact);
		Assert.False(result.HasRawOutput);
		Assert.Equal("", await result.InspectRawAsync());
		Assert.DoesNotContain("nativeOnly", result.Output);
		Assert.Contains("Disabled", result.Output);
	}

	[Fact]
	public async Task ExtractAsync_InspectionIsBoundedAndIndependentAfterDisposal()
	{
		using var client = new Client { LargeRaw = true };
		using var result = await new DocumentExtractionService().ExtractAsync(client,
			new SelectedDocument("input.png", "image/png", [1]), false);
		Assert.False(client.Artifact!.IsDisposed);
		var raw = await result.InspectRawAsync();
		result.Dispose();
		Assert.True(client.Artifact.IsDisposed);
		Assert.Contains("\"NativeTruncated\": true", raw);
		Assert.Contains("\"BodyTruncated\": true", raw);
		Assert.True(raw.Length < 600_000);
	}

	[Fact]
	public async Task ExtractAsync_RawGetterFailureDoesNotAffectNormalizedResultOrPageCount()
	{
		using var client = new Client { ThrowRaw = true };
		using var result = await new DocumentExtractionService().ExtractAsync(client,
			new SelectedDocument("input.png", "image/png", [1]), true);
		Assert.Equal(0, client.NativeReads);
		Assert.Equal(2, result.PageCount);
		Assert.Contains("DocumentTableElement", result.Output);
		var raw = await result.InspectRawAsync();
		Assert.Equal(1, client.NativeReads);
		Assert.Contains("Native inspection failed", raw);
		Assert.Contains("wireOnly", raw);
		Assert.DoesNotContain("SECRET", raw);
		Assert.DoesNotContain("inspection failed", result.Output);
	}

	[Fact]
	public async Task Dispose_WithoutInspectionClosesOwnedBodyExactlyOnceAndRejectsLateInspection()
	{
		using var client = new Client();
		var result = await new DocumentExtractionService().ExtractAsync(client,
			new SelectedDocument("input.png", "image/png", [1]), false);
		result.Dispose();
		result.Dispose();
		Assert.True(result.IsDisposed);
		Assert.False(result.HasRawOutput);
		Assert.Equal(2, result.PageCount);
		Assert.NotEmpty(result.Output);
		Assert.Equal(0, client.NativeReads);
		Assert.Equal(0, client.Body!.ReadCalls);
		Assert.Equal(1, client.Body.DisposeCount);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => result.InspectRawAsync());
	}

	[Fact]
	public async Task Dispose_CancelsInFlightInspectionBeforeClosingBody()
	{
		using var client = new Client { BlockRead = true };
		var result = await new DocumentExtractionService().ExtractAsync(client,
			new SelectedDocument("input.png", "image/png", [1]), false);
		var pending = result.InspectRawAsync();
		await client.Body!.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		result.Dispose();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
		Assert.Equal(1, client.Body.DisposeCount);
	}

	[Fact]
	public async Task ExtractAsync_CancellationOrFinalizationFailureDisposesOperationArtifacts()
	{
		foreach (var cancel in new[] { true, false })
		{
			using var cancellation = new CancellationTokenSource();
			using var client = new Client { Cancel = cancel ? cancellation : null, DuplicatePage = !cancel };
			var action = async () => await new DocumentExtractionService().ExtractAsync(client,
				new SelectedDocument("input.png", "image/png", [1]), true, cancellationToken: cancellation.Token);
			if (cancel)
				await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
			else
				await Assert.ThrowsAsync<ArgumentException>(action);
			Assert.True(client.Artifact!.IsDisposed);
		}
	}

	private sealed class SynchronousProgress : IProgress<DocumentExtractionProgress>
	{
		public List<int> Pages { get; } = [];
		public void Report(DocumentExtractionProgress value) => Pages.Add(value.PagesProcessed);
	}

	private sealed class Client : IDocumentExtractionClient
	{
		public DocumentArtifact? Artifact { get; private set; }
		public Stream? Input { get; private set; }
		public TrackingStream? Body { get; private set; }
		public int NativeReads { get; private set; }
		public bool LargeRaw { get; init; }
		public bool ThrowRaw { get; init; }
		public bool BlockRead { get; init; }
		public bool DuplicatePage { get; init; }
		public CancellationTokenSource? Cancel { get; init; }
		public async IAsyncEnumerable<DocumentExtractionPage> ExtractPagesAsync(
			Stream document, string identifier, string mediaType, DocumentExtractionOptions? options = null,
			[EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Input = document;
			var capture = options?.CaptureRawOutput != false;
			var body = LargeRaw ? new string('x', 500_000) : """{"wireOnly":"original body"}""";
			if (capture)
			{
				Body = new TrackingStream(System.Text.Encoding.UTF8.GetBytes(body), BlockRead);
				Artifact = new("application/json", Body);
			}
			var output = new DocumentSourceOutput("operation", capture ? new Native(() =>
			{
				NativeReads++;
				if (ThrowRaw) throw new InvalidOperationException("SECRET getter failure.");
				return LargeRaw ? body : "raw";
			}) : null,
				Artifact, captureDisabled: !capture);
			for (var number = 1; number <= 2; number++)
			{
				await Task.Yield();
				cancellationToken.ThrowIfCancellationRequested();
				var pageNumber = DuplicatePage ? 1 : number;
				var frame = new DocumentSourcePage($"frame/{pageNumber}", pageNumber, new(1, 1),
					DocumentCoordinateUnit.Normalized, DocumentCoordinateOrigin.BottomLeft, output);
				var annotation = new DocumentAnnotation("native-cell", role: "actual-role",
					regions: [new(frame, [new(0.123456789012345, 0), new(1, 0), new(1, 1)])],
					sourceOutput: output, rawRepresentation: capture ? new { nativeOnly = "cell" } : null);
				yield return new(pageNumber,
					[new DocumentExtractionTable($"table/{number}", 0, "", 2, 2, [new(0, 0, null, 2, 2, annotations: [annotation])])],
					sourcePage: frame, sourceOutputs: [output]);
				if (number == 1)
					Cancel?.Cancel();
			}
		}
		public object? GetService(Type serviceType, object? serviceKey = null) => null;
		public void Dispose() { }
	}

	private sealed class Native(Func<string> getter)
	{
		public string nativeOnly => getter();
	}

	private sealed class TrackingStream(byte[] bytes, bool blockRead) : MemoryStream(bytes, writable: false)
	{
		public int ReadCalls { get; private set; }
		public int DisposeCount { get; private set; }
		public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			ReadCalls++;
			ReadStarted.TrySetResult();
			if (blockRead)
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
		}
		protected override void Dispose(bool disposing) { if (disposing) DisposeCount++; base.Dispose(disposing); }
	}
}
