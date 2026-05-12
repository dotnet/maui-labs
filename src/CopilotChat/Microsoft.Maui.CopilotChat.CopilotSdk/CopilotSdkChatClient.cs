using System.Runtime.CompilerServices;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotChat;

/// <summary>
/// An <see cref="IChatClient"/> adapter that wraps the GitHub Copilot SDK.
/// Creates a <see cref="CopilotClient"/> and <see cref="CopilotSession"/> on first use,
/// and maps streaming events to the M.E.AI streaming interface.
/// </summary>
public sealed class CopilotSdkChatClient : IChatClient
{
    private readonly CopilotChatConfiguration _config;
    private CopilotClient? _client;
    private CopilotSession? _session;
    private bool _disposed;

    public CopilotSdkChatClient(CopilotChatConfiguration config)
    {
        _config = config;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Collect streaming into a single response
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
        await EnsureSessionAsync(cancellationToken);

        // Build prompt from the last user message
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var prompt = lastUserMessage?.Text ?? "";

        if (string.IsNullOrEmpty(prompt))
            yield break;

        var done = new TaskCompletionSource<bool>();
        var updates = new System.Collections.Concurrent.ConcurrentQueue<ChatResponseUpdate>();

        using var sub = _session!.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageDeltaEvent delta:
                    updates.Enqueue(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(delta.Data.DeltaContent)]
                    });
                    break;

                case AssistantMessageEvent msg:
                    updates.Enqueue(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(msg.Data.Content)]
                    });
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

        // Yield updates as they arrive
        while (!done.Task.IsCompleted || !updates.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (updates.TryDequeue(out var update))
                yield return update;

            if (!done.Task.IsCompleted)
                await Task.WhenAny(done.Task, Task.Delay(50, cancellationToken));
        }

        // Drain remaining
        while (updates.TryDequeue(out var remaining))
            yield return remaining;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(CopilotClient))
            return _client;
        if (serviceType == typeof(CopilotSession))
            return _session;
        return null;
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
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
}
