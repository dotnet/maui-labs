namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Compatibility alias for <see cref="Presentation.ThinkingContentBlock"/>.
/// </summary>
public sealed class ThinkingContentBlock(string text = "Thinking…")
    : Presentation.ThinkingContentBlock(text)
{
    /// <summary>Gets the status text to render.</summary>
    public new string Text => base.Text;
}
