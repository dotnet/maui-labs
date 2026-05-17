using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Headless chat session contract that can be hosted by MAUI, WinForms, console apps, or other UI layers.
/// </summary>
/// <remarks>
/// The <see cref="Changed"/> event may be raised on any thread. UI consumers must marshal
/// to their own thread (e.g., via <c>Dispatcher.Dispatch</c>) before updating controls.
/// </remarks>
public interface IChatSession
{
    event EventHandler<ChatSessionChangedEventArgs>? Changed;

    IReadOnlyList<ChatEntry> Messages { get; }

    IReadOnlyCollection<ChatEntry> PendingApprovals { get; }

    bool IsBusy { get; }

    bool HasPendingApprovals { get; }

    bool AllowMultipleToolCalls { get; set; }

    string? ConversationId { get; }

    string? SystemPrompt { get; set; }

    Task SendAsync(string userMessage, CancellationToken cancellationToken = default);

    Task SubmitApprovalAsync(ToolApprovalResponseContent response, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the currently active streaming request without clearing the conversation.
    /// </summary>
    Task CancelAsync();

    /// <summary>
    /// Cancels any active request and clears all conversation history.
    /// </summary>
    void Clear();
}
