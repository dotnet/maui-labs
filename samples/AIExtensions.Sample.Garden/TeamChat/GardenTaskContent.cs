using Microsoft.Maui.Chat.Controls;

namespace AIExtensions.Sample.Garden;

/// <summary>A provider-neutral custom content item rendered as a task card in the crew chat.</summary>
public sealed class GardenTaskContent(
    string title,
    string assignee,
    string dueText) : MessageContent
{
    public string Title { get; } =
        string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("A task title is required.", nameof(title))
            : title;

    public string Assignee { get; } =
        string.IsNullOrWhiteSpace(assignee)
            ? throw new ArgumentException("An assignee is required.", nameof(assignee))
            : assignee;

    public string DueText { get; } =
        string.IsNullOrWhiteSpace(dueText)
            ? throw new ArgumentException("A due time is required.", nameof(dueText))
            : dueText;
}
