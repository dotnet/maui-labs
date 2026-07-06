using AIExtensions.Sample.Garden.Chat;
using AIExtensions.Sample.Garden.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls;

namespace AIExtensions.Sample.Garden.Views;

/// <summary>
/// Hosts the drop-in <see cref="CopilotChatView"/> and swaps its content templates between the rich
/// (fancy) Garden views and the plain built-in views. The header owns the toggle state and broadcasts
/// a <see cref="ChatTemplateModeChangedMessage"/>; this view reacts to it (messaging, not a shared VM
/// reference).
/// </summary>
public partial class ChatView : ContentView, IRecipient<ChatTemplateModeChangedMessage>
{
    public ChatView()
    {
        InitializeComponent();

        // Start in the rich (fancy) mode; the header toggle defaults to fancy too.
        ApplyTemplates(fancy: true);

        WeakReferenceMessenger.Default.Register(this);
    }

    void IRecipient<ChatTemplateModeChangedMessage>.Receive(ChatTemplateModeChangedMessage message) =>
        ApplyTemplates(message.IsFancy);

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
