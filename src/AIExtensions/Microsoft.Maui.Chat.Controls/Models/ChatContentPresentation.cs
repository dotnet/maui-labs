namespace Microsoft.Maui.Chat.Controls;

/// <summary>Controls whether message content is placed inside the standard chat bubble.</summary>
public enum ChatContentPresentation
{
    /// <summary>Render the content inside the themed message bubble.</summary>
    Bubble,

    /// <summary>
    /// Render the content directly in the message chrome. The participant avatar, name, direction,
    /// grouping, timestamp, and delivery status remain, but the content supplies its own visual surface.
    /// </summary>
    Bare,
}
