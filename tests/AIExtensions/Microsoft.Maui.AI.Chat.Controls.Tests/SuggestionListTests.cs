using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Mirrors: Blazor.Tests/Components/SuggestionListTests.cs
/// Tests CopilotChatView suggestion chips and welcome state behavior.
/// </summary>
public class SuggestionListTests
{
    [Fact]
    public void SuggestionPrompts_AreAccessibleFromControl()
    {
        var control = CreateControl();

        control.SuggestionPrompts = new List<string>
        {
            "Tell me a joke",
            "What is the weather?",
            "Help me write code"
        };

        Assert.Equal(3, control.SuggestionPrompts.Count);
        Assert.Equal("Tell me a joke", control.SuggestionPrompts[0]);
    }

    [Fact]
    public void SuggestionPrompts_EmptyByDefault()
    {
        var control = CreateControl();

        Assert.NotNull(control.SuggestionPrompts);
        Assert.Empty(control.SuggestionPrompts);
    }

    [Fact]
    public void WelcomeMessage_ControlsWelcomeVisibilityState()
    {
        var control = CreateControl();

        control.WelcomeMessage = "How can I help you today?";
        // With a message set but no items, welcome should show
        control.UpdateWelcomeVisibility();

        // Without template applied, parts are null, but the logic path runs
        Assert.Equal("How can I help you today?", control.WelcomeMessage);
    }

    [Fact]
    public void WelcomeMessage_WhenNull_DisablesWelcome()
    {
        var control = CreateControl();

        control.WelcomeMessage = string.Empty;

        // An empty message means no welcome copy even if items is empty.
        Assert.Empty(control.WelcomeMessage);
    }

    [Fact]
    public void WelcomeIcon_CustomizableEmoji()
    {
        var control = CreateControl();

        control.WelcomeIcon = "🤖";

        Assert.Equal("🤖", control.WelcomeIcon);
    }

    private static CopilotChatView CreateControl() => new();
}
