using RootComponentReconciler = Microsoft.Maui.Platforms.Shared.RootComponentReconciler<Microsoft.Maui.Platforms.Shared.Tests.RootComponentReconcilerTests.RootComponent>;

namespace Microsoft.Maui.Platforms.Shared.Tests;

public class RootComponentReconcilerTests
{
	public sealed class RootComponent
	{
		public string? Selector { get; init; }
	}

	[Fact]
	public async Task Replacements_WaitForPendingAdd_AndRemoveBeforeReusingSelector()
	{
		var reconciler = new RootComponentReconciler();
		var first = new RootComponent { Selector = "#app" };
		var replacement = new RootComponent { Selector = "#app" };
		RootComponent[] desired = [first];
		var adding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var operations = new List<string>();
		var active = new HashSet<string>();
		async Task Add(RootComponent component)
		{
			Assert.True(active.Add(component.Selector!));
			if (component == first)
			{
				operations.Add("add first");
				adding.SetResult();
				await release.Task;
			}
			else
				operations.Add("add replacement");
		}
		Task Remove(RootComponent component)
		{
			Assert.True(active.Remove(component.Selector!));
			operations.Add("remove first");
			return Task.CompletedTask;
		}

		var initial = reconciler.SynchronizeAsync(() => desired, Add, Remove);
		await adding.Task.WaitAsync(TimeSpan.FromSeconds(10));
		desired = [];
		var cleared = reconciler.SynchronizeAsync(() => desired, Add, Remove);
		desired = [replacement];
		var replaced = reconciler.SynchronizeAsync(() => desired, Add, Remove);
		Assert.False(cleared.IsCompleted);
		Assert.False(replaced.IsCompleted);
		try
		{
			Assert.Equal(["add first"], operations);
		}
		finally
		{
			release.SetResult();
		}
		await Task.WhenAll(initial, cleared, replaced).WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Equal(["add first", "remove first", "add replacement"], operations);
		Assert.Equal(["#app"], active);
	}

	[Fact]
	public async Task Replacements_WaitForPendingRemoval_WithoutDuplicateRemoveOrAdd()
	{
		var reconciler = new RootComponentReconciler();
		var first = new RootComponent { Selector = "#app" };
		var replacement = new RootComponent { Selector = "#app" };
		RootComponent[] desired = [first];
		var removing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var additions = 0;
		var removals = 0;
		Task Add(RootComponent component)
		{
			additions++;
			return Task.CompletedTask;
		}
		async Task Remove(RootComponent component)
		{
			Assert.Same(first, component);
			removals++;
			removing.SetResult();
			await release.Task;
		}
		await reconciler.SynchronizeAsync(() => desired, Add, Remove);
		desired = [];
		var clear = reconciler.SynchronizeAsync(() => desired, Add, Remove);
		await removing.Task.WaitAsync(TimeSpan.FromSeconds(10));
		desired = [replacement];
		var replace = reconciler.SynchronizeAsync(() => desired, Add, Remove);
		try
		{
			Assert.False(replace.IsCompleted);
			Assert.Equal(1, additions);
			Assert.Equal(1, removals);
		}
		finally
		{
			release.SetResult();
		}
		await Task.WhenAll(clear, replace).WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Equal(2, additions);
		Assert.Equal(1, removals);
	}

	[Fact]
	public async Task UpdateFailure_IsReported_AndDoesNotPoisonLaterReconciliation()
	{
		var reconciler = new RootComponentReconciler();
		var component = new RootComponent { Selector = "#app" };
		var failure = new InvalidOperationException("renderer failed");
		Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
			reconciler.SynchronizeAsync(
				() => [component],
				_ => Task.FromException(failure),
				_ => Task.CompletedTask)));

		var additions = 0;
		await reconciler.SynchronizeAsync(
			() => [component],
			_ => { additions++; return Task.CompletedTask; },
			_ => throw new InvalidOperationException("An unsuccessful add must not be registered."));
		Assert.Equal(1, additions);
	}
}
