using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Base class of the scenarios' cards. Besides refreshing when its block changes, a card refreshes when the session's
/// status changes, so a card whose turn stopped stops showing that it is running.
/// </summary>
public abstract class ScenarioCardView : BlockView
{
    private AgentContext? _watched;
    private IDisposable? _registration;

    /// <inheritdoc />
    protected sealed override void OnBlockChanged()
    {
        Watch(Session);
        if (Block is not null)
            Refresh();
    }

    /// <summary>Updates the card from <see cref="BlockView.Block"/>.</summary>
    protected abstract void Refresh();

    /// <summary>Gets whether a block belongs to the response that is still running, so it may still change.</summary>
    protected bool IsRunning(ContentBlock block) =>
        Session is { Status: ConversationStatus.Streaming or ConversationStatus.AwaitingInput } session &&
        session.Turns.Count > 0 &&
        session.Turns[^1].ResponseBlocks.Contains(block);

    /// <summary>Gets whether the turn that shows a block was stopped.</summary>
    protected bool IsStopped(ContentBlock block) =>
        Session?.Turns.FirstOrDefault(turn => turn.ResponseBlocks.Contains(block))?.IsStopped == true;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        Watch(Handler is null ? null : Session);
    }

    private void Watch(AgentContext? session)
    {
        if (ReferenceEquals(_watched, session))
            return;

        _registration?.Dispose();
        _watched = session;
        _registration = session?.RegisterOnStatusChanged(_ => Dispatcher.Dispatch(() =>
        {
            if (Block is not null)
                Refresh();
        }));
    }
}
