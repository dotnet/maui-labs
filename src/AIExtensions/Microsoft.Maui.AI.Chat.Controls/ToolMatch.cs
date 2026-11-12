namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches the calls and UI actions of a tool: the blocks that show the tool's calls.</summary>
/// <remarks>
/// <para>
/// A tool call is a <see cref="FunctionInvocationContentBlock"/>, including a typed block for the tool, and a UI action
/// is a <see cref="UIActionBlock"/>. To pick only one of them, combine this match with a <see cref="BlockTypeMatch"/> in
/// an <see cref="AllMatch"/>. An approval (<see cref="ToolApprovalBlock"/>) doesn't match, so a template for a tool's
/// card never takes the approval in its place: an <see cref="ApprovalWrapper"/> shows the approval around the card that
/// this match picks for its call, and its own <see cref="BlockWrapper.When"/> can be a tool match.
/// </para>
/// <para>In XAML, <c>{chat:Tool calculate}</c> creates one. Without a <see cref="Name"/>, no block matches.</para>
/// </remarks>
public sealed class ToolMatch : BlockMatch
{
    /// <summary>Gets or sets the tool name, compared case-insensitively.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    public override bool Matches(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var tool = block switch
        {
            FunctionInvocationContentBlock call => call.ToolName,
            UIActionBlock action => action.ToolName,
            _ => null,
        };
        return !string.IsNullOrEmpty(Name) && string.Equals(tool, Name, StringComparison.OrdinalIgnoreCase);
    }
}
