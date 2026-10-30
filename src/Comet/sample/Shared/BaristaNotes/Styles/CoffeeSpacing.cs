namespace CometSamples.BaristaNotes.Styles;

/// <summary>Spacing scale — mirrors reference <c>AppSpacing</c>.</summary>
public static class CoffeeSpacing
{
    public const float XS  = 4f;
    public const float S   = 8f;
    public const float M   = 16f;
    public const float L   = 24f;
    public const float XL  = 32f;
    public const float XXL = 48f;
    public const float Divider = 1f;
    public const float HeaderHeight = 120f;
    public const float ActionRowHeight = 72f;
    public const float ActionRowTopPadding = 18f;
    public const float ActionRowBottomPadding = 30f;
#if IOS || __IOS__
    public const float VoiceOverlayBottomSafeArea = 34f;
#else
    public const float VoiceOverlayBottomSafeArea = 0f;
#endif
    public const float VoiceControlBottomClearance = VoiceOverlayBottomSafeArea + M;
}
