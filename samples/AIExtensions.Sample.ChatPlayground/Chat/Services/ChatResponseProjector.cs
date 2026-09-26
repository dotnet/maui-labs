using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Projects one response into transcript changes, retaining streaming and tool state.</summary>
internal sealed class ChatResponseProjector(TranscriptEmitter transcript, Action<TranscriptChange> emit, bool streaming, bool structuredJson)
{
    private long? _activeTextEntryId;
    private readonly StringBuilder _activeText = new();
    private readonly StringBuilder _pendingText = new();
    private readonly StringBuilder _allText = new();
    private readonly StringBuilder _reasoningText = new();
    private long? _reasoningEntryId;
    private readonly Dictionary<string, (long EntryId, string Arguments)> _toolEntriesByCallId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenCallIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenResultIds = new(StringComparer.Ordinal);
    private bool _hasToolActivity;
    private bool _hasImage;

    public void ProjectMessage(ChatMessage message)
    {
        foreach (var content in message.Contents)
            ProjectContent(content);
        FlushText();
    }

    public void ProjectContent(AIContent content)
    {
        // Tool results can resolve call bubbles; text, reasoning, and images update visible entries.
        switch (content)
        {
            case FunctionCallContent call when streaming || ShouldProcess(call.CallId, _seenCallIds):
                FlushText();
                _hasToolActivity = true;
                var arguments = $"Arguments:\n{FormatValue(call.Arguments)}";
                var details = call.Exception is not null
                    ? $"{arguments}\n\nCall error:\n{call.Exception.Message}"
                    : $"{arguments}\n\nResult:\nWaiting for result…";
                var callEntryId = transcript.NextEntryId();
                emit(new TranscriptChange.EntryAdded(callEntryId, TranscriptEntryKind.Tool, "Tool call",
                    string.IsNullOrWhiteSpace(call.Name) ? "Unnamed function" : call.Name, details));
                if (!string.IsNullOrEmpty(call.CallId))
                    _toolEntriesByCallId[call.CallId] = (callEntryId, arguments);
                break;

            case FunctionResultContent result when streaming || ShouldProcess(result.CallId, _seenResultIds):
                FlushText();
                _hasToolActivity = true;
                var resultText = result.Exception is null ? FormatValue(result.Result) : result.Exception.Message;
                var resultLabel = result.Exception is null ? "Result" : "Result error";
                if (!string.IsNullOrEmpty(result.CallId) &&
                    _toolEntriesByCallId.TryGetValue(result.CallId, out var priorCall))
                {
                    emit(new TranscriptChange.ToolCallResolved(priorCall.EntryId,
                        result.Exception is null ? "Tool result" : "Tool error",
                        $"{priorCall.Arguments}\n\n{resultLabel}:\n{resultText}"));
                }
                else
                {
                    emit(new TranscriptChange.EntryAdded(
                        transcript.NextEntryId(), TranscriptEntryKind.Tool, "Tool result",
                        "Result for unknown function",
                        $"Call ID: {result.CallId ?? "(none)"}\n\n{resultLabel}:\n{resultText}"));
                }
                break;

            case TextContent text when !string.IsNullOrEmpty(text.Text):
                if (streaming)
                {
                    if (_activeTextEntryId is null)
                    {
                        _activeTextEntryId = transcript.NextEntryId();
                        emit(new TranscriptChange.EntryAdded(_activeTextEntryId.Value, TranscriptEntryKind.Assistant,
                            structuredJson ? "Streaming JSON" : "Streaming text", "Thinking…", IsStreaming: true));
                    }
                    _activeText.Append(text.Text);
                    emit(new TranscriptChange.EntryTextChanged(_activeTextEntryId.Value, _activeText.ToString()));
                }
                else
                {
                    _pendingText.Append(text.Text);
                }
                break;

            case TextReasoningContent reasoning when !string.IsNullOrWhiteSpace(reasoning.Text):
                if (!streaming)
                    FlushText();
                if (streaming)
                {
                    _reasoningText.Append(reasoning.Text);
                    if (_reasoningEntryId is null)
                    {
                        _reasoningEntryId = transcript.NextEntryId();
                        emit(new TranscriptChange.EntryAdded(_reasoningEntryId.Value, TranscriptEntryKind.Reasoning,
                            "Reasoning summary", _reasoningText.ToString()));
                    }
                    else
                    {
                        emit(new TranscriptChange.EntryTextChanged(_reasoningEntryId.Value, _reasoningText.ToString()));
                    }
                }
                else
                {
                    emit(new TranscriptChange.EntryAdded(transcript.NextEntryId(), TranscriptEntryKind.Reasoning,
                        "Reasoning summary", reasoning.Text));
                }
                break;

            case DataContent image when image.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                if (!streaming)
                    FlushText();
                AddImage(image);
                break;

            case ImageGenerationToolCallContent:
                FlushText();
                _hasToolActivity = true;
                break;

            case ImageGenerationToolResultContent imageResult:
                if (!streaming)
                    FlushText();
                _hasToolActivity = true;
                foreach (var image in imageResult.Outputs?.OfType<DataContent>() ?? [])
                    AddImage(image);
                break;
        }
    }

