namespace Microsoft.Maui.AI.Chat.Controls.Blazor;

internal static class AgentActionRunner
{
    internal static async Task InvokeAsync(Func<Task> invocation)
    {
        try
        {
            await invocation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // AgentContext owns the conversation-level cancellation state.
        }
        catch
        {
            // AgentContext observes the same task and projects the retryable error state.
        }
    }
}
