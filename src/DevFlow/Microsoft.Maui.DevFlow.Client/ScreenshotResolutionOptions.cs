namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Optional screenshot resolution hints. Agents never upscale beyond the native capture and may
/// return a smaller image when a backend or pixel budget requires it.
/// </summary>
public sealed class ScreenshotResolutionOptions
{
    /// <summary>Requested screenshot pixels per logical app unit.</summary>
    public double? TargetScale { get; init; }

    /// <summary>Maximum returned width in pixels.</summary>
    public int? MaxPixelWidth { get; init; }

    /// <summary>Maximum returned height in pixels.</summary>
    public int? MaxPixelHeight { get; init; }

    /// <summary>Maximum returned pixel count.</summary>
    public long? MaxPixels { get; init; }

    /// <summary>
    /// Maximum encoded response bytes the client will buffer. This is a local transport limit and
    /// is not sent to the agent.
    /// </summary>
    public int? MaxEncodedBytes { get; init; }
}
