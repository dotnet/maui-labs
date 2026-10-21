#if WINDOWS
using Microsoft.Extensions.AI;
using Microsoft.Windows.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

// Windows owns request-scoped models, while caller-supplied models remain caller-owned.
public class WindowsAIChatClientLifecycleTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	[Fact]
	public void RequireReady_NotReady_PreservesStateAndExplicitPreparationGuidance()
	{
		var error = Assert.Throws<NotSupportedException>(() =>
			WindowsAIChatClient.RequireReady(AIFeatureReadyState.NotReady));
		Assert.Contains("NotReady", error.Message, StringComparison.Ordinal);
		Assert.Contains("EnsureReadyAsync", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AwaitModelCreationAsync_Failure_Propagates(bool canceled)
	{
		var cause = new InvalidOperationException("Creation failed");
		var creation = canceled
			? Task.FromCanceled<Model>(new CancellationToken(true))
			: Task.FromException<Model>(cause);

		if (canceled)
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
				WindowsAIChatClient.AwaitModelCreationAsync(creation, default));
		else
			Assert.Same(cause, await Assert.ThrowsAsync<InvalidOperationException>(() =>
				WindowsAIChatClient.AwaitModelCreationAsync(creation, default)));
	}

	[Fact]
	public async Task AwaitModelCreationAsync_CallerCancels_DisposesEventualModel()
	{
		var creation = new TaskCompletionSource<Model>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new CancellationTokenSource();
		var request = WindowsAIChatClient.AwaitModelCreationAsync(creation.Task, cancellation.Token);
		cancellation.Cancel();
		var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(Timeout));
		Assert.Equal(cancellation.Token, error.CancellationToken);

		var model = new Model();
		creation.SetResult(model);
		await WaitForAsync(() => model.DisposeCount == 1);
	}

	[Fact]
	public void ModelLease_OwnedModel_DisposesExactlyOnce()
	{
		var model = new Model();
		var lease = new WindowsAIChatClient.ModelLease<Model>(model, ownsModel: true);
		Assert.Same(model, lease.Model);
		lease.Dispose();
		lease.Dispose();
		Assert.Equal(1, model.DisposeCount);
	}

	[Fact]
	public void ModelLease_SuppliedModel_RemainsCallerOwned()
	{
		var model = new Model();
		var lease = new WindowsAIChatClient.ModelLease<Model>(model, ownsModel: false);
		Assert.Same(model, lease.Model);
		lease.Dispose();
		Assert.Equal(0, model.DisposeCount);
	}

	[Fact]
	public async Task DisposedClient_RejectsBeforeNativeWork()
	{
		using var client = new WindowsAIChatClient();
		((IDisposable)client).Dispose();
		await Assert.ThrowsAsync<ObjectDisposedException>(() =>
			client.GetResponseAsync([new(ChatRole.User, "Hello")]));
	}

	[Fact]
	public async Task EnsureReadyAsync_DelayedPreparation_IsAwaitedBeforeReportingPrepared()
	{
		var setup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var reads = 0;
		var prepared = 0;
		var pending = WindowsAIChatClient.EnsureReadyAsync(
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
			WindowsAIChatClient.EnsureReadyAsync(() => AIFeatureReadyState.NotReady,
				_ => Task.FromException(cause), default, () => prepared++));
		Assert.Same(cause, error);
		using var cancellation = new CancellationTokenSource();
		var setup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = WindowsAIChatClient.EnsureReadyAsync(() => AIFeatureReadyState.NotReady,
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
			WindowsAIChatClient.EnsureReadyAsync(
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

	internal sealed class Model : IDisposable
	{
		private int _disposeCount;
		internal int DisposeCount => Volatile.Read(ref _disposeCount);
		public void Dispose() => Interlocked.Increment(ref _disposeCount);
	}
}
#endif
