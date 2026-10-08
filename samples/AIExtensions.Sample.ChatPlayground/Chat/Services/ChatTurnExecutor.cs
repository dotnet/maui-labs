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
        Action<ChatResponseUpdate> onUpdate,
        CancellationToken cancellationToken)
    {
        if (!streaming)
        {
            var response = await client.GetResponseAsync(_history, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var message in response.Messages)
                ClearProviderMetadata(message);
            _history.AddRange(response.Messages);
            foreach (var update in response.ToChatResponseUpdates())
                onUpdate(update);
            return;
        }

        // Rebuild protocol history from stream fragments, grouping text until a message or tool boundary.
        var historyStart = _history.Count;
        ChatMessage? activeText = null;
        ChatMessage? activeReasoning = null;
        var reasoningByMessageId = new Dictionary<string, ChatMessage>(StringComparer.Ordinal);
        string? messageId = null;
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var update in client.GetStreamingResponseAsync(_history, options, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update.MessageId is { } nextMessageId && messageId != nextMessageId)
            {
                activeText = null;
                activeReasoning = null;
                messageId = nextMessageId;
            }
            List<AIContent> acceptedContents = [];
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call when ShouldProcess(call.CallId, callIds):
                        _history.Add(new ChatMessage(ChatRole.Assistant, [call]));
                        activeText = null;
                        activeReasoning = null;
                        break;
                    case FunctionResultContent result when ShouldProcess(result.CallId, resultIds):
                        _history.Add(new ChatMessage(ChatRole.Tool, [result]));
                        activeText = null;
                        activeReasoning = null;
                        break;
                    case TextContent text when !string.IsNullOrEmpty(text.Text):
                        activeReasoning = null;
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
                        activeText = null;
                        // Final signature-only updates may refer to a reasoning item before the answer.
                        if (string.IsNullOrEmpty(reasoning.Text) && reasoning.ProtectedData is not null &&
                            update.MessageId is { } protectedMessageId &&
                            reasoningByMessageId.TryGetValue(protectedMessageId, out var protectedMessage))
                        {
                            ((TextReasoningContent)protectedMessage.Contents[0]).ProtectedData = reasoning.ProtectedData;
                            break;
                        }
                        if (activeReasoning is null)
                        {
                            activeReasoning = new ChatMessage(ChatRole.Assistant,
                                [new TextReasoningContent(reasoning.Text) { ProtectedData = reasoning.ProtectedData }])
                                { MessageId = messageId };
                            _history.Add(activeReasoning);
                            if (messageId is not null)
                                reasoningByMessageId[messageId] = activeReasoning;
                        }
                        else
                        {
                            var accumulated = (TextReasoningContent)activeReasoning.Contents[0];
                            accumulated.Text += reasoning.Text;
                            if (reasoning.ProtectedData is not null)
                                accumulated.ProtectedData = reasoning.ProtectedData;
                        }
                        // Providers without message identity use protected completion as an item boundary.
                        if (reasoning.ProtectedData is not null && messageId is null)
                            activeReasoning = null;
                        break;
                    case DataContent image when image.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase):
                        _history.Add(new ChatMessage(ChatRole.Assistant, [image]));
                        activeText = null;
                        activeReasoning = null;
                        break;
                    case ImageGenerationToolCallContent imageCall:
                        _history.Add(new ChatMessage(update.Role ?? ChatRole.Assistant, [imageCall]));
                        activeText = null;
                        activeReasoning = null;
                        break;
                    case ImageGenerationToolResultContent imageResult:
                        _history.Add(new ChatMessage(update.Role ?? ChatRole.Tool, [imageResult]));
                        activeText = null;
                        activeReasoning = null;
                        break;
                    default:
                        continue;
                }

                acceptedContents.Add(content);
            }
            var acceptedUpdate = update.Clone();
            acceptedUpdate.Contents = acceptedContents;
            onUpdate(acceptedUpdate);
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
