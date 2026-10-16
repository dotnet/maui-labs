using Microsoft.Maui.ApplicationModel.Communication;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

public class LinuxEmail : LinuxCommunication, IEmail
{
    private const string TempFilePrefix = "mauimail-";
    private static readonly TimeSpan TempFileLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan StaleFileLifetime = TimeSpan.FromDays(1);

    private const string EmlHandlerRequiredMessage =
    "HTML bodies and attachments require a default handler for message/rfc822 (.eml files). " +
    "Install a mail client that can open .eml files, such as Thunderbird, " +
    "and set it as the default for .eml files " +
    "(e.g. xdg-mime default org.mozilla.Thunderbird.desktop message/rfc822).";

    public LinuxEmail() : base("message/rfc822")
    {
    }

    public bool IsComposeSupported => true;

    public async Task ComposeAsync(EmailMessage? message)
    {
        var needsEml = RequiresEml(message);

        if (needsEml && !HasHandler)
            throw new FeatureNotSupportedException(EmlHandlerRequiredMessage);

        CleanupStaleEmlFiles();

        var uri = await GetUri(message);
        if (needsEml) uri = await WriteTempEmlAsync(uri);
        try
        {
            Launch(uri);
        }
        finally
        {
            if (needsEml) ScheduleTempFileCleanup(uri);
        }
    }

    internal static bool RequiresEml(EmailMessage? message) =>
        message is not null &&
        (message.BodyFormat != EmailBodyFormat.PlainText ||
         message.Attachments?.Count > 0);

    internal static async Task<string> GetUri(EmailMessage? message)
    {
        if (message is null)
            return "mailto:";

        return RequiresEml(message)
            ? await message.ConvertToEml()
            : await message.ConvertToMailTo();
    }

    private static async Task<string> WriteTempEmlAsync(string content)
    {
        var path = Path.Combine(
            FileSystem.CacheDirectory,
            $"{TempFilePrefix}{Guid.NewGuid():N}.eml");

        // Emails may contain private data: create the file readable by the current user only.
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        };

        await using var stream = new FileStream(path, options);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content);

        return path;
    }

    private static void ScheduleTempFileCleanup(string path) =>
        _ = Task.Delay(TempFileLifetime).ContinueWith(
            _ => TryDelete(path),
            TaskScheduler.Default);

    private static void CleanupStaleEmlFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow - StaleFileLifetime;

            foreach (var path in Directory.EnumerateFiles(
                         FileSystem.CacheDirectory, $"{TempFilePrefix}*.eml"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                    TryDelete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file may still be in use; a later stale-file sweep retries.
        }
    }
}

/// <summary>
/// Provides extension methods for sending emails with attachments and converting emails to different formats.
/// </summary>
public static class LinuxEmailExtensions
{

    private const string CrLf = "\r\n";

    private static StringBuilder AppendCrLf(this StringBuilder builder, string value = "")
     => builder.Append(value).Append(CrLf);

    private static string EncodeQuotedPrintable(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var result = new StringBuilder();
        var lineLength = 0;

        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];

            // Normalize input newlines to CRLF without encoding them.
            if (b == (byte)'\r' && i + 1 < bytes.Length &&
                bytes[i + 1] == (byte)'\n')
            {
                result.Append(CrLf);
                lineLength = 0;
                i++;
                continue;
            }

            if (b == (byte)'\r' || b == (byte)'\n')
            {
                result.Append(CrLf);
                lineLength = 0;
                continue;
            }

            var atLineEnd = i + 1 == bytes.Length ||
                bytes[i + 1] == (byte)'\r' ||
                bytes[i + 1] == (byte)'\n';

            string token;

            if ((b >= 33 && b <= 60) || (b >= 62 && b <= 126))
            {
                token = ((char)b).ToString();
            }
            else if ((b == (byte)' ' || b == (byte)'\t') && !atLineEnd)
            {
                token = ((char)b).ToString();
            }
            else
            {
                token = $"={b:X2}";
            }

