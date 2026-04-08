using Microsoft.Maui.Accessibility;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Accessibility;

public class LinuxSemanticScreenReader : ISemanticScreenReader
{
	public void Announce(string text)
	{
		// Best-effort: try to use AT-SPI2 via speech-dispatcher
		try
		{
			var psi = new System.Diagnostics.ProcessStartInfo("spd-say")
				{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
			psi.ArgumentList.Add(text);
			System.Diagnostics.Process.Start(psi);
		}
		catch { }
	}
}
