using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
        await EnsureSessionAsync(options?.Tools, cancellationToken).ConfigureAwait(false);

        // Build prompt from the last user message (Copilot SDK manages conversation state server-side)
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var prompt = lastUserMessage?.Text ?? "";

        if (string.IsNullOrEmpty(prompt))
            yield break;

        // Use Channel for immediate wake-on-write — each SDK event wakes the consumer
        // immediately instead of batching behind a polling delay.
        var channel = Channel.CreateUnbounded<ChatResponseUpdate>(
            new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true,
            });
        var lastActivity = DateTime.UtcNow;
        Exception? streamError = null;

        using var sub = _session!.On(evt =>
        {
            lastActivity = DateTime.UtcNow;
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    channel.Writer.TryWrite(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(delta.Data.DeltaContent)]
                    });
                    break;

                // Skip AssistantMessageEvent to avoid duplicating content already sent as deltas
                case AssistantMessageEvent:
                    break;

                case ToolExecutionStartEvent tool:
                    channel.Writer.TryWrite(new ChatResponseUpdate
                    {
                        Contents = [new FunctionCallContent(tool.Data.ToolCallId ?? "", tool.Data.ToolName ?? "")]
                    });
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    channel.Writer.TryWrite(new ChatResponseUpdate
                    {
                        Contents = [new FunctionResultContent(
                            toolComplete.Data.ToolCallId ?? "",
                            toolComplete.Data.Result)]
                    });
                    break;

                case SessionIdleEvent:
                    channel.Writer.TryComplete();
                    break;

                case SessionErrorEvent err:
                    streamError = new InvalidOperationException(err.Data.Message);
                    channel.Writer.TryComplete(streamError);
                    break;
            }
        });

        // Start inactivity timeout monitor
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(async () =>
        {
            while (!timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), timeoutCts.Token).ConfigureAwait(false);
                if (DateTime.UtcNow - lastActivity > StreamingTimeout)
                {
                    streamError = new TimeoutException(
                        $"No response from Copilot SDK within {StreamingTimeout.TotalSeconds}s.");
                    channel.Writer.TryComplete(streamError);
                    break;
                }
            }
        }, timeoutCts.Token);

        await _session!.SendAsync(new MessageOptions { Prompt = prompt });

        // Yield each update as soon as it arrives — no batching
        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        timeoutCts.Cancel();

        if (streamError is not null)
            throw streamError;
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
                _sessionTools = null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private IList<AITool>? _sessionTools;

    private async Task EnsureSessionAsync(IList<AITool>? tools, CancellationToken cancellationToken)
    {
        // If tools changed, reset session to pick up new tools
        var toolsChanged = _session is not null && !ReferenceEquals(tools, _sessionTools) && tools is { Count: > 0 };
        if (toolsChanged)
        {
            await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
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

        if (_session is not null) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null) return;

            _client ??= new CopilotClient(new CopilotClientOptions
            {
                UseLoggedInUser = _config.UseLoggedInUser,
                GitHubToken = _config.GitHubToken,
                CliPath = _config.CliPath,
            });

            await _client.StartAsync(cancellationToken);

            var sessionConfig = new SessionConfig
            {
                Model = _config.Model,
                Streaming = true,
                OnPermissionRequest = PermissionHandler.ApproveAll,
            };

            // Pass tools to the SDK session — the SDK handles tool invocation natively
            if (tools is { Count: > 0 })
                sessionConfig.Tools = [.. tools.OfType<AIFunction>()];

            if (!string.IsNullOrEmpty(_config.SystemMessage))
            {
                sessionConfig.SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = _config.SystemMessage,
                };
            }

            _session = await _client.CreateSessionAsync(sessionConfig);
            _sessionTools = tools;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