            // Leave room for the soft line break '='.
            if (lineLength + token.Length > 75)
            {
                result.Append('=').Append(CrLf);
                lineLength = 0;
            }

            result.Append(token);
            lineLength += token.Length;
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts an <see cref="EmailMessage"/> to a <c>mailto:</c> URI string.
    /// </summary>
    /// <param name="message">The email message to convert.</param>
    /// <returns>A properly formatted <c>mailto:</c> URI.</returns>
    internal static Task<string> ConvertToMailTo(this EmailMessage message)
    {
        var query = new List<string>();

        if (!string.IsNullOrEmpty(message.Subject))
            query.Add("subject=" + Uri.EscapeDataString(message.Subject));

        if (!string.IsNullOrEmpty(message.Body))
            query.Add("body=" + Uri.EscapeDataString(message.Body));

        if (message.Cc?.Any() == true)
            query.Add("cc=" + Uri.EscapeDataString(string.Join(",", message.Cc)));

        if (message.Bcc?.Any() == true)
            query.Add("bcc=" + Uri.EscapeDataString(string.Join(",", message.Bcc)));

        var recipients = string.Join(",", message.To?.Select(Uri.EscapeDataString) ?? []);

        var uri = $"mailto:{recipients}";
        if (query.Count > 0)
            uri += "?" + string.Join("&", query);

        return Task.FromResult(uri);
    }

