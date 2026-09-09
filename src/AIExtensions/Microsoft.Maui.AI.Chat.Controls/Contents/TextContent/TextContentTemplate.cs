using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Maps a <see cref="RichContentBlock"/> to the provider-neutral text message view.</summary>
public class TextContentTemplate : ContentTemplate
{
    /// <summary>
    /// Optional role filter. Use "User" or "Assistant" to restrict matching.
    /// </summary>
    public string? Role { get; set; }

    public override bool When(ContentContext context)
    {
        if (context.Block is not RichContentBlock)
            return false;

        if (Role is not null)
        {
            var expectedRole = Role.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            if (context.Role != expectedRole)
                return false;
        }

        return true;
    }

    protected override DataTemplate CreateTemplate()
    {
        if (ViewType is not null)
            return base.CreateTemplate();

        return CreateMessageTemplate(() => new ChatTextContentView());
    }

    internal override int GetPriority(ContentContext context) =>
        base.GetPriority(context) + (Role is null ? 0 : 100);
}
