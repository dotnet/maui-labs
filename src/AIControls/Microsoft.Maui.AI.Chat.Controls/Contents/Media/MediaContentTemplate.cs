using Microsoft.AspNetCore.Components.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Matches <see cref="MediaContentBlock"/> blocks containing images or files.
/// </summary>
public class MediaContentTemplate : ContentTemplate
{
    public MediaContentTemplate()
    {
        ViewType = typeof(MediaContentView);
    }

    public override bool When(ContentContext context)
        => context.Block is MediaContentBlock;
}
