namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>One item of the message list. Rows are reused while the turn or block they show stays visible.</summary>
internal abstract class ChatRow
{
    /// <summary>Raised once per coalesced batch of changes to the row's content.</summary>
    public event EventHandler? Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>The user's message: the text and media they sent in one bubble.</summary>
internal sealed class UserMessageRow(ConversationTurn turn) : ChatRow
{
    public ConversationTurn Turn { get; } = turn;

    public IReadOnlyList<ContentBlock> Blocks { get; private set; } = [];

    /// <summary>Replaces the blocks shown in the bubble and reports whether they changed.</summary>
    public bool SetBlocks(List<ContentBlock> blocks)
    {
        if (Blocks.SequenceEqual(blocks))
            return false;

        Blocks = blocks;
        return true;
    }
}

/// <summary>One content block rendered with the template selected for it.</summary>
internal sealed class BlockRow(ContentBlock block, BlockTemplate template, AgentContext session, ConversationTurn turn, AIChatView owner) : ChatRow
{
    public ContentBlock Block { get; } = block;

    public BlockTemplate Template { get; } = template;

    /// <summary>Gets the <see cref="BlockTemplate.Version"/> the row was created with; an edited template recreates its views.</summary>
    public int TemplateVersion { get; } = template.Version;

    public AgentContext Session { get; } = session;

    public ConversationTurn Turn { get; } = turn;

    public AIChatView Owner { get; } = owner;
}

/// <summary>Shown while the assistant is working and nothing visible has streamed yet.</summary>
internal sealed class ThinkingRow : ChatRow
{
}

/// <summary>Shown under a turn that failed, with a retry action.</summary>
internal sealed class TurnErrorRow(ConversationTurn turn, AgentContext session) : ChatRow
{
    public ConversationTurn Turn { get; } = turn;

    public AgentContext Session { get; } = session;
}

/// <summary>Marks a response the user stopped; its partial output stays above it.</summary>
internal sealed class TurnStoppedRow(ConversationTurn turn) : ChatRow
{
    public ConversationTurn Turn { get; } = turn;
}

/// <summary>The app's <see cref="AIChatView.ResponseFooterTemplate"/> under a completed response.</summary>
internal sealed class TurnFooterRow(ConversationTurn turn, DataTemplate template) : ChatRow
{
    public ConversationTurn Turn { get; } = turn;

    public DataTemplate Template { get; } = template;
}
