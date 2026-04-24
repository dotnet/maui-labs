namespace CometStressTest.Pages;

public class ListTestPageState
{
public string StatusText { get; set; } = "No item selected";
}

public class ListTestPage : Component<ListTestPageState>
{
static readonly Color[] IndicatorColors = new[]
{
Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple,
Colors.Teal, Colors.Brown, Colors.Magenta, Colors.Cyan, Colors.Gold,
};

readonly ObservableCollection<string> items;

public ListTestPage()
{
items = new ObservableCollection<string>(
Enumerable.Range(1, 50).Select(i => $"Item {i}")
);
}

public override View Render()
{
var list = new ListView<string>(() => items.ToList())
{
ViewFor = item =>
{
var index = items.IndexOf(item);
var color = IndicatorColors[index % IndicatorColors.Length];
return HStack(10,
new ShapeView(new Circle())
.Frame(width: 12, height: 12)
.Background(new SolidPaint(color)),
VStack(LayoutAlignment.Start, 2,
Text(item)
.FontSize(16),
Text($"Subtitle for {item}")
.FontSize(12)
.Color(Colors.Gray)
)
).Padding(8);
},
Header = Text("List Stress Test (50 Items)")
.FontSize(20)
.Padding(12)
.Background(new SolidPaint(Colors.LightGray)),
Footer = Text("— End of list —")
.FontSize(14)
.Padding(12)
.Color(Colors.Gray),
ItemSelected = selection =>
{
SetState(s => s.StatusText = $"Selected: {selection.item} (row {selection.row})");
},
};

return VStack(
Text(State.StatusText)
.FontSize(14)
.Padding(8)
.Background(new SolidPaint(Colors.LightYellow)),
Button("Add Item", () =>
{
items.Add($"Item {items.Count + 1} (dynamic)");
SetState(s => s.StatusText = $"Added item #{items.Count}");
}).Padding(8),
list
);
}
}
