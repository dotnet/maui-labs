using System.Diagnostics;
using System.Globalization;
using Microsoft.Maui.Media;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Media;

public class LinuxTextToSpeech : ITextToSpeech
{
	public Task<IEnumerable<Locale>> GetLocalesAsync()
	{
		// Locale has an internal constructor — create via reflection
		var culture = CultureInfo.CurrentCulture;
		try
		{
			var ctor = typeof(Locale).GetConstructor(
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
				null, new[] { typeof(string), typeof(string), typeof(string), typeof(string) }, null);
			if (ctor is not null)
			{
				var locale = (Locale)ctor.Invoke(new object[] {
					culture.TwoLetterISOLanguageName, culture.Name, culture.DisplayName, culture.Name });
				return Task.FromResult<IEnumerable<Locale>>(new[] { locale });
			}
			else
			{
				System.Diagnostics.Debug.WriteLine("LinuxTextToSpeech: Could not find Locale constructor via reflection. Locale list will be empty.");
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"LinuxTextToSpeech: Failed to create Locale via reflection: {ex.Message}");
		}
		return Task.FromResult<IEnumerable<Locale>>(Array.Empty<Locale>());
	}

	public async Task SpeakAsync(string text, SpeechOptions? options = null, CancellationToken cancelToken = default)
	{
		if (string.IsNullOrWhiteSpace(text))
			return;

		// Try spd-say (speech-dispatcher CLI) first, then espeak-ng
		var commands = new[]
		{
			("spd-say", BuildSpdSayArgs(text, options)),
			("espeak-ng", BuildEspeakArgs(text, options)),
			("espeak", BuildEspeakArgs(text, options)),
		};

		foreach (var (cmd, argList) in commands)
		{
			try
			{
				var psi = new ProcessStartInfo(cmd)
				{
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				foreach (var arg in argList)
					psi.ArgumentList.Add(arg);
				using var process = Process.Start(psi);
				if (process is null) continue;
				await process.WaitForExitAsync(cancelToken);
				if (process.ExitCode == 0) return;
			}
			catch { continue; }
		}

		throw new PlatformNotSupportedException(
			"Text-to-speech requires speech-dispatcher (spd-say) or espeak-ng to be installed.");
	}

	private static string[] BuildSpdSayArgs(string text, SpeechOptions? options)
	{
		var args = new List<string> { text };
		if (options?.Pitch.HasValue == true)
		{
			args.Add("-p");
			args.Add(((int)((options.Pitch.Value - 1.0f) * 100)).ToString(CultureInfo.InvariantCulture));
		}
		if (options?.Volume.HasValue == true)
		{
			args.Add("-i");
			args.Add(((int)(options.Volume.Value * 100)).ToString(CultureInfo.InvariantCulture));
		}
		return args.ToArray();
	}

	private static string[] BuildEspeakArgs(string text, SpeechOptions? options)
	{
		var args = new List<string> { text };
		if (options?.Pitch.HasValue == true)
		{
			args.Add("-p");
			args.Add(((int)(options.Pitch.Value * 50)).ToString(CultureInfo.InvariantCulture));
		}
		if (options?.Volume.HasValue == true)
		{
			args.Add("-a");
			args.Add(((int)(options.Volume.Value * 200)).ToString(CultureInfo.InvariantCulture));
		}
		return args.ToArray();
	}
}
