using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>
/// The single redaction ruleset shared by the Web Inspector data tabs and the evidence bundle.
///
/// Every rule here is applied at INGESTION — before a value is serialized into a bundle entry or
/// returned to a host UI — so no downstream renderer can accidentally surface an unredacted value.
/// <see cref="Version"/> is written into every manifest and preview so a reader knows which
/// ruleset produced a bundle.
/// </summary>
internal static class EvidenceRedaction
{
    /// <summary>Ruleset version. Bump whenever the rules below change what is masked or dropped.</summary>
    public const int Version = 2;

    private const string Redacted = "<redacted>";

    // ── Secret masking (shared with the Inspector data tabs) ─────────────────────────────────

    private static readonly Regex UrlSecretRegex = new(
        @"(?i)([?&](?:access_token|refresh_token|id_token|token|api[_-]?key|apikey|key|secret|password|code|sig|signature)=)[^&#\s]+",
        RegexOptions.Compiled);

    private static readonly Regex JwtRegex = new(
        @"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}",
        RegexOptions.Compiled);

    private static readonly Regex BearerRegex = new(
        @"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]{12,}",
        RegexOptions.Compiled);

    private static readonly Regex PrefixedSecretRegex = new(
        @"(?ix)\b(?:
            sk[-_][a-z0-9_-]{16,} |
            xox[baprs]-[a-z0-9-]{10,} |
            gh[pousr]_[a-z0-9]{20,} |
            github_pat_[a-z0-9_]{20,} |
            glpat-[a-z0-9_-]{16,} |
            AIza[a-z0-9_-]{20,} |
            (?:AKIA|ASIA)[A-Z0-9]{16}
        )\b",
        RegexOptions.Compiled);

    private static readonly Regex PemPrivateKeyRegex = new(
        @"(?is)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----.*?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex BasicAuthUrlRegex = new(
        @"(?i)(https?://)[^/\s:@]+:[^/\s@]+@",
        RegexOptions.Compiled);

    private static readonly Regex SecretKvRegex = new(
        "(?i)(\"(?:[a-z0-9_-]*(?:token|secret|password|passwd|pwd|cookie|private[_-]?key|access[_-]?key|signing[_-]?key|apikey|api[_-]?key|authorization)[a-z0-9_-]*)\"\\s*:\\s*)\"[^\"]*\"",
        RegexOptions.Compiled);

    // key=value / key: value assignments in free-form log text (not JSON).
    private static readonly Regex SecretAssignmentRegex = new(
        @"(?i)\b([a-z0-9_-]*(?:token|secret|password|passwd|pwd|cookie|private[_-]?key|access[_-]?key|signing[_-]?key|apikey|api[_-]?key|authorization|credential)[a-z0-9_-]*)\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;}{\]]+)",
        RegexOptions.Compiled);

    // Absolute filesystem paths that would leak the developer's machine layout: drive-rooted,
    // UNC shares (which also leak internal host names), and the common Unix/WSL roots.
    private static readonly Regex WindowsPathRegex = new(
        @"(?i)(\\\\[^\s""'<>|]+|\b[a-z]:[\\/][^\s""'<>|]*)",
        RegexOptions.Compiled);

    private static readonly Regex UnixPathRegex = new(
        @"(?<![\w.])/(?:Users|home|root|var|private|Volumes|tmp|opt|data|storage|mnt|media|srv|workspace|workspaces|builds|github/workspace|agent/_work|__w)/[^\s""'<>|]*",
        RegexOptions.Compiled);

    private static readonly Regex FileUriRegex = new(
        @"(?i)file:///[^\s""'<>|]*",
        RegexOptions.Compiled);

    /// <summary>
    /// Masks JWTs, bearer tokens, and secret-shaped JSON values in free-form text. Replacements
    /// never introduce a quote, so masking a serialized JSON document keeps it parseable.
    /// </summary>
    public static string MaskSecrets(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = PemPrivateKeyRegex.Replace(text, "<private-key>");
        text = PrefixedSecretRegex.Replace(text, Redacted);
        text = BasicAuthUrlRegex.Replace(text, "$1" + Redacted + "@");
        text = JwtRegex.Replace(text, "<jwt>");
        text = BearerRegex.Replace(text, "$1" + Redacted);
        text = SecretKvRegex.Replace(text, "$1\"" + Redacted + "\"");
        return text;
    }

    /// <summary>Strips secret query-string values from a URL while keeping the rest readable.</summary>
    public static string MaskUrlSecrets(string url)
        => string.IsNullOrEmpty(url) ? url : UrlSecretRegex.Replace(url, "$1" + Redacted);

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "proxy-authorization", "cookie", "set-cookie", "x-api-key", "x-auth-token",
    };

    private static readonly string[] SensitiveHeaderFragments =
        ["token", "secret", "auth", "cookie", "apikey", "api-key", "api_key"];

    /// <summary>Masks sensitive header values in place (case-insensitive name and fragment match).</summary>
    public static void RedactHeaders(Dictionary<string, string[]>? headers)
    {
        if (headers is null) return;
        foreach (var key in headers.Keys.ToList())
        {
            var lower = key.ToLowerInvariant();
            if (SensitiveHeaders.Contains(key) || SensitiveHeaderFragments.Any(f => lower.Contains(f, StringComparison.Ordinal)))
                headers[key] = [Redacted];
        }
    }

    // ── Bundle-side scrubbing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full ingestion scrub for any free-form string entering a bundle: masks secrets and secret
    /// assignments, replaces absolute paths with their file name, drops control characters, and
    /// truncates to <paramref name="maxChars"/>.
    /// </summary>
    public static string? Scrub(string? text, int maxChars)
    {
        if (text is null) return null;
        if (text.Length == 0) return string.Empty;

        var scrubbed = MaskSecrets(text);
        scrubbed = SecretAssignmentRegex.Replace(scrubbed, "$1=" + Redacted);
        scrubbed = MaskUrlSecrets(scrubbed);
        scrubbed = StripAbsolutePaths(scrubbed);
        scrubbed = RemoveControlCharacters(scrubbed);
        return Truncate(scrubbed, maxChars);
    }

    /// <summary>Replaces absolute Windows/Unix/file-URI paths with their file name only.</summary>
    public static string StripAbsolutePaths(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = FileUriRegex.Replace(text, m => LastSegment(m.Value));
        text = WindowsPathRegex.Replace(text, m => LastSegment(m.Value));
        text = UnixPathRegex.Replace(text, m => LastSegment(m.Value));
        return text;
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);
        var segment = index >= 0 && index < trimmed.Length - 1 ? trimmed[(index + 1)..] : trimmed;
        return string.IsNullOrEmpty(segment) ? "<path>" : segment;
    }

    private static string RemoveControlCharacters(string text)
    {
        var needsWork = false;
        foreach (var c in text)
        {
            if ((char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ||
                char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                needsWork = true;
                break;
            }
        }
        if (!needsWork) return text;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ((!char.IsControl(c) || c is '\n' or '\r' or '\t') &&
                char.GetUnicodeCategory(c) != UnicodeCategory.Format)
                builder.Append(c);
        }
        return builder.ToString();
    }

    public static string Truncate(string value, int maxChars)
    {
        if (maxChars <= 0) return string.Empty;
        if (value.Length <= maxChars) return value;
        const string marker = "…[truncated]";
        if (maxChars <= marker.Length)
            return marker[..maxChars];
        return string.Concat(value.AsSpan(0, maxChars - marker.Length), marker);
    }

    /// <summary>
    /// Developer-authored identifiers (AutomationId, element type, category names) are safe to keep,
    /// but they still get length-capped, control-character stripped, and secret-masked in case an
    /// app generated one from a token.
    /// </summary>
    public static string? SafeIdentifier(string? value, int maxChars = EvidenceFormat.MaxIdentifierChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = RemoveControlCharacters(value.Trim());
        trimmed = MaskSecrets(trimmed);
        trimmed = SecretAssignmentRegex.Replace(trimmed, "$1=" + Redacted);
        trimmed = StripAbsolutePaths(trimmed);
        return trimmed.Length == 0 ? null : Truncate(trimmed, maxChars);
    }

    /// <summary>
    /// Keeps the route shape while removing every query value and fragment. Query parameter names
    /// are developer-authored metadata; values frequently contain user or authentication state.
    /// </summary>
    public static string? ScrubRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;

        var value = route.Trim();
        var fragmentIndex = value.IndexOf('#');
        if (fragmentIndex >= 0)
            value = value[..fragmentIndex];

        var queryIndex = value.IndexOf('?');
        var path = queryIndex >= 0 ? value[..queryIndex] : value;
        var safePath = Scrub(path, EvidenceFormat.MaxIdentifierChars);
        if (queryIndex < 0 || queryIndex == value.Length - 1)
            return safePath;

        var keys = value[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var equals = pair.IndexOf('=');
                var key = equals >= 0 ? pair[..equals] : pair;
                try { key = Uri.UnescapeDataString(key); } catch { }
                return SafeIdentifier(key, 64);
            })
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Take(EvidenceFormat.MaxQueryKeys)
            .Select(static key => $"{key}=<redacted>")
            .ToArray();

        return keys.Length == 0 ? safePath : $"{safePath}?{string.Join("&", keys)}";
    }

    /// <summary>
    /// Converts a source-file path into evidence-safe form: project-relative with forward slashes
    /// when the file lives under <paramref name="projectRoot"/>, otherwise the file name only.
    /// Never returns an absolute path, a traversal, or a drive-rooted string.
    /// </summary>
    public static string? NormalizeSourcePath(string? path, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var value = path.Trim();
        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { value = new Uri(value).LocalPath; }
            catch { /* fall through and treat as a plain path */ }
        }

        // A root is only allowed to relativize paths if it actually describes a checkout. A bare
        // volume root encloses the whole machine, so accepting one would turn this function from a
        // redaction into a disclosure: every absolute path on the box becomes "project-relative"
        // and the bundle publishes the user's home directory layout instead of dropping it.
        if (IsShareableSourceRoot(projectRoot))
        {
            try
            {
                var root = Path.GetFullPath(projectRoot!);
                var full = Path.GetFullPath(value);
                var comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                var prefix = root.EndsWith(Path.DirectorySeparatorChar)
                    ? root
                    : root + Path.DirectorySeparatorChar;
                if (full.StartsWith(prefix, comparison))
                    return ToRelativeForm(full[prefix.Length..]);
            }
            catch
            {
                // Unparseable path — fall through to the file-name-only policy.
            }
        }

        if (!IsRooted(value))
        {
            var relative = ToRelativeForm(value);
            // A relative path that escapes upward still describes the machine layout — drop to name.
            if (!relative.Contains("../", StringComparison.Ordinal) && relative != "..")
                return relative;
        }

        var name = LastSegment(value);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Whether a root may be used to rewrite absolute source paths into relative ones.
    ///
    /// <para>Rejects anything that is not a real directory <em>below</em> a volume: an empty value,
    /// a POSIX root, a Windows drive designator, and a bare UNC share. Those roots enclose every
    /// file a machine can address, so relativizing against one publishes the absolute layout
    /// verbatim minus its leading separator — exactly the disclosure the file-name-only fallback
    /// exists to prevent. A caller that supplies one is treated as having supplied nothing.</para>
    ///
    /// <para>The value is judged both as written and as resolved, so a root that only becomes bare
    /// after traversal (<c>C:\repo\..</c>) is caught too. Ordinary checkout roots — <c>C:\src\app</c>,
    /// <c>/home/me/app</c>, <c>\\server\share\app</c> — are unaffected.</para>
    /// </summary>
    public static bool IsShareableSourceRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;
        if (IsBareFilesystemRoot(root!))
            return false;
        try
        {
            return !IsBareFilesystemRoot(Path.GetFullPath(root!));
        }
        catch
        {
            // A root this process cannot even resolve cannot be shown to be safe.
            return false;
        }
    }

    /// <summary>
    /// Decided lexically and on both separator styles, so the answer does not depend on which OS
    /// the bundle is produced on: an agent can report Windows paths from a capture driven on Linux
    /// and vice versa, and a Windows-hosted test must be able to state the POSIX case.
    /// </summary>
    private static bool IsBareFilesystemRoot(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        if (normalized.Length == 0)
            return true;

        // "C:", "C:/", "C://" — a drive designator with nothing named under it. Note that "C:" with
        // no separator is also drive-relative, which is no more shareable than the root itself.
        if (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
            return normalized[2..].Trim('/').Length == 0;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return true; // "/", "//", "\", "\\"
        // A UNC path needs a server and a share before it names a directory.
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            return segments.Length <= 2;
        return false;
    }

    private static bool IsRooted(string value)
    {
        if (value.Length == 0) return false;
        if (value[0] is '/' or '\\') return true;
        return value.Length >= 2 && value[1] == ':' && char.IsLetter(value[0]);
    }

    private static string ToRelativeForm(string value)
        => value.Replace('\\', '/').TrimStart('/');
}