    public void Complete()
    {
        if (!streaming)
        {
            if (structuredJson)
            {
                var text = _allText.ToString();
                emit(new TranscriptChange.EntryAdded(transcript.NextEntryId(), TranscriptEntryKind.Assistant,
                    "Structured JSON", string.IsNullOrWhiteSpace(text) && _hasToolActivity
                        ? "(The provider completed tool activity without a final structured response.)"
                        : FormatStructuredJson(text)));
            }
            return;
        }

        if (_activeTextEntryId is null || string.IsNullOrWhiteSpace(_activeText.ToString()))
        {
            if (!_hasToolActivity && !_hasImage && _reasoningEntryId is null)
                throw new InvalidOperationException("The model returned an empty response.");
            if (_hasToolActivity && !_hasImage && _reasoningEntryId is null)
                emit(new TranscriptChange.EntryAdded(transcript.NextEntryId(), TranscriptEntryKind.Assistant,
                    structuredJson ? "Streaming JSON" : "Streaming text",
                    structuredJson
                        ? "(The provider completed tool activity without a final structured response.)"
                        : "(The provider completed tool activity without a final text response.)"));
            return;
        }

        if (structuredJson)
            emit(new TranscriptChange.EntryTextChanged(
                _activeTextEntryId.Value, FormatStructuredJson(_activeText.ToString())));
        emit(new TranscriptChange.EntryStreamingStopped(_activeTextEntryId.Value));
    }

    public void Abort()
    {
        if (_activeTextEntryId is { } entryId)
        {
            emit(new TranscriptChange.EntryStreamingStopped(entryId));
            if (_activeText.Length == 0)
                emit(new TranscriptChange.EntryRemoved(entryId));
        }
    }

    private void FlushText()
    {
        if (streaming)
        {
            if (_activeTextEntryId is { } entryId)
                emit(new TranscriptChange.EntryStreamingStopped(entryId));
            _activeTextEntryId = null;
            _activeText.Clear();
        }
        else if (_pendingText.Length > 0)
        {
            var text = _pendingText.ToString();
            // Tool boundaries flush visible text, but structured JSON still needs the complete response.
            _allText.Append(text);
            if (!structuredJson)
                emit(new TranscriptChange.EntryAdded(
                    transcript.NextEntryId(), TranscriptEntryKind.Assistant, "Text", text));
            _pendingText.Clear();
        }
    }

    private void AddImage(DataContent image)
    {
        emit(new TranscriptChange.EntryAdded(transcript.NextEntryId(), TranscriptEntryKind.Image,
            "Generated image", string.Empty, ImageBytes: image.Data.ToArray()));
        _hasImage = true;
    }

    private static bool ShouldProcess(string? callId, ISet<string> seenCallIds) =>
        string.IsNullOrEmpty(callId) || seenCallIds.Add(callId);

    private static string FormatStructuredJson(string json)
    {
        var response = JsonSerializer.Deserialize(json, PlaygroundJsonContext.Default.PlaygroundResponse)
            ?? throw new JsonException("The schema response was empty or could not be deserialized.");
        return JsonSerializer.Serialize(response, PlaygroundJsonContext.Default.PlaygroundResponse);
    }

    private static string FormatValue(object? value) =>
        value switch
        {
            null => "(none)",
            JsonElement element => element.GetRawText(),
            IEnumerable<KeyValuePair<string, object?>> values =>
                string.Join(Environment.NewLine, values.Select(pair => $"{pair.Key}: {FormatValue(pair.Value)}")),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(none)",
        };
}
