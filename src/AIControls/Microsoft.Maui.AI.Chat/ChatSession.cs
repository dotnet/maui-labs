using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Headless orchestration engine for a single conversation.
/// </summary>
/// <remarks>
/// The <see cref="Changed"/> event may be raised on a background thread.
/// UI consumers must marshal to their own thread (e.g., via <c>Dispatcher.Dispatch</c>).
/// </remarks>
public sealed class ChatSession : IChatSession, IDisposable
{
    private readonly IChatClient _chatClient;
    private readonly IList<AITool> _tools;
    private readonly List<ChatMessage> _conversationHistory = [];
    private readonly List<ChatEntry> _messages = [];
    private readonly Dictionary<string, string> _toolNamesByCallId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatEntry> _pendingApprovalsById = new(StringComparer.Ordinal);
    private CancellationTokenSource? _activeRequestCancellation;

    public ChatSession(IEnumerable<AITool> tools, IChatClient chatClient)
    {
        _tools = tools?.ToList() ?? throw new ArgumentNullException(nameof(tools));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    public event EventHandler<ChatSessionChangedEventArgs>? Changed;

    public IReadOnlyList<ChatEntry> Messages => _messages;

    public IReadOnlyCollection<ChatEntry> PendingApprovals => _pendingApprovalsById.Values.ToList().AsReadOnly();

    public bool IsBusy { get; private set; }

    public bool HasPendingApprovals => _pendingApprovalsById.Count > 0;

    public bool AllowMultipleToolCalls { get; set; }

    public string? ConversationId { get; private set; }

    public string? SystemPrompt { get; set; }

    public void Clear()
    {
        _activeRequestCancellation?.Cancel();
        _activeRequestCancellation?.Dispose();
        _activeRequestCancellation = null;

        _conversationHistory.Clear();
        _toolNamesByCallId.Clear();
        _pendingApprovalsById.Clear();
        _messages.Clear();
        ConversationId = null;
        SetIsBusy(false);
        OnChanged(ChatSessionChangeKind.Reset);
    }

    public Task CancelAsync()
    {
        _activeRequestCancellation?.Cancel();
        return Task.CompletedTask;
    }

    public async Task SendAsync(string userMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return;

        // Implicitly reject any pending approvals so the middleware sees
        // matched request/response pairs in the conversation history.
        AutoRejectPendingApprovals();

        var trimmedMessage = userMessage.Trim();
        AddEntry(new TextContent(trimmedMessage), ContentRole.User);

        await ContinueConversationAsync(new ChatMessage(ChatRole.User, trimmedMessage), cancellationToken);
    }

    public async Task SubmitApprovalAsync(ToolApprovalResponseContent response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!_pendingApprovalsById.TryGetValue(response.RequestId, out var pendingEntry))
            throw new InvalidOperationException("This approval request is no longer pending.");

        if (pendingEntry.Content is not ToolApprovalRequestContent request)
            throw new InvalidOperationException("Pending approval entries must be backed by a ToolApprovalRequestContent.");

        if (!IsValidApprovalResponse(request, response))
        {
            throw new InvalidOperationException(
                "Edited approval responses must preserve the original tool call identity.");
        }

        ReplaceEntry(
            pendingEntry,
            pendingEntry with
            {
                ApprovalState = response.Approved ? ToolApprovalState.Approved : ToolApprovalState.Rejected,
            });

        _pendingApprovalsById.Remove(response.RequestId);
        OnChanged(ChatSessionChangeKind.StateChanged);

        await ContinueConversationAsync(new ChatMessage(ChatRole.User, [response]), cancellationToken);
    }

    public void Dispose()
    {
        _activeRequestCancellation?.Cancel();
        _activeRequestCancellation?.Dispose();
    }

    /// <summary>
    /// Reject all pending approvals, adding rejection responses to the
    /// conversation history so the middleware sees matched pairs.
    /// </summary>
    private void AutoRejectPendingApprovals()
    {
        if (_pendingApprovalsById.Count == 0)
            return;

        foreach (var (_, pendingEntry) in _pendingApprovalsById)
        {
            if (pendingEntry.Content is not ToolApprovalRequestContent request)
                continue;

            var rejection = request.CreateResponse(approved: false, "Superseded by new user message");
            _conversationHistory.Add(new ChatMessage(ChatRole.User, [rejection]));

            ReplaceEntry(
                pendingEntry,
                pendingEntry with { ApprovalState = ToolApprovalState.Rejected });
        }

        _pendingApprovalsById.Clear();
        OnChanged(ChatSessionChangeKind.StateChanged);
    }

