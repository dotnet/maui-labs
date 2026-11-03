namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Compatibility alias for <see cref="Presentation.ErrorContentBlock"/>.
/// </summary>
public sealed class ErrorContentBlock(string message)
    : Presentation.ErrorContentBlock(message)
{
    /// <summary>Gets the safe message to render.</summary>
    public new string Message => base.Message;
}
