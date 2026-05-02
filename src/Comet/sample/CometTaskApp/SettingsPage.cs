namespace CometTaskApp;

public class SettingsPageState { }

/// <summary>
/// Settings page with toggles and action buttons.
/// Exercises: Toggle-like behavior with State<bool>, computed UI, danger zone actions.
/// </summary>
public class SettingsPage : Component<SettingsPageState>
{
[State] readonly AppState _state = AppState.Instance;

public override View Render() =>
ScrollView(
VStack(20,
Text("Settings")
.FontSize(24)
.FontWeight(FontWeight.Bold)
.SemanticHeadingLevel(SemanticHeadingLevel.Level1),

SettingRow(
"Show Completed Tasks",
"Include completed tasks in the list",
_state.ShowCompletedTasks.Value
? Text("Showing").Color(Colors.Green)
: Text("Hidden").Color(Colors.Red),
() => _state.ShowCompletedTasks.Value = !_state.ShowCompletedTasks.Value
),

SettingRow(
"Dark Mode",
"Switch between light and dark theme",
_state.DarkMode.Value
? Text("Dark").Color(Colors.Purple)
: Text("Light").Color(Colors.Orange),
() => _state.DarkMode.Value = !_state.DarkMode.Value
),

Spacer().Frame(height: 8),

Text("Danger Zone")
.FontSize(18)
.FontWeight(FontWeight.Semibold)
.Color(Colors.Red)
.SemanticHeadingLevel(SemanticHeadingLevel.Level2),

Button("Clear All Completed", () =>
{
var list = new List<TaskItem>(_state.Tasks.Value ?? new List<TaskItem>());
list.RemoveAll(t => t.IsCompleted);
_state.Tasks.Value = list;
})
.Color(Colors.OrangeRed)
.SemanticDescription("Remove all completed tasks"),

Button("Reset to Sample Data", () =>
{
_state.Tasks.Value = new List<TaskItem>
{
new() { Title = "Buy groceries", Description = "Milk, eggs, bread", Priority = TaskPriority.Medium, Category = TaskCategory.Shopping },
new() { Title = "Finish report", Description = "Q4 summary", Priority = TaskPriority.High, Category = TaskCategory.Work },
new() { Title = "Go for a run", Description = "30 min", Priority = TaskPriority.Low, Category = TaskCategory.Health },
};
})
.Color(Colors.Red)
.SemanticDescription("Reset all tasks to sample data"),

Spacer().Frame(height: 20),

Text("About")
.FontSize(18)
.FontWeight(FontWeight.Semibold)
.SemanticHeadingLevel(SemanticHeadingLevel.Level2),

VStack(4,
Text("Comet Task Manager")
.FontSize(14).FontWeight(FontWeight.Semibold),
Text("Built with Comet MVU for .NET MAUI")
.FontSize(12).Color(Colors.Gray),
Text("Demonstrates: Navigation, Lists, Forms, Gestures, State Management, Accessibility")
.FontSize(11).Color(Colors.DarkGray)
)
.Padding(12)
.Background(new SolidPaint(Colors.WhiteSmoke))
.ClipShape(new RoundedRectangle(8))
)
.Padding(16)
);

View SettingRow(string title, string subtitle, View indicator, Action onTap)
{
return HStack(12,
VStack(2,
Text(title).FontSize(16).FontWeight(FontWeight.Semibold),
Text(subtitle).FontSize(12).Color(Colors.Gray)
),
Spacer(),
indicator
)
.Padding(12)
.Background(new SolidPaint(Colors.WhiteSmoke))
.ClipShape(new RoundedRectangle(8))
.OnTap(_ => onTap())
.SemanticDescription($"{title}: {subtitle}");
}
}
