namespace CometTaskApp;

public class StatsPageState { }

/// <summary>
/// Statistics dashboard with progress visualization.
/// Exercises: Computed state, ShapeView for visual charts, dynamic rendering.
/// </summary>
public class StatsPage : Component<StatsPageState>
{
[State] readonly AppState _state = AppState.Instance;

public override View Render()
{
var total = _state.TotalCount;
var completed = _state.CompletedCount;
var pending = _state.PendingCount;
var percentage = total > 0 ? (int)(completed * 100.0 / total) : 0;

return ScrollView(
VStack(20,
Text("Task Statistics")
.FontSize(24)
.FontWeight(FontWeight.Bold)
.SemanticHeadingLevel(SemanticHeadingLevel.Level1),

ZStack(
new ShapeView(new Circle())
.Frame(120, 120)
.Background(new SolidPaint(Colors.LightGray)),
new ShapeView(new Circle())
.Frame(100, 100)
.Background(new SolidPaint(Colors.White)),
Text($"{percentage}%")
.FontSize(28)
.FontWeight(FontWeight.Bold)
.Color(percentage >= 75 ? Colors.Green : percentage >= 50 ? Colors.Orange : Colors.Red)
)
.Frame(120, 120),

StatCard("Total Tasks", total.ToString(), "#", Colors.DodgerBlue),
StatCard("Completed", completed.ToString(), "*", Colors.Green),
StatCard("Pending", pending.ToString(), "~", Colors.Orange),

Text("By Category")
.FontSize(18)
.FontWeight(FontWeight.Semibold)
.SemanticHeadingLevel(SemanticHeadingLevel.Level2),

CategoryBreakdown(),

Text("By Priority")
.FontSize(18)
.FontWeight(FontWeight.Semibold)
.SemanticHeadingLevel(SemanticHeadingLevel.Level2),

PriorityBreakdown()
)
.Padding(16)
);
}

View StatCard(string label, string value, string emoji, Color color)
{
return HStack(12,
Text(emoji).FontSize(28),
VStack(2,
Text(value)
.FontSize(22)
.FontWeight(FontWeight.Bold)
.Color(color),
Text(label)
.FontSize(13)
.Color(Colors.Gray)
),
Spacer()
)
.Padding(12)
.Background(new SolidPaint(Colors.WhiteSmoke))
.ClipShape(new RoundedRectangle(8))
.SemanticDescription($"{label}: {value}");
}

View CategoryBreakdown()
{
var tasks = _state.Tasks.Value ?? new List<TaskItem>();
var categories = Enum.GetValues<TaskCategory>();

return VStack(6,
categories.Select(cat =>
{
var count = tasks.Count(t => t.Category == cat);
var emoji = cat switch
{
TaskCategory.Personal => "Personal",
TaskCategory.Work => "Work",
TaskCategory.Shopping => "Shopping",
TaskCategory.Health => "Health",
TaskCategory.Learning => "Learning",
_ => "Other"
};
return BarRow($"{emoji} {cat}", count, tasks.Count);
}).ToArray()
);
}

View PriorityBreakdown()
{
var tasks = _state.Tasks.Value ?? new List<TaskItem>();
var priorities = Enum.GetValues<TaskPriority>();

return VStack(6,
priorities.Select(p =>
{
var count = tasks.Count(t => t.Priority == p);
return BarRow($"{p}", count, tasks.Count);
}).ToArray()
);
}

View BarRow(string label, int count, int total)
{
var fraction = total > 0 ? (double)count / total : 0;
var barWidth = Math.Max(4, fraction * 200);

return HStack(8,
Text(label)
.FontSize(13)
.Frame(width: 100),
new ShapeView(new RoundedRectangle(4))
.Frame(width: (float)barWidth, height: 16)
.Background(new SolidPaint(Colors.DodgerBlue)),
Text($"{count}")
.FontSize(13)
.Color(Colors.Gray)
)
.SemanticDescription($"{label}: {count} of {total}");
}
}
