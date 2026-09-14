using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI.Chat.Recording;

internal static partial class ChatRecordingSecurity
{
    private const string SanitizedProviderError = "[REDACTED provider error]";

    [GeneratedRegex(@"(?i)\b(bearer\s+\S+|api[_ -]?key\s*[=:]|authorization\s*[=:]|set-?cookie\s*[=:]|cookie\s*[=:]|connection\s*string\s*[=:]|x-ms-[\w-]*\s*[=:]|trace(parent|state)\s*[=:]|https?://\S+|(openai|azure|anthropic|googleapis|amazonaws)\.(com|net|azure\.com))")]
    private static partial Regex SensitiveError();

    internal static (string? Type, string Message) SanitizeException(Exception exception, ChatRecordingOptions settings)
    {
        var type = exception.GetType().FullName;
        if (SensitiveError().IsMatch(exception.Message) || (type is not null && SensitiveError().IsMatch(type)))
        {
            if (settings.Recording?.Manifest["redacted"]?.GetValue<int>() is { } redacted)
                settings.Recording.Manifest["redacted"] = redacted + 1;
            return ("ProviderError", SanitizedProviderError);
        }

        return (type, exception.Message);
    }

    internal static bool IsSafeUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        !Regex.IsMatch(uri.Host, @"(?i)(^|\.)(api\.)?(openai|azure|anthropic|googleapis|amazonaws)(\.|$)");
}
