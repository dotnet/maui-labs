using GitHub.Copilot;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// Abstraction over one GitHub Copilot SDK session. Keeps the sealed
/// <c>CopilotSession</c> behind a seam for unit testing. Not part of the public API.
/// </summary>
internal interface ICopilotSession : IAsyncDisposable
{
    /// <summary>The durable session id used as the conversation id.</summary>
    string SessionId { get; }

    /// <summary>Sends a message to the session. Returns once the message is accepted, not when the turn completes.</summary>
    Task SendAsync(MessageOptions options, CancellationToken cancellationToken);

    /// <summary>Aborts any in-flight work in the session.</summary>
    Task AbortAsync(CancellationToken cancellationToken);
}
