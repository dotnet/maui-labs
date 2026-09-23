using System.Globalization;
using System.Text;
using System.Text.Json;
using System.ComponentModel;
using ChatClientPlayground.Models;
using ChatClientPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.ViewModels;

/// <summary>Coordinates real chat requests, protocol history, and response presentation.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ChatClientService _chatClients;
    private readonly PlaygroundTools _tools;
    private readonly ChatRecordingService _recording;
    private readonly List<ChatMessage> _history = [];
    private ChatClientSelection _selectedClient = null!;
    private CancellationTokenSource? _requestCancellation;
    private ChatMessageViewModel? _systemInstructionsMessage;
    private long _requestGeneration;

    /// <summary>Initializes the child view models and request orchestration.</summary>
    public MainViewModel(
        ChatClientService chatClients,
        PlaygroundTools tools,
        ChatRecordingService recording,
        SettingsPaneViewModel settings,
        ChatAreaViewModel chat)
    {
        _chatClients = chatClients;
        _tools = tools;
        _recording = recording;
        Settings = settings;
        Chat = chat;
        Chat.SendCommand = new AsyncRelayCommand(SendAsync, () => Chat.CanSend);
        Chat.CancelCommand = new RelayCommand(Cancel, () => Chat.IsBusy);
        Settings.PropertyChanged += SettingsPropertyChanged;
        Settings.Recording.PropertyChanged += RecordingPropertyChanged;
        Chat.PropertyChanged += ChatPropertyChanged;
        ApplyClientSelection(_chatClients.GetClient(Settings.SelectedClient));
    }

    /// <summary>Gets the settings-pane state.</summary>
    public SettingsPaneViewModel Settings { get; }

    /// <summary>Gets the chat-area state.</summary>
    public ChatAreaViewModel Chat { get; }

    /// <summary>Gets the command that clears visible messages and protocol history.</summary>
    public IRelayCommand ClearConversationCommand => _clearConversationCommand ??= new RelayCommand(ClearConversation);

    private IRelayCommand? _clearConversationCommand;

    private async Task SendAsync()
    {
        var selection = _selectedClient;
        var descriptor = selection.Descriptor;
        var attachment = Chat.SelectedImage;
        var isReplay = _recording.Mode == RecordingMode.Replay;
        if (isReplay && !_recording.HasReplayRemaining)
        {
            Chat.StatusMessage = "Replay reached the end of the recording. Restart replay, load a recording, or switch modes.";
            return;
        }
        if (!isReplay && (!selection.IsAvailable || selection.Client is null))
        {
            Chat.StatusMessage = descriptor.Status;
            return;
        }
        if (string.IsNullOrWhiteSpace(Chat.Prompt) && attachment is null)
        {
            Chat.StatusMessage = "Enter a prompt or attach an image before sending.";
            return;
        }
        if (!isReplay && !descriptor.SupportsImageInput && HistoryContainsImage)
        {
            Chat.StatusMessage = $"This conversation contains image input. {descriptor.DisplayName} cannot replay image messages; clear the conversation or switch to a provider that supports images.";
            return;
        }
        if (!isReplay && attachment is not null && !descriptor.SupportsImageInput)
        {
            Chat.StatusMessage = $"{descriptor.DisplayName} does not support image input. Switch to a provider that supports images or remove the image.";
            return;
        }

        var tools = CreateEnabledTools();
        ChatOptions options;
        try
        {
            options = Settings.CreateChatOptions(tools);
        }
        catch (ArgumentException exception)
        {
            Chat.StatusMessage = exception.Message;
            AddError(exception.Message);
            return;
        }

        var prompt = Chat.Prompt;
        var userMessage = CreateUserMessage(prompt, attachment);
        var userText = string.IsNullOrWhiteSpace(prompt) ? $"Attached {attachment!.FileName}" : prompt.Trim();
        var useStructuredJson = Settings.UseStructuredJson;
        UpdateSystemInstructions(options.Instructions);
        _history.Add(userMessage);
        Chat.Messages.Add(new ChatMessageViewModel
        {
            IsUser = true,
            Text = userText,
            Label = "You",
            ImageSource = attachment is null ? null : CreateImageSource(attachment.Bytes),
        });
        Chat.Prompt = string.Empty;
        Chat.ClearSelectedImage();

        var requestGeneration = ++_requestGeneration;
        var requestCancellation = new CancellationTokenSource();
        _requestCancellation = requestCancellation;
        Chat.IsBusy = true;
        Settings.IsBusy = true;
        Chat.StatusMessage = "Sending request…";

        try
        {
            var client = _chatClients.GetRequestClient(selection);
            if (Settings.UseStreaming)
                await SendStreamingAsync(client, options, useStructuredJson, requestGeneration, requestCancellation.Token);
            else
                await SendResponseAsync(client, options, useStructuredJson, requestGeneration, requestCancellation.Token);

            if (IsCurrentRequest(requestGeneration))
                Chat.StatusMessage = isReplay
                    ? $"Replay complete ({_recording.ReplayPosition}/{_recording.InteractionCount})."
                    : _recording.Mode == RecordingMode.Record
                        ? $"Response complete and recorded ({_recording.InteractionCount} interactions)."
                        : "Response complete.";
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentRequest(requestGeneration))
            {
                Chat.StatusMessage = "Request cancelled.";
                AddError("Request cancelled.");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentRequest(requestGeneration))
            {
                Chat.StatusMessage = $"Request failed: {exception.Message}";
                AddError($"Request failed: {exception.Message}");
            }
        }
        finally
        {
            requestCancellation.Dispose();
            if (ReferenceEquals(_requestCancellation, requestCancellation))
                _requestCancellation = null;
            if (IsCurrentRequest(requestGeneration))
            {
                Chat.IsBusy = false;
                Settings.IsBusy = false;
            }
        }
    }

    private async Task SendResponseAsync(
        IChatClient client,
        ChatOptions options,
        bool useStructuredJson,
        long requestGeneration,
        CancellationToken cancellationToken)
    {
        var response = await client.GetResponseAsync(_history, options, cancellationToken);
        if (!IsCurrentRequest(requestGeneration))
            return;

        _history.AddRange(response.Messages);
        var responseText = RenderResponseMessages(response.Messages, renderText: !useStructuredJson, "Text");
        if (useStructuredJson)
        {
            if (string.IsNullOrWhiteSpace(responseText) && HasToolActivity(response.Messages))
                AddAssistant("(The provider completed tool activity without a final structured response.)", "Structured JSON");
            else
                AddAssistant(FormatStructuredJson(responseText), "Structured JSON");
        }
    }

    private async Task SendStreamingAsync(
        IChatClient client,
        ChatOptions options,
        bool useStructuredJson,
        long requestGeneration,
        CancellationToken cancellationToken)
    {
        ChatMessage? activeTextHistoryMessage = null;
        ChatMessageViewModel? activeTextBubble = null;
        var activeText = new StringBuilder();
        var toolBubbles = new Dictionary<string, ChatMessageViewModel>(StringComparer.Ordinal);
        var toolDetails = new Dictionary<string, string>(StringComparer.Ordinal);
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        var hasToolActivity = false;

        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(_history, options, cancellationToken))
            {
                if (!IsCurrentRequest(requestGeneration))
                    return;

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent functionCall when ShouldProcess(functionCall.CallId, callIds):
                            hasToolActivity = true;
                            _history.Add(new ChatMessage(ChatRole.Assistant, [functionCall]));
                            if (activeTextBubble is not null)
                                activeTextBubble.IsStreaming = false;
                            activeTextHistoryMessage = null;
                            activeTextBubble = null;
                            activeText.Clear();
                            AddToolCall(functionCall, toolBubbles, toolDetails);
                            break;

                        case FunctionResultContent functionResult when ShouldProcess(functionResult.CallId, resultIds):
                            hasToolActivity = true;
                            _history.Add(new ChatMessage(ChatRole.Tool, [functionResult]));
                            if (activeTextBubble is not null)
                                activeTextBubble.IsStreaming = false;
                            activeTextHistoryMessage = null;
                            activeTextBubble = null;
                            activeText.Clear();
                            AddToolResult(functionResult, toolBubbles, toolDetails);
                            break;

                        case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                            if (activeTextHistoryMessage is null)
                            {
                                activeTextHistoryMessage = new ChatMessage(ChatRole.Assistant, [textContent]);
                                _history.Add(activeTextHistoryMessage);
                                activeTextBubble = new ChatMessageViewModel
                                {
                                    Text = "Thinking…",
                                    IsStreaming = true,
                                    Label = useStructuredJson ? "Streaming JSON" : "Streaming text",
                                };
                                Chat.Messages.Add(activeTextBubble);
                            }
                            else
                            {
                                activeTextHistoryMessage.Contents.Add(textContent);
                            }

                            activeText.Append(textContent.Text);
                            var visibleText = activeText.ToString();
                            await MainThread.InvokeOnMainThreadAsync(() => activeTextBubble!.Text = visibleText);
                            break;
                    }
                }
            }

            if (!IsCurrentRequest(requestGeneration))
                return;
            if (activeTextBubble is null || string.IsNullOrWhiteSpace(activeText.ToString()))
            {
                if (!hasToolActivity)
                    throw new InvalidOperationException("The model returned an empty response.");

                AddAssistant(
                    useStructuredJson
                        ? "(The provider completed tool activity without a final structured response.)"
                        : "(The provider completed tool activity without a final text response.)",
                    useStructuredJson ? "Streaming JSON" : "Streaming text");
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (useStructuredJson)
                    activeTextBubble.Text = FormatStructuredJson(activeText.ToString());
                activeTextBubble.IsStreaming = false;
            });
        }
        catch
        {
            if (IsCurrentRequest(requestGeneration))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (activeTextBubble is not null)
                    {
                        activeTextBubble.IsStreaming = false;
                        if (activeText.Length == 0)
                            Chat.Messages.Remove(activeTextBubble);
                    }
                });
            }

            throw;
        }
    }

    private string RenderResponseMessages(
        IEnumerable<ChatMessage> messages,
        bool renderText,
        string label)
    {
        var toolBubbles = new Dictionary<string, ChatMessageViewModel>(StringComparer.Ordinal);
        var toolDetails = new Dictionary<string, string>(StringComparer.Ordinal);
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var resultIds = new HashSet<string>(StringComparer.Ordinal);
        var allText = new StringBuilder();
        var pendingText = new StringBuilder();

        void FlushText()
        {
            if (pendingText.Length == 0)
                return;

            var text = pendingText.ToString();
            allText.Append(text);
            if (renderText)
                AddAssistant(text, label);
            pendingText.Clear();
        }

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent functionCall when ShouldProcess(functionCall.CallId, callIds):
                        FlushText();
                        AddToolCall(functionCall, toolBubbles, toolDetails);
                        break;
                    case FunctionResultContent functionResult when ShouldProcess(functionResult.CallId, resultIds):
                        FlushText();
                        AddToolResult(functionResult, toolBubbles, toolDetails);
                        break;
                    case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                        pendingText.Append(textContent.Text);
                        break;
                }
            }
            FlushText();
        }

        return allText.ToString();
    }

    private void AddToolCall(
        FunctionCallContent functionCall,
        Dictionary<string, ChatMessageViewModel> toolBubbles,
        Dictionary<string, string> toolDetails)
    {
        var callId = functionCall.CallId;
        var arguments = $"Arguments:\n{FormatValue(functionCall.Arguments)}";
        var details = functionCall.Exception is not null
            ? $"{arguments}\n\nCall error:\n{functionCall.Exception.Message}"
            : $"{arguments}\n\nResult:\nWaiting for result…";

        var bubble = new ChatMessageViewModel
        {
            IsTool = true,
            Label = "Tool call",
            Text = string.IsNullOrWhiteSpace(functionCall.Name) ? "Unnamed function" : functionCall.Name,
            DetailText = details,
        };
        Chat.Messages.Add(bubble);
        if (!string.IsNullOrEmpty(callId))
        {
            toolBubbles[callId] = bubble;
            toolDetails[callId] = arguments;
        }
    }

    private void AddToolResult(
        FunctionResultContent functionResult,
        Dictionary<string, ChatMessageViewModel> toolBubbles,
        Dictionary<string, string> toolDetails)
    {
        var callId = functionResult.CallId;
        var result = functionResult.Exception is null
            ? FormatValue(functionResult.Result)
            : functionResult.Exception.Message;
        var resultLabel = functionResult.Exception is null ? "Result" : "Result error";

        if (!string.IsNullOrEmpty(callId) && toolBubbles.TryGetValue(callId, out var bubble))
        {
            bubble.Label = functionResult.Exception is null ? "Tool result" : "Tool error";
            bubble.DetailText = $"{toolDetails[callId]}\n\n{resultLabel}:\n{result}";
            return;
        }

        Chat.Messages.Add(new ChatMessageViewModel
        {
            IsTool = true,
            Label = "Tool result",
            Text = "Result for unknown function",
            DetailText = $"Call ID: {callId ?? "(none)"}\n\n{resultLabel}:\n{result}",
        });
    }

    private List<AITool> CreateEnabledTools()
    {
        var tools = new List<AITool>();
        if (Settings.UseDateTimeTool)
            tools.Add(_tools.CurrentLocalDateTime);
        if (Settings.UseCalculatorTool)
            tools.Add(_tools.Calculator);
        return tools;
    }

    private void UpdateSystemInstructions(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            if (_systemInstructionsMessage is not null)
                Chat.Messages.Remove(_systemInstructionsMessage);
            _systemInstructionsMessage = null;
            return;
        }

        if (_systemInstructionsMessage is null)
        {
            _systemInstructionsMessage = new ChatMessageViewModel
            {
                IsSystem = true,
                Label = "System instructions",
                Text = instructions,
            };
            Chat.Messages.Insert(0, _systemInstructionsMessage);
        }
        else
        {
            _systemInstructionsMessage.Text = instructions;
        }
    }

    private static bool ShouldProcess(string? callId, ISet<string> seenCallIds) =>
        string.IsNullOrEmpty(callId) || seenCallIds.Add(callId);

    private static bool HasToolActivity(IEnumerable<ChatMessage> messages) =>
        messages.SelectMany(message => message.Contents)
            .Any(content => content is FunctionCallContent or FunctionResultContent);

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

    private static ChatMessage CreateUserMessage(string prompt, ImageAttachment? attachment)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(prompt))
            contents.Add(new TextContent(prompt.Trim()));
        if (attachment is not null)
            contents.Add(new DataContent(attachment.Bytes, attachment.MediaType));
        return new ChatMessage(ChatRole.User, contents);
    }

    private static ImageSource CreateImageSource(byte[] bytes) =>
        ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));

    private bool HistoryContainsImage => _history
        .SelectMany(message => message.Contents)
        .OfType<DataContent>()
        .Any();

    private bool CanSend()
    {
        var selection = _selectedClient;
        if (Chat.IsBusy ||
            string.IsNullOrWhiteSpace(Chat.Prompt) && !Chat.HasSelectedImage)
        {
            return false;
        }

        if (_recording.Mode == RecordingMode.Replay)
            return _recording.HasReplayRemaining;

        if (!selection.IsAvailable ||
            (Chat.HasSelectedImage && !selection.Descriptor.SupportsImageInput) ||
            (!selection.Descriptor.SupportsImageInput && HistoryContainsImage))
        {
            return false;
        }

        return Settings.ToolMode != "RequireAny" ||
            Settings.UseDateTimeTool ||
            Settings.UseCalculatorTool;
    }

    private void ApplyClientSelection(ChatClientSelection selection)
    {
        _selectedClient = selection;
        var descriptor = selection.Descriptor;
        var isReplay = _recording.Mode == RecordingMode.Replay;
        Chat.IsImageSupported = isReplay || descriptor.SupportsImageInput;
        Chat.StatusMessage = isReplay
            ? _recording.HasReplayRemaining
                ? $"Replay ready ({_recording.ReplayPosition + 1}/{_recording.InteractionCount}); no provider will be invoked."
                : "Replay is at the end of the recording. Restart replay or load a recording."
            : !descriptor.SupportsImageInput && Chat.HasSelectedImage
            ? $"{descriptor.Status} The pending image cannot be sent by {descriptor.DisplayName}."
            : !descriptor.SupportsImageInput && HistoryContainsImage
                ? $"{descriptor.Status} This conversation contains image input and cannot be replayed by {descriptor.DisplayName}."
                : descriptor.Status;
        UpdateChatCanSend();
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPaneViewModel.SelectedClient))
            ApplyClientSelection(_chatClients.GetClient(Settings.SelectedClient));
        else
            UpdateChatCanSend();
    }

    private void RecordingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecordingViewModel.Mode) or
            nameof(RecordingViewModel.InteractionCount) or
            nameof(RecordingViewModel.ReplayPosition))
        {
            ApplyClientSelection(_chatClients.GetClient(Settings.SelectedClient));
        }
        else
        {
            UpdateChatCanSend();
        }
    }

    private void ChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatAreaViewModel.Prompt) or
            nameof(ChatAreaViewModel.HasSelectedImage) or
            nameof(ChatAreaViewModel.IsBusy))
        {
            UpdateChatCanSend();
        }
    }

    private void UpdateChatCanSend()
    {
        Chat.CanSend = CanSend();
        Chat.RefreshCommands();
    }

    private void Cancel()
    {
        _requestCancellation?.Cancel();
        Chat.StatusMessage = "Cancelling request…";
    }

    private void ClearConversation()
    {
        _requestGeneration++;
        var requestCancellation = _requestCancellation;
        _requestCancellation = null;
        requestCancellation?.Cancel();
        _history.Clear();
        Chat.Messages.Clear();
        _systemInstructionsMessage = null;
        if (_recording.Mode == RecordingMode.Replay)
            _recording.RestartReplay();
        Chat.IsBusy = false;
        Settings.IsBusy = false;
        Chat.StatusMessage = _recording.Mode == RecordingMode.Replay
            ? "Conversation cleared and replay restarted."
            : "Conversation cleared.";
        UpdateChatCanSend();
    }

    private bool IsCurrentRequest(long requestGeneration) => requestGeneration == _requestGeneration;

    private void AddAssistant(string text, string label) =>
        Chat.Messages.Add(new ChatMessageViewModel
        {
            Text = string.IsNullOrWhiteSpace(text) ? "(The model returned no text.)" : text,
            Label = label,
        });

    private void AddError(string text) =>
        Chat.Messages.Add(new ChatMessageViewModel { Text = text, IsError = true, Label = "Error" });
}
