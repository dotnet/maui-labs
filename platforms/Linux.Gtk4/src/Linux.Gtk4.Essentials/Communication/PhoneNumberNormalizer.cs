using System.Text;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Communication;

internal static class PhoneNumberNormalizer
{
    internal static string Normalize(string number, string paramName)
    {
        var span = number.AsSpan().Trim();

        if (span.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            span = span[4..].Trim();
        else if (span.StartsWith("sms:", StringComparison.OrdinalIgnoreCase))
            span = span[4..].Trim();

        var sb = new StringBuilder(span.Length);
        var digits = 0;

        foreach (var c in span)
        {
            if (char.IsWhiteSpace(c))
                continue;

            if (char.IsAsciiDigit(c))
            {
                sb.Append(c);
                digits++;
            }
            else if (c == '+' && sb.Length == 0)
                sb.Append(c);
            else if (c is '-' or '.' or '(' or ')' or '*')
                sb.Append(c);
            else if (c == '#')
                sb.Append("%23");
            else
                throw new ArgumentException(
                    $"Phone number contains an invalid character: '{c}'.", paramName);
        }

        if (digits == 0)
            throw new ArgumentException("Phone number must contain at least one digit.", paramName);

        return sb.ToString();
    }
}
