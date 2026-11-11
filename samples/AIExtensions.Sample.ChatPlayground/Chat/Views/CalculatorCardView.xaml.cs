using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Shows a <c>calculate</c> tool call as arithmetic, while it runs and once it has a result.</summary>
public partial class CalculatorCardView : BlockView
{
    /// <summary>Initializes the card.</summary>
    public CalculatorCardView() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnBlockChanged()
    {
        if (ExpressionLabel is null)
            return;

        var call = Block as FunctionInvocationContentBlock;
        var approval = call is null ? null : FindApproval(call);
        var expression = CalculatorDisplay.FormatExpression(call?.Arguments) ?? "\u2026";

        // A rejected call gets a result that only says it was rejected; it is not an answer to show after "=".
        var rejected = approval?.Status == ApprovalStatus.Rejected;
        var result = rejected ? null : CalculatorDisplay.FormatResult(call?.Result);
        var error = rejected ? null : CalculatorDisplay.FormatError(call?.Result);
        var waiting = call?.Result is null && approval?.Status == ApprovalStatus.Pending && IsRunning(call, approval);
        var running = call?.Result is null && !waiting && !rejected && IsRunning(call, approval);

        ExpressionLabel.Text = result is not null ? $"{expression} = {result}"
            : running ? $"{expression} = \u2026"
            : expression;
        BusyIndicator.IsRunning = BusyIndicator.IsVisible = running;

        StatusLabel.Text = rejected ? "Not calculated: the call was rejected."
            : error is not null ? $"Could not calculate: {error}"
            : waiting ? "Waiting for approval"
            : running ? "Calculating\u2026"
            : result is null ? "Not calculated"
            : null;
        StatusLabel.IsVisible = StatusLabel.Text is not null;
        StatusLabel.SetDynamicResource(
            Label.TextColorProperty,
            error is not null ? AIChatThemeKeys.ErrorColor : AIChatThemeKeys.SecondaryTextColor);

        // Inside an approval card the approval provides the card, so this one draws no second border.
        if (approval is null)
        {
            Card.ClearValue(Border.StrokeThicknessProperty);
            Card.ClearValue(Border.PaddingProperty);
            Card.ClearValue(Border.BackgroundColorProperty);
        }
        else
        {
            Card.StrokeThickness = 0;
            Card.Padding = new Thickness(0, 2);
            Card.BackgroundColor = Colors.Transparent;
        }

        SemanticProperties.SetDescription(Card, StatusLabel.Text is { } status
            ? $"Calculator: {ExpressionLabel.Text}. {status}"
            : $"Calculator: {ExpressionLabel.Text}");
    }

    private ToolApprovalBlock? FindApproval(FunctionInvocationContentBlock call)
    {
        if (Session is not { } session)
            return null;

        foreach (var turn in session.Turns)
        {
            foreach (var block in turn.ResponseBlocks)
            {
                if (block is ToolApprovalBlock approval && ReferenceEquals(approval.InnerBlock, call))
                    return approval;
            }
        }

        return null;
    }

    /// <summary>Gets whether the call's turn is still running, so it may still get a result.</summary>
    private bool IsRunning(FunctionInvocationContentBlock? call, ToolApprovalBlock? approval)
    {
        if (call is null || Session is not { Status: ConversationStatus.Streaming or ConversationStatus.AwaitingInput } session ||
            session.Turns.Count == 0)
        {
            return false;
        }

        var shown = (ContentBlock?)approval ?? call;
        return session.Turns[^1].ResponseBlocks.Contains(shown);
    }
}
