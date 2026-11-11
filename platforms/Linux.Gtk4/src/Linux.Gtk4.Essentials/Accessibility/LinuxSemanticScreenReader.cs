using Microsoft.Maui.Accessibility;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Accessibility;

public class LinuxSemanticScreenReader : ISemanticScreenReader
{
	public void Announce(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return;

		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher is not null && dispatcher.IsDispatchRequired)
		{
			dispatcher.Dispatch(() => AnnounceCore(text));
			return;
		}

		AnnounceCore(text);
	}

	static void AnnounceCore(string text)
	{
		var window = (Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow()
			?? throw new InvalidOperationException(
				"A running GTK application window is required for semantic announcements.");
		window.Announce(text, Gtk.AccessibleAnnouncementPriority.Medium);
	}
}
