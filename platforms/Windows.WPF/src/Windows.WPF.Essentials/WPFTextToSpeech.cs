using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Maui.Media;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public sealed class WPFTextToSpeech : ITextToSpeech
{
	const int AsyncFlag = 1;
	const int PurgeBeforeSpeakFlag = 2;
#if !NET11_0_OR_GREATER
	static readonly ConstructorInfo? LocaleConstructor = typeof(Locale)
		.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
			[typeof(string), typeof(string), typeof(string), typeof(string)], null);
#endif

	public Task<IEnumerable<Locale>> GetLocalesAsync() =>
		RunStaAsync<IEnumerable<Locale>>(() =>
		{
			dynamic voice = CreateVoice();
			try
			{
				dynamic voices = voice.GetVoices(string.Empty, string.Empty);
				var result = new List<Locale>();
				for (var index = 0; index < (int)voices.Count; index++)
				{
					dynamic token = voices.Item(index);
					var id = (string)token.Id;
					var name = (string)token.GetDescription(0);
					var culture = TryGetCulture((string)token.GetAttribute("Language"));
					result.Add(CreateLocale(
						culture?.TwoLetterISOLanguageName ?? string.Empty,
						culture?.Name.Contains('-', StringComparison.Ordinal) == true
							? culture.Name[(culture.Name.IndexOf('-', StringComparison.Ordinal) + 1)..]
							: null,
						name,
						id));
				}

				return result;
			}
			finally
			{
				Marshal.FinalReleaseComObject(voice);
			}
		});

	public async Task SpeakAsync(
		string text,
		SpeechOptions? options = null,
		CancellationToken cancelToken = default)
	{
		ValidateSpeechRequest(text, options);

		await RunStaAsync(() =>
		{
			cancelToken.ThrowIfCancellationRequested();
			dynamic voice = CreateVoice();

			try
			{
				ApplyOptions(voice, options);
				voice.Speak(text, AsyncFlag);
				while (!(bool)voice.WaitUntilDone(100))
				{
					if (!cancelToken.IsCancellationRequested)
						continue;

					voice.Speak(string.Empty, AsyncFlag | PurgeBeforeSpeakFlag);
					cancelToken.ThrowIfCancellationRequested();
				}
			}
			finally
			{
				Marshal.FinalReleaseComObject(voice);
			}
		}).ConfigureAwait(false);
	}

	static dynamic CreateVoice()
	{
		var type = Type.GetTypeFromProgID("SAPI.SpVoice")
			?? throw new FeatureNotSupportedException("Windows SAPI text-to-speech is not available.");
		return Activator.CreateInstance(type)
			?? throw new FeatureNotSupportedException("Windows SAPI text-to-speech could not be initialized.");
	}

	static void ApplyOptions(dynamic voice, SpeechOptions? options)
	{
		if (options is null)
			return;

		if (options.Volume.HasValue)
			voice.Volume = (int)Math.Round(options.Volume.Value * 100);
		if (options.Rate.HasValue)
			voice.Rate = (int)Math.Round((options.Rate.Value - 1f) * 10f);

		if (options.Locale is { } locale && HasVoiceSelector(locale))
		{
			dynamic voices = voice.GetVoices(string.Empty, string.Empty);
			for (var index = 0; index < (int)voices.Count; index++)
			{
				dynamic token = voices.Item(index);
				var culture = TryGetCulture((string)token.GetAttribute("Language"));
				if (MatchesVoice(
					(string)token.Id,
					(string)token.GetDescription(0),
					culture?.Name,
					culture?.Name.Contains('-', StringComparison.Ordinal) == true
						? culture.Name[(culture.Name.IndexOf('-', StringComparison.Ordinal) + 1)..]
						: null,
					locale))
				{
					voice.Voice = token;
					break;
				}
			}
		}
	}

	internal static void ValidateSpeechRequest(string text, SpeechOptions? options)
	{
		if (string.IsNullOrEmpty(text))
			throw new ArgumentNullException(nameof(text), "Text cannot be null or empty.");

		if (options?.Volume is { } volume && (volume < 0f || volume > 1f))
			throw new ArgumentOutOfRangeException(nameof(options.Volume), "Volume must be between 0 and 1.");
		if (options?.Pitch is { } pitch && (pitch < 0f || pitch > 2f))
			throw new ArgumentOutOfRangeException(nameof(options.Pitch), "Pitch must be between 0 and 2.");
		if (options?.Rate is { } rate && (rate < 0.1f || rate > 2f))
			throw new ArgumentOutOfRangeException(nameof(options.Rate), "Rate must be between 0.1 and 2.");
		if (options?.Pitch.HasValue == true)
		{
			throw new FeatureNotSupportedException(
				"Windows SAPI voice pitch is not supported by this WPF backend.");
		}
	}

	static bool HasVoiceSelector(Locale locale) =>
		!string.IsNullOrWhiteSpace(locale.Id) ||
		!string.IsNullOrWhiteSpace(locale.Language) ||
		!string.IsNullOrWhiteSpace(locale.Name);

	static CultureInfo? TryGetCulture(string? hexadecimalLcid)
	{
		if (string.IsNullOrWhiteSpace(hexadecimalLcid))
			return null;

		var firstLcid = hexadecimalLcid.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];
		return int.TryParse(firstLcid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid)
			? CultureInfo.GetCultureInfo(lcid)
			: null;
	}

	internal static bool MatchesVoice(
		string voiceId,
		string voiceName,
		string? voiceLanguage,
		string? voiceCountry,
		Locale locale)
	{
		if (!string.IsNullOrWhiteSpace(locale.Id))
			return string.Equals(voiceId, locale.Id, StringComparison.OrdinalIgnoreCase);

		if (!string.IsNullOrWhiteSpace(locale.Language))
		{
			if (!LanguageMatches(voiceLanguage, locale.Language))
				return false;

			return string.IsNullOrWhiteSpace(locale.Country) ||
				string.Equals(voiceCountry, locale.Country, StringComparison.OrdinalIgnoreCase);
		}

		return string.Equals(voiceName, locale.Name, StringComparison.OrdinalIgnoreCase);
	}

	static bool LanguageMatches(string? voiceLanguage, string requestedLanguage)
	{
		if (string.IsNullOrWhiteSpace(voiceLanguage))
			return false;
		if (string.Equals(voiceLanguage, requestedLanguage, StringComparison.OrdinalIgnoreCase))
			return true;

		var separator = voiceLanguage.IndexOf('-');
		return separator > 0 &&
			string.Equals(
				voiceLanguage[..separator],
				requestedLanguage,
				StringComparison.OrdinalIgnoreCase);
	}

	internal static Locale CreateLocale(string language, string? country, string name, string id)
	{
#if NET11_0_OR_GREATER
		return new Locale(language, country, name, id);
#else
		if (LocaleConstructor is null)
			throw new MissingMethodException(
				"MAUI 10 Locale does not expose the expected four-string constructor.");

		return (Locale)LocaleConstructor.Invoke([language, country, name, id]);
#endif
	}

	static Task RunStaAsync(Action action) =>
		RunStaAsync(() =>
		{
			action();
			return true;
		});

	static Task<T> RunStaAsync<T>(Func<T> action)
	{
		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			try
			{
				completion.TrySetResult(action());
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
		})
		{
			IsBackground = true,
			Name = "WPF text-to-speech",
		};
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return completion.Task;
	}
}