    private async Task ContinueConversationAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        SetIsBusy(true);

        _activeRequestCancellation?.Cancel();
        _activeRequestCancellation?.Dispose();

        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeRequestCancellation = requestCancellation;
        _conversationHistory.Add(message);

        try
        {
            await RunStreamingLoopAsync(requestCancellation.Token);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AddEntry(new ErrorContent(ex.Message), ContentRole.Error);
        }
        finally
        {
            requestCancellation.Dispose();

            if (ReferenceEquals(_activeRequestCancellation, requestCancellation))
                _activeRequestCancellation = null;

            SetIsBusy(false);
        }
    }

    private async Task RunStreamingLoopAsync(CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            Tools = [.. _tools],
            AllowMultipleToolCalls = AllowMultipleToolCalls,
            ConversationId = ConversationId,
        };

        var history = BuildHistory();
        ChatEntry? assistantEntry = null;
        var responseText = string.Empty;
        var responseUpdates = new List<ChatResponseUpdate>();

        // Run on a thread pool thread so the network I/O never touches the
        // caller's synchronization context. Android throws
        // NetworkOnMainThreadException when HTTP calls hit the UI thread.
        await Task.Run(async () =>
        {
            await foreach (var update in _chatClient.GetStreamingResponseAsync(history, options, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                responseUpdates.Add(update.Clone());

                if (!string.IsNullOrWhiteSpace(update.ConversationId) &&
                    !string.Equals(ConversationId, update.ConversationId, StringComparison.Ordinal))
                {
                    ConversationId = update.ConversationId;
                    OnChanged(ChatSessionChangeKind.StateChanged);
                }

                foreach (var content in update.Contents)
                    ProcessResponseContent(content, ref assistantEntry, ref responseText);
            }
        }, cancellationToken);

        if (responseUpdates.Count > 0)
        {
            _conversationHistory.AddMessages(responseUpdates);
            return;
        }

        if (assistantEntry is null && responseText.Length == 0)
        {
            var noResponse = new TextContent("(no response)");
            AddEntry(noResponse, ContentRole.Assistant);
            _conversationHistory.Add(new ChatMessage(ChatRole.Assistant, [noResponse]));
        }
    }

    private void ProcessResponseContent(
        AIContent content,
        ref ChatEntry? assistantEntry,
        ref string responseText)
    {
        switch (content)
        {
            case ToolApprovalRequestContent approval:
            {
                var approvalEntry = AddEntry(approval, ContentRole.Approval, ToolApprovalState.Pending);
                _pendingApprovalsById[approval.RequestId] = approvalEntry;
                OnChanged(ChatSessionChangeKind.StateChanged);
                break;
            }

            case ToolApprovalResponseContent response:
                if (_pendingApprovalsById.TryGetValue(response.RequestId, out var pendingEntry))
                {
                    ReplaceEntry(
                        pendingEntry,
                        pendingEntry with
                        {
                            ApprovalState = response.Approved ? ToolApprovalState.Approved : ToolApprovalState.Rejected,
                        });
                    _pendingApprovalsById.Remove(response.RequestId);
                    OnChanged(ChatSessionChangeKind.StateChanged);
                }
                break;

            case FunctionCallContent call:
                AddEntry(call, ContentRole.Tool);
                break;

            case FunctionResultContent result:
                AddEntry(result, ContentRole.Tool);
                break;

            case TextContent textContent when textContent.Text is not null:
                responseText += textContent.Text;
                if (assistantEntry is null)
                {
                    assistantEntry = AddEntry(new TextContent(responseText), ContentRole.Assistant);
                }
                else
                {
                    assistantEntry = ReplaceEntry(assistantEntry, assistantEntry with
                    {
                        Content = new TextContent(responseText),
                    });
                }
                break;
        }
    }

    private List<ChatMessage> BuildHistory()
    {
        // Collect CallIds of function results already in the history.
        // These indicate tool calls that have been fully executed, so their
        // approval request/response pairs are no longer needed by the middleware.
        var executedCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var msg in _conversationHistory)
            foreach (var content in msg.Contents)
                if (content is FunctionResultContent frc && !string.IsNullOrEmpty(frc.CallId))
                    executedCallIds.Add(frc.CallId);

        var history = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
            history.Add(new ChatMessage(ChatRole.System, SystemPrompt));

        foreach (var msg in _conversationHistory)
        {
            if (executedCallIds.Count == 0)
            {
                history.Add(msg);
                continue;
            }

            List<AIContent>? filtered = null;
            for (int i = 0; i < msg.Contents.Count; i++)
            {
                bool isResolvedApproval = msg.Contents[i] switch
                {
                    ToolApprovalRequestContent { ToolCall: FunctionCallContent fcc }
                        when executedCallIds.Contains(fcc.CallId) => true,
                    ToolApprovalResponseContent { ToolCall: FunctionCallContent fcc }
                        when executedCallIds.Contains(fcc.CallId) => true,
                    _ => false,
                };

                if (isResolvedApproval)
                {
                    filtered ??= msg.Contents.Take(i).ToList();
                }
                else
                {
                    filtered?.Add(msg.Contents[i]);
                }
            }

            if (filtered is null)
            {
                history.Add(msg);
            }
            else if (filtered.Count > 0)
            {
                var clone = msg.Clone();
                clone.Contents = filtered;
                history.Add(clone);
            }
        }

        return history;
    }

    private ChatEntry AddEntry(AIContent content, ContentRole role, ToolApprovalState approvalState = ToolApprovalState.None)
    {
        var entry = new ChatEntry(
            Guid.NewGuid().ToString("n"),
            content,
            role,
            DateTimeOffset.UtcNow,
            ResolveToolName(content),
            approvalState);

        _messages.Add(entry);
        OnChanged(ChatSessionChangeKind.MessageAdded, entry, _messages.Count - 1);
        return entry;
    }

    private ChatEntry ReplaceEntry(ChatEntry previous, ChatEntry updated)
    {
        var index = _messages.FindIndex(message => message.Id == previous.Id);
        if (index < 0)
            return updated;

        _messages[index] = updated;
        OnChanged(ChatSessionChangeKind.MessageUpdated, updated, index);
        return updated;
    }

    private string? ResolveToolName(AIContent content)
    {
        switch (content)
        {
            case FunctionCallContent call:
                if (!string.IsNullOrWhiteSpace(call.CallId))
                    _toolNamesByCallId[call.CallId] = call.Name;
                return call.Name;

            case ToolApprovalRequestContent approval when approval.ToolCall is FunctionCallContent call:
                if (!string.IsNullOrWhiteSpace(call.CallId))
                    _toolNamesByCallId[call.CallId] = call.Name;
                return call.Name;

            case FunctionResultContent result when
                !string.IsNullOrWhiteSpace(result.CallId) &&
                _toolNamesByCallId.TryGetValue(result.CallId, out var toolName):
                return toolName;

            default:
                return null;
        }
    }

    private void SetIsBusy(bool isBusy)
    {
        if (IsBusy == isBusy)
            return;

        IsBusy = isBusy;
        OnChanged(ChatSessionChangeKind.StateChanged);
    }

    private static bool IsValidApprovalResponse(
        ToolApprovalRequestContent request,
        ToolApprovalResponseContent response)
    {
        if (response.ToolCall is null)
            return true;

        if (request.ToolCall is FunctionCallContent originalCall &&
            response.ToolCall is FunctionCallContent editedCall)
        {
            return string.Equals(editedCall.CallId, originalCall.CallId, StringComparison.Ordinal) &&
                   string.Equals(editedCall.Name, originalCall.Name, StringComparison.OrdinalIgnoreCase);
        }

        return request.ToolCall?.GetType() == response.ToolCall.GetType();
    }

    private void OnChanged(ChatSessionChangeKind kind, ChatEntry? entry = null, int? index = null) =>
        Changed?.Invoke(this, new ChatSessionChangedEventArgs(kind, entry, index));
}
