using CometProjectManager.Controls;
using CometProjectManager.Models;
using Syncfusion.Maui.Toolkit.TextInputLayout;

using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiPicker = Microsoft.Maui.Controls.Picker;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiCheckBox = Microsoft.Maui.Controls.CheckBox;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;

namespace CometProjectManager.Pages;

public class TaskDetailPageState { }

public class TaskDetailPage : Component<TaskDetailPageState>
{
[State] readonly DataStore _store = DataStore.Instance;
readonly ProjectTask? _existingTask;
readonly int _defaultProjectId;
readonly bool _wrapInNav;

static readonly Color DarkOnLightBg = Color.FromArgb("#0D0D0D");
static readonly Color LightBg = Color.FromArgb("#F2F2F2");

public TaskDetailPage(ProjectTask? task, int defaultProjectId, bool wrapInNav = true)
{
_existingTask = task;
_defaultProjectId = defaultProjectId;
_wrapInNav = wrapInNav;
}

public override View Render()
{
var projects = _store.Projects.Value ?? new List<Project>();
var isExisting = _existingTask != null;

var contentStack = new Microsoft.Maui.Controls.VerticalStackLayout
{
Spacing = 5,
Padding = new Thickness(15),
};

// Task title
var titleEntry = new MauiEntry
{
Text = _existingTask?.Title ?? "",
Placeholder = "What needs to be done?",
};
contentStack.Add(new SfTextInputLayout
{
Hint = "Task",
ContainerType = ContainerType.Outlined,
ContainerBackground = new SolidColorBrush(Colors.Transparent),
Content = titleEntry,
});

// Completed checkbox
var completedCheck = new MauiCheckBox
{
IsChecked = _existingTask?.IsCompleted ?? false,
HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.End,
};
contentStack.Add(new SfTextInputLayout
{
Hint = "Completed",
ContainerType = ContainerType.Outlined,
ContainerBackground = new SolidColorBrush(Colors.Transparent),
Content = completedCheck,
});

// Project picker (only for existing tasks)
var projectPicker = new MauiPicker
{
ItemsSource = projects.Select(p => p.Name).ToList(),
SelectedIndex = Math.Max(0, projects.FindIndex(p =>
p.ID == (_existingTask?.ProjectID ?? _defaultProjectId))),
};
if (isExisting)
{
contentStack.Add(new SfTextInputLayout
{
Hint = "Project",
ContainerType = ContainerType.Outlined,
ContainerBackground = new SolidColorBrush(Colors.Transparent),
Content = projectPicker,
});
}

// Save button
var saveBtn = new MauiButton { Text = "Save", HeightRequest = 44 };
saveBtn.Clicked += (s, e) =>
{
var title = titleEntry.Text?.Trim();
if (string.IsNullOrEmpty(title)) return;

var allProjects = _store.Projects.Value ?? new List<Project>();
var projectId = projectPicker.SelectedIndex >= 0 && projectPicker.SelectedIndex < allProjects.Count
? allProjects[projectPicker.SelectedIndex].ID
: _defaultProjectId;

if (_existingTask != null)
{
_existingTask.Title = title;
_existingTask.IsCompleted = completedCheck.IsChecked;
_existingTask.ProjectID = projectId;
_store.AllTasks.Value = new List<ProjectTask>(_store.AllTasks.Value!);
}
else
{
_store.AddTask(new ProjectTask
{
Title = title,
IsCompleted = completedCheck.IsChecked,
ProjectID = projectId,
});
}
AppNavigation.GoBack(this);
_ = AppNavigation.ShowToastAsync("Task saved");
};
contentStack.Add(saveBtn);

// Delete button (only for existing tasks)
if (_existingTask != null)
{
var deleteBtn = new MauiButton
{
Text = "Delete",
HeightRequest = 44,
BackgroundColor = Color.FromArgb("#FF3300"),
TextColor = Colors.White,
};
deleteBtn.Clicked += (s, e) =>
{
_store.DeleteTask(_existingTask.ID);
AppNavigation.GoBack(this);
_ = AppNavigation.ShowToastAsync("Task deleted");
};
contentStack.Add(deleteBtn);
}

// Root
var rootGrid = new MauiGrid { BackgroundColor = LightBg };
rootGrid.Add(new MauiScrollView { Content = contentStack });

if (!_wrapInNav) return new MauiViewHost(rootGrid);

return NavigationView(
new MauiViewHost(rootGrid)
)
.Title("Task")
.Background(LightBg);
}
}
