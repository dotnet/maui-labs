namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Matches the calls, approvals, and UI actions of a tool.</summary>
/// <remarks>
/// <para>
/// A tool call is a <see cref="FunctionInvocationContentBlock"/>; an approval (<see cref="ToolApprovalBlock"/>) and a
/// UI action (<see cref="UIActionBlock"/>) are <see cref="InteractiveFunctionBlock"/>s. To pick only one of them,
/// combine this match with a <see cref="BlockTypeMatch"/> in an <see cref="AllMatch"/>.
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
            InteractiveFunctionBlock interactive => interactive.ToolName,
            _ => null,
        };
        return !string.IsNullOrEmpty(Name) && string.Equals(tool, Name, StringComparison.OrdinalIgnoreCase);
    }
}
