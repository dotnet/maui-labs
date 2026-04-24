using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;

namespace CometStressTest.Pages;

public class LayoutTestPageState { }

public class LayoutTestPage : Component<LayoutTestPageState>
{
public override View Render()
{
var root = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 20, Padding = new Thickness(12) };

root.Add(new MauiLabel { Text = "Layout Stress Tests", FontSize = 22, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });

// Complex Grid
root.Add(new MauiLabel { Text = "Complex Grid (rows/columns with spans)", FontSize = 16, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
root.Add(BuildComplexGrid());

// FlexLayout
root.Add(new MauiLabel { Text = "FlexLayout (wrap, 12 items)", FontSize = 16, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
root.Add(BuildFlexLayout());

// AbsoluteLayout
root.Add(new MauiLabel { Text = "AbsoluteLayout (overlapping)", FontSize = 16, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
root.Add(BuildAbsoluteLayout());

// Nested ScrollView > VStack > HStack
root.Add(new MauiLabel { Text = "Nested Scroll + Stacks", FontSize = 16, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold });
root.Add(BuildNestedStacks());

var scroll = new MauiScrollView { Content = root };
return new MauiViewHost(scroll);
}

Microsoft.Maui.Controls.View BuildComplexGrid()
{
var grid = new MauiGrid
{
RowSpacing = 4,
ColumnSpacing = 4,
};

grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = new GridLength(50) });
grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = new GridLength(50) });
grid.RowDefinitions.Add(new Microsoft.Maui.Controls.RowDefinition { Height = new GridLength(50) });

grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
grid.ColumnDefinitions.Add(new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

// Row 0: span 2 columns + 1 column
var cell1 = MakeGridCell("Span 2 cols", Colors.CornflowerBlue);
MauiGrid.SetRow(cell1, 0);
MauiGrid.SetColumn(cell1, 0);
MauiGrid.SetColumnSpan(cell1, 2);
grid.Add(cell1);

var cell1b = MakeGridCell("1x1", Colors.Coral);
MauiGrid.SetRow(cell1b, 0);
MauiGrid.SetColumn(cell1b, 2);
grid.Add(cell1b);

// Row 1: 1 column + span 2 rows + 1 column
var cell2a = MakeGridCell("1x1", Colors.MediumSeaGreen);
MauiGrid.SetRow(cell2a, 1);
MauiGrid.SetColumn(cell2a, 0);
grid.Add(cell2a);

var cell2 = MakeGridCell("Span 2 rows", Colors.MediumOrchid);
MauiGrid.SetRow(cell2, 1);
MauiGrid.SetColumn(cell2, 1);
MauiGrid.SetRowSpan(cell2, 2);
grid.Add(cell2);

var cell2c = MakeGridCell("1x1", Colors.Gold);
MauiGrid.SetRow(cell2c, 1);
MauiGrid.SetColumn(cell2c, 2);
grid.Add(cell2c);

// Row 2
var cell3a = MakeGridCell("1x1", Colors.Salmon);
MauiGrid.SetRow(cell3a, 2);
MauiGrid.SetColumn(cell3a, 0);
grid.Add(cell3a);

var cell3c = MakeGridCell("1x1", Colors.SteelBlue);
MauiGrid.SetRow(cell3c, 2);
MauiGrid.SetColumn(cell3c, 2);
grid.Add(cell3c);

return grid;
}

static MauiBorder MakeGridCell(string text, Color bg) => new MauiBorder
{
StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) },
BackgroundColor = bg,
StrokeThickness = 0,
Content = new MauiLabel
{
Text = text,
TextColor = Colors.White,
FontSize = 12,
HorizontalTextAlignment = TextAlignment.Center,
VerticalTextAlignment = TextAlignment.Center,
},
};

Microsoft.Maui.Controls.View BuildFlexLayout()
{
var flex = new Microsoft.Maui.Controls.FlexLayout
{
Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
AlignContent = Microsoft.Maui.Layouts.FlexAlignContent.Start,
};

var colors = new[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple, Colors.Teal,
Colors.Brown, Colors.Magenta, Colors.Cyan, Colors.Gold, Colors.Coral, Colors.Navy };

for (int i = 0; i < 12; i++)
{
flex.Children.Add(new MauiBorder
{
StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
BackgroundColor = colors[i],
StrokeThickness = 0,
WidthRequest = 80,
HeightRequest = 50,
Margin = new Thickness(4),
Content = new MauiLabel
{
Text = $"Flex {i + 1}",
TextColor = Colors.White,
FontSize = 12,
HorizontalTextAlignment = TextAlignment.Center,
VerticalTextAlignment = TextAlignment.Center,
}
});
}

return flex;
}

Microsoft.Maui.Controls.View BuildAbsoluteLayout()
{
var abs = new Microsoft.Maui.Controls.AbsoluteLayout { HeightRequest = 200 };

var box1 = new MauiBoxView { Color = Colors.LightBlue, CornerRadius = 8 };
Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutBounds(box1, new Rect(0, 0, 200, 150));
abs.Add(box1);

var box2 = new MauiBoxView { Color = Colors.Coral, CornerRadius = 8, Opacity = 0.8 };
Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutBounds(box2, new Rect(50, 30, 200, 120));
abs.Add(box2);

var box3 = new MauiBoxView { Color = Colors.MediumPurple, CornerRadius = 8, Opacity = 0.7 };
Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutBounds(box3, new Rect(100, 60, 180, 100));
abs.Add(box3);

var label = new MauiLabel
{
Text = "Overlapping!",
TextColor = Colors.White,
FontSize = 18,
FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
};
Microsoft.Maui.Controls.AbsoluteLayout.SetLayoutBounds(label, new Rect(120, 90, 150, 40));
abs.Add(label);

return abs;
}

Microsoft.Maui.Controls.View BuildNestedStacks()
{
var outer = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 8 };

for (int row = 0; row < 5; row++)
{
var hStack = new Microsoft.Maui.Controls.HorizontalStackLayout { Spacing = 6 };
for (int col = 0; col < 6; col++)
{
hStack.Add(new MauiBorder
{
StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(4) },
BackgroundColor = Color.FromHsla(row * 0.1 + col * 0.05, 0.6, 0.5),
StrokeThickness = 0,
WidthRequest = 50,
HeightRequest = 40,
Content = new MauiLabel
{
Text = $"{row},{col}",
TextColor = Colors.White,
FontSize = 10,
HorizontalTextAlignment = TextAlignment.Center,
VerticalTextAlignment = TextAlignment.Center,
}
});
}
outer.Add(hStack);
}

var nestedScroll = new MauiScrollView
{
Orientation = ScrollOrientation.Horizontal,
Content = outer,
HeightRequest = 250,
};

return nestedScroll;
}
}
