using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.CopilotChat;

/// <summary>
/// Observable message model displayed by <see cref="CopilotChatView"/>.
/// <see cref="Text"/> is mutable so streaming assistant replies can update in place.
/// </summary>
public sealed class CopilotChatMessage : INotifyPropertyChanged
{
    private string _text;
    private string? _toolArgs;
    private string? _toolResult;
    private bool _isExpanded;

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

    /// <summary>Command that toggles the expanded state. Used by default tool message template.</summary>
    public System.Windows.Input.ICommand ToggleExpandCommand { get; }

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
