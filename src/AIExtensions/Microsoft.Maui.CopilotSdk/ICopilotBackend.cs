using GitHub.Copilot;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// Abstraction over the shared GitHub Copilot SDK client. Exists to keep the sealed
/// <see cref="CopilotClient"/> behind a seam so that <see cref="CopilotSdkChatClient"/> can be
/// unit tested without a live runtime. Not part of the public API.
/// </summary>
internal interface ICopilotBackend : IAsyncDisposable, IDisposable
{
    /// <summary>Creates a brand new session and subscribes <paramref name="onEvent"/> to its events.</summary>
    Task<ICopilotSession> CreateSessionAsync(
        CopilotSessionParameters parameters,
        Action<SessionEvent> onEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resumes an existing session by id and subscribes <paramref name="onEvent"/> to its events.
    /// When <paramref name="continuePendingWork"/> is <see langword="true"/> the runtime resumes any
    /// tool calls or permission prompts that were pending when the session was last suspended.
    /// </summary>
    Task<ICopilotSession> ResumeSessionAsync(
        string sessionId,
        CopilotSessionParameters parameters,
        bool continuePendingWork,
        Action<SessionEvent> onEvent,
        CancellationToken cancellationToken);

    /// <summary>Lists the models advertised by the runtime.</summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken);

    /// <summary>Permanently deletes a session and its on-disk data.</summary>
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the underlying <see cref="CopilotClient"/> for <see cref="Microsoft.Extensions.AI.IChatClient.GetService"/>,
    /// or <see langword="null"/> when the backend is not backed by a real client (for example in tests).
    /// </summary>
    object? GetUnderlyingClient();
}
