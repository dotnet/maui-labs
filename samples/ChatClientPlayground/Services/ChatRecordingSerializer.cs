using System.Text.Json;
using System.Text.Json.Nodes;
using ChatClientPlayground.Models;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.Services;

/// <summary>Canonical JSON conversion for the intentionally small subset used by this sample.</summary>
internal static class ChatRecordingSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static JsonObject Request(IEnumerable<ChatMessage> messages, ChatOptions? options) =>
        new()
        {
            ["messages"] = new JsonArray(messages.Select(Message).ToArray()),
            ["options"] = Options(options),
        };

    public static JsonObject Update(ChatResponseUpdate update)
    {
        RejectAdditional(update.AdditionalProperties, "ChatResponseUpdate.AdditionalProperties");
        return new JsonObject
        {
            ["authorName"] = update.AuthorName,
            ["role"] = update.Role?.Value,
            ["responseId"] = update.ResponseId,
            ["messageId"] = update.MessageId,
            ["conversationId"] = update.ConversationId,
            ["createdAt"] = update.CreatedAt?.ToString("O"),
            ["finishReason"] = update.FinishReason?.Value,
            ["modelId"] = update.ModelId,
            ["contents"] = new JsonArray(update.Contents.Select(Content).ToArray()),
        };
    }

    public static JsonObject Message(ChatMessage message)
    {
        RejectAdditional(message.AdditionalProperties, "ChatMessage.AdditionalProperties");
        return new JsonObject
        {
            ["role"] = message.Role.Value,
            ["authorName"] = message.AuthorName,
            ["messageId"] = message.MessageId,
            ["createdAt"] = message.CreatedAt?.ToString("O"),
            ["contents"] = new JsonArray(message.Contents.Select(Content).ToArray()),
        };
    }

