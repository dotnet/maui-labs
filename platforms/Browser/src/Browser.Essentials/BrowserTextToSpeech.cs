using System.Runtime.Versioning;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>Text to speech backed by the Web Speech API (speechSynthesis).</summary>
[SupportedOSPlatform("browser")]
public class BrowserTextToSpeech : ITextToSpeech
{
	public async Task<IEnumerable<Locale>> GetLocalesAsync()
	{
		// Locale has no public constructor, so voices cannot be surfaced through the
		// MAUI Locale type. SpeakAsync honors SpeechOptions.Volume and Pitch; a
		// specific voice can only be selected by the browser's language matching.
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		return [];
	}

	public async Task SpeakAsync(string text, SpeechOptions? options = null, CancellationToken cancelToken = default)
	{
		await BrowserEssentials.WhenInitializedAsync().ConfigureAwait(false);
		cancelToken.ThrowIfCancellationRequested();

		using var registration = cancelToken.CanBeCanceled
			? cancelToken.Register(BrowserEssentialsInterop.SpeechCancel)
			: default;

		try
		{
			await BrowserEssentialsInterop.Speak(
				text,
				options?.Locale?.Language,
				options?.Pitch ?? -1,
				-1,
				options?.Volume ?? -1).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
		{
			throw new FeatureNotSupportedException("The Web Speech API is not available in this browser.");
		}
		cancelToken.ThrowIfCancellationRequested();
	}
}
