using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using Microsoft.Windows.AI.Text;
using System.Runtime.Versioning;
using Windows.Foundation;

// CS8305: the Windows AI imaging models (ImageGenerator, ImageDescriptionGenerator) are marked
// [Experimental] in WinRT metadata. Stable Windows App SDK 2.5 does not expose image generation.
#pragma warning disable CS8305

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
		await EnsureReadyAsync(
			"Windows AI language model",
			LanguageModel.GetReadyState,
			LanguageModel.EnsureReadyAsync);

		return await LanguageModel.CreateAsync();
	}

	/// <summary>
	/// Creates an <see cref="ImageGenerator"/> instance, ensuring the Windows image generation model is ready.
	/// </summary>
	/// <returns>A ready-to-use <see cref="ImageGenerator"/> instance.</returns>
	/// <exception cref="NotSupportedException">
	/// Thrown when image generation is not supported on the current system, disabled by the user, or not ready.
	/// </exception>
	public static async Task<ImageGenerator> CreateImageGeneratorAsync()
	{
		await EnsureReadyAsync(
			"Windows image generation",
			ImageGenerator.GetReadyState,
			ImageGenerator.EnsureReadyAsync);

		return await ImageGenerator.CreateAsync();
	}

	/// <summary>
	/// Runs the shared Windows AI readiness handshake: check the state, download the model if
	/// needed, then confirm it became ready.
	/// </summary>
	/// <param name="featureName">The display name used in error messages.</param>
	/// <param name="getReadyState">Reads the current readiness of the feature.</param>
	/// <param name="ensureReadyAsync">Makes the feature ready, downloading the model if required.</param>
	/// <exception cref="NotSupportedException">Thrown when the feature cannot be made ready.</exception>
	private static async Task EnsureReadyAsync(
		string featureName,
		Func<AIFeatureReadyState> getReadyState,
		Func<IAsyncOperationWithProgress<AIFeatureReadyResult, double>> ensureReadyAsync)
	{
		var readyState = getReadyState();

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
			throw new NotSupportedException($"{featureName} is not available: {message}");
		}

		if (readyState is AIFeatureReadyState.NotReady)
		{
			var result = await ensureReadyAsync();

			if (result.Status is not AIFeatureReadyResultState.Success)
			{
				var reason = string.IsNullOrWhiteSpace(result.ErrorDisplayText)
					? result.Status.ToString()
					: $"{result.Status}: {result.ErrorDisplayText}";
				throw new NotSupportedException($"{featureName} could not be made ready: {reason}", result.ExtendedError);
			}
		}

		var finalState = getReadyState();
		if (finalState is not AIFeatureReadyState.Ready)
		{
			throw new NotSupportedException($"{featureName} is not available after setup: {finalState}");
		}
	}
}
