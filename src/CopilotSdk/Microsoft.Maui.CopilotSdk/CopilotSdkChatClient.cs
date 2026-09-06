using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// An <see cref="IChatClient"/> adapter that wraps the GitHub Copilot SDK.
/// Creates a <see cref="CopilotClient"/> and <see cref="CopilotSession"/> on first use,
/// and maps streaming events to the M.E.AI streaming interface.
/// </summary>
/// <remarks>
/// <para><b>Supported ChatOptions:</b></para>
/// <list type="bullet">
///   <item><see cref="ChatOptions.Tools"/> — passed to the SDK session natively</item>
///   <item><see cref="ChatOptions.ModelId"/> — overrides the configured model (resets session)</item>
///   <item><see cref="ChatOptions.ResponseFormat"/> — injects JSON instruction via system message</item>
///   <item><c>AdditionalProperties["ReasoningEffort"]</c> — maps to <see cref="SessionConfig.ReasoningEffort"/></item>
/// </list>
/// <para><b>Not supported (Copilot SDK limitation):</b> Temperature, MaxOutputTokens, StopSequences,
/// FrequencyPenalty, PresencePenalty, TopP, Seed.</para>
/// </remarks>
public sealed class CopilotSdkChatClient : IChatClient, IAsyncDisposable
{
    private readonly CopilotSdkConfiguration _config;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private CopilotClient? _client;
    private CopilotSession? _session;
    private string? _sessionModel;
    private bool _disposed;

    /// <summary>
    /// Inactivity timeout for streaming. If no event is received within this duration,
    /// the stream completes with an error. Default: 5 minutes.
    /// </summary>
    public TimeSpan StreamingTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public CopilotSdkChatClient(CopilotSdkConfiguration config)
    {
        _config = config;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
        var contents = new List<AIContent>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent tc
                    && tc.AdditionalProperties?.ContainsKey("reasoning") == true)
                    continue;

                contents.Add(content);
            }
        }

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)])
        {
            ModelId = _sessionModel,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureSessionAsync(options, cancellationToken).ConfigureAwait(false);

        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var prompt = lastUserMessage?.Text ?? "";

        if (string.IsNullOrEmpty(prompt))
            yield break;

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
                        ModelId = _sessionModel,
                        Contents = [new TextContent(delta.Data.DeltaContent)]
                    });
                    break;

                case AssistantMessageEvent:
                    break;

                case AssistantReasoningDeltaEvent reasoning:
                    channel.Writer.TryWrite(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        ModelId = _sessionModel,
                        Contents = [new TextContent(reasoning.Data.DeltaContent)
                        {
                            AdditionalProperties = new AdditionalPropertiesDictionary
                            {
                                ["reasoning"] = true,
                            }
                        }]
                    });
                    break;

                case AssistantReasoningEvent:
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

        // Build the message options with optional image attachments
        var messageOptions = new MessageOptions { Prompt = prompt };
        if (lastUserMessage is not null)
        {
            var attachments = BuildAttachments(lastUserMessage);
            if (attachments.Count > 0)
                messageOptions.Attachments = attachments;
        }

        await _session!.SendAsync(messageOptions);

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
                _sessionModel = null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Lists available models from the Copilot SDK.
    /// Requires a started client (call after first message or call EnsureSessionAsync).
    /// </summary>
    public async Task<IList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        return await _client!.ListModelsAsync().ConfigureAwait(false);
    }

    private IList<AITool>? _sessionTools;

    /// <summary>
    /// Extracts image content from a ChatMessage and converts to SDK attachments.
    /// </summary>
    private static List<UserMessageAttachment> BuildAttachments(ChatMessage message)
    {
        var attachments = new List<UserMessageAttachment>();
        foreach (var content in message.Contents)
        {
            if (content is DataContent data && data.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            {
                attachments.Add(new UserMessageAttachmentBlob
                {
                    Data = Convert.ToBase64String(data.Data.ToArray()),
                    MimeType = data.MediaType,
                });
            }
            else if (content is UriContent uri && uri.Uri?.Scheme == "data")
            {
                var dataUri = uri.Uri.OriginalString;
                if (dataUri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIdx = dataUri.IndexOf(',');
                    if (commaIdx > 0)
                    {
                        var header = dataUri[..commaIdx];
                        var mimeType = header.Replace("data:", "").Replace(";base64", "");
                        attachments.Add(new UserMessageAttachmentBlob
                        {
                            Data = dataUri[(commaIdx + 1)..],
                            MimeType = mimeType,
                        });
                    }
                }
            }
        }
        return attachments;
    }

    private async Task EnsureClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null) return;

            _client = new CopilotClient(new CopilotClientOptions
            {
                UseLoggedInUser = _config.UseLoggedInUser,
                GitHubToken = _config.GitHubToken,
                CliPath = _config.CliPath,
            });

            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _client = null;
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task EnsureSessionAsync(ChatOptions? options, CancellationToken cancellationToken)
    {
        var tools = options?.Tools;
        var requestedModel = options?.ModelId;

        // Determine if we need to reset the session
        var toolsChanged = _session is not null && !ReferenceEquals(tools, _sessionTools) && tools is { Count: > 0 };
        var modelChanged = _session is not null && !string.IsNullOrEmpty(requestedModel) && requestedModel != _sessionModel;

        if (toolsChanged || modelChanged)
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

            await EnsureClientAsync(cancellationToken).ConfigureAwait(false);

            var model = requestedModel ?? _config.Model;
            var sessionConfig = new SessionConfig
            {
                Model = model,
                Streaming = true,
                OnPermissionRequest = PermissionHandler.ApproveAll,
            };

            if (tools is { Count: > 0 })
                sessionConfig.Tools = [.. tools.OfType<AIFunction>()];

            // Map ReasoningEffort from ChatOptions.AdditionalProperties
            if (options?.AdditionalProperties?.TryGetValue("ReasoningEffort", out var effort) == true
                && effort is string reasoningEffort)
            {
                sessionConfig.ReasoningEffort = reasoningEffort;
            }

            // Build system message: base config + optional JSON format instruction
            var systemParts = new List<string>();
            if (!string.IsNullOrEmpty(_config.SystemMessage))
                systemParts.Add(_config.SystemMessage);

            if (options?.ResponseFormat is ChatResponseFormatJson)
                systemParts.Add("Respond with valid JSON only. Do not include markdown formatting or code fences.");

            if (systemParts.Count > 0)
            {
                sessionConfig.SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = string.Join("\n\n", systemParts),
                };
            }

            _session = await _client!.CreateSessionAsync(sessionConfig);
            _sessionTools = tools;
            _sessionModel = model;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
