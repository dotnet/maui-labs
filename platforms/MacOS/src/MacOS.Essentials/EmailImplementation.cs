using Microsoft.Maui.ApplicationModel.Communication;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class EmailImplementation : IEmail
{
	public bool IsComposeSupported => true;

	public Task ComposeAsync(EmailMessage? message)
	{
		if (message == null)
		{
			AppKit.NSWorkspace.SharedWorkspace.OpenUrl(new Foundation.NSUrl("mailto:"));
			return Task.CompletedTask;
		}

		// Build mailto: URI with subject, body, to, cc, bcc
		var uri = "mailto:";
		if (message.To?.Count > 0)
			uri += string.Join(",", message.To.Select(Uri.EscapeDataString));

		var parameters = new List<string>();
		if (!string.IsNullOrWhiteSpace(message.Subject))
			parameters.Add("subject=" + Uri.EscapeDataString(message.Subject));
		if (!string.IsNullOrWhiteSpace(message.Body))
			parameters.Add("body=" + Uri.EscapeDataString(message.Body));
		if (message.Cc?.Count > 0)
			parameters.Add("cc=" + string.Join(",", message.Cc.Select(Uri.EscapeDataString)));
		if (message.Bcc?.Count > 0)
			parameters.Add("bcc=" + string.Join(",", message.Bcc.Select(Uri.EscapeDataString)));

		if (parameters.Count > 0)
			uri += "?" + string.Join("&", parameters);

		AppKit.NSWorkspace.SharedWorkspace.OpenUrl(new Foundation.NSUrl(uri));
		return Task.CompletedTask;
	}
}
