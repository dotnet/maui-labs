using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    public partial string? Detail { get; set; }

    /// <summary>Tool details are collapsed by default; normal messages are always effectively open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    public partial bool IsExpanded { get; set; } = false;

    public bool IsUser => Kind == ChatMessageKind.User;
    public bool IsAssistant => Kind == ChatMessageKind.Assistant;
    public bool IsTool => Kind == ChatMessageKind.Tool;
    public bool IsError => Kind == ChatMessageKind.Error;

    public bool IsDetailVisible => !string.IsNullOrEmpty(Detail) && (!IsTool || IsExpanded);
    public string Chevron => IsExpanded ? "▾" : "▸";

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (IsTool)
            IsExpanded = !IsExpanded;
    }
}
