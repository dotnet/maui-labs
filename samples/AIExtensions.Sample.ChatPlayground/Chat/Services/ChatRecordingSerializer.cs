using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Stores chat data using the JSON contracts supplied by Microsoft.Extensions.AI.</summary>
internal static class ChatRecordingSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = AIJsonUtilities.DefaultOptions;

    public static JsonObject Request(IEnumerable<ChatMessage> messages, ChatOptions? options) => new()
    {
        ["messages"] = JsonSerializer.SerializeToNode(messages.ToArray(), JsonOptions),
        ["options"] = JsonSerializer.SerializeToNode(options, JsonOptions),
    };

    public static JsonObject Update(ChatResponseUpdate update) =>
        JsonSerializer.SerializeToNode(update, JsonOptions) as JsonObject
        ?? throw new InvalidDataException("A chat update could not be serialized.");

    public static JsonObject Response(ChatResponse response) =>
        JsonSerializer.SerializeToNode(response, JsonOptions) as JsonObject
        ?? throw new InvalidDataException("A chat response could not be serialized.");

    public static JsonNode? Message(ChatMessage message) =>
        JsonSerializer.SerializeToNode(message, JsonOptions);

    public static ChatResponseUpdate ReadUpdate(JsonObject node) =>
        node.Deserialize<ChatResponseUpdate>(JsonOptions)
        ?? throw new InvalidDataException("A recorded update is empty.");

    public static ChatResponse ReadResponse(JsonObject node) =>
        node.Deserialize<ChatResponse>(JsonOptions)
        ?? throw new InvalidDataException("A recorded response is empty.");

    public static IReadOnlyList<ChatMessage> ReadRequestMessages(JsonObject request) =>
        request["messages"]?.Deserialize<ChatMessage[]>(JsonOptions)
        ?? throw new InvalidDataException("The recorded request has no messages.");

    public static string? ReadInstructions(JsonObject request) =>
        request["options"]?["instructions"]?.GetValue<string>();

    public static ChatOptions? ReadOptions(JsonObject request) =>
        request["options"]?.Deserialize<ChatOptions>(JsonOptions);

    public static bool IsStructuredJson(JsonObject request) =>
        ReadOptions(request)?.ResponseFormat is ChatResponseFormatJson;

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
        ValidatePlayback(recording);
        return recording;
    }

    public static async Task<ChatRecording> DeserializeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var recording = await JsonSerializer.DeserializeAsync<ChatRecording>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The recording file is empty.");
        Validate(recording);
        ValidatePlayback(recording);
        return recording;
    }

    private static void ValidatePlayback(ChatRecording recording)
    {
        foreach (var interaction in recording.Interactions)
        {
            _ = ReadRequestMessages(interaction.Request);
            _ = ReadOptions(interaction.Request);
            if (interaction.IsStreaming)
            {
                foreach (var update in interaction.Updates)
                    _ = ReadUpdate(update);
            }
            else
            {
                _ = ReadResponse(interaction.Response!);
            }
        }
    }

    private static void Validate(ChatRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (recording.Format != ChatRecording.FormatName || recording.SchemaVersion != ChatRecording.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported recording format '{recording.Format}' version {recording.SchemaVersion}. " +
                $"This sample supports {ChatRecording.FormatName} v{ChatRecording.CurrentSchemaVersion}.");
        if (recording.Interactions is null)
            throw new InvalidDataException("The recording has no interactions list.");

        for (var index = 0; index < recording.Interactions.Count; index++)
        {
            var interaction = recording.Interactions[index];
            if (interaction is null || interaction.Request is null || interaction.Updates is null)
                throw new InvalidDataException($"Interaction {index + 1} is incomplete.");
            if (interaction.Sequence != index)
                throw new InvalidDataException($"Interaction {index + 1} has sequence {interaction.Sequence}; expected {index}.");
            if (interaction.IsStreaming == (interaction.Response is not null))
                throw new InvalidDataException($"Interaction {index + 1} does not contain the expected response shape.");
            if (interaction.IsStreaming && interaction.Updates.Any(static update => update is null))
                throw new InvalidDataException($"Interaction {index + 1} has an invalid streaming update.");
        }
    }
}
