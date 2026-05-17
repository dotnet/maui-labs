using Microsoft.Maui.AI.Chat.Controls;
using Microsoft.Maui.AI.Chat.Controls.Themes;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class CopilotChatViewPropertyTests
{
    private static CopilotChatView Create() => new();

    // ── Input Area Defaults ──

    [Fact]
    public void Placeholder_DefaultsToTypeAMessage()
    {
        var control = Create();
        Assert.Equal("Type a message...", control.Placeholder);
    }

    [Fact]
    public void SendButtonText_DefaultsToArrow()
    {
        var control = Create();
        Assert.Equal("➤", control.SendButtonText);
    }

    [Fact]
    public void InputAreaCornerRadius_DefaultsTo14()
    {
        var control = Create();
        Assert.Equal(14.0, control.InputAreaCornerRadius);
    }

    // ── Welcome Defaults ──

    [Fact]
    public void WelcomeMessage_DefaultsToNull()
    {
        var control = Create();
        Assert.Null(control.WelcomeMessage);
    }

    [Fact]
    public void WelcomeIcon_DefaultsToEmoji()
    {
        var control = Create();
        Assert.Equal("💬", control.WelcomeIcon);
    }

    // ── Avatar Defaults ──

    [Fact]
    public void ShowAvatars_DefaultsToFalse()
    {
        var control = Create();
        Assert.False(control.ShowAvatars);
    }

    [Fact]
    public void AvatarSize_DefaultsTo28()
    {
        var control = Create();
        Assert.Equal(28.0, control.AvatarSize);
    }

    [Fact]
    public void UserDisplayName_DefaultsToYou()
    {
        var control = Create();
        Assert.Equal("You", control.UserDisplayName);
    }

    [Fact]
    public void AssistantDisplayName_DefaultsToAssistant()
    {
        var control = Create();
        Assert.Equal("Assistant", control.AssistantDisplayName);
    }

    // ── Timestamp & Visibility Defaults ──

    [Fact]
    public void ShowTimestamps_DefaultsToFalse()
    {
        var control = Create();
        Assert.False(control.ShowTimestamps);
    }

    [Fact]
    public void ShowToolCalls_DefaultsToTrue()
    {
        var control = Create();
        Assert.True(control.ShowToolCalls);
    }

    [Fact]
    public void ShowToolResults_DefaultsToTrue()
    {
        var control = Create();
        Assert.True(control.ShowToolResults);
    }

    // ── Bubble Styling Defaults ──

    [Fact]
    public void BubbleCornerRadius_DefaultsTo16()
    {
        var control = Create();
        Assert.Equal(16.0, control.BubbleCornerRadius);
    }

    [Fact]
    public void BubbleStrokeThickness_DefaultsTo0()
    {
        var control = Create();
        Assert.Equal(0.0, control.BubbleStrokeThickness);
    }

    [Fact]
    public void MaxBubbleWidth_DefaultsTo340()
    {
        var control = Create();
        Assert.Equal(340.0, control.MaxBubbleWidth);
    }

    // ── Layout Template Defaults ──

    [Fact]
    public void HeaderTemplate_DefaultsToNull()
    {
        var control = Create();
        Assert.Null(control.HeaderTemplate);
    }

    [Fact]
    public void FooterTemplate_DefaultsToNull()
    {
        var control = Create();
        Assert.Null(control.FooterTemplate);
    }

    // ── Suggestions Default ──

    [Fact]
    public void SuggestionPrompts_DefaultsToEmptyList()
    {
        var control = Create();
        Assert.NotNull(control.SuggestionPrompts);
        Assert.Empty(control.SuggestionPrompts);
    }

    // ── Property Change Roundtrip ──

    [Fact]
    public void Placeholder_BindableProperty_HasCorrectDefault()
    {
        // Verify the default value of the bindable property itself
        Assert.Equal("Type a message...", CopilotChatView.PlaceholderProperty.DefaultValue);
    }

    [Fact]
    public void Placeholder_BindableProperty_NameIsCorrect()
    {
        Assert.Equal("Placeholder", CopilotChatView.PlaceholderProperty.PropertyName);
    }

    [Fact]
    public void ShowTimestamps_SetToTrue_Roundtrips()
    {
        var control = Create();
        control.ShowTimestamps = true;
        Assert.True(control.ShowTimestamps);
    }

    [Fact]
    public void BubbleCornerRadius_SetAndGet_Roundtrips()
    {
        var control = Create();
        control.BubbleCornerRadius = 8.0;
        Assert.Equal(8.0, control.BubbleCornerRadius);
    }

    [Fact]
    public void MaxBubbleWidth_SetAndGet_Roundtrips()
    {
        var control = Create();
        control.MaxBubbleWidth = 500.0;
        Assert.Equal(500.0, control.MaxBubbleWidth);
    }

    [Fact]
    public void SuggestionPrompts_SetAndGet_Roundtrips()
    {
        var control = Create();
        var prompts = new List<string> { "Hello", "Help" };
        control.SuggestionPrompts = prompts;
        Assert.Same(prompts, control.SuggestionPrompts);
    }

    [Fact]
    public void WelcomeMessage_SetAndGet_Roundtrips()
    {
        var control = Create();
        control.WelcomeMessage = "Welcome!";
        Assert.Equal("Welcome!", control.WelcomeMessage);
    }
}

public class ChatThemeKeysTests
{
    [Theory]
    [InlineData(ChatThemeKeys.ChatMessageTemplate, "ExtensionsAI.ChatMessageTemplate")]
    [InlineData(ChatThemeKeys.FunctionCallTemplate, "ExtensionsAI.FunctionCallTemplate")]
    [InlineData(ChatThemeKeys.FunctionResultTemplate, "ExtensionsAI.FunctionResultTemplate")]
    [InlineData(ChatThemeKeys.ErrorTemplate, "ExtensionsAI.ErrorTemplate")]
    [InlineData(ChatThemeKeys.DefaultTemplate, "ExtensionsAI.DefaultTemplate")]
    [InlineData(ChatThemeKeys.ToolApprovalTemplate, "ExtensionsAI.ToolApprovalTemplate")]
    [InlineData(ChatThemeKeys.InputBackground, "ExtensionsAI.Input.Background")]
    [InlineData(ChatThemeKeys.SendBackground, "ExtensionsAI.Send.Background")]
    [InlineData(ChatThemeKeys.SuggestionBackground, "ExtensionsAI.Suggestion.Background")]
    [InlineData(ChatThemeKeys.TimestampTextColor, "ExtensionsAI.Timestamp.TextColor")]
    [InlineData(ChatThemeKeys.BubbleMaxWidth, "ExtensionsAI.Bubble.MaxWidth")]
    public void ThemeKeyConstants_HaveExpectedValues(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }
}
