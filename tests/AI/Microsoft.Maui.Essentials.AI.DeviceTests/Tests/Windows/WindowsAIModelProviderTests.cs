#if WINDOWS
using Microsoft.Extensions.AI;
using Microsoft.Windows.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

// Windows uses a request-scoped owned model, while caller-supplied models remain caller-owned.
public class WindowsAIModelProviderTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	[Fact]
	public async Task Request_NotReady_FailsBeforeCreationOrPreparation()
	{
		var creations = 0;
		using var client = new WindowsAIChatClient(() => AIFeatureReadyState.NotReady, () =>
		{
			Interlocked.Increment(ref creations);
			throw new InvalidOperationException("Must not create");
		});
		var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello")]));
		Assert.Contains("NotReady", error.Message, StringComparison.Ordinal);
		Assert.Contains("EnsureReadyAsync", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, creations);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AcquireAsync_FailedCreation_LaterRequestRetries(bool canceled)
	{
		var count = 0;
		var model = new Model();
		using var models = new WindowsAIModelProvider<Model>(() => ++count == 1
			? canceled ? Task.FromCanceled<Model>(new CancellationToken(true))
				: Task.FromException<Model>(new InvalidOperationException("First creation failed"))
			: Task.FromResult(model), () => AIFeatureReadyState.Ready);

		if (canceled)
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => models.AcquireAsync());
		else
			await Assert.ThrowsAsync<InvalidOperationException>(() => models.AcquireAsync());
		using var second = await models.AcquireAsync();
		Assert.Same(model, second.Model);
		Assert.Equal(2, count);
		second.Dispose();
		Assert.Equal(1, model.DisposeCount);
	}

	[Fact]
	public async Task AcquireAsync_EachRequestOwnsFreshModel()
	{
		var count = 0;
		using var models = new WindowsAIModelProvider<Model>(
			() => Task.FromResult(new Model(Interlocked.Increment(ref count))),
			() => AIFeatureReadyState.Ready);

		var first = await models.AcquireAsync();
		var second = await models.AcquireAsync();
		Assert.NotSame(first.Model, second.Model);
		Assert.Equal(1, first.Model.Id);
		Assert.Equal(2, second.Model.Id);
		first.Dispose();
		second.Dispose();
		Assert.Equal(1, first.Model.DisposeCount);
		Assert.Equal(1, second.Model.DisposeCount);
	}

	[Fact]
	public async Task AcquireAsync_ConcurrentRequests_CreateIndependentModels()
	{
		const int requestCount = 8;
		var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var count = 0;
		using var models = new WindowsAIModelProvider<Model>(async () =>
		{
			var id = Interlocked.Increment(ref count);
			if (id == requestCount)
				allStarted.TrySetResult();
			await release.Task;
			return new Model(id);
		}, () => AIFeatureReadyState.Ready);

		var requests = Enumerable.Range(0, requestCount).Select(_ => models.AcquireAsync()).ToArray();
		await allStarted.Task.WaitAsync(Timeout);
		Assert.Equal(requestCount, count);
		release.SetResult();
		var leases = await Task.WhenAll(requests).WaitAsync(Timeout);
		Assert.Equal(requestCount, leases.Select(lease => lease.Model).Distinct().Count());
		foreach (var lease in leases)
			lease.Dispose();
		Assert.All(leases, lease => Assert.Equal(1, lease.Model.DisposeCount));
	}

	[Fact]
	public async Task AcquireAsync_CallerCancelsDuringCreation_DisposesEventualModel()
	{
		var creation = new TaskCompletionSource<Model>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var models = new WindowsAIModelProvider<Model>(() => creation.Task, () => AIFeatureReadyState.Ready);
		using var cancellation = new CancellationTokenSource();
		var request = models.AcquireAsync(cancellation.Token);
		cancellation.Cancel();
		var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(Timeout));
		Assert.Equal(cancellation.Token, error.CancellationToken);
		var model = new Model();
		creation.SetResult(model);
		await WaitForAsync(() => model.DisposeCount == 1);
	}

	[Fact]
	public async Task Dispose_DuringCreation_DisposesEventualModelExactlyOnce()
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var creation = new TaskCompletionSource<Model>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var models = new WindowsAIModelProvider<Model>(() =>
		{
			started.SetResult();
			return creation.Task;
		}, () => AIFeatureReadyState.Ready);
		var request = models.AcquireAsync();
		await started.Task.WaitAsync(Timeout);
		models.Dispose();
		models.Dispose();
		var model = new Model();
		creation.SetResult(model);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => request.WaitAsync(Timeout));
		Assert.Equal(1, model.DisposeCount);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => models.AcquireAsync());
	}

	[Fact]
	public async Task SuppliedModel_ReadinessChangesAndDispose_NeverReplacesOrDisposes()
	{
		var state = AIFeatureReadyState.Ready;
		var model = new Model();
		using var models = new WindowsAIModelProvider<Model>(model, () => state);
		using var first = await models.AcquireAsync();
		state = AIFeatureReadyState.NotReady;
		await Assert.ThrowsAsync<NotSupportedException>(() => models.AcquireAsync());
		state = AIFeatureReadyState.Ready;
		using var second = await models.AcquireAsync();
		Assert.Same(model, first.Model);
		Assert.Same(model, second.Model);
		models.Dispose();
		first.Dispose();
		second.Dispose();
		Assert.Equal(0, model.DisposeCount);
		await Assert.ThrowsAsync<ObjectDisposedException>(() => models.AcquireAsync());
	}

	[Fact]
	public async Task EnsureReadyAsync_DelayedPreparation_IsAwaitedBeforeReportingPrepared()
	{
		var setup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var reads = 0;
		var prepared = 0;
		var pending = WindowsAIModelFactory.EnsureReadyAsync(
			() => ++reads == 1 ? AIFeatureReadyState.NotReady : AIFeatureReadyState.Ready,
			_ => setup.Task, default, () => prepared++);
		Assert.False(pending.IsCompleted);
		Assert.Equal(0, prepared);
		setup.SetResult();
		await pending.WaitAsync(Timeout);
		Assert.Equal(2, reads);
		Assert.Equal(1, prepared);
	}

	[Fact]
	public async Task EnsureReadyAsync_FailedOrCanceledPreparation_DoesNotReportPrepared()
	{
		var cause = new InvalidOperationException("Native setup failure");
		var prepared = 0;
		var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			WindowsAIModelFactory.EnsureReadyAsync(() => AIFeatureReadyState.NotReady,
				_ => Task.FromException(cause), default, () => prepared++));
		Assert.Same(cause, error);
		using var cancellation = new CancellationTokenSource();
		var setup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = WindowsAIModelFactory.EnsureReadyAsync(() => AIFeatureReadyState.NotReady,
			token => setup.Task.WaitAsync(token), cancellation.Token, () => prepared++);
		cancellation.Cancel();
		var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
		Assert.Equal(cancellation.Token, canceled.CancellationToken);
		Assert.Equal(0, prepared);
	}

	[Fact]
	public async Task EnsureReadyAsync_CallerCancelsAfterNativeSuccess_StillReportsPreparation()
	{
		using var cancellation = new CancellationTokenSource();
		var reads = 0;
		var prepared = 0;
		var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			WindowsAIModelFactory.EnsureReadyAsync(
				() => ++reads == 1 ? AIFeatureReadyState.NotReady : AIFeatureReadyState.Ready,
				_ =>
				{
					cancellation.Cancel();
					return Task.CompletedTask;
				}, cancellation.Token, () => prepared++));
		Assert.Equal(cancellation.Token, error.CancellationToken);
		Assert.Equal(1, prepared);
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		while (!condition())
		{
			if (stopwatch.Elapsed >= Timeout)
				throw new TimeoutException("Timed out waiting for the expected model lifecycle.");
			await Task.Delay(10);
		}
	}

	internal sealed class Model(int id = 0) : IDisposable
	{
		private int _disposeCount;
		internal int Id { get; } = id;
		internal int DisposeCount => Volatile.Read(ref _disposeCount);
		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}
}
#endif
