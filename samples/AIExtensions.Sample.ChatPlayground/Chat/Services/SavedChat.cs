using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// What the chat saves between runs: the conversation, which the engine writes to a <see cref="FileConversationThread"/>,
/// and the notes, which <see cref="SharedNotes"/> saves next to it.
/// </summary>
public static class SavedChat
{
    /// <summary>
    /// Shows the conversation and the notes that were saved when the app last ran. The notes come back even when the
    /// conversation cannot, such as from a damaged file, so the next edit does not overwrite them.
    /// </summary>
    /// <param name="session">The chat, which has no turns yet.</param>
    /// <param name="thread">The saved conversation.</param>
    /// <param name="notes">The notes bar.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>
    /// What to tell the user: that the conversation was restored, or why it could not be read; or
    /// <see langword="null"/> when none was saved.
    /// </returns>
    public static async Task<string?> RestoreAsync(
        AgentContext session,
        FileConversationThread thread,
        NotesViewModel notes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(notes);

        // Restoring resets the notes, and the conversation cannot replay the user's own edits or decisions, so the
        // saved notes are read first and put back afterwards, also when the restore fails.
        var saved = notes.ReadSaved();
        try
        {
            if (thread.LoadError is { } loadError)
                return loadError;
            if (thread.TurnCount == 0)
                return null;

            await session.RestoreAsync(cancellationToken);
            return $"Restored the saved conversation ({thread.TurnCount} {(thread.TurnCount == 1 ? "turn" : "turns")}).";
        }
        finally
        {
            notes.Load(saved);
        }
    }

    /// <summary>
    /// Starts a new chat: clears the conversation, which also clears the saved one and empties the notes, and deletes
    /// the saved notes, also when the notes were empty already.
    /// </summary>
    /// <param name="session">The chat.</param>
    /// <param name="notes">The notes bar.</param>
    public static async Task ClearAsync(AgentContext session, NotesViewModel notes)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(notes);

        await session.ClearAsync();
        notes.DeleteSaved();
    }
}
