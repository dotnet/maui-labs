using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

internal static class NativeBindingFailureDiagnostics
{
    private static readonly Regex SetupFailure = new(
        @"(?:Native|WebKit) CDP response binding setup failed during " +
        @"(?:Runtime\.enable|Runtime\.addBinding|native handler registration|document binding installation|binding handshake dispatch|binding handshake callback); " +
        @"callbacks=\d+, (?:(?:matchingBindings|matchingScopes)=\d+, )?acceptedPayloads=\d+" +
        @"(?:, lastSender=\((?:none|null=(?:True|False), managedSame=(?:True|False), nativeSame=(?:True|False))\))?\.",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static async Task<string> CaptureAsync(Func<Task<string>> getLogs)
    {
        try
        {
            using var logs = JsonDocument.Parse(await getLogs().WaitAsync(TimeSpan.FromSeconds(3)));
            if (logs.RootElement.ValueKind != JsonValueKind.Array)
                return "Native binding diagnostics unavailable: unexpected log response.";
            var failures = new List<string>();
            foreach (var entry in logs.RootElement.EnumerateArray().Take(50))
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("c", out var category) || category.ValueKind != JsonValueKind.String ||
                    category.GetString() != "WebView.DevFlow" ||
                    !entry.TryGetProperty("l", out var level) || level.ValueKind != JsonValueKind.String ||
                    level.GetString() != "Error" ||
                    !entry.TryGetProperty("m", out var message) || message.ValueKind != JsonValueKind.String)
                    continue;
                var text = message.GetString()!;
                var match = SetupFailure.Match(text.Length > 4096 ? text[..4096] : text);
                if (match.Success && !failures.Contains(match.Value))
                    failures.Add(match.Value);
                if (failures.Count == 5) break;
            }
            return failures.Count == 0
                ? "No native binding setup failures in the latest 50 WebView log entries."
                : string.Join(Environment.NewLine, failures);
        }
        catch (Exception ex)
        {
            // Diagnostics must report their own failure without replacing the original test error.
            return $"Native binding diagnostics unavailable ({ex.GetType().Name}).";
        }
    }
}
