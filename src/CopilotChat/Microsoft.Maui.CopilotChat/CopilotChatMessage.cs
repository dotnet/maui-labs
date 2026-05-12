using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Microsoft.Maui.CopilotChat;

/// <summary>
/// Observable message model displayed by <see cref="Controls.CopilotChatView"/>.
/// <see cref="Text"/> is mutable so streaming assistant replies can update in place.
/// </summary>
public sealed class CopilotChatMessage : INotifyPropertyChanged
{
    private string _text;
    private string? _toolArgs;
    private string? _toolResult;
    private bool _isExpanded;
    private bool _isStreaming;
    private string? _authorName;
    private ImageSource? _avatarSource;
    private string? _avatarText;
    private DateTimeOffset _timestamp = DateTimeOffset.Now;

    public CopilotChatMessage(ChatMessageKind kind, string text, string? icon = null)
    {
        Kind = kind;
        _text = text;
        Icon = icon;
        ToggleExpandCommand = new Command(ToggleExpand);
    }

    public ChatMessageKind Kind { get; }

    /// <summary>Optional icon glyph (Unicode).</summary>
    public string? Icon { get; }

    public bool HasIcon => Icon is not null;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    // ── Avatar & identity ──

    /// <summary>Display name of the message author (e.g. "You", "Assistant").</summary>
    public string? AuthorName
    {
        get => _authorName;
        set => SetProperty(ref _authorName, value);
    }

    /// <summary>Avatar image. If null, falls back to <see cref="AvatarText"/> initials.</summary>
    public ImageSource? AvatarSource
    {
        get => _avatarSource;
        set { SetProperty(ref _avatarSource, value); OnPropertyChanged(nameof(HasAvatarImage)); }
    }

    /// <summary>Fallback text for the avatar circle (e.g. "You", "AI").</summary>
    public string? AvatarText
    {
        get => _avatarText;
        set => SetProperty(ref _avatarText, value);
    }

    public bool HasAvatarImage => AvatarSource is not null;

    // ── Timestamps & state ──

    /// <summary>When the message was created.</summary>
    public DateTimeOffset Timestamp
    {
        get => _timestamp;
        set => SetProperty(ref _timestamp, value);
    }

    /// <summary>True while the assistant is still streaming this message.</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }

    // ── Tool details ──

    /// <summary>Tool call arguments (JSON-formatted). Populated for Tool messages.</summary>
    public string? ToolArgs
    {
        get => _toolArgs;
        set { SetProperty(ref _toolArgs, value); OnPropertyChanged(nameof(HasDetails)); }
    }

    /// <summary>Tool call result. Populated after the tool returns.</summary>
    public string? ToolResult
    {
        get => _toolResult;
        set { SetProperty(ref _toolResult, value); OnPropertyChanged(nameof(HasDetails)); }
    }

    public bool HasDetails => ToolArgs is not null || ToolResult is not null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { SetProperty(ref _isExpanded, value); OnPropertyChanged(nameof(ExpandIcon)); }
    }

    public string ExpandIcon => IsExpanded ? ChatIcons.ChevronDown : ChatIcons.ChevronRight;

    /// <summary>Command that toggles the expanded state.</summary>
    public ICommand ToggleExpandCommand { get; }

    public void ToggleExpand()
    {
        if (HasDetails)
            IsExpanded = !IsExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
