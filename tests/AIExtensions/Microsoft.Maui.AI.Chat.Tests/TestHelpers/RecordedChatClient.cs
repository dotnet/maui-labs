// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.TestHelpers;

/// <summary>
/// Replays a provider stream captured in <c>Baselines/*.recording.json</c>. Each request receives the
/// next recorded interaction's updates in order; requests are not matched against the recording.
/// </summary>
internal sealed class RecordedChatClient : IChatClient
{
    private readonly IReadOnlyList<JsonObject> _interactions;
    private int _replayed;

    private RecordedChatClient(IReadOnlyList<JsonObject> interactions) => _interactions = interactions;

    public int InteractionCount => _interactions.Count;

    public static RecordedChatClient Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Baselines", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Recording baseline not found: {path}. Ensure it is copied to the output directory.",
                path);
        }

        return LoadFile(path);
    }

    /// <summary>Replays the recording at <paramref name="path"/>, such as one a <see cref="RecordingChatClient"/> wrote.</summary>
    public static RecordedChatClient LoadFile(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return new RecordedChatClient(
            root["interactions"]!.AsArray().Select(node => node!.AsObject()).ToList());
    }

    public void AssertFullyReplayed() => Assert.Equal(_interactions.Count, _replayed);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var updates = await GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToListAsync(cancellationToken);
        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (_replayed >= _interactions.Count)
            throw new InvalidOperationException($"Recording ended before interaction {_replayed}.");

        var interaction = _interactions[_replayed++];
        foreach (var recorded in interaction["updates"]!.AsArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ReadUpdate(recorded!["value"]!.AsObject());
        }

        if (interaction["outcome"]?.GetValue<string>() == "error")
        {
            throw new InvalidOperationException(
                interaction["errorMessage"]?.GetValue<string>() ?? "The recorded interaction failed.");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IChatClient) && serviceKey is null ? this : null;

    public void Dispose()
    {
    }

    private static ChatResponseUpdate ReadUpdate(JsonObject node)
    {
        var update = new ChatResponseUpdate
        {
            AuthorName = node["authorName"]?.GetValue<string>(),
            ResponseId = node["responseId"]?.GetValue<string>(),
            MessageId = node["messageId"]?.GetValue<string>(),
            ConversationId = node["conversationId"]?.GetValue<string>(),
            ModelId = node["modelId"]?.GetValue<string>(),
        };
        if (node["role"]?.GetValue<string>() is { } role)
            update.Role = new ChatRole(role);
        if (node["createdAt"]?.GetValue<string>() is { } createdAt)
            update.CreatedAt = DateTimeOffset.Parse(createdAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (node["finishReason"]?.GetValue<string>() is { } finishReason)
            update.FinishReason = new ChatFinishReason(finishReason);
        if (node["contents"] is JsonArray contents)
        {
            foreach (var content in contents)
                update.Contents.Add(ReadContent(content!.AsObject()));
        }

        return update;
    }

    private static AIContent ReadContent(JsonObject node) => node["type"]?.GetValue<string>() switch
    {
        "text" => new TextContent(node["text"]?.GetValue<string>()),
        "functionCall" => new FunctionCallContent(
            node["callId"]!.GetValue<string>(),
            node["name"]!.GetValue<string>(),
            ReadValue(node["arguments"]) as Dictionary<string, object?>)
        {
            InformationalOnly = node["informationalOnly"]?.GetValue<bool>() ?? false,
        },
        "functionResult" => new FunctionResultContent(node["callId"]!.GetValue<string>(), ReadValue(node["result"])),
        "toolApprovalRequest" => new ToolApprovalRequestContent(
            node["requestId"]!.GetValue<string>(),
            (ToolCallContent)ReadContent(node["toolCall"]!.AsObject())),
        "usage" => new UsageContent(node["details"]?.Deserialize<UsageDetails>() ?? new UsageDetails()),
        var type => throw new InvalidDataException($"Unsupported recorded content type '{type}'."),
    };

    private static object? ReadValue(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                return obj.ToDictionary(pair => pair.Key, pair => ReadValue(pair.Value));
            case JsonArray array:
                return array.Select(ReadValue).ToList();
        }

        var element = node.Deserialize<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            _ => throw new InvalidDataException($"Unsupported JSON value kind '{element.ValueKind}'."),
        };
    }
}
