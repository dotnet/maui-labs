// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Wraps a Microsoft.Extensions.AI <see cref="IChatClient"/>: sends messages, streams the response, owns the
/// chat history, and runs each update through the <see cref="BlockMappingPipeline"/> to yield
/// <see cref="ContentBlock"/>s.
/// </summary>
/// <remarks>
/// Configured with <see cref="UIAgentOptions"/> (instructions, tools, custom handlers). The stateful,
/// UI-facing wrapper on top of it is <see cref="AgentContext"/>.
/// </remarks>
public class UIAgent : IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly UIAgentOptions _options;
    private readonly ILogger _logger;
    private readonly List<ChatMessage> _history = new();
    private bool _disposed;

    internal UIAgentOptions Options => _options;

    public UIAgent(IChatClient chatClient)
        : this(chatClient, configure: null)
    {
    }

    public UIAgent(IChatClient chatClient, ChatOptions chatOptions)
        : this(chatClient, options => options.ChatOptions = chatOptions)
    {
    }

    public UIAgent(IChatClient chatClient, ChatOptions chatOptions, ILoggerFactory? loggerFactory)
        : this(chatClient, options => options.ChatOptions = chatOptions, loggerFactory)
    {
    }

    public UIAgent(IChatClient chatClient, Action<UIAgentOptions>? configure)
        : this(chatClient, configure, loggerFactory: null)
    {
    }

    public UIAgent(IChatClient chatClient, Action<UIAgentOptions>? configure, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
        _options = new UIAgentOptions();
        configure?.Invoke(_options);
        _logger = (ILogger?)loggerFactory?.CreateLogger<BlockMappingPipeline>() ?? NullLogger.Instance;
    }

    /// <summary>Clears the accumulated chat history so the next message starts a fresh conversation.</summary>
    public void ClearHistory()
    {
        _history.Clear();
    }

    internal int CaptureHistoryCheckpoint() => _history.Count;

    internal void RollbackHistory(int checkpoint, ChatMessage requestMessage)
    {
        if (checkpoint < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint));

        if (checkpoint > _history.Count)
            return;

        var requestWasAdded = _history.Count > checkpoint;
        _history.RemoveRange(checkpoint, _history.Count - checkpoint);
        if (requestWasAdded)
            _history.Add(requestMessage);
    }

    public async IAsyncEnumerable<ContentBlock> SendMessageAsync(
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);

        cancellationToken.ThrowIfCancellationRequested();
        _history.Add(message);
        ChatMessage[] historySnapshot = [.. _history];
        var pipeline = new BlockMappingPipeline(_options, _logger);

        // Process user message through pipeline
        var userUpdate = new ChatResponseUpdate
        {
            Role = message.Role,
            Contents = [.. message.Contents]
        };
        await foreach (var block in pipeline.Process(userUpdate, cancellationToken).ConfigureAwait(false))
        {
            yield return block;
        }
        foreach (var block in pipeline.Finalize())
        {
            yield return block;
        }

        // Stream assistant response
        UIAgentLog.StreamingAssistantResponse(_logger);
        var assistantUpdates = new List<ChatResponseUpdate>();
        string? turnId = null;
        var chatOptions = _options.ChatOptions;

        var updateIndex = 0;
        await foreach (var update in _chatClient.GetStreamingResponseAsync(historySnapshot, chatOptions, cancellationToken).ConfigureAwait(false))
        {
            var contentTypes = string.Join(", ", update.Contents.Select(c => c.GetType().Name));
            UIAgentLog.ReceivedUpdate(_logger, updateIndex++, update.Role?.Value, contentTypes);

            assistantUpdates.Add(update);
            turnId ??= update.ResponseId;

            await foreach (var block in pipeline.Process(update, cancellationToken).ConfigureAwait(false))
            {
                yield return block;
            }
        }

        UIAgentLog.StreamComplete(_logger, assistantUpdates.Count);

        foreach (var block in pipeline.Finalize())
        {
            yield return block;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Add assistant response to history
        var response = assistantUpdates.ToChatResponse();
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var msg in response.Messages)
            _history.Add(msg);

        UIAgentLog.AddedToHistory(_logger, response.Messages.Count);
    }

    internal async Task<FunctionResultContent> InvokeToolAsync(
        FunctionCallContent call, CancellationToken cancellationToken)
    {
        var function = FindBackendFunction(call.Name);
        if (function is null)
        {
            UIAgentLog.BackendFunctionNotFound(_logger, call.Name);
            return new FunctionResultContent(call.CallId, $"Error: Function '{call.Name}' not found.");
        }

        UIAgentLog.InvokingBackendFunction(_logger, call.Name, call.CallId);
        var args = call.Arguments is not null ? new AIFunctionArguments(call.Arguments) : null;
        var result = await function.InvokeAsync(args, cancellationToken);
        return new FunctionResultContent(call.CallId, result);
    }

    private AIFunction? FindBackendFunction(string name)
    {
        if (_options.ChatOptions?.Tools is null)
        {
            return null;
        }

        foreach (var tool in _options.ChatOptions.Tools)
        {
            if (tool is AIFunction function && function.Name == name)
            {
                return function;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
