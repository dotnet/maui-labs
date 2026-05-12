using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.Garden.ViewModels;

/// <summary>
/// View model for one chat message row. <see cref="Text"/> is mutable so a
/// streaming assistant reply can be updated in place while bound.
/// </summary>
public sealed partial class ChatMessageViewModel(ChatMessageKind kind, string text, string? icon = null) : ObservableObject
{
    public ChatMessageKind Kind { get; } = kind;

    /// <summary>
    /// Optional Fluent icon glyph rendered with FluentFilled font.
    /// </summary>
    public string? Icon { get; } = icon;

    public bool HasIcon => Icon is not null;

    [ObservableProperty]
    public partial string Text { get; set; } = text;

    /// <summary>
    /// Tool call arguments (JSON-formatted). Populated for Tool messages.
    /// </summary>
    [ObservableProperty]
    public partial string? ToolArgs { get; set; }

    /// <summary>
    /// Tool call result. Populated after the tool returns.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    public partial string? ToolResult { get; set; }

    public bool HasDetails => ToolArgs is not null || ToolResult is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandIcon))]
    public partial bool IsExpanded { get; set; }

    public string ExpandIcon => IsExpanded ? FluentIcons.ChevronDown : FluentIcons.ChevronRight;

    [RelayCommand]
    private void ToggleExpand()
    {
        if (HasDetails)
            IsExpanded = !IsExpanded;
    }
}
