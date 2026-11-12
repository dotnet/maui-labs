namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Decides whether a <see cref="BlockTemplate"/> applies to a <see cref="ContentBlock"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each derived type tests one thing: <see cref="BlockTypeMatch"/> the block's type, <see cref="ToolMatch"/> the tool
/// of a call, approval, or UI action, and <see cref="RoleMatch"/> the role. <see cref="AllMatch"/>,
/// <see cref="AnyMatch"/>, and <see cref="NotMatch"/> combine them. Derive from <see cref="BlockMatch"/> for any other
/// condition.
/// </para>
/// <para>
/// In XAML, write the common cases with the markup extensions (<c>{chat:Is chat:TextContentBlock}</c>,
/// <c>{chat:Tool calculate}</c>, <c>{chat:Role user}</c>) and compose the types as elements. In code, use the factory
/// methods, such as <c>BlockMatch.All(BlockMatch.Is&lt;FunctionInvocationContentBlock&gt;(), BlockMatch.Tool("calculate"))</c>.
/// </para>
/// <para>
/// A template is selected again whenever a list shows a block, and some lists select again as the block streams, so
/// keep <see cref="Matches(ContentBlock)"/> fast and free of side effects.
/// </para>
/// </remarks>
public abstract class BlockMatch
{
    /// <summary>Initializes a new instance of the <see cref="BlockMatch"/> class.</summary>
    protected BlockMatch()
    {
    }

    /// <summary>Gets whether <paramref name="block"/> satisfies this match.</summary>
    /// <param name="block">The block to test.</param>
    /// <returns><see langword="true"/> when the block satisfies the match.</returns>
    public abstract bool Matches(ContentBlock block);

    /// <summary>Matches blocks of type <typeparamref name="TBlock"/>, including derived types.</summary>
    /// <typeparam name="TBlock">The block type, such as <see cref="TextContentBlock"/> or a <c>[ToolBlock]</c> type.</typeparam>
    /// <returns>A <see cref="BlockTypeMatch"/>.</returns>
    public static BlockMatch Is<TBlock>()
        where TBlock : ContentBlock =>
        new BlockTypeMatch { Type = typeof(TBlock) };

    /// <summary>Matches the calls and UI actions of a tool (case-insensitive), but not its approvals.</summary>
    /// <param name="name">The tool name.</param>
    /// <returns>A <see cref="ToolMatch"/>.</returns>
    public static BlockMatch Tool(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new ToolMatch { Name = name };
    }

    /// <summary>Matches blocks with a role, such as <see cref="ChatRole.User"/> (case-insensitive).</summary>
    /// <param name="role">The role.</param>
    /// <returns>A <see cref="RoleMatch"/>.</returns>
    public static BlockMatch Role(ChatRole role) => new RoleMatch { Role = role.Value };

    /// <summary>Matches blocks that satisfy every one of <paramref name="matches"/>.</summary>
    /// <param name="matches">The conditions.</param>
    /// <returns>An <see cref="AllMatch"/>.</returns>
    public static BlockMatch All(params BlockMatch[] matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var all = new AllMatch();
        foreach (var match in matches)
            all.Conditions.Add(match);
        return all;
    }

    /// <summary>Matches blocks that satisfy at least one of <paramref name="matches"/>.</summary>
    /// <param name="matches">The conditions.</param>
    /// <returns>An <see cref="AnyMatch"/>.</returns>
    public static BlockMatch Any(params BlockMatch[] matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var any = new AnyMatch();
        foreach (var match in matches)
            any.Conditions.Add(match);
        return any;
    }

    /// <summary>Matches blocks that don't satisfy <paramref name="match"/>.</summary>
    /// <param name="match">The condition to invert.</param>
    /// <returns>A <see cref="NotMatch"/>.</returns>
    public static BlockMatch Not(BlockMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new NotMatch { Match = match };
    }

    /// <summary>Matches blocks for which <paramref name="predicate"/> returns <see langword="true"/>.</summary>
    /// <param name="predicate">The condition, such as <c>block =&gt; block is FunctionInvocationContentBlock { Result.Exception: not null }</c>.</param>
    /// <returns>A match that calls <paramref name="predicate"/>.</returns>
    public static BlockMatch When(Func<ContentBlock, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new PredicateMatch(predicate);
    }

    private sealed class PredicateMatch(Func<ContentBlock, bool> predicate) : BlockMatch
    {
        public override bool Matches(ContentBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);
            return predicate(block);
        }
    }
}
