using System.Diagnostics;
using Microsoft.Maui.ApplicationModel.Communication;

namespace Platform.Maui.Linux.Gtk4.Essentials.Communication;

public class LinuxEmail : IEmail
{
	public bool IsComposeSupported => true;

	public Task ComposeAsync(EmailMessage? message)
	{
		if (message is null)
		{
			Process.Start(new ProcessStartInfo("xdg-open", "mailto:")
				{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
			return Task.CompletedTask;
		}

		var to = message.To?.Count > 0 ? string.Join(",", message.To) : "";
		var subject = Uri.EscapeDataString(message.Subject ?? "");
		var body = Uri.EscapeDataString(message.Body ?? "");
		var cc = message.Cc?.Count > 0 ? $"&cc={string.Join(",", message.Cc)}" : "";
		var bcc = message.Bcc?.Count > 0 ? $"&bcc={string.Join(",", message.Bcc)}" : "";

		var mailto = $"mailto:{to}?subject={subject}&body={body}{cc}{bcc}";

		Process.Start(new ProcessStartInfo("xdg-open", mailto)
			{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });

		return Task.CompletedTask;
	}
}
