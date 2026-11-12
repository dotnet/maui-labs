namespace Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;

internal sealed class RootComponentReconciler
{
	readonly SemaphoreSlim _gate = new(1, 1);
	readonly HashSet<RootComponent> _registered = [];

	internal async Task SynchronizeAsync(
		Func<IEnumerable<RootComponent>> getDesired,
		Func<RootComponent, Task> add,
		Func<RootComponent, Task> remove)
	{
		await _gate.WaitAsync();
		try
		{
			// Read after acquiring the gate: an earlier asynchronous update may be stale.
			var desired = getDesired().ToHashSet();
			foreach (var item in _registered.Except(desired).ToArray())
			{
				await remove(item);
				_registered.Remove(item);
			}
			foreach (var item in desired.Except(_registered).ToArray())
			{
				await add(item);
				_registered.Add(item);
			}
		}
		finally
		{
			_gate.Release();
		}
	}
}
