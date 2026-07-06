using System.ComponentModel;
using AIExtensions.Sample.Garden.Chat;
using AIExtensions.Sample.Garden.ViewModels;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Views;

/// <summary>
/// Hosts the drop-in <see cref="CopilotChatView"/> and swaps its content templates between the rich
/// (fancy) Garden views and the plain built-in views when <see cref="ChatViewModel.IsFancy"/> changes.
/// </summary>
public partial class ChatView : ContentView
{
    private ChatViewModel? _viewModel;

    public ChatView()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = BindingContext as ChatViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTemplates(_viewModel.IsFancy);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.IsFancy) && _viewModel is not null)
            ApplyTemplates(_viewModel.IsFancy);
    }

    private void ApplyTemplates(bool fancy)
    {
        // Mutating the collection re-triggers the control's template-selector rebuild, which resets the
        // CollectionView's ItemTemplate and re-realizes existing cells with the new set.
        var templates = ChatControl.ContentTemplates;
        templates.Clear();
        foreach (var template in fancy ? BuildFancyTemplates() : BuildPlainTemplates())
            templates.Add(template);
    }

    /// <summary>Rich set: custom Garden views for assistant text, product results, and tool cards.</summary>
    private static IEnumerable<ContentTemplate> BuildFancyTemplates() =>
    [
        new ThinkingContentTemplate(),
        new TextContentTemplate { Role = "User" },
        new GenericContentTemplate { BlockType = typeof(GardenFormattedTextBlock), ViewType = typeof(GardenFormattedTextView) },
        new MediaContentTemplate(),
        new GenericContentTemplate { BlockType = typeof(ProductResultsBlock), ViewType = typeof(ProductResultsView) },
        new GenericContentTemplate { BlockType = typeof(FunctionInvocationContentBlock), ViewType = typeof(GardenToolView) },
        new ToolApprovalTemplate(),
        new ErrorContentTemplate(),
        new DefaultContentTemplate(),
    ];

    /// <summary>
    /// Plain set: only built-in templates. Formatted assistant text falls back to raw markdown text,
    /// product results render as the default block summary, and tools show the built-in call/result —
    /// the raw-vs-rich contrast.
    /// </summary>
    private static IEnumerable<ContentTemplate> BuildPlainTemplates() =>
    [
        new ThinkingContentTemplate(),
        new TextContentTemplate(),
        new MediaContentTemplate(),
        new FunctionInvocationTemplate(),
        new ToolApprovalTemplate(),
        new ErrorContentTemplate(),
        new DefaultContentTemplate(),
    ];
}
