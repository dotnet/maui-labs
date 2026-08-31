using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Agent.Core.Diagnostics;

internal static partial class DiagnosticRedactor
{
    private const int MaxTextLength = 2048;

    public static string RedactText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var redacted = JwtRegex().Replace(value, "<jwt>");
        redacted = BearerRegex().Replace(redacted, "$1<redacted>");
        redacted = SecretAssignmentRegex().Replace(redacted, "$1<redacted>");
        return redacted.Length <= MaxTextLength
            ? redacted
            : redacted[..MaxTextLength] + "…";
    }

    public static string StableId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]{12,}", RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)((?:token|secret|password|pwd|api[_-]?key|authorization|cookie|connection\s*string)\s*[=:]\s*)(?:""[^""]*""|'[^']*'|[^\s,;]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();
}
