using Microsoft.Windows.AI;

namespace Microsoft.Maui.Essentials.AI;

// Owned models are request-scoped. A supplied model remains caller-owned.
internal sealed class WindowsAIModelProvider<T> : IDisposable where T : class, IDisposable
{
	private readonly Func<AIFeatureReadyState> _getReadyState;
	private readonly Func<Task<T>>? _createModel;
	private readonly T? _suppliedModel;
	private int _disposed;

	internal WindowsAIModelProvider(
		Func<Task<T>> createModel,
		Func<AIFeatureReadyState> getReadyState)
	{
		_createModel = createModel;
		_getReadyState = getReadyState;
	}

	internal WindowsAIModelProvider(T suppliedModel, Func<AIFeatureReadyState> getReadyState)
	{
		_suppliedModel = suppliedModel;
		_getReadyState = getReadyState;
	}

	internal void ThrowIfDisposed() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	internal async Task<Lease> AcquireAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		WindowsAIModelFactory.RequireReady(_getReadyState());

		if (_suppliedModel is not null)
		{
			ThrowIfDisposed();
			cancellationToken.ThrowIfCancellationRequested();
			return new Lease(_suppliedModel, ownsModel: false);
		}

		// A native creation entry point can block. Keep it off the caller's thread,
		// and dispose its eventual model if this request leaves before creation ends.
		var creation = Task.Run(_createModel!);
		T model;
		try
		{
			model = await creation.WaitAsync(cancellationToken).ConfigureAwait(false)
				?? throw new InvalidOperationException("Windows AI returned a null language model.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_ = DisposeAfterCreationAsync(creation);
			throw;
		}

		try
		{
			ThrowIfDisposed();
			cancellationToken.ThrowIfCancellationRequested();
			return new Lease(model, ownsModel: true);
		}
		catch
		{
			model.Dispose();
			throw;
		}
	}

	private static async Task DisposeAfterCreationAsync(Task<T> creation)
	{
		try
		{
			(await creation.ConfigureAwait(false))?.Dispose();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.TraceError($"Windows AI model creation failed after its request ended: {ex}");
		}
	}

	public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

	internal sealed class Lease : IDisposable
	{
		private T? _ownedModel;

		internal T Model { get; }

		internal Lease(T model, bool ownsModel)
		{
			Model = model;
			_ownedModel = ownsModel ? model : null;
		}

		public void Dispose() => Interlocked.Exchange(ref _ownedModel, null)?.Dispose();
	}
}
