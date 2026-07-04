using CommunityToolkit.Mvvm.ComponentModel;

namespace GenerativeUI.Sample.Garden.ViewModels;

public enum ChatMessageKind
{
    User,
    Assistant,
    Tool,
    Error,
}

/// <summary>A single row in the chat transcript.</summary>
public sealed partial class ChatMessageViewModel(ChatMessageKind kind, string text) : ObservableObject
{
    public ChatMessageKind Kind { get; } = kind;

    [ObservableProperty]
    public partial string Text { get; set; } = text;

    /// <summary>Tool call arguments / result, shown under a tool row.</summary>
    [ObservableProperty]
    public partial string? Detail { get; set; }

    public bool IsUser => Kind == ChatMessageKind.User;
    public bool IsAssistant => Kind == ChatMessageKind.Assistant;
    public bool IsTool => Kind == ChatMessageKind.Tool;
    public bool IsError => Kind == ChatMessageKind.Error;
}
