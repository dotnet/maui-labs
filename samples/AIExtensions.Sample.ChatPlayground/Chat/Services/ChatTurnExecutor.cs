using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Executes IChatClient turns while retaining the protocol request history.</summary>
internal sealed class ChatTurnExecutor
{
    private readonly List<ChatMessage> _history = [];

    public bool HistoryContainsImage => _history.SelectMany(message => message.Contents)
        .Any(content => content is DataContent ||
            content is ImageGenerationToolResultContent { Outputs: { } outputs } &&
                outputs.OfType<DataContent>().Any());

    public void Clear() => _history.Clear();

    public void AppendUserMessage(ChatMessage message) => _history.Add(message);

    public IReadOnlyList<ChatMessage> RestoreRecordedRequest(JsonObject request, out bool replacedHistory)
    {
        var messages = ChatRecordingSerializer.ReadRequestMessages(request);
        // Recorded requests include full history; append only the suffix not yet replayed.
        var append = _history.Count > 0 &&
            _history.Count <= messages.Count &&
            _history.Select((message, index) =>
                JsonNode.DeepEquals(
                    ChatRecordingSerializer.Message(message),
                    ChatRecordingSerializer.Message(messages[index])))
                .All(matches => matches);
        replacedHistory = !append;
        if (replacedHistory)
            Clear();

        var newMessages = append ? messages.Skip(_history.Count).ToList() : messages;
        _history.AddRange(newMessages);
        return newMessages;
    }

    public async Task ExecuteTurnAsync(
        IChatClient client,
        ChatOptions? options,
        bool streaming,
        Action<ChatResponse> onResponse,
        Action<AIContent> onContent,
        CancellationToken cancellationToken)
    {
        if (!streaming)
        {
            var response = await client.GetResponseAsync(_history, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var message in response.Messages)
                ClearProviderMetadata(message);
            _history.AddRange(response.Messages);
            onResponse(response);
            return;
        }

        // Rebuild protocol history from stream fragments, grouping text until a tool boundary.
        var historyStart = _history.Count;
        ChatMessage? activeText = null;
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var update in client.GetStreamingResponseAsync(_history, options, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call when ShouldProcess(call.CallId, callIds):
                        _history.Add(new ChatMessage(ChatRole.Assistant, [call]));
                        activeText = null;
                        break;
                    case FunctionResultContent result when ShouldProcess(result.CallId, resultIds):
                        _history.Add(new ChatMessage(ChatRole.Tool, [result]));
                        activeText = null;
                        break;
                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        if (activeText is null)
                        {
                            activeText = new ChatMessage(ChatRole.Assistant, [text]);
                            _history.Add(activeText);
                        }
                        else
                        {
                            activeText.Contents.Add(text);
                        }
                        break;
                    case TextReasoningContent reasoning:
                        _history.Add(new ChatMessage(ChatRole.Assistant, [reasoning]));
                        break;
                    case DataContent image when image.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                        _history.Add(new ChatMessage(ChatRole.Assistant, [image]));
                        break;
                    case ImageGenerationToolCallContent imageCall:
                        _history.Add(new ChatMessage(update.Role ?? ChatRole.Assistant, [imageCall]));
                        activeText = null;
                        break;
                    case ImageGenerationToolResultContent imageResult:
                        _history.Add(new ChatMessage(update.Role ?? ChatRole.Tool, [imageResult]));
                        break;
                    default:
                        continue;
                }

                onContent(content);
            }
        }

        // Function invocation marks completed calls after the stream; recorded updates retain the earlier value.
        var completedCallIds = _history.Skip(historyStart)
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = historyStart; index < _history.Count; index++)
        {
            var message = _history[index];
            foreach (var call in message.Contents.OfType<FunctionCallContent>())
            {
                if (completedCallIds.Contains(call.CallId))
                    call.InformationalOnly = true;
            }
            ClearProviderMetadata(message);
        }
    }

    private static void ClearProviderMetadata(ChatMessage message)
    {
        // Image-tool responses can repeat native reasoning items; only portable content belongs in subsequent requests.
        message.RawRepresentation = null;
        foreach (var content in message.Contents)
        {
            content.RawRepresentation = null;
            if (content is ImageGenerationToolResultContent { Outputs: { } outputs })
            {
                foreach (var output in outputs)
                    output.RawRepresentation = null;
            }
        }
    }

    private static bool ShouldProcess(string? callId, ISet<string> seenCallIds) =>
        string.IsNullOrEmpty(callId) || seenCallIds.Add(callId);
}
