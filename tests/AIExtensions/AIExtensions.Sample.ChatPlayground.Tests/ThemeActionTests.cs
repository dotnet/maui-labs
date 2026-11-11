using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The <c>set_theme</c> UI action: the app runs it as soon as the model calls it, or after the user approves it while
/// "Ask before running tools" is on.
/// </summary>
public sealed class ThemeActionTests
{
    [Fact]
    public async Task SetTheme_RunsAtOnceWhileToolsDoNotAskFirst()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Call("call-1", "dark")),
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "It's dark now.")));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);

        await harness.Session.SendMessageAsync("Switch to dark mode");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal([AppTheme.Dark], harness.AppliedThemes);
        var action = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal(ThemeAction.ToolName, action.ToolName);
        Assert.Equal(UIActionInvocationMode.Automatic, action.Mode);
        Assert.True(action.IsComplete);
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, block => block is ToolApprovalBlock);

        // The model hears what happened in the request that continues the turn.
        var sent = Assert.Single(model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call-1", sent.CallId);
        Assert.Equal("The app now uses the dark theme.", sent.Result?.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetTheme_AsksFirstWhileToolsAskBeforeRunning(bool approve)
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Call("call-1", "light")),
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, approve ? "It's light now." : "Okay.")));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);
        harness.Settings.RequireToolApproval = true;
        AppTheme[]? appliedWhileAsking = null;
        using var decide = harness.Session.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            appliedWhileAsking = [.. harness.AppliedThemes];
            var approval = harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>().Single();
            if (approve)
                approval.Approve();
            else
                approval.Reject("The user rejected this call.");
        });

        await harness.Session.SendMessageAsync("Switch to light mode");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Empty(appliedWhileAsking!);
        AppTheme[] applied = approve ? [AppTheme.Light] : [];
        Assert.Equal(applied, harness.AppliedThemes);
        var approval = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ThemeAction.ToolName, approval.ToolName);
        Assert.Equal(approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected, approval.Status);
        Assert.Equal(approve, approval.HasResult && approval.Result?.Result?.ToString() == "The app now uses the light theme.");
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, block => block is UIActionBlock);
    }

    [Theory]
    [InlineData("light", AppTheme.Light, "The app now uses the light theme.")]
    [InlineData(" Dark ", AppTheme.Dark, "The app now uses the dark theme.")]
    [InlineData("system", AppTheme.Unspecified, "The app now follows the system theme.")]
    public async Task SetTheme_AppliesTheThemeAndSaysWhatChanged(string theme, AppTheme expected, string message)
    {
        var applied = new List<AppTheme>();
        var action = ThemeAction.Create(applied.Add);

        var result = await action.InvokeAsync(new AIFunctionArguments { ["theme"] = theme });

        Assert.Equal([expected], applied);
        Assert.Equal(message, result?.ToString());
        Assert.NotNull(action.GetService<ApprovalRequiredAIFunction>());
    }

    [Fact]
    public async Task SetTheme_RejectsAnUnknownThemeWithoutChangingAnything()
    {
        var applied = new List<AppTheme>();

        var result = await ThemeAction.Create(applied.Add).InvokeAsync(new AIFunctionArguments { ["theme"] = "purple" });

        Assert.Empty(applied);
        Assert.StartsWith("Error:", result?.ToString(), StringComparison.Ordinal);
    }

    private static ChatResponseUpdate Call(string callId, string theme) =>
        new(ChatRole.Assistant,
            [new FunctionCallContent(callId, ThemeAction.ToolName, new Dictionary<string, object?> { ["theme"] = theme })])
        {
            MessageId = "call",
        };
}
