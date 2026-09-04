using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Chat;

/// <summary>
/// The "raw block" rendering mode: a single catch-all template that renders EVERY block with
/// <see cref="BlockPreviewView"/> (type heading + values). Register it as the only template on a control
/// to visualize the underlying blocks the pipeline produced, independent of the designed views.
/// </summary>
public sealed class BlockPreviewTemplate : ContentTemplate
{
    public BlockPreviewTemplate() => ViewType = typeof(BlockPreviewView);

    public override bool When(ContentContext context) => true;
}
