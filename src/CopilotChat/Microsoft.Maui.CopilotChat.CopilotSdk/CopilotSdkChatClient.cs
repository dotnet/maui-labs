using System.Runtime.CompilerServices;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotChat;

/// <summary>
/// An <see cref="IChatClient"/> adapter that wraps the GitHub Copilot SDK.
/// Creates a <see cref="CopilotClient"/> and <see cref="CopilotSession"/> on first use,
/// and maps streaming events to the M.E.AI streaming interface.
/// </summary>
public sealed class CopilotSdkChatClient : IChatClient, IAsyncDisposable
{
    private readonly CopilotChatConfiguration _config;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private CopilotClient? _client;
    private CopilotSession? _session;
    private bool _disposed;

    /// <summary>
    /// Inactivity timeout for streaming. If no event is received within this duration,
    /// the stream completes with an error. Default: 5 minutes.
    /// </summary>
    public TimeSpan StreamingTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public CopilotSdkChatClient(CopilotChatConfiguration config)
    {
        _config = config;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Fire-and-forget async cleanup to avoid sync-over-async deadlock
        _ = DisposeAsyncCore();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisposeAsyncCore().ConfigureAwait(false);
    }

    private async Task DisposeAsyncCore()
    {
        if (_session is not null)
            await _session.DisposeAsync().ConfigureAwait(false);
        if (_client is not null)
            await _client.DisposeAsync().ConfigureAwait(false);
        _initLock.Dispose();
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
            chunks.Add(update);

        var fullText = string.Join("", chunks
            .SelectMany(c => c.Contents.OfType<TextContent>())
            .Select(tc => tc.Text));

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, fullText)]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        // Build prompt from the last user message (Copilot SDK manages conversation state server-side)
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var prompt = lastUserMessage?.Text ?? "";

        if (string.IsNullOrEmpty(prompt))
            yield break;

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = new System.Collections.Concurrent.ConcurrentQueue<ChatResponseUpdate>();
        var lastActivity = DateTime.UtcNow;

        using var sub = _session!.On(evt =>
        {
            lastActivity = DateTime.UtcNow;
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    updates.Enqueue(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(delta.Data.DeltaContent)]
                    });
                    break;

                // Skip AssistantMessageEvent to avoid duplicating content already sent as deltas
                case AssistantMessageEvent:
                    break;

                case ToolExecutionStartEvent tool:
                    updates.Enqueue(new ChatResponseUpdate
                    {
                        Contents = [new FunctionCallContent(tool.Data.ToolCallId ?? "", tool.Data.ToolName ?? "")]
                    });
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    updates.Enqueue(new ChatResponseUpdate
                    {
                        Contents = [new FunctionResultContent(
                            toolComplete.Data.ToolCallId ?? "",
                            toolComplete.Data.Result)]
                    });
                    break;

                case SessionIdleEvent:
                    done.TrySetResult(true);
                    break;

                case SessionErrorEvent err:
                    done.TrySetException(new InvalidOperationException(err.Data.Message));
                    break;
            }
        });

        await _session!.SendAsync(new MessageOptions { Prompt = prompt });

        // Yield updates as they arrive with inactivity timeout
        while (!done.Task.IsCompleted || !updates.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (updates.TryDequeue(out var update))
                yield return update;

            if (!done.Task.IsCompleted)
            {
                // Check for inactivity timeout
                if (DateTime.UtcNow - lastActivity > StreamingTimeout)
                {
                    done.TrySetException(new TimeoutException(
                        $"No response from Copilot SDK within {StreamingTimeout.TotalSeconds}s."));
                }

                await Task.WhenAny(done.Task, Task.Delay(50, cancellationToken)).ConfigureAwait(false);
            }
        }

        // Drain remaining
        while (updates.TryDequeue(out var remaining))
            yield return remaining;

        // Propagate errors stored in done.Task
        if (done.Task.IsFaulted)
            await done.Task.ConfigureAwait(false);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(CopilotClient))
            return _client;
        if (serviceType == typeof(CopilotSession))
            return _session;
        return null;
    }

    /// <summary>Resets the session. The next call will create a new session.</summary>
    public async Task ResetSessionAsync()
    {
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is not null) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_session is not null) return;

            _client ??= new CopilotClient(new CopilotClientOptions
            {
                UseLoggedInUser = _config.UseLoggedInUser,
                GitHubToken = _config.GitHubToken,
            });

            await _client.StartAsync(cancellationToken);

            var sessionConfig = new SessionConfig
            {
                Model = _config.Model,
                Streaming = true,
                OnPermissionRequest = PermissionHandler.ApproveAll,
            };

            if (!string.IsNullOrEmpty(_config.SystemMessage))
            {
                sessionConfig.SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = _config.SystemMessage,
                };
            }

            _session = await _client.CreateSessionAsync(sessionConfig);
        }
        finally
        {
            _initLock.Release();
        }
    }
}
