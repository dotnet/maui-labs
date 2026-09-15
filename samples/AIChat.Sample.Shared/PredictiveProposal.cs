using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Presentation;

namespace AIChat.Sample.Shared;

/// <summary>Identifies only the manual predictive proposal action for narrow renderer registration.</summary>
public static class PredictiveProposal
{
    public static bool IsPredictiveProposal(AgentBlockContent content) =>
        content.Block is UIActionBlock
        {
            Call.Name: "propose_document",
        };
}