#pragma warning disable MEAI001 // Recording preserves preview continuation-token metadata when present.
    public static JsonObject Response(ChatResponse response)
    {
        RejectAdditional(response.AdditionalProperties, "ChatResponse.AdditionalProperties");
        return new JsonObject
        {
            ["responseId"] = response.ResponseId,
            ["conversationId"] = response.ConversationId,
            ["modelId"] = response.ModelId,
            ["createdAt"] = response.CreatedAt?.ToString("O"),
            ["finishReason"] = response.FinishReason?.Value,
            ["usage"] = response.Usage is null ? null : JsonSerializer.SerializeToNode(response.Usage, JsonOptions),
            ["continuationToken"] = response.ContinuationToken is null
                ? null
                : Convert.ToBase64String(response.ContinuationToken.ToBytes().Span),
            ["messages"] = new JsonArray(response.Messages.Select(Message).ToArray()),
        };
    }

    public static ChatResponseUpdate ReadUpdate(JsonObject node)
    {
        var result = new ChatResponseUpdate
        {
            AuthorName = String(node, "authorName"),
            ResponseId = String(node, "responseId"),
            MessageId = String(node, "messageId"),
            ConversationId = String(node, "conversationId"),
            ModelId = String(node, "modelId"),
        };
        if (String(node, "role") is { } role)
            result.Role = new ChatRole(role);
        if (String(node, "createdAt") is { } createdAt)
            result.CreatedAt = DateTimeOffset.Parse(createdAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        if (String(node, "finishReason") is { } finishReason)
            result.FinishReason = new ChatFinishReason(finishReason);
        foreach (var content in node["contents"]?.AsArray() ?? [])
            result.Contents.Add(ReadContent(content?.AsObject() ?? throw new InvalidDataException("A response update contained null content.")));
        return result;
    }

    public static ChatMessage ReadMessage(JsonObject node)
    {
        var role = String(node, "role") ?? throw new InvalidDataException("A recorded message has no role.");
        var message = new ChatMessage(new ChatRole(role), [])
        {
            AuthorName = String(node, "authorName"),
            MessageId = String(node, "messageId"),
        };
        if (String(node, "createdAt") is { } createdAt)
            message.CreatedAt = DateTimeOffset.Parse(createdAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        foreach (var content in node["contents"]?.AsArray() ?? [])
            message.Contents.Add(ReadContent(content?.AsObject() ?? throw new InvalidDataException("A recorded message contained null content.")));
        return message;
    }

    public static ChatResponse ReadResponse(JsonObject node)
    {
        var response = new ChatResponse(
            (node["messages"]?.AsArray() ?? [])
                .Select(message => ReadMessage(message?.AsObject() ?? throw new InvalidDataException("A recorded response contained a null message.")))
                .ToList())
        {
            ResponseId = String(node, "responseId"),
            ConversationId = String(node, "conversationId"),
            ModelId = String(node, "modelId"),
            Usage = node["usage"]?.Deserialize<UsageDetails>(JsonOptions),
        };
        if (String(node, "createdAt") is { } createdAt)
            response.CreatedAt = DateTimeOffset.Parse(createdAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        if (String(node, "finishReason") is { } finishReason)
            response.FinishReason = new ChatFinishReason(finishReason);
        if (String(node, "continuationToken") is { } continuationToken)
            response.ContinuationToken = ResponseContinuationToken.FromBytes(Convert.FromBase64String(continuationToken));
        return response;
    }
#pragma warning restore MEAI001

    public static string Serialize(ChatRecording recording)
    {
        Validate(recording);
        return JsonSerializer.Serialize(recording, JsonOptions);
    }

    public static ChatRecording Deserialize(string json)
    {
        var recording = JsonSerializer.Deserialize<ChatRecording>(json, JsonOptions)
            ?? throw new InvalidDataException("The recording file is empty.");
        Validate(recording);
        return recording;
    }

    public static void Validate(ChatRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (recording.Format != ChatRecording.FormatName || recording.SchemaVersion != ChatRecording.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported recording format '{recording.Format}' version {recording.SchemaVersion}. This sample supports {ChatRecording.FormatName} v{ChatRecording.CurrentSchemaVersion}.");

        for (var index = 0; index < recording.Interactions.Count; index++)
        {
            var interaction = recording.Interactions[index];
            if (interaction.Sequence != index)
                throw new InvalidDataException($"Interaction {index + 1} has sequence {interaction.Sequence}; expected {index}.");
            if (interaction.IsStreaming == (interaction.Response is not null))
                throw new InvalidDataException($"Interaction {index + 1} does not contain the expected response shape.");
            if (interaction.IsStreaming && interaction.Updates.Any(static update => update is null))
                throw new InvalidDataException($"Interaction {index + 1} has an invalid streaming update.");
        }
    }

    public static void AssertEqual(int interaction, JsonNode expected, JsonNode actual) =>
        Compare(interaction, "$", expected, actual);

    private static JsonObject Options(ChatOptions? options)
    {
        if (options is null)
            return new JsonObject();

        if (options.RawRepresentationFactory is not null)
            throw new NotSupportedException("Chat playground recording does not support ChatOptions.RawRepresentationFactory.");
        RejectAdditional(options.AdditionalProperties, "ChatOptions.AdditionalProperties");

        return new JsonObject
        {
            ["instructions"] = options.Instructions,
            ["temperature"] = options.Temperature,
            ["maxOutputTokens"] = options.MaxOutputTokens,
            ["topP"] = options.TopP,
            ["topK"] = options.TopK,
            ["frequencyPenalty"] = options.FrequencyPenalty,
            ["presencePenalty"] = options.PresencePenalty,
            ["seed"] = options.Seed,
            ["stopSequences"] = options.StopSequences is null ? null : new JsonArray(options.StopSequences.Select(static value => JsonValue.Create(value)).ToArray()),
            ["allowMultipleToolCalls"] = options.AllowMultipleToolCalls,
            ["toolMode"] = ToolMode(options.ToolMode),
            ["tools"] = new JsonArray((options.Tools ?? []).Select(Tool).ToArray()),
            ["responseFormat"] = ResponseFormat(options.ResponseFormat),
        };
    }

    private static JsonObject Tool(AITool tool)
    {
        if (tool is not AIFunctionDeclaration function)
            throw new NotSupportedException($"Chat playground recording only supports function tools; '{tool.GetType().FullName}' is unsupported.");

        return new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["inputSchema"] = function.JsonSchema is { } schema ? JsonNode.Parse(schema.GetRawText()) : null,
        };
    }

    private static JsonObject? ToolMode(ChatToolMode? mode) => mode switch
    {
        null => null,
        AutoChatToolMode => new JsonObject { ["kind"] = "auto" },
        NoneChatToolMode => new JsonObject { ["kind"] = "none" },
        RequiredChatToolMode required => new JsonObject { ["kind"] = "required", ["function"] = required.RequiredFunctionName },
        _ => throw new NotSupportedException($"Chat playground recording does not support tool mode '{mode.GetType().FullName}'."),
    };

    private static JsonObject? ResponseFormat(ChatResponseFormat? format) => format switch
    {
        null => null,
        ChatResponseFormatText => new JsonObject { ["kind"] = "text" },
        ChatResponseFormatJson json => new JsonObject
        {
            ["kind"] = "json",
            ["schema"] = json.Schema is { } schema ? JsonNode.Parse(schema.GetRawText()) : null,
            ["schemaName"] = json.SchemaName,
            ["schemaDescription"] = json.SchemaDescription,
        },
        _ => throw new NotSupportedException($"Chat playground recording does not support response format '{format.GetType().FullName}'."),
    };

    private static JsonObject Content(AIContent content)
    {
        RejectAdditional(content.AdditionalProperties, $"{content.GetType().Name}.AdditionalProperties");
        if (content.Annotations is { Count: > 0 })
            throw new NotSupportedException($"Chat playground recording does not support annotations on '{content.GetType().Name}'.");

        return content switch
        {
            TextContent text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
            DataContent data => new JsonObject
            {
                ["type"] = "data",
                ["mediaType"] = data.MediaType,
                ["name"] = data.Name,
                ["data"] = Convert.ToBase64String(data.Data.Span),
            },
            FunctionCallContent call when call.Exception is null => new JsonObject
            {
                ["type"] = "functionCall",
                ["callId"] = call.CallId,
                ["name"] = call.Name,
                ["arguments"] = Value(call.Arguments),
                ["informationalOnly"] = call.InformationalOnly,
            },
            FunctionCallContent => throw new NotSupportedException("Chat playground recording does not persist failed function calls."),
            FunctionResultContent result when result.Exception is null => new JsonObject
            {
                ["type"] = "functionResult",
                ["callId"] = result.CallId,
                ["result"] = Value(result.Result),
            },
            FunctionResultContent => throw new NotSupportedException("Chat playground recording does not persist failed function results."),
            ErrorContent error => new JsonObject
            {
                ["type"] = "error",
                ["message"] = error.Message,
                ["errorCode"] = error.ErrorCode,
                ["details"] = error.Details,
            },
            UsageContent usage => new JsonObject
            {
                ["type"] = "usage",
                ["details"] = JsonSerializer.SerializeToNode(usage.Details, JsonOptions),
            },
            _ => throw new NotSupportedException($"Chat playground recording does not support content type '{content.GetType().FullName}'."),
        };
    }

    private static AIContent ReadContent(JsonObject node)
    {
        return String(node, "type") switch
        {
            "text" => new TextContent(String(node, "text")),
            "data" => new DataContent(Convert.FromBase64String(String(node, "data") ?? throw new InvalidDataException("Data content has no base64 data.")), String(node, "mediaType") ?? throw new InvalidDataException("Data content has no media type."))
            {
                Name = String(node, "name"),
            },
            "functionCall" => new FunctionCallContent(
                String(node, "callId") ?? throw new InvalidDataException("Function call has no call ID."),
                String(node, "name") ?? throw new InvalidDataException("Function call has no name."),
                ReadValue(node["arguments"]) as Dictionary<string, object?>)
            {
                InformationalOnly = node["informationalOnly"]?.GetValue<bool>() ?? false,
            },
            "functionResult" => new FunctionResultContent(
                String(node, "callId") ?? throw new InvalidDataException("Function result has no call ID."),
                ReadValue(node["result"])),
            "error" => new ErrorContent(String(node, "message"))
            {
                ErrorCode = String(node, "errorCode"),
                Details = String(node, "details"),
            },
            "usage" => new UsageContent(node["details"]?.Deserialize<UsageDetails>(JsonOptions) ?? new UsageDetails()),
            var type => throw new NotSupportedException($"Recording contains unsupported content type '{type ?? "(missing)"}'."),
        };
    }

    private static JsonNode? Value(object? value) => value switch
    {
        null => null,
        string or bool or byte or short or int or long or float or double or decimal => JsonValue.Create(value),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        JsonNode node => node.DeepClone(),
        IDictionary<string, object?> dictionary => new JsonObject(dictionary
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(static pair => pair.Key, static pair => Value(pair.Value))),
        IEnumerable<object?> values => new JsonArray(values.Select(Value).ToArray()),
        _ => throw new NotSupportedException($"Chat playground recording cannot serialize value type '{value.GetType().FullName}'."),
    };

    private static object? ReadValue(JsonNode? node)
    {
        if (node is null)
            return null;
        if (node is JsonObject obj)
            return obj.ToDictionary(static pair => pair.Key, static pair => ReadValue(pair.Value));
        if (node is JsonArray array)
            return array.Select(ReadValue).ToList();

        var element = node.Deserialize<JsonElement>(JsonOptions);
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Null => null,
            _ => throw new InvalidDataException($"Unsupported JSON value '{element.ValueKind}'."),
        };
    }

    private static void Compare(int interaction, string path, JsonNode? expected, JsonNode? actual)
    {
        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            foreach (var key in expectedObject.Select(static pair => pair.Key)
                .Union(actualObject.Select(static pair => pair.Key), StringComparer.Ordinal)
                .OrderBy(static key => key, StringComparer.Ordinal))
            {
                Compare(interaction, $"{path}.{key}", expectedObject[key], actualObject[key]);
            }
            return;
        }
        if (expected is JsonArray expectedArray && actual is JsonArray actualArray)
        {
            if (expectedArray.Count != actualArray.Count)
                throw new ChatRecordingMismatchException(interaction, $"{path}.length", JsonValue.Create(expectedArray.Count), JsonValue.Create(actualArray.Count));
            for (var index = 0; index < expectedArray.Count; index++)
                Compare(interaction, $"{path}[{index}]", expectedArray[index], actualArray[index]);
            return;
        }
        if (!JsonNode.DeepEquals(expected, actual))
            throw new ChatRecordingMismatchException(interaction, path, expected, actual);
    }

    private static string? String(JsonObject node, string property) => node[property]?.GetValue<string>();

    private static void RejectAdditional(AdditionalPropertiesDictionary? properties, string name)
    {
        if (properties is { Count: > 0 })
            throw new NotSupportedException($"Chat playground recording cannot persist {name}.");
    }
}
