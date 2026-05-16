// Copyright (c) Microsoft. All rights reserved.

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI;

/// <summary>
/// Observable wrapper around <see cref="ChatMessage"/> for MAUI data binding.
/// Accumulates streamed content and exposes avatar, timestamp, tool details, and expand/collapse state.
/// </summary>
public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUser))]
    [NotifyPropertyChangedFor(nameof(IsAssistant))]
    private ChatRole _role;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _authorName = string.Empty;

    [ObservableProperty]
    private ImageSource? _avatarSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatarImage))]
    private string? _avatarText;

    [ObservableProperty]
    private bool _showAvatar = true;

    [ObservableProperty]
    private double _avatarSize = 28;

    [ObservableProperty]
    private bool _showTimestamp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimestampText))]
    private DateTimeOffset _timestamp = DateTimeOffset.Now;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReasoning))]
    private string _reasoningText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private string? _toolArgs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private string? _toolResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    private bool _isExpanded;

    /// <summary>All content items received for this message.</summary>
    public ObservableCollection<AIContent> Contents { get; } = [];

    /// <summary>True when the message role is <see cref="ChatRole.User"/>.</summary>
    public bool IsUser => Role == ChatRole.User;

    /// <summary>True when the message role is <see cref="ChatRole.Assistant"/>.</summary>
    public bool IsAssistant => Role == ChatRole.Assistant;

    public bool HasAvatarImage => AvatarSource is not null;

    /// <summary>Formatted timestamp for display.</summary>
    public string TimestampText => Timestamp.LocalDateTime.ToString("h:mm tt");

    public bool HasDetails => ToolArgs is not null || ToolResult is not null;

    public bool HasReasoning => !string.IsNullOrEmpty(ReasoningText);

    public string ExpandIcon => IsExpanded ? "▼" : "▶";

    /// <summary>Optional icon glyph (Unicode).</summary>
    public string? Icon { get; init; }

    public bool HasIcon => Icon is not null;

    public ChatMessageViewModel()
    {
    }

    public ChatMessageViewModel(ChatRole role, string text = "")
    {
        _role = role;
        _text = text;
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (HasDetails)
            IsExpanded = !IsExpanded;
    }

    /// <summary>Creates a view model from an existing <see cref="ChatMessage"/>.</summary>
    public static ChatMessageViewModel FromChatMessage(ChatMessage message)
    {
        var vm = new ChatMessageViewModel(message.Role, message.Text ?? string.Empty)
        {
            AuthorName = message.AuthorName ?? string.Empty
        };

        foreach (var content in message.Contents)
        {
            vm.Contents.Add(content);
        }

        return vm;
    }

    /// <summary>
    /// Reconstructs a <see cref="ChatMessage"/> with text and data content only (for history).
    /// Tool calls and results are handled within a single streaming call by
    /// <see cref="FunctionInvokingChatClient"/> and must not be resent in history.
    /// </summary>
    public ChatMessage ToChatMessage()
    {
        var contents = new List<AIContent>();

        foreach (var content in Contents)
        {
            switch (content)
            {
                case TextContent:
                case DataContent:
                    contents.Add(content);
                    break;
                // Skip FunctionCallContent, FunctionResultContent — handled by FunctionInvokingChatClient
            }
        }

        if (contents.Count == 0 && !string.IsNullOrEmpty(Text))
        {
            contents.Add(new TextContent(Text));
        }

        return new ChatMessage(Role, [.. contents]);
    }
}
