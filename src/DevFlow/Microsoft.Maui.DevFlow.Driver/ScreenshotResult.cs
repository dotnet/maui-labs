namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Result of a screenshot capture request made through <see cref="AgentClient.ScreenshotResultAsync"/>.
/// On success, <see cref="Data"/> holds the captured PNG bytes. On failure, <see cref="Error"/>,
/// <see cref="Reason"/>, <see cref="Retryable"/>, and <see cref="Suggestions"/> describe an
/// actionable cause when the agent could provide one (for example, the macOS app window not
/// being the frontmost application).
/// </summary>
public sealed class ScreenshotResult
{
    /// <summary>Whether the capture succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Captured PNG bytes when <see cref="Success"/> is <c>true</c>.</summary>
    public byte[]? Data { get; init; }

    /// <summary>Human-readable, actionable error message when the capture failed.</summary>
    public string? Error { get; init; }

    /// <summary>Machine-readable cause identifier (e.g. <c>window-not-frontmost</c>) when available.</summary>
    public string? Reason { get; init; }

    /// <summary>Whether retrying (e.g. after foregrounding the app) may succeed.</summary>
    public bool Retryable { get; init; }

    /// <summary>Optional actionable suggestions surfaced by the agent.</summary>
    public IReadOnlyList<string>? Suggestions { get; init; }

    /// <summary>Capture epoch associated with the screenshot.</summary>
    public long? CaptureEpoch { get; init; }

    /// <summary>Native registration generation associated with the screenshot.</summary>
    public long? RegistryGeneration { get; init; }

    /// <summary>Window index associated with the screenshot.</summary>
    public int? WindowId { get; init; }

    public static ScreenshotResult Ok(byte[] data) =>
        Ok(data, captureEpoch: null, registryGeneration: null, windowId: null);

    public static ScreenshotResult Ok(
        byte[] data,
        long? captureEpoch,
        long? registryGeneration,
        int? windowId) =>
        new()
        {
            Success = true,
            Data = data,
            CaptureEpoch = captureEpoch,
            RegistryGeneration = registryGeneration,
            WindowId = windowId
        };

    public static ScreenshotResult Failure(
        string? error,
        string? reason = null,
        bool retryable = false,
        IReadOnlyList<string>? suggestions = null) =>
        new()
        {
            Success = false,
            Error = error,
            Reason = reason,
            Retryable = retryable,
            Suggestions = suggestions
        };
}
