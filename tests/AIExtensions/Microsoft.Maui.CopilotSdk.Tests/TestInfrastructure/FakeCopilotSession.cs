using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

/// <summary>
/// A scriptable <see cref="ICopilotSession"/> that drives the chat client by emitting real SDK
/// <see cref="SessionEvent"/> objects through the callback captured at create/resume time.
/// </summary>
internal sealed class FakeCopilotSession : ICopilotSession
{
    private Action<SessionEvent>? _onEvent;
    private IReadOnlyList<AIFunctionDeclaration> _tools = [];

    public FakeCopilotSession(string sessionId = "session-1")
    {
        SessionId = sessionId;
    }

    public string SessionId { get; }

    public string? ResumedFromSessionId { get; set; }

    public List<MessageOptions> SentMessages { get; } = [];

    public List<(string RequestId, object? Result, string? Error)> ToolCallResults { get; } = [];

    public int AbortCount { get; private set; }

    public int DisposeCount { get; private set; }

    /// <summary>Invoked when the client sends a prompt. Use <see cref="Emit"/> to script the response.</summary>
    public Func<FakeCopilotSession, MessageOptions, Task>? OnSend { get; set; }

    /// <summary>Invoked when the client submits a tool result. Use <see cref="Emit"/> to continue the stream.</summary>
    public Func<FakeCopilotSession, string, object?, string?, Task>? OnHandleToolCall { get; set; }

    public Action<FakeCopilotSession>? OnAttach { get; set; }

    public void Configure(CopilotSessionParameters parameters) =>
        _tools = parameters.ToolDeclarations;

    public void Attach(Action<SessionEvent> onEvent)
    {
        _onEvent = onEvent;
        OnAttach?.Invoke(this);
    }

    public void Emit(SessionEvent evt)
    {
        _onEvent!(evt);
        if (evt is ExternalToolRequestedEvent request)
            StartToolInvocation(request);
    }

    public void EmitAll(params SessionEvent[] events)
    {
        foreach (var evt in events)
        {
            Emit(evt);
        }
    }

    public async Task SendAsync(MessageOptions options, CancellationToken cancellationToken)
    {
        SentMessages.Add(options);
        if (OnSend is not null)
        {
            await OnSend(this, options).ConfigureAwait(false);
        }
    }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        AbortCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    private void StartToolInvocation(ExternalToolRequestedEvent request)
    {
        var function = _tools
            .OfType<AIFunction>()
            .FirstOrDefault(tool => tool.Name == request.Data.ToolName);
        if (function is null)
            return;

        var arguments = new AIFunctionArguments
        {
            Context = new Dictionary<object, object?>
            {
                [typeof(ToolInvocation)] = new ToolInvocation
                {
                    SessionId = SessionId,
                    ToolCallId = request.Data.ToolCallId,
                    ToolName = request.Data.ToolName,
                    Arguments = request.Data.Arguments,
                },
            },
        };
        if (request.Data.Arguments is { ValueKind: System.Text.Json.JsonValueKind.Object } json)
        {
            foreach (var property in json.EnumerateObject())
                arguments[property.Name] = property.Value.Clone();
        }

        _ = CompleteToolInvocationAsync(
            request.Data.RequestId,
            function.InvokeAsync(arguments).AsTask());
    }

    private async Task CompleteToolInvocationAsync(
        string requestId,
        Task<object?> invocation)
    {
        object? result = null;
        string? error = null;
        try
        {
            result = await invocation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        ToolCallResults.Add((requestId, result, error));
        if (OnHandleToolCall is not null)
            await OnHandleToolCall(this, requestId, result, error).ConfigureAwait(false);
    }
}
