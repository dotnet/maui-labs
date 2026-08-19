using Microsoft.Maui.Chat.Controls;

namespace ChatControls.Sample;

public partial class GardenTaskView : ChatContentView
{
    public GardenTaskView()
    {
        InitializeComponent();
        AutomationId = "GardenTaskCard";
    }

    protected override void RefreshContent()
    {
        var task = Item?.Content as GardenTaskContent;
        var author = Item?.Participant.DisplayName ?? string.Empty;
        SharedByLabel.Text = author.Length == 0
            ? string.Empty
            : $"{author} shared a task";
        TitleLabel.Text = task?.Title ?? string.Empty;
        AssigneeLabel.Text = task is null
            ? string.Empty
            : $"Assigned to {task.Assignee}";
        PriorityLabel.Text = task?.Priority == GardenTaskPriority.High
            ? "HIGH"
            : string.Empty;
        DueLabel.Text = task is null
            ? string.Empty
            : $"Due {task.DueText}";
        SemanticProperties.SetDescription(
            this,
            task is null
                ? string.Empty
                : $"{author} shared a task: {task.Title}. Assigned to {task.Assignee}. Due {task.DueText}.");
    }
}
