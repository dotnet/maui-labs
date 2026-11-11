using Microsoft.Maui.ApplicationModel.Communication;
using System.Text;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxSms : LinuxCommunication, ISms
{
    public LinuxSms() : base("sms")
    {
    }

    public bool IsComposeSupported => HasHandler;

    public Task ComposeAsync(SmsMessage? message)
    {
        if (!IsComposeSupported)
            throw new FeatureNotSupportedException("SMS is not available on this Linux system.");

        Launch(BuildSmsUri(message));
        return Task.CompletedTask;
    }

    internal static string BuildSmsUri(SmsMessage? message)
    {
        if (message is null)
            return "sms:";

        var recipients = (message.Recipients ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => PhoneNumberNormalizer.Normalize(r, nameof(message)))
            .Distinct(StringComparer.Ordinal);

        // RFC 5724: sms:<to>[,<to>...][?body=<text>]
        var uri = new StringBuilder("sms:").AppendJoin(',', recipients);

        if (!string.IsNullOrEmpty(message.Body))
            uri.Append("?body=").Append(Uri.EscapeDataString(message.Body));

        return uri.ToString();
    }
}