#if WINDOWS
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Windows.Foundation;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class WindowsAIChatClientStreamingLifecycleTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	// Exercises the WinRT IAsyncOperationWithProgress bridge's handler identity, synchronous deltas, and final-result deduplication.
	[Fact]
	public async Task ForwardResponseAsync_Progress_IsSynchronousAndOrderedWithoutFinalDuplication()
	{
		var operation = new TestOperation();
		var handler = new StreamingResponseHandler();
		await using var updates = handler.ReadAllAsync(default).GetAsyncEnumerator();
		var forward = WindowsAIChatClient.ForwardResponseAsync(operation, handler, default, text => text);
		var progress = operation.Progress;

		for (var i = 0; i < 10; i++)
		{
			operation.Report(i.ToString());
			var next = updates.MoveNextAsync();
			Assert.True(next.IsCompletedSuccessfully);
			Assert.True(await next);
			Assert.Equal(i.ToString(), updates.Current.Text);
		}

		Assert.Same(progress, operation.Progress);
		operation.Complete("0123456789");
		await forward.WaitAsync(Timeout);
		handler.Complete();
		Assert.False(await updates.MoveNextAsync());
	}

	// Windows final results are complete text, not necessarily identical to progress already reported.
	[Theory]
	[InlineData("Hello", "Hello", "Hello")]
	[InlineData("Hello", "Hello world", "Hello world")]
	[InlineData("{\"value\":", "{\"value\":1}", "{\"value\":1}")]
	public async Task StreamResponseAsync_FinalExtendsProgress_EmitsOnlyMissingSuffix(string progress, string final, string expected)
	{
		var operation = new TestOperation();
		var updates = new List<ChatResponseUpdate>();
		var read = ReadAsync(CreateStream(operation), updates);
		await operation.Attached.Task.WaitAsync(Timeout);
		operation.Report(progress);
		operation.Complete(final);
		await read.WaitAsync(Timeout);
		Assert.Equal(expected, string.Concat(updates.Select(update => update.Text)));
		Assert.Equal(progress == final ? 1 : 2, updates.Count);
	}

	[Fact]
	public async Task StreamResponseAsync_IncompatibleFinal_FailsExplicitly()
	{
		var operation = new TestOperation();
		var updates = new List<ChatResponseUpdate>();
		var read = ReadAsync(CreateStream(operation), updates);
		await operation.Attached.Task.WaitAsync(Timeout);
		operation.Report("Hello");
		operation.Complete("Goodbye");
		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => read.WaitAsync(Timeout));
		Assert.Contains("diverged", error.Message, StringComparison.Ordinal);
		Assert.Equal("Hello", Assert.Single(updates).Text);
	}

	// Managed cancellation is not native Completed: request contexts and shared model leases must survive it.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StreamResponseAsync_DelayedNativeCancellation_RetainsResourcesUntilNativeTerminalCompletion(bool disposeIterator)
	{
		using var cancellation = new CancellationTokenSource();
		var operation = new TestOperation { DelayCancellation = true };
		var model = new TrackedDisposable();
		var context = new TrackedDisposable();
		var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var stream = WindowsAIChatClient.StreamResponseAsync(async (handler, token) =>
		{
			try
			{
				using var requestModel = model;
				using var requestContext = context;
				await WindowsAIChatClient.ForwardResponseAsync(operation, handler, token, text => text);
			}
			finally
			{
				released.TrySetResult();
			}
		}, cancellation.Token);
		await using var iterator = stream.GetAsyncEnumerator();
		try
		{
			var first = iterator.MoveNextAsync().AsTask();
			await operation.Attached.Task.WaitAsync(Timeout);
			operation.Report("First");
			Assert.True(await first.WaitAsync(Timeout));
			if (disposeIterator)
				await iterator.DisposeAsync().AsTask().WaitAsync(Timeout);
			else
			{
				var next = iterator.MoveNextAsync().AsTask();
				cancellation.Cancel();
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(Timeout));
			}
			await operation.CancelRequested.Task.WaitAsync(Timeout);
			Assert.Equal(AsyncStatus.Started, operation.Status);
			Assert.Equal(0, context.DisposeCount);
			Assert.Equal(0, model.DisposeCount);
			Assert.False(released.Task.IsCompleted);

			operation.CompleteCancellation();
			await released.Task.WaitAsync(Timeout);
			Assert.Equal(1, context.DisposeCount);
			Assert.Equal(1, model.DisposeCount);
		}
		finally
		{
			operation.CompleteCancellation();
			await released.Task.WaitAsync(Timeout);
		}
	}

	// Windows's WinRT bridge synthesizes a delta from GetResults when native progress has supplied no text.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StreamResponseAsync_WithoutNonemptyProgress_UsesFinalText(bool emptyProgress)
	{
		var operation = new TestOperation();
		var updates = new List<ChatResponseUpdate>();
		var read = ReadAsync(CreateStream(operation), updates);
		await operation.Attached.Task.WaitAsync(Timeout);

		if (emptyProgress)
			operation.Report("");
		operation.Complete("Final response");

		await read.WaitAsync(Timeout);
		var update = Assert.Single(updates);
		Assert.Equal("Final response", update.Text);
		Assert.Equal("windows-ai-language-model", update.ModelId);
		Assert.Equal(ChatRole.Assistant, update.Role);
	}

	// Windows validates the final WinRT result after progress; this asserts the bridge preserves that exact exception chain.
	[Fact]
	public async Task StreamResponseAsync_FinalResultValidationAfterProgress_PreservesError()
	{
		var operation = new TestOperation();
		var nativeError = new InvalidOperationException("Native failure");
		var cause = new InvalidOperationException("Final result validation failed", nativeError);
		var updates = new List<ChatResponseUpdate>();
		var stream = WindowsAIChatClient.StreamResponseAsync((handler, token) =>
			WindowsAIChatClient.ForwardResponseAsync(operation, handler, token, _ => throw cause));
		var read = ReadAsync(stream, updates);
		await operation.Attached.Task.WaitAsync(Timeout);

		operation.Report("Partial");
		operation.Complete("Partial final");

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => read.WaitAsync(Timeout));
		Assert.Equal("Partial", Assert.Single(updates).Text);
		Assert.Same(cause, error);
		Assert.Same(nativeError, error.InnerException);
	}

	// A null WinRT success payload must fault even if native progress has already yielded partial text.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StreamResponseAsync_NullNativeResult_Faults(bool withProgress)
	{
		var operation = new TestOperation();
		var updates = new List<ChatResponseUpdate>();
		var read = ReadAsync(CreateStream(operation), updates);
		await operation.Attached.Task.WaitAsync(Timeout);
		if (withProgress)
			operation.Report("Partial");
		operation.Complete(null!);

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => read.WaitAsync(Timeout));
		Assert.Contains("null native result", error.Message, StringComparison.Ordinal);
		Assert.Equal(withProgress ? 1 : 0, updates.Count);
	}

	// The WinRT AsyncStatus.Error bridge must preserve the native operation's exception instance.
	[Fact]
	public async Task StreamResponseAsync_NativeError_PreservesException()
	{
		var operation = new TestOperation();
		var cause = new InvalidOperationException("Native operation failure");
		var read = ReadAsync(CreateStream(operation), []);
		await operation.Attached.Task.WaitAsync(Timeout);
		operation.Fail(cause);

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => read.WaitAsync(Timeout));
		Assert.Same(cause, error);
	}

	// Inspects IAsyncOperationWithProgress.Cancel and token propagation inside Windows's WinRT-to-Task bridge.
	[Fact]
	public async Task ForwardResponseAsync_Cancellation_CancelsNativeOperationWithSameToken()
	{
		using var source = new CancellationTokenSource();
		var operation = new TestOperation();
		var forward = WindowsAIChatClient.ForwardResponseAsync(
			operation, new StreamingResponseHandler(), source.Token, text => text);

		source.Cancel();

		await operation.CancelRequested.Task.WaitAsync(Timeout);
		var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => forward.WaitAsync(Timeout));
		Assert.Equal(source.Token, error.CancellationToken);
	}

	// Inspects Windows's worker token and WinRT Cancel callback; the shared streaming API cannot expose these internals.
	[Fact]
	public async Task StreamResponseAsync_EarlyDisposal_CancelsNativeOperationAndCleansUp()
	{
		var operation = new TestOperation();
		var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken workerToken = default;
		var stream = WindowsAIChatClient.StreamResponseAsync(async (handler, token) =>
		{
			workerToken = token;
			try
			{
				await WindowsAIChatClient.ForwardResponseAsync(operation, handler, token, text => text);
			}
			finally
			{
				disposed.TrySetResult();
			}
		});
		var enumerator = stream.GetAsyncEnumerator();
		var first = enumerator.MoveNextAsync().AsTask();
		await operation.Attached.Task.WaitAsync(Timeout);
		operation.Report("First");
		Assert.True(await first.WaitAsync(Timeout));
		Assert.False(disposed.Task.IsCompleted);

		try
		{
			await enumerator.DisposeAsync().AsTask().WaitAsync(Timeout);

			await operation.CancelRequested.Task.WaitAsync(Timeout);
			Assert.True(workerToken.IsCancellationRequested);
			await disposed.Task.WaitAsync(Timeout);
		}
		finally
		{
			await enumerator.DisposeAsync();
		}
	}

	// Windows closes its progress channel before a blocked synchronous native producer returns; this inspects that channel directly.
	[Fact]
	public async Task StreamResponseAsync_EarlyDisposal_ClosesProgressBeforeBlockedProducerFinishes()
	{
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource<StreamingResponseHandler>(TaskCreationOptions.RunContinuationsAsynchronously);
		var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var stream = WindowsAIChatClient.StreamResponseAsync((handler, _) =>
		{
			handler.ProcessContent("First");
			started.TrySetResult(handler);

			try
			{
				release.Wait();
				handler.ProcessContent("Late progress");
				return Task.CompletedTask;
			}
			finally
			{
				finished.TrySetResult();
			}
		});
		await using var enumerator = stream.GetAsyncEnumerator();

		try
		{
			Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));
			var handler = await started.Task.WaitAsync(Timeout);

			await enumerator.DisposeAsync().AsTask().WaitAsync(Timeout);
			Assert.False(finished.Task.IsCompleted);

			var remaining = new List<ChatResponseUpdate>();
			await ReadAsync(handler.ReadAllAsync(default), remaining).WaitAsync(Timeout);
			Assert.Empty(remaining);
		}
		finally
		{
			release.Set();
			await finished.Task.WaitAsync(Timeout);
		}
	}

	// Windows can block while starting a WinRT operation; cancellation must also reach an operation attached after startup resumes.
	[Fact]
	public async Task StreamResponseAsync_CancellationDuringSynchronousStartup_CancelsLateOperationAndCleansUp()
	{
		using var source = new CancellationTokenSource();
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var operation = new TestOperation();
		CancellationToken workerToken = default;
		var stream = WindowsAIChatClient.StreamResponseAsync(async (handler, token) =>
		{
			workerToken = token;
			try
			{
				started.TrySetResult();
				release.Wait();
				await WindowsAIChatClient.ForwardResponseAsync(operation, handler, token, text => text);
			}
			finally
			{
				disposed.TrySetResult();
			}
		}, source.Token);
		await using var enumerator = stream.GetAsyncEnumerator();
		var first = enumerator.MoveNextAsync().AsTask();

		try
		{
			await started.Task.WaitAsync(Timeout);
			source.Cancel();

			var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
			Assert.Equal(source.Token, error.CancellationToken);
			Assert.True(workerToken.WaitHandle.WaitOne(0));
			Assert.False(disposed.Task.IsCompleted);
		}
		finally
		{
			release.Set();
			await disposed.Task.WaitAsync(Timeout);
		}

		await operation.CancelRequested.Task.WaitAsync(Timeout);
	}

	// Asserts Windows's Trace error message for a native startup failure arriving after its progress reader was canceled.
	[Fact]
	public async Task StreamResponseAsync_LateStartupFailure_IsTracedAfterCancellation()
	{
		using var source = new CancellationTokenSource();
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var marker = $"Late startup failure {Guid.NewGuid()}";
		using var listener = new ErrorListener(marker);
		Trace.Listeners.Add(listener);
		var stream = WindowsAIChatClient.StreamResponseAsync((_, _) =>
		{
			started.TrySetResult();
			release.Wait();
			throw new InvalidOperationException(marker);
		}, source.Token);
		await using var enumerator = stream.GetAsyncEnumerator();
		var first = enumerator.MoveNextAsync().AsTask();

		try
		{
			await started.Task.WaitAsync(Timeout);
			source.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Timeout));
			release.Set();

			var message = await listener.Error.Task.WaitAsync(Timeout);
			Assert.Contains("Windows AI generation failed", message, StringComparison.Ordinal);
			Assert.Contains(marker, message, StringComparison.Ordinal);
		}
		finally
		{
			release.Set();
			Trace.Listeners.Remove(listener);
		}
	}

	private static IAsyncEnumerable<ChatResponseUpdate> CreateStream(TestOperation operation) =>
		WindowsAIChatClient.StreamResponseAsync((handler, token) =>
			WindowsAIChatClient.ForwardResponseAsync(operation, handler, token, text => text));

	// WinRT can complete with a failed response status after cancellation has already released the reader.
	[Fact]
	public async Task StreamResponseAsync_LateNativeResultFailure_IsValidatedAndTraced()
	{
		using var source = new CancellationTokenSource();
		var operation = new TestOperation { DelayCancellation = true };
		var marker = $"Late result failure {Guid.NewGuid()}";
		using var listener = new ErrorListener(marker);
		Trace.Listeners.Add(listener);
		var read = ReadAsync(WindowsAIChatClient.StreamResponseAsync((handler, token) =>
			WindowsAIChatClient.ForwardResponseAsync(operation, handler, token,
				_ => throw new InvalidOperationException(marker)), source.Token), []);
		try
		{
			await operation.Attached.Task.WaitAsync(Timeout);
			source.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(Timeout));
			operation.Complete("Native failure result");
			var message = await listener.Error.Task.WaitAsync(Timeout);
			Assert.Contains("Windows AI generation failed", message, StringComparison.Ordinal);
		}
		finally
		{
			operation.CompleteCancellation();
			Trace.Listeners.Remove(listener);
		}
	}

	private static async Task ReadAsync(IAsyncEnumerable<ChatResponseUpdate> stream, List<ChatResponseUpdate> updates)
	{
		await foreach (var update in stream)
			updates.Add(update);
	}

	private sealed class TrackedDisposable : IDisposable
	{
		private int _disposeCount;
		internal int DisposeCount => Volatile.Read(ref _disposeCount);
		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}

	// Deliberately not ITaskAwareAsyncInfo: exercise the WinRT bridge, not its managed-task fast path.
	private sealed class TestOperation : IAsyncOperationWithProgress<string, string>
	{
		private AsyncOperationWithProgressCompletedHandler<string, string>? _completed;
		private string _result = "";

		public TaskCompletionSource Attached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource CancelRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public bool DelayCancellation { get; init; }
		public uint Id => 1;
		public AsyncStatus Status { get; private set; } = AsyncStatus.Started;
		public Exception ErrorCode { get; private set; } = null!;
		public AsyncOperationProgressHandler<string, string> Progress { get; set; } = null!;
		public AsyncOperationWithProgressCompletedHandler<string, string> Completed
		{
			get => _completed!;
			set
			{
				_completed = value;
				if (Status != AsyncStatus.Started)
					value(this, Status);
				Attached.TrySetResult();
			}
		}

		public void Report(string text) => Progress(this, text);

		public void Complete(string result)
		{
			_result = result;
			Finish(AsyncStatus.Completed);
		}

		public void Fail(Exception error)
		{
			ErrorCode = error;
			Finish(AsyncStatus.Error);
		}

		public void Cancel()
		{
			CancelRequested.TrySetResult();
			if (!DelayCancellation)
				CompleteCancellation();
		}

		public void CompleteCancellation() => Finish(AsyncStatus.Canceled);
		public void Close() { }
		public string GetResults() => _result;

		private void Finish(AsyncStatus status)
		{
			if (Status != AsyncStatus.Started)
				return;
			Status = status;
			_completed?.Invoke(this, status);
		}
	}

	private sealed class ErrorListener(string marker) : TraceListener
	{
		public TaskCompletionSource<string> Error { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override void Write(string? message)
		{
			if (message?.Contains(marker, StringComparison.Ordinal) == true)
				Error.TrySetResult(message);
		}

		public override void WriteLine(string? message) => Write(message);
	}
}
#endif
