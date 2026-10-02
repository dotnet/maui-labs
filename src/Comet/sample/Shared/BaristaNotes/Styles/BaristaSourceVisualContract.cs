#nullable enable
using Microsoft.Maui.Graphics;

namespace CometSamples.BaristaNotes.Styles;

public static class BaristaSourceVisualContract
{
    public const float ContentInset = 16f;
    public const float SectionTopPadding = 24f;
    public const float SectionBottomPadding = 10f;

    public const float PickerNormalFontSize = 22f;
    public const float PickerSelectedFontSize = 28f;
    public const float PickerHorizontalPadding = 24f;
    public const float PickerVerticalPadding = 18f;

    public const float VoicePanelHeight = 420f;
    public const float VoiceMicSize = 80f;
    public static readonly Color VoicePanel = Color.FromArgb("#1E1E1E");
    public static readonly Color VoicePrimaryText = Colors.White;
    public static readonly Color VoiceSecondaryText = Color.FromArgb("#CCCCCC");
    public static readonly Color VoiceActive = Color.FromArgb("#FF9500");
    public static readonly Color VoiceResponse = Color.FromArgb("#90EE90");
    public static readonly Color VoiceMicIdle = Color.FromArgb("#333333");
    public static readonly Color VoiceMicIdleOutline = Color.FromArgb("#888888");

    public const float SourceButtonHorizontalPadding = 14f;
    public const float SourceButtonVerticalPadding = 10f;
    public const float SourceButtonMinimumHeight = 44f;
    public const float SourceButtonRadius = 8f;
    public const float BagStatusFontSize = 11f;
}
