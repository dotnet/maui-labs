using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.CopilotSdk;

namespace CopilotSdkSample;

public sealed class ChatViewModel : INotifyPropertyChanged
{
    private readonly CopilotSdkChatClient _chatClient;
    private readonly IList<AITool> _tools;
    private bool _isBusy;
    private string _selectedModel = "gpt-4.1";
    private bool _requestJson;

    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    public List<string> AvailableModels { get; } =
    [
        "gpt-4.1",
        "gpt-4o",
        "claude-sonnet-4.5",
        "o4-mini",
    ];

    public string SelectedModel
    {
        get => _selectedModel;
        set { _selectedModel = value; OnPropertyChanged(); }
    }

    public bool RequestJson
    {
        get => _requestJson;
        set { _requestJson = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
    }

    public bool IsNotBusy => !_isBusy;

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public ChatViewModel(IChatClient chatClient, IList<AITool> tools)
    {
        _chatClient = (CopilotSdkChatClient)chatClient;
        _tools = tools;
    }

    public async Task SendMessageAsync(string text)
    {
        if (IsBusy) return;

        Messages.Add(ChatMessageItem.User(text));
        IsBusy = true;
        StatusText = "Connecting to Copilot...";

        try
        {
            var options = new ChatOptions
            {
                Tools = _tools,
                ModelId = SelectedModel,
            };

            if (RequestJson)
                options.ResponseFormat = ChatResponseFormat.Json;

            var assistantItem = ChatMessageItem.Assistant("");
            Messages.Add(assistantItem);

            // Run SDK calls on thread pool to avoid main-thread SynchronizationContext
            // deadlocks — the SDK's StartAsync may synchronously block in Mac Catalyst.
            await Task.Run(async () =>
            {
                await foreach (var update in _chatClient.GetStreamingResponseAsync(
                    [new ChatMessage(ChatRole.User, text)], options))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        StatusText = "Streaming...";
                        foreach (var content in update.Contents)
                        {
                            switch (content)
                            {
                                case TextContent tc when tc.AdditionalProperties?.ContainsKey("reasoning") == true:
                                    var reasoningItem = GetOrAddReasoningBubble();
                                    reasoningItem.Text += tc.Text;
                                    break;

                                case TextContent tc:
                                    assistantItem.Text += tc.Text;
                                    break;

                                case FunctionCallContent fc:
                                    Messages.Add(ChatMessageItem.ToolCall($"🔧 {fc.Name}"));
                                    break;

                                case FunctionResultContent fr:
                                    var result = fr.Result?.ToString() ?? "";
                                    if (result.Length > 100) result = result[..100] + "…";
                                    Messages.Add(ChatMessageItem.ToolResult($"✅ {result}"));
                                    break;
                            }
                        }
                    });
                }
            });

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(assistantItem.Text))
                    assistantItem.Text = "(no text response)";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                Messages.Add(ChatMessageItem.Error($"⚠️ {ex.Message}")));
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => { IsBusy = false; StatusText = ""; });
        }
    }

    public async Task ResetSessionAsync()
    {
        await _chatClient.ResetSessionAsync();
        Messages.Clear();
        Messages.Add(ChatMessageItem.System("Session reset."));
    }

    private ChatMessageItem GetOrAddReasoningBubble()
    {
        // Reuse the last reasoning bubble if it exists
        if (Messages.Count > 0 && Messages[^1].Kind == MessageKind.Reasoning)
            return Messages[^1];

        var item = ChatMessageItem.Reasoning("");
        Messages.Add(item);
        return item;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum MessageKind { User, Assistant, ToolCall, ToolResult, Reasoning, System, Error }

public sealed class ChatMessageItem : INotifyPropertyChanged
{
    private string _text = "";

    public MessageKind Kind { get; init; }

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    public Color BackgroundColor => Kind switch
    {
        MessageKind.User => Color.FromArgb("#512BD4"),
        MessageKind.Assistant => Color.FromArgb("#E0E0E0"),
        MessageKind.ToolCall => Color.FromArgb("#FFF3CD"),
        MessageKind.ToolResult => Color.FromArgb("#D4EDDA"),
        MessageKind.Reasoning => Color.FromArgb("#E8DAEF"),
        MessageKind.System => Color.FromArgb("#D1ECF1"),
        MessageKind.Error => Color.FromArgb("#F8D7DA"),
        _ => Color.FromArgb("#E0E0E0"),
    };

    public Color TextColor => Kind switch
    {
        MessageKind.User => Colors.White,
        _ => Colors.Black,
    };

    public LayoutOptions Alignment => Kind switch
    {
        MessageKind.User => LayoutOptions.End,
        MessageKind.System => LayoutOptions.Center,
        _ => LayoutOptions.Start,
    };

    public double FontSize => Kind switch
    {
        MessageKind.ToolCall or MessageKind.ToolResult => 12,
        MessageKind.Reasoning => 12,
        MessageKind.System => 12,
        _ => 14,
    };

    public bool IsItalic => Kind is MessageKind.Reasoning or MessageKind.System;

    public static ChatMessageItem User(string text) => new() { Text = text, Kind = MessageKind.User };
    public static ChatMessageItem Assistant(string text) => new() { Text = text, Kind = MessageKind.Assistant };
    public static ChatMessageItem ToolCall(string text) => new() { Text = text, Kind = MessageKind.ToolCall };
    public static ChatMessageItem ToolResult(string text) => new() { Text = text, Kind = MessageKind.ToolResult };
    public static ChatMessageItem Reasoning(string text) => new() { Text = text, Kind = MessageKind.Reasoning };
    public static ChatMessageItem System(string text) => new() { Text = text, Kind = MessageKind.System };
    public static ChatMessageItem Error(string text) => new() { Text = text, Kind = MessageKind.Error };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
