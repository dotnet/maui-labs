// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>
/// Records what a live provider streams into a <c>Baselines/*.recording.json</c> file that
/// <see cref="RecordedChatClient"/> replays: one interaction per request, written once its stream completes, so
/// the file holds exactly the interactions of the run. Put it directly over the provider's client, below
/// <see cref="FunctionInvokingChatClient"/>, so a replay runs function invocation on the provider's own updates.
/// </summary>
/// <remarks>
/// It writes the updates and a summary of each request (the messages' contents and the tool names); never the
/// endpoint, the key, or the provider's raw objects. A content type that <see cref="RecordedChatClient"/> can't read
/// throws, rather than being left out of the recording.
/// </remarks>
internal sealed class RecordingChatClient(IChatClient innerClient, string path) : DelegatingChatClient(innerClient)
{
    private static readonly JsonSerializerOptions s_fileOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonArray _interactions = [];

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var updates = await GetStreamingResponseAsync(messages, options, cancellationToken).ToListAsync(cancellationToken);
        return updates.ToChatResponse();
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = WriteRequest(messages, options);
        var updates = new JsonArray();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            updates.Add(new JsonObject
            {
                ["sequence"] = updates.Count,
                ["value"] = WriteUpdate(update),
                ["delayMs"] = null,
            });
            yield return update;
        }

        _interactions.Add(new JsonObject
        {
            ["sequence"] = _interactions.Count,
            ["name"] = $"interaction-{_interactions.Count}",
            ["request"] = request,
            ["updates"] = updates,
            ["outcome"] = "completed",
            ["errorType"] = null,
            ["errorMessage"] = null,
        });
        Save();
    }

    private void Save()
    {
        var recording = new JsonObject
        {
            ["format"] = "maui-ai-chat-recording",
            ["schemaVersion"] = 1,
            ["metadata"] = new JsonObject
            {
                ["meaiVersion"] = typeof(IChatClient).Assembly.GetName().Version?.ToString(),
                ["pipelineBoundary"] = "provider",
            },
            ["interactions"] = _interactions.DeepClone(),
        };
        File.WriteAllText(path, recording.ToJsonString(s_fileOptions) + "\n");
    }

    private static JsonObject WriteRequest(IEnumerable<ChatMessage> messages, ChatOptions? options) => new()
    {
        ["messages"] = new JsonArray(
        [
            .. messages.Select(message => new JsonObject
            {
                ["role"] = message.Role.Value,
                ["contents"] = new JsonArray([.. message.Contents.Select(WriteContent)]),
            }),
        ]),
        ["tools"] = new JsonArray([.. (options?.Tools ?? []).Select(tool => JsonValue.Create(tool.Name))]),
    };

    private static JsonObject WriteUpdate(ChatResponseUpdate update) => new()
    {
        ["authorName"] = update.AuthorName,
        ["role"] = update.Role?.Value,
        ["responseId"] = update.ResponseId,
        ["messageId"] = update.MessageId,
        ["conversationId"] = update.ConversationId,
        ["createdAt"] = update.CreatedAt?.ToString("O", CultureInfo.InvariantCulture),
        ["finishReason"] = update.FinishReason?.Value,
        ["modelId"] = update.ModelId,
        ["contents"] = new JsonArray([.. update.Contents.Select(WriteContent)]),
    };

    // The shapes RecordedChatClient reads.
    private static JsonObject WriteContent(AIContent content) => content switch
    {
        TextContent text => new() { ["type"] = "text", ["text"] = text.Text },
        FunctionCallContent call => new()
        {
            ["type"] = "functionCall",
            ["callId"] = call.CallId,
            ["name"] = call.Name,
            ["arguments"] = JsonSerializer.SerializeToNode(call.Arguments, AIJsonUtilities.DefaultOptions),
            ["informationalOnly"] = call.InformationalOnly,
        },
        FunctionResultContent result => new()
        {
            ["type"] = "functionResult",
            ["callId"] = result.CallId,
            ["result"] = JsonSerializer.SerializeToNode(result.Result, AIJsonUtilities.DefaultOptions),
        },
        UsageContent usage => new() { ["type"] = "usage", ["details"] = JsonSerializer.SerializeToNode(usage.Details) },
        _ => throw new NotSupportedException(
            $"{nameof(RecordingChatClient)} can't record {content.GetType().Name}. Add it here and to {nameof(RecordedChatClient)}."),
    };
}
