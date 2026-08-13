using GitHub.Copilot;

namespace Microsoft.Maui.CopilotSdk.Tests;

/// <summary>
/// A fake <see cref="ICopilotBackend"/> that hands out pre-scripted <see cref="FakeCopilotSession"/>
/// instances and records how the client drove it.
/// </summary>
internal sealed class FakeCopilotBackend : ICopilotBackend
{
    private int _index;

    /// <summary>The sessions handed out, in order, for create/resume calls.</summary>
    public List<FakeCopilotSession> Sessions { get; } = [];

    /// <summary>Records every create/resume call the client made.</summary>
    public List<RecordedSessionCall> Calls { get; } = [];

    public IReadOnlyList<ModelInfo> Models { get; set; } = [];

    public object? UnderlyingClient { get; set; }

    public List<string> DeletedSessions { get; } = [];

    public int DisposeAsyncCount { get; private set; }

    public int DisposeCount { get; private set; }

    public FakeCopilotSession AddSession(FakeCopilotSession session)
    {
        Sessions.Add(session);
        return session;
    }

    public Task<ICopilotSession> CreateSessionAsync(
        CopilotSessionParameters parameters,
        Action<SessionEvent> onEvent,
        CancellationToken cancellationToken)
    {
        Calls.Add(new RecordedSessionCall(RecordedSessionCallKind.Create, parameters, ContinuePendingWork: false, SessionId: null));
        return Task.FromResult<ICopilotSession>(Next(parameters, onEvent));
    }

    public Task<ICopilotSession> ResumeSessionAsync(
        string sessionId,
        CopilotSessionParameters parameters,
        bool continuePendingWork,
        Action<SessionEvent> onEvent,
        CancellationToken cancellationToken)
    {
        Calls.Add(new RecordedSessionCall(RecordedSessionCallKind.Resume, parameters, continuePendingWork, sessionId));
        var session = Next(parameters, onEvent);
        session.ResumedFromSessionId = sessionId;
        return Task.FromResult<ICopilotSession>(session);
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        => Task.FromResult(Models);

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        DeletedSessions.Add(sessionId);
        return Task.CompletedTask;
    }

    public object? GetUnderlyingClient() => UnderlyingClient;

    public ValueTask DisposeAsync()
    {
        DisposeAsyncCount++;
        return ValueTask.CompletedTask;
    }

    public void Dispose() => DisposeCount++;

    private FakeCopilotSession Next(
        CopilotSessionParameters parameters,
        Action<SessionEvent> onEvent)
    {
        var session = _index < Sessions.Count ? Sessions[_index] : new FakeCopilotSession();
        _index++;
        session.Configure(parameters);
        session.Attach(onEvent);
        return session;
    }
}

internal enum RecordedSessionCallKind
{
    Create,
    Resume,
}

internal sealed record RecordedSessionCall(
    RecordedSessionCallKind Kind,
    CopilotSessionParameters Parameters,
    bool ContinuePendingWork,
    string? SessionId);
