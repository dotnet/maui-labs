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
	/// Creates a <see cref="LanguageModel"/> instance, ensuring the Windows AI language model is ready.
	/// </summary>
	/// <returns>A ready-to-use <see cref="LanguageModel"/> instance.</returns>
	/// <exception cref="NotSupportedException">
	/// Thrown when the language model is not supported on the current system, disabled by the user, or not ready.
	/// </exception>
	public static async Task<LanguageModel> CreateModelAsync()
	{
		var readyState = LanguageModel.GetReadyState();

		if (readyState is not AIFeatureReadyState.Ready and not AIFeatureReadyState.NotReady)
		{
			var message = readyState switch
			{
				AIFeatureReadyState.NotSupportedOnCurrentSystem => "Not supported on current system",
				AIFeatureReadyState.DisabledByUser => "Disabled by user",
				AIFeatureReadyState.CapabilityMissing => "Missing systemAIModels capability",
				AIFeatureReadyState.NotCompatibleWithSystemHardware => "Not compatible with system hardware",
				AIFeatureReadyState.OSUpdateNeeded => "OS update needed",
				_ => readyState.ToString()
			};
			throw new NotSupportedException($"Windows AI language model is not available: {message}");
		}

		if (readyState is AIFeatureReadyState.NotReady)
		{
			var result = await LanguageModel.EnsureReadyAsync();

			if (result.Status is not AIFeatureReadyResultState.Success)
			{
				var reason = string.IsNullOrWhiteSpace(result.ErrorDisplayText)
					? result.Status.ToString()
					: $"{result.Status}: {result.ErrorDisplayText}";
				throw new NotSupportedException($"Windows AI language model could not be made ready: {reason}", result.ExtendedError);
			}
		}

		var finalState = LanguageModel.GetReadyState();
		if (finalState is not AIFeatureReadyState.Ready)
		{
			throw new NotSupportedException($"Windows AI language model is not available after setup: {finalState}");
		}

		return await LanguageModel.CreateAsync();
	}
}
