using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;

namespace CometStressTest.Pages;

public class CollectionTestPageState { }

public class CollectionTestPage : Component<CollectionTestPageState>
{
static readonly Color[] BoxColors = new[]
{
Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple,
Colors.Teal, Colors.Brown, Colors.Magenta, Colors.Cyan, Colors.Gold,
Colors.Coral, Colors.DarkSlateBlue, Colors.ForestGreen, Colors.HotPink,
Colors.IndianRed, Colors.Khaki, Colors.LimeGreen, Colors.MediumOrchid,
Colors.Navy, Colors.OliveDrab,
};

public override View Render()
{
var root = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 16, Padding = new Thickness(12) };

// Section: Horizontal CollectionView
root.Add(new MauiLabel { Text = "Horizontal CollectionView (20 items)", FontSize = 18, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
root.Add(BuildHorizontalCollection());

// Section: Vertical CollectionView
root.Add(new MauiLabel { Text = "Vertical CollectionView (20 items)", FontSize = 18, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
root.Add(BuildVerticalCollection());

// Section: Grid CollectionView
root.Add(new MauiLabel { Text = "Grid CollectionView (2 columns, 20 items)", FontSize = 18, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
root.Add(BuildGridCollection());

var scroll = new MauiScrollView { Content = root };
return new MauiViewHost(scroll);
}

Microsoft.Maui.Controls.View BuildHorizontalCollection()
{
var root = new Microsoft.Maui.Controls.HorizontalStackLayout { Spacing = 8 };
for (int i = 0; i < 20; i++)
{
var stack = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 4 };
stack.Add(new MauiBoxView { WidthRequest = 80, HeightRequest = 80, CornerRadius = 8, Color = BoxColors[i] });
stack.Add(new MauiLabel { Text = $"Box {i}", HorizontalTextAlignment = TextAlignment.Center, FontSize = 12 });
root.Add(stack);
}
return new MauiScrollView
{
Orientation = ScrollOrientation.Horizontal,
HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
Content = root,
HeightRequest = 120,
};
}

Microsoft.Maui.Controls.View BuildVerticalCollection()
{
var stack = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 4 };
for (int i = 1; i <= 20; i++)
{
stack.Add(new MauiBorder
{
StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
StrokeThickness = 1,
Stroke = Colors.LightGray,
Padding = new Thickness(12, 8),
Content = new MauiLabel { Text = $"Vertical Item {i}", FontSize = 14 },
});
}
return new MauiScrollView { Content = stack, HeightRequest = 300 };
}

Microsoft.Maui.Controls.View BuildGridCollection()
{
var grid = new MauiGrid { ColumnSpacing = 8, RowSpacing = 8 };
grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

int rows = 10;
for (int r = 0; r < rows; r++)
grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = new GridLength(60) });

for (int i = 0; i < 20; i++)
{
var border = new MauiBorder
{
StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
BackgroundColor = Colors.LightBlue,
StrokeThickness = 0,
Padding = new Thickness(12),
Content = new MauiLabel { Text = $"Grid Item {i + 1}", FontSize = 14, VerticalTextAlignment = TextAlignment.Center },
};
MauiGrid.SetRow(border, i / 2);
MauiGrid.SetColumn(border, i % 2);
grid.Add(border);
}
return grid;
}
}