    /// <summary>
    /// Converts an <see cref="EmailMessage"/> to a complete EML (RFC 822) file content as a string.
    /// </summary>
    /// <param name="message">The email message to convert.</param>
    /// <returns>A string containing the full EML file content, including MIME headers, body, and attachments.</returns>
    /// <remarks>
    /// <para>
    /// This method generates a standard .eml file that can be:
    /// <list type="bullet">
    /// <item><description>Saved to disk and opened in email clients (Outlook, Thunderbird, Apple Mail)</description></item>
    /// <item><description>Sent as an attachment</description></item>
    /// <item><description>Opened via <c>xdg-open</c> (Linux), <c>open</c> (macOS), or default association (Windows)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The generated EML complies with RFC 822 and MIME standards. It includes:
    /// <list type="bullet">
    /// <item><description>Required headers (Date, MIME-Version, Subject, recipients)</description></item>
    /// <item><description>X-Unsent header (marks email as draft)</description></item>
    /// <item><description>Multipart structure for HTML + plain text (when applicable)</description></item>
    /// <item><description>Base64-encoded attachments</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Note:</b> Attachments are read asynchronously; the method calls <see cref="FileBase.OpenReadAsync"/> for each attachment.
    /// For large attachments, consider streaming to disk rather than building in-memory.
    /// </para>
    /// </remarks>
    internal static async Task<string> ConvertToEml(this EmailMessage message)
    {
        var builder = new StringBuilder();
        var boundary = $"----=_NextPart_{Guid.NewGuid():N}";

        var hasAttachments = message.Attachments?.Count > 0;
        var hasBody = !string.IsNullOrEmpty(message.Body);
        var bodyType = message.BodyFormat == EmailBodyFormat.Html ? "text/html" : "text/plain";

        // Strip CR/LF so user data can never inject extra headers
        static string Clean(string s) => s.Replace("\r", "").Replace("\n", "");

        // === HEADERS SECTION ===
        if (message.To?.Count > 0)
            builder.AppendCrLf($"To: {Clean(string.Join(",", message.To))}");

        if (message.Cc?.Count > 0)
            builder.AppendCrLf($"Cc: {Clean(string.Join(",", message.Cc))}");

        if (message.Bcc?.Count > 0)
            builder.AppendCrLf($"Bcc: {Clean(string.Join(",", message.Bcc))}");

        if (!string.IsNullOrWhiteSpace(message.Subject))
            builder.AppendCrLf($"Subject: {EncodeSubject(Clean(message.Subject))}");

        builder.AppendCrLf($"Date: {DateTimeOffset.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture)}");
        builder.AppendCrLf("MIME-Version: 1.0");
        builder.AppendCrLf("X-Unsent: 1");

        if (hasAttachments)
        {
            builder.AppendCrLf($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        }
        else
        {
            // Single-part message: all of its headers belong here, before the blank line
            builder.AppendCrLf($"Content-Type: {bodyType}; charset=utf-8");
            if (hasBody)
                builder.AppendCrLf("Content-Transfer-Encoding: quoted-printable");
        }

        // Empty line separating headers from body
        builder.AppendCrLf();

        // === BODY SECTION ===
        if (hasAttachments)
        {
            builder.AppendCrLf("This is a multipart message in MIME format.");

            // Body part: always emitted, even when the body is empty (attachment-only messages)
            builder.AppendCrLf($"--{boundary}");
            builder.AppendCrLf($"Content-Type: {bodyType}; charset=utf-8");
            builder.AppendCrLf("Content-Transfer-Encoding: quoted-printable");
            builder.AppendCrLf();
            builder.AppendCrLf(hasBody ? EncodeQuotedPrintable(message.Body!) : string.Empty);
            builder.AppendCrLf();

            // Attachments
            foreach (var attachment in message.Attachments!)
                await AppendAttachmentAsync(builder, boundary, attachment);

            // End of multipart message
            builder.AppendCrLf($"--{boundary}--");
        }
        else if (hasBody)
        {
            builder.AppendCrLf(EncodeQuotedPrintable(message.Body!));
            builder.AppendCrLf();
        }

        return builder.ToString();
    }

    static async Task AppendAttachmentAsync(StringBuilder builder, string boundary, EmailAttachment attachment)
    {
        var contentType = ContentTypeResolver.ResolveContentType(attachment);

        await using var file = new FileStream(
            attachment.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

        builder.AppendCrLf($"--{boundary}");
        builder.AppendCrLf($"Content-Type: {contentType}; {MimeParam("name", attachment.FileName)}");
        builder.AppendCrLf("Content-Transfer-Encoding: base64");
        builder.AppendCrLf($"Content-Disposition: attachment; {MimeParam("filename", attachment.FileName)}");
        builder.AppendCrLf();

        await using var base64Stream = new CryptoStream(file, new ToBase64Transform(), CryptoStreamMode.Read);

        var buffer = new byte[4096];
        var lineLength = 0;
        int bytesRead;

        while ((bytesRead = await base64Stream.ReadAsync(buffer)) > 0)
        {
            for (var i = 0; i < bytesRead; i++)
            {
                builder.Append((char)buffer[i]);

                if (++lineLength == 76)
                {
                    builder.AppendCrLf();
                    lineLength = 0;
                }
            }
        }

        if (lineLength > 0)
            builder.AppendCrLf();

        builder.AppendCrLf();
    }

    static string MimeParam(string name, string value)
    {
        value = value.Replace("\r", "").Replace("\n", "");

        static string Quote(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        if (value.All(c => c is >= ' ' and < (char)0x7f))
            return $"{name}=\"{Quote(value)}\"";

        // Non-ASCII: RFC 2231 extended parameter only
        return $"{name}*=UTF-8''{Uri.EscapeDataString(value)}";
    }

    /// <summary>
    /// Encodes a subject line for inclusion in an email header using RFC 2047 quoted-printable or Base64 encoding.
    /// </summary>
    /// <param name="subject">The subject text to encode.</param>
    /// <returns>
    /// The original subject if it contains only ASCII characters (0-127);
    /// otherwise, a UTF-8 Base64 encoded string in the format <c>=?utf-8?B?...?=</c>.
    /// </returns>
    /// <remarks>
    /// This method ensures that non-ASCII characters (e.g., accented letters, Chinese, emojis) are properly encoded
    /// according to email standards, preventing garbled text in older email clients.
    /// </remarks>
    private static string EncodeSubject(string subject)
    {
        if (subject.Any(c => c > 127))
        {
            var bytes = Encoding.UTF8.GetBytes(subject);
            var base64 = Convert.ToBase64String(bytes);
            return $"=?utf-8?B?{base64}?=";
        }
        return subject;
    }
}
