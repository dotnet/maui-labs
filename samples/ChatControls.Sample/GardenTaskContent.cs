using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

public enum GardenTaskPriority
{
    Normal,
    High,
}

public sealed class GardenTaskContent : MessageContent
{
    public GardenTaskContent(
        string title,
        string assignee,
        string dueText,
        GardenTaskPriority priority = GardenTaskPriority.Normal)
    {
        Title = string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("A task title is required.", nameof(title))
            : title;
        Assignee = string.IsNullOrWhiteSpace(assignee)
            ? throw new ArgumentException("An assignee is required.", nameof(assignee))
            : assignee;
        DueText = string.IsNullOrWhiteSpace(dueText)
            ? throw new ArgumentException("A due time is required.", nameof(dueText))
            : dueText;
        Priority = priority;
        Presentation = ChatContentPresentation.Bare;
    }

    public string Title { get; }

    public string Assignee { get; }

    public string DueText { get; }

    public GardenTaskPriority Priority { get; }
}
