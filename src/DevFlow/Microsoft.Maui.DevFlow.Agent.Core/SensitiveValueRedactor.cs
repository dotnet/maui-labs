namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class SensitiveValueRedactor
{
    public const string RedactedValue = "[REDACTED]";

    public static string? Redact(string? value, bool isSensitive)
        => isSensitive && value is not null ? RedactedValue : value;
}
