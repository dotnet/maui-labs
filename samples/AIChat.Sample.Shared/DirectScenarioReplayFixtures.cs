using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Recording;

namespace AIChat.Sample.Shared;

/// <summary>Creates deliberately synthetic, semantic schema-v1 recordings for every direct-client demo.</summary>
public static class DirectScenarioReplayFixtures
{
    public static ChatRecording Create(string scenarioId)
    {
        var recording = NewRecording(scenarioId);
        switch (scenarioId)
        {
            case "basic":
                Add(recording, scenarioId, Text("Hello"), Text(" from the direct replay."), Text(" Streaming is complete."));
                break;
            case "weather":
                Add(recording, scenarioId,
                    FunctionCall("get_weather", """{"location":"Seattle"}"""),
                    FunctionResult("get_weather-1", "Sunny and 20°C in Seattle."),
                    Text("It is sunny and 20°C in Seattle."));
                break;
            case "approval":
                Add(recording, scenarioId, ApprovalRequest("meeting-1"));
                Add(recording, $"{scenarioId}-continuation", Text("Your meeting approval decision was recorded."));
                break;
            case "frontend-action":
                Add(recording, scenarioId, FunctionCall("show_weather_card", """{"conditions":"Sunny"}"""));
                Add(recording, $"{scenarioId}-continuation", Text("I displayed the weather card."));
                break;
            case "predictive":
                Add(recording, scenarioId, FunctionCall("propose_document", """{"proposal":{"document":{"content":"Synthetic proposed document."}}}"""));
                Add(recording, $"{scenarioId}-continuation", Text("The document decision has been applied."));
                break;
            case "reasoning":
                Add(recording, scenarioId, Reasoning("I checked the requested details."), Text("Here is the concise answer."));
                break;
            case "attachments":
                Add(recording, scenarioId,
                    Data(
                        "image/png",
                        "preview.png",
                        Convert.FromBase64String(
                            "iVBORw0KGgoAAAANSUhEUgAAAKAAAABaCAYAAAA/xl1SAAAA+klEQVR42u3SsRGAIBBEUQqhDxNrIqZYYxKLOIYCyBxvRl/wG9h9pR4tpKyKEQSgAJQAFICrsw/p8QAUgAIQQAEoAAEUgAIQQAEoAAEUgALQWAJQAEoACkAJQAEoASgAJQAF4LbrDn00AAUggAACKAABBBBAAQgggAAKQAABBFAAOgpAAAWgAARQAApAAAWgAARQAApAAAWgAARQAApAAAWgAARQAApAAAWgAARQAApAAAWgAAQQQAAFIIAAAigAAQQQQAEoASgAJQAFoASgAJQAFIASgAJQAAIoAAUggAJQAAIoAAUggAJQAAKoZIDSWwEoAAWgBKD+1wTl7nPe1CoN2QAAAABJRU5ErkJggg==")),
                    Data("text/plain", "notes.txt", "sample attachment"u8.ToArray()),
                    Text("I received the image and file."));
                break;
            case "restore":
                Add(recording, scenarioId, outcome: "error", errorType: "ReplayTransientException", errorMessage: "Synthetic retryable failure.");
                Add(recording, $"{scenarioId}-retry", Text("The restored retry completed successfully."));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenarioId), scenarioId, "Unknown direct replay scenario.");
        }

        return recording;
    }

    private static ChatRecording NewRecording(string scenarioId)
    {
        var recording = new ChatRecording();
        recording.Metadata["fixture"] = "synthetic-direct-semantic-v1";
        recording.Metadata["scenario"] = scenarioId;
        recording.Metadata["description"] = "Deterministic semantic replay fixture; it does not represent a provider response.";
        return recording;
    }

    private static void Add(
        ChatRecording recording,
        string name,
        params JsonObject[] updates) =>
        Add(recording, name, "completed", null, null, updates);

    private static void Add(
        ChatRecording recording,
        string name,
        string outcome,
        string? errorType,
        string? errorMessage,
        params JsonObject[] updates)
    {
        recording.Interactions.Add(new RecordedChatInteraction
        {
            Sequence = recording.Interactions.Count,
            Name = name,
            Legacy = true,
            Request = new JsonObject { ["fixture"] = "synthetic-direct-semantic-v1" },
            Outcome = outcome,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            Updates = updates.Select((value, sequence) => new RecordedChatUpdate
            {
                Sequence = sequence,
                Value = value,
            }).ToList(),
        });
    }

    private static JsonObject Text(string text) => Update(new JsonObject
    {
        ["type"] = "text",
        ["text"] = text,
    });

    private static JsonObject Reasoning(string text) => Update(new JsonObject
    {
        ["type"] = "reasoning",
        ["text"] = text,
    });

    private static JsonObject Data(string mediaType, string name, byte[] data) => Update(new JsonObject
    {
        ["type"] = "data",
        ["mediaType"] = mediaType,
        ["name"] = name,
        ["data"] = Convert.ToBase64String(data),
    });

    private static JsonObject FunctionCall(string name, string arguments) => Update(new JsonObject
    {
        ["type"] = "functionCall",
        ["callId"] = $"{name}-1",
        ["name"] = name,
        ["arguments"] = JsonNode.Parse(arguments),
        ["informationalOnly"] = false,
    });

    private static JsonObject FunctionResult(string callId, object result) => Update(new JsonObject
    {
        ["type"] = "functionResult",
        ["callId"] = callId,
        ["result"] = JsonSerializer.SerializeToNode(result),
    });

    private static JsonObject ApprovalRequest(string callId) => Update(new JsonObject
    {
        ["type"] = "toolApprovalRequest",
        ["requestId"] = "approval-1",
        ["toolCall"] = new JsonObject
        {
            ["type"] = "functionCall",
            ["callId"] = callId,
            ["name"] = "book_meeting",
            ["arguments"] = new JsonObject { ["title"] = "Synthetic meeting", ["time"] = "tomorrow" },
            ["informationalOnly"] = false,
        },
    });

    private static JsonObject Update(JsonObject content) => new()
    {
        ["role"] = "assistant",
        ["contents"] = new JsonArray(content),
    };
}
