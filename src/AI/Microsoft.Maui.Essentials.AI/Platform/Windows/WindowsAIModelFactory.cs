using System.Runtime.Versioning;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Factory for creating and ensuring readiness of Windows AI model instances.
/// </summary>
[SupportedOSPlatform("windows10.0.26100.0")]
internal static class WindowsAIModelFactory
{
	/// <summary>
	/// Creates a <see cref="LanguageModel"/> instance only when the Windows AI language model is already ready.
	/// </summary>
	/// <returns>A ready-to-use <see cref="LanguageModel"/> instance.</returns>
	/// <exception cref="NotSupportedException">
	/// Thrown when the language model is not supported on the current system, disabled by the user, or not ready.
	/// </exception>
	public static async Task<LanguageModel> CreateModelAsync()
	{
		RequireReady(LanguageModel.GetReadyState());
		return await LanguageModel.CreateAsync()
			?? throw new InvalidOperationException("Windows AI returned a null language model.");
	}

	internal static void RequireReady(AIFeatureReadyState state)
	{
		if (state != AIFeatureReadyState.Ready)
			throw new NotSupportedException($"Windows AI language model is not ready: {DescribeReadyState(state)}. Call WindowsAIChatClient.EnsureReadyAsync explicitly to prepare the model.");
	}

	internal static Task EnsureReadyAsync(CancellationToken cancellationToken = default) =>
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
		{
			throw new NotSupportedException($"Windows AI language model is not available: {DescribeReadyState(readyState)}");
		}

		if (readyState is AIFeatureReadyState.NotReady)
			await ensureReady(cancellationToken).ConfigureAwait(false);

		var finalState = getReadyState();
		if (finalState is not AIFeatureReadyState.Ready)
			throw new NotSupportedException($"Windows AI language model is not available after setup: {DescribeReadyState(finalState)}");
		if (readyState is AIFeatureReadyState.NotReady)
			prepared?.Invoke();
		cancellationToken.ThrowIfCancellationRequested();
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

	internal static string DescribeReadyState(AIFeatureReadyState state)
		=> state switch
		{
			AIFeatureReadyState.CapabilityMissing => $"{state}: Missing systemAIModels capability",
			AIFeatureReadyState.NotCompatibleWithSystemHardware =>
				$"{state}: The model is not compatible with the current system hardware",
			AIFeatureReadyState.OSUpdateNeeded => $"{state}: The current OS version is incompatible with the model",
			_ => state.ToString()
		};

	internal static ChatModelAvailability MapAvailability(AIFeatureReadyState state) => state switch
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
}
