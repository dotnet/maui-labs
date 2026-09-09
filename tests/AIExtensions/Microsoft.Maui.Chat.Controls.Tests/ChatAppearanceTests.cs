using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>Covers <see cref="ChatAppearance"/>: defaults, overrides, and timestamp formatting.</summary>
public class ChatAppearanceTests
{
    [Fact]
    public void Defaults_AreUsableWithoutConfiguration()
    {
        var appearance = new ChatAppearance();

        Assert.True(appearance.ShowAvatars);
        Assert.True(appearance.ShowParticipantNames);
        Assert.True(appearance.ShowTimestamps);
        Assert.True(appearance.ShowMessageStatus);
        Assert.Equal(32.0, appearance.AvatarSize);
        Assert.Equal(18.0, appearance.BubbleCornerRadius);
        Assert.Equal(0.0, appearance.BubbleStrokeThickness);
        Assert.Equal(360.0, appearance.MaxBubbleWidth);
        Assert.Equal(2.0, appearance.ContentSpacing);
        Assert.Equal(10.0, appearance.GroupSpacing);
        Assert.Equal("t", appearance.TimestampFormat);
    }

    [Fact]
    public void Colors_DefaultToNullSoTheThemeDecides()
    {
        var appearance = new ChatAppearance();

        Assert.Null(appearance.IncomingBubbleColor);
        Assert.Null(appearance.OutgoingBubbleColor);
        Assert.Null(appearance.IncomingTextColor);
        Assert.Null(appearance.OutgoingTextColor);
        Assert.Null(appearance.BubbleStrokeColor);
    }

    [Fact]
    public void Default_IsShared()
    {
        Assert.Same(ChatAppearance.Default, ChatAppearance.Default);
        Assert.NotSame(ChatAppearance.Default, new ChatAppearance());
    }

    [Fact]
    public void Properties_AreBindableAndNotify()
    {
        var appearance = new ChatAppearance();
        using var recorder = new PropertyRecorder(appearance);

        appearance.ShowAvatars = false;
        appearance.AvatarSize = 48;
        appearance.OutgoingBubbleColor = Colors.Red;

        Assert.False(appearance.ShowAvatars);
        Assert.Equal(48, appearance.AvatarSize);
        Assert.Equal(Colors.Red, appearance.OutgoingBubbleColor);
        Assert.Contains(nameof(ChatAppearance.ShowAvatars), recorder.Names);
        Assert.Contains(nameof(ChatAppearance.AvatarSize), recorder.Names);
        Assert.Contains(nameof(ChatAppearance.OutgoingBubbleColor), recorder.Names);
    }

    [Fact]
    public void FormatTimestamp_UsesTheConfiguredFormat()
    {
        var appearance = new ChatAppearance { TimestampFormat = "yyyy-MM-dd" };
        var timestamp = new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero);

        Assert.Equal("2024-03-04", appearance.FormatTimestamp(timestamp));
    }

    [Fact]
    public void FormatTimestamp_WithTimestampsHidden_ReturnsEmpty()
    {
        var appearance = new ChatAppearance { ShowTimestamps = false };

        Assert.Equal(string.Empty, appearance.FormatTimestamp(DateTimeOffset.Now));
    }

    [Fact]
    public void FormatTimestamp_WithEmptyFormat_FallsBackToTheDefaultFormat()
    {
        var appearance = new ChatAppearance { TimestampFormat = string.Empty };

        Assert.False(string.IsNullOrEmpty(appearance.FormatTimestamp(DateTimeOffset.Now)));
    }
}
