using Microsoft.AspNetCore.Components.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Matches <see cref="ReasoningContentBlock"/> blocks (AI "thinking" content).
/// </summary>
public class ReasoningContentTemplate : ContentTemplate
{
    public ReasoningContentTemplate()
    {
        ViewType = typeof(ReasoningContentView);
    }

    public override bool When(ContentContext context)
        => context.Block is ReasoningContentBlock;
}
