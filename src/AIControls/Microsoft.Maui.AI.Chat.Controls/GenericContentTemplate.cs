using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// A fully declarative content template that matches blocks by Role, ToolName, and/or BlockType.
/// Use this in XAML to match specific content without writing a subclass:
/// <code>
/// &lt;chat:GenericContentTemplate Role="Assistant" ViewType="{x:Type local:MyAssistantView}" /&gt;
/// &lt;chat:GenericContentTemplate ToolName="GetWeather" ViewType="{x:Type local:WeatherView}" /&gt;
/// &lt;chat:GenericContentTemplate BlockType="{x:Type ai:ReasoningContentBlock}" ViewType="{x:Type local:ReasoningView}" /&gt;
/// </code>
/// All filter properties are optional; when multiple are set, ALL must match (AND logic).
/// </summary>
public class GenericContentTemplate : ContentTemplate
{
    public static readonly BindableProperty RoleProperty =
        BindableProperty.Create(nameof(Role), typeof(string), typeof(GenericContentTemplate));

    public static readonly BindableProperty ToolNameProperty =
        BindableProperty.Create(nameof(ToolName), typeof(string), typeof(GenericContentTemplate));

    public static readonly BindableProperty BlockTypeProperty =
        BindableProperty.Create(nameof(BlockType), typeof(Type), typeof(GenericContentTemplate));

    /// <summary>
    /// Optional role filter. "User", "Assistant", or "Tool".
    /// </summary>
    public string? Role
    {
        get => (string?)GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    /// <summary>
    /// Optional tool name filter. Matches FunctionInvocationContentBlock or FunctionApprovalBlock tool name.
    /// </summary>
    public string? ToolName
    {
        get => (string?)GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    /// <summary>
    /// Optional block type filter. Matches the concrete type of the ContentBlock.
    /// Use <c>{x:Type ai:RichContentBlock}</c> syntax in XAML.
    /// </summary>
    public Type? BlockType
    {
        get => (Type?)GetValue(BlockTypeProperty);
        set => SetValue(BlockTypeProperty, value);
    }

    public override bool When(ContentContext context)
    {
        if (Role is not null)
        {
            var expectedRole = Role switch
            {
                var r when r.Equals("User", StringComparison.OrdinalIgnoreCase) => ChatRole.User,
                var r when r.Equals("Assistant", StringComparison.OrdinalIgnoreCase) => ChatRole.Assistant,
                var r when r.Equals("Tool", StringComparison.OrdinalIgnoreCase) => ChatRole.Tool,
                _ => new ChatRole(Role),
            };
            if (context.Role != expectedRole)
                return false;
        }

        if (ToolName is not null)
        {
            if (!string.Equals(context.ToolName, ToolName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (BlockType is not null)
        {
            if (!BlockType.IsAssignableFrom(context.Block.GetType()))
                return false;
        }

        return true;
    }

    internal override int GetPriority(ContentContext context)
    {
        var boost = 0;
        if (Role is not null) boost += 50;
        if (ToolName is not null) boost += 100;
        if (BlockType is not null) boost += 25;
        return base.GetPriority(context) + boost;
    }
}
