using Microsoft.Extensions.AI;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;

namespace Microsoft.Maui.Essentials.AI;

public sealed partial class WindowsAIChatClient
{
	private readonly LanguageModel? _suppliedModel;
	private int _disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsAIChatClient"/> class.
	/// </summary>
	/// <remarks>
	/// The client creates and disposes a separate <see cref="LanguageModel"/> for each request.
	/// </remarks>
	public WindowsAIChatClient() { }

	/// <summary>
	/// Initializes a new instance of the <see cref="WindowsAIChatClient"/> class
	/// with the specified <see cref="LanguageModel"/>.
	/// </summary>
	/// <param name="model">The <see cref="LanguageModel"/> to use for chat interactions.</param>
	/// <remarks>
	/// When using this constructor, the client does not own the <see cref="LanguageModel"/>
	/// and will not dispose it. The caller is responsible for disposing the model.
	/// </remarks>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="model"/> is <see langword="null"/>.</exception>
	public WindowsAIChatClient(LanguageModel model)
	{
		ArgumentNullException.ThrowIfNull(model);
		_suppliedModel = model;
	}

	/// <summary>Reads current native availability and effective capabilities without caching or downloading.</summary>
	/// <param name="cancellationToken">A token that cancels the status request before the native read.</param>
	/// <returns>A fresh snapshot preserving the unavailable reason from <see cref="LanguageModel.GetReadyState"/>.</returns>
	public static Task<ChatModelStatus> GetModelStatusAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(CreateModelStatus(LanguageModel.GetReadyState()));
	}

	internal static ChatModelStatus CreateModelStatus(AIFeatureReadyState state) =>
		new(
			MapAvailability(state),
			state is AIFeatureReadyState.Ready ? null : state.ToString(),
			[ChatModelCapability.Text, ChatModelCapability.Streaming, ChatModelCapability.StructuredOutput]);

	/// <summary>Ensures the system language model is ready, downloading its components if necessary.</summary>
	/// <param name="cancellationToken">A token that cancels native setup.</param>
	/// <returns>A task that completes when the native model reports ready.</returns>
	/// <exception cref="NotSupportedException">The native state or setup result prevents use; native error details are preserved.</exception>
	public static Task EnsureReadyAsync(CancellationToken cancellationToken = default) =>
		EnsureReadyAsync(LanguageModel.GetReadyState, async token =>
		{
			var operation = LanguageModel.EnsureReadyAsync();
			var completion = operation.AsTask();
			using var registration = token.Register(() =>
			{
				try
				{
					operation.Cancel();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Trace.TraceError($"Windows AI preparation cancellation failed: {ex}");
				}
			});
			AIFeatureReadyResult result;
			try
			{
				result = await completion.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				throw new OperationCanceledException(token);
			}
			if (result is null)
				throw new InvalidOperationException("Windows AI returned a null model setup result.");
			ValidateReadyResult(result.Status, result.ErrorDisplayText, result.ExtendedError);
		}, cancellationToken);

	internal static async Task EnsureReadyAsync(
		Func<AIFeatureReadyState> getReadyState,
		Func<CancellationToken, Task> ensureReady,
		CancellationToken cancellationToken,
		Action? prepared = null)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var readyState = getReadyState();
		if (readyState is not AIFeatureReadyState.Ready and not AIFeatureReadyState.NotReady)
			throw new NotSupportedException($"Windows AI language model is not available: {DescribeReadyState(readyState)}");

		if (readyState is AIFeatureReadyState.NotReady)
			await ensureReady(cancellationToken).ConfigureAwait(false);

		var finalState = getReadyState();
		if (finalState is not AIFeatureReadyState.Ready)
			throw new NotSupportedException($"Windows AI language model is not available after setup: {DescribeReadyState(finalState)}");
		if (readyState is AIFeatureReadyState.NotReady)
			prepared?.Invoke();
		cancellationToken.ThrowIfCancellationRequested();
	}

	private async Task<ModelLease<LanguageModel>> AcquireModelAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		cancellationToken.ThrowIfCancellationRequested();
		RequireReady(LanguageModel.GetReadyState());

		if (_suppliedModel is not null)
		{
			ThrowIfDisposed();
			cancellationToken.ThrowIfCancellationRequested();
			return new ModelLease<LanguageModel>(_suppliedModel, ownsModel: false);
		}

		var creation = Task.Run(CreateModelAsync);
		var model = await AwaitModelCreationAsync(creation, cancellationToken).ConfigureAwait(false);
		try
		{
			ThrowIfDisposed();
			cancellationToken.ThrowIfCancellationRequested();
			return new ModelLease<LanguageModel>(model, ownsModel: true);
		}
		catch
		{
			model.Dispose();
			throw;
		}
	}

	private static async Task<LanguageModel> CreateModelAsync()
	{
		RequireReady(LanguageModel.GetReadyState());
		return await LanguageModel.CreateAsync()
			?? throw new InvalidOperationException("Windows AI returned a null language model.");
	}

	internal static async Task<T> AwaitModelCreationAsync<T>(
		Task<T> creation,
		CancellationToken cancellationToken)
		where T : class, IDisposable
	{
		try
		{
			return await creation.WaitAsync(cancellationToken).ConfigureAwait(false)
				?? throw new InvalidOperationException("Windows AI returned a null language model.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_ = DisposeAfterCreationAsync(creation);
			throw;
		}
	}

	private static async Task DisposeAfterCreationAsync<T>(Task<T> creation)
		where T : class, IDisposable
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

	private void ThrowIfDisposed() =>
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	internal static void RequireReady(AIFeatureReadyState state)
	{
		if (state != AIFeatureReadyState.Ready)
			throw new NotSupportedException($"Windows AI language model is not ready: {DescribeReadyState(state)}. Call WindowsAIChatClient.EnsureReadyAsync explicitly to prepare the model.");
	}

	internal static void ValidateReadyResult(AIFeatureReadyResultState status, string? errorDisplayText, Exception? extendedError)
	{
		if (status is not AIFeatureReadyResultState.Success)
		{
			var reason = string.IsNullOrWhiteSpace(errorDisplayText)
				? status.ToString()
				: $"{status}: {errorDisplayText}";
			throw new NotSupportedException($"Windows AI language model could not be made ready: {reason}", extendedError);
		}
	}

	internal static string DescribeReadyState(AIFeatureReadyState state) =>
		state switch
		{
			AIFeatureReadyState.CapabilityMissing => $"{state}: Missing systemAIModels capability",
			AIFeatureReadyState.NotCompatibleWithSystemHardware =>
				$"{state}: The model is not compatible with the current system hardware",
			AIFeatureReadyState.OSUpdateNeeded => $"{state}: The current OS version is incompatible with the model",
			_ => state.ToString()
		};

	internal static ChatModelAvailability MapAvailability(AIFeatureReadyState state) =>
		state switch
		{
			AIFeatureReadyState.Ready => ChatModelAvailability.Available,
			AIFeatureReadyState.NotReady => ChatModelAvailability.NotReady,
			AIFeatureReadyState.NotSupportedOnCurrentSystem or
				AIFeatureReadyState.NotCompatibleWithSystemHardware => ChatModelAvailability.NotSupported,
			AIFeatureReadyState.DisabledByUser => ChatModelAvailability.Disabled,
			AIFeatureReadyState.CapabilityMissing or
				AIFeatureReadyState.OSUpdateNeeded => ChatModelAvailability.RequirementsNotMet,
			_ => ChatModelAvailability.Unknown,
		};

	void IDisposable.Dispose() => Interlocked.Exchange(ref _disposed, 1);

	internal sealed class ModelLease<T> : IDisposable
		where T : class, IDisposable
	{
		private T? _ownedModel;

		internal T Model { get; }

		internal ModelLease(T model, bool ownsModel)
		{
			Model = model;
			_ownedModel = ownsModel ? model : null;
		}

		public void Dispose() => Interlocked.Exchange(ref _ownedModel, null)?.Dispose();
	}
}
