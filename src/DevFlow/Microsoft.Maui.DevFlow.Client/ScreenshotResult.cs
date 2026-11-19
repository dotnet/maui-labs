namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Result of a screenshot capture request made through <see cref="AgentClient.ScreenshotResultAsync"/>.
/// On success, <see cref="Data"/> holds the captured PNG bytes. On failure, <see cref="Error"/>,
/// <see cref="Reason"/>, <see cref="Retryable"/>, and <see cref="Suggestions"/> describe an
/// actionable cause when the agent could provide one (for example, the macOS app window not
/// being the frontmost application). A backend that does not support screenshots is also
/// reported this way, with <see cref="Reason"/> set to <c>"not_supported"</c>, instead of
/// throwing <see cref="NotSupportedByAgentException"/>.
/// </summary>
public sealed class ScreenshotResult
{
    /// <summary>Whether the capture succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Captured PNG bytes when <see cref="Success"/> is <c>true</c>.</summary>
    public byte[]? Data { get; init; }

    /// <summary>Human-readable, actionable error message when the capture failed.</summary>
    public string? Error { get; init; }

    /// <summary>Machine-readable cause identifier (e.g. <c>window-not-visible</c>) when available.</summary>
    public string? Reason { get; init; }

    /// <summary>Whether retrying after correcting the reported precondition may succeed.</summary>
    public bool Retryable { get; init; }

    /// <summary>Optional actionable suggestions surfaced by the agent.</summary>
    public IReadOnlyList<string>? Suggestions { get; init; }

    /// <summary>Capture epoch associated with the screenshot.</summary>
    public long? CaptureEpoch { get; init; }

    /// <summary>Native registration generation associated with the screenshot.</summary>
    public long? RegistryGeneration { get; init; }

    /// <summary>Window index associated with the screenshot.</summary>
    public int? WindowId { get; init; }

    /// <summary>Actual returned PNG width in pixels.</summary>
    public int? PixelWidth { get; init; }

    /// <summary>Actual returned PNG height in pixels.</summary>
    public int? PixelHeight { get; init; }

    /// <summary>Captured logical width reported by the agent.</summary>
    public double? LogicalWidth { get; init; }

    /// <summary>Captured logical height reported by the agent.</summary>
    public double? LogicalHeight { get; init; }

    /// <summary>Actual returned pixels per logical unit.</summary>
    public double? CaptureScale { get; init; }

    /// <summary>Native capture pixels per logical unit before negotiated downsampling.</summary>
    public double? NativeScale { get; init; }

    /// <summary>Actual image format. Current agents return <c>png</c>.</summary>
    public string? Format { get; init; }

    /// <summary>Whether the agent bounded the requested resolution.</summary>
    public bool? ResolutionClamped { get; init; }

    public static ScreenshotResult Ok(byte[] data) =>
        Ok(data, captureEpoch: null, registryGeneration: null, windowId: null);

    public static ScreenshotResult Ok(
        byte[] data,
        long? captureEpoch,
        long? registryGeneration,
        int? windowId) =>
        Ok(
            data,
            captureEpoch,
            registryGeneration,
            windowId,
            pixelWidth: null,
            pixelHeight: null,
            logicalWidth: null,
            logicalHeight: null,
            captureScale: null,
            nativeScale: null,
            format: null,
            resolutionClamped: null);

    public static ScreenshotResult Ok(
        byte[] data,
        long? captureEpoch,
        long? registryGeneration,
        int? windowId,
        int? pixelWidth,
        int? pixelHeight,
        double? logicalWidth,
        double? logicalHeight,
        double? captureScale,
        double? nativeScale,
        string? format,
        bool? resolutionClamped) =>
        new()
        {
            Success = true,
            Data = data,
            CaptureEpoch = captureEpoch,
            RegistryGeneration = registryGeneration,
            WindowId = windowId,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            LogicalWidth = logicalWidth,
            LogicalHeight = logicalHeight,
            CaptureScale = captureScale,
            NativeScale = nativeScale,
            Format = format,
            ResolutionClamped = resolutionClamped
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
