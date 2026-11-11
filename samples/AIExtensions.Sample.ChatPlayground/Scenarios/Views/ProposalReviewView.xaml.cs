using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Reviews a <c>propose_document</c> proposal: accept or reject it, or see what became of it.</summary>
public partial class ProposalReviewView : ScenarioCardView
{
    public ProposalReviewView() => InitializeComponent();

    protected override void Refresh()
    {
        if (TitleLabel is null || Block is not UIActionBlock action)
            return;

        var words = (ScenarioJson.ReadString(action.Arguments, "document") ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var accepted = action.Arguments is { } arguments &&
            arguments.TryGetValue(PredictiveStateScenario.AcceptedArgument, out var value)
            ? ScenarioJson.AsBool(value)
            : null;

        ButtonsRow.IsVisible = action.IsAwaitingInput;
        (CaptionLabel.Text, TitleLabel.Text, MessageLabel.Text) = action switch
        {
            { IsAwaitingInput: true } => ("PREDICTIVE UPDATE", "Confirm changes",
                $"The document shows the proposed edit ({words} {(words == 1 ? "word" : "words")}) with its changes " +
                "marked. Keep it?"),
            { IsComplete: true } when accepted != false => ("ACCEPTED", "\u2713 Changes committed",
                "The proposal is now the document."),
            { IsComplete: true } => ("REJECTED", "\u2715 Changes discarded",
                "The document is back to what it was."),
            _ when IsRunning(action) => ("PREDICTIVE UPDATE", "Applying your decision\u2026", string.Empty),
            _ => ("NOT REVIEWED", "Rolled back",
                "The turn ended before a decision, so the document is back to what it was."),
        };
        MessageLabel.IsVisible = MessageLabel.Text.Length > 0;
        TitleLabel.SetDynamicResource(Label.TextColorProperty, action is { IsComplete: true } && accepted != false
            ? AIChatThemeKeys.SuccessColor
            : AIChatThemeKeys.TextColor);
        SemanticProperties.SetDescription(Card, $"{CaptionLabel.Text}. {TitleLabel.Text}. {MessageLabel.Text}");
    }

    private void AcceptClicked(object? sender, EventArgs e) => Decide(accepted: true);

    private void RejectClicked(object? sender, EventArgs e) => Decide(accepted: false);

    private void Decide(bool accepted)
    {
        if (Block is not UIActionBlock { IsAwaitingInput: true } action)
            return;

        // The arguments are the call's own, so the action, and the next request, see the decision.
        if (action.Call is { } call)
        {
            call.Arguments ??= new Dictionary<string, object?>();
            call.Arguments[PredictiveStateScenario.AcceptedArgument] = accepted;
        }

        _ = action.InvokeAsync();
    }
}
