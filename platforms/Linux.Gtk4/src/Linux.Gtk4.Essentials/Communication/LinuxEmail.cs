using System.Diagnostics;
using Microsoft.Maui.ApplicationModel.Communication;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxEmail : IEmail
{
	public bool IsComposeSupported => true;

	public Task ComposeAsync(EmailMessage? message)
	{
		if (message is null)
		{
			var psi = new ProcessStartInfo("xdg-open")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			psi.ArgumentList.Add("mailto:");
			using var process = Process.Start(psi);
			return Task.CompletedTask;
		}

		var to = message.To?.Count > 0 ? string.Join(",", message.To.Select(Uri.EscapeDataString)) : "";
		var subject = Uri.EscapeDataString(message.Subject ?? "");
		var body = Uri.EscapeDataString(message.Body ?? "");
		var cc = message.Cc?.Count > 0 ? $"&cc={string.Join(",", message.Cc.Select(Uri.EscapeDataString))}" : "";
		var bcc = message.Bcc?.Count > 0 ? $"&bcc={string.Join(",", message.Bcc.Select(Uri.EscapeDataString))}" : "";

		var mailto = $"mailto:{to}?subject={subject}&body={body}{cc}{bcc}";

		var composePsi = new ProcessStartInfo("xdg-open")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		composePsi.ArgumentList.Add(mailto);
		using var composeProcess = Process.Start(composePsi);

		return Task.CompletedTask;
	}
}
