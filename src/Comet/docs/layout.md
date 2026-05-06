# Layout System Guide

Comet provides a complete set of layout containers for arranging views. Layouts
use C# collection initializer syntax -- add child views directly inside braces.
Spacing, padding, margin, and alignment are controlled through fluent extension
methods.


## Layout Containers

### VStack

Arranges children vertically from top to bottom.

```csharp
new VStack(spacing: 12)
{
	new Text("First"),
	new Text("Second"),
	new Text("Third"),
}
```

**Constructor:** `VStack(LayoutAlignment alignment = LayoutAlignment.Fill, float? spacing = null)`

- `alignment` -- horizontal alignment of children (`Fill`, `Start`, `Center`, `End`)
- `spacing` -- vertical space between children (default 6)


### HStack

Arranges children horizontally from leading to trailing edge.

```csharp
new HStack(spacing: 8)
{
	new Image("icon.png").Frame(width: 24, height: 24),
	new Text("Label"),
	new Spacer(),
	new Button("Action", () => DoSomething()),
}
```

**Constructor:** `HStack(LayoutAlignment alignment = LayoutAlignment.Fill, float? spacing = null)`

- `alignment` -- vertical alignment of children
- `spacing` -- horizontal space between children (default 6)


### ZStack

Overlays children on top of each other. Last child renders on top.

```csharp
new ZStack
{
	new Image("background.jpg"),
	new Text("Overlay Text")
		.Color(Colors.White)
		.Alignment(Alignment.Center),
}
```

ZStack has zero default padding. It does not accept spacing or alignment
constructor parameters.


### Grid

Classic grid layout with explicit rows and columns. Column and row sizes can be
absolute values, `"*"` (star/proportional), or `"Auto"`.

```csharp
new Grid(
	columns: new object[] { "*", "*" },
	rows: new object[] { "Auto", "*" },
	spacing: 4)
{
	new Text("Name:").Cell(row: 0, column: 0),
	new TextField(() => name.Value, "").Cell(row: 0, column: 1),
	new TextEditor(() => bio.Value).Cell(row: 1, column: 0, colSpan: 2),
}
```

**Constructor:**
```csharp
Grid(
	object[] columns = null,
	object[] rows = null,
	float? spacing = null,
	float? columnSpacing = null,
	float? rowSpacing = null,
	object defaultRowHeight = null,
	object defaultColumnWidth = null)
```

Children are positioned with the `.Cell()` extension method:
```csharp
view.Cell(row: 0, column: 1, rowSpan: 1, colSpan: 2)
```

MAUI-familiar names are also available:
```csharp
view.GridRow(1).GridColumn(0).GridRowSpan(2).GridColumnSpan(3)
```


### VGrid

Auto-flowing vertical grid. Children fill columns left to right, then wrap to
the next row.

```csharp
// Three columns, equal width
new VGrid(columns: 3, spacing: 8)
{
	new Text("A"), new Text("B"), new Text("C"),
	new Text("D"), new Text("E"), new Text("F"),
}

// Explicit column widths
new VGrid(columns: new object[] { 100, "*", "*" })
{
	new Image("thumb.png"),
	new Text("Title"),
	new Text("Subtitle"),
}
```

**Constructors:**
- `VGrid(int columns, float? spacing = null, object columnWidth = null, object rowHeight = null)`
- `VGrid(object[] columns, float? spacing = null, object columnWidth = null, object rowHeight = null)`


### HGrid

Auto-flowing horizontal grid. Children fill rows top to bottom, then wrap to
the next column.

```csharp
new HGrid(rows: 2, spacing: 8)
{
	new Text("A"), new Text("B"),
	new Text("C"), new Text("D"),
}
```

**Constructors:**
- `HGrid(int rows, float? spacing = null, object rowHeight = null, object columnWidth = null)`
- `HGrid(object[] rows, float? spacing = null, object rowHeight = null, object columnWidth = null)`


### ScrollView

Makes content scrollable when it exceeds available space.

```csharp
new ScrollView(Orientation.Vertical)
{
	new VStack(spacing: 4)
	{
		// many children...
	}
}
```

**Constructor:** `ScrollView(Orientation orientation = Orientation.Vertical)`

**Key properties:**
- `Orientation` -- `Vertical` or `Horizontal`
- `HorizontalScrollBarVisibility`, `VerticalScrollBarVisibility`

ScrollView accepts a single child. Wrap multiple views in a VStack or HStack.


### AbsoluteLayout

Manual positioning with optional proportional coordinates.

```csharp
new AbsoluteLayout
{
	new Text("Centered")
		.LayoutBounds(new Rect(0.5, 0.5, 100, 40))
		.LayoutFlags(AbsoluteLayoutFlags.PositionProportional),

	new Image("corner.png")
		.LayoutBounds(new Rect(0, 0, 50, 50))
		.LayoutFlags(AbsoluteLayoutFlags.None),
}
```

Child positioning methods:
- `.LayoutBounds(Rect)` -- position and size
- `.LayoutFlags(AbsoluteLayoutFlags)` -- which values are proportional (0-1)

Available flags: `None`, `XProportional`, `YProportional`, `WidthProportional`,
`HeightProportional`, `PositionProportional`, `SizeProportional`, `All`


### FlexLayout

CSS-like flexbox layout for advanced arrangements.

```csharp
new FlexLayout(
	direction: FlexDirection.Row,
	wrap: FlexWrap.Wrap,
	justifyContent: FlexJustify.SpaceBetween,
	alignItems: FlexAlignItems.Center)
{
	new Text("Tag 1").FlexGrow(0).FlexShrink(1),
	new Text("Tag 2").FlexGrow(0).FlexShrink(1),
	new Text("Tag 3").FlexGrow(1),
}
```

**Constructor:**
```csharp
FlexLayout(
	FlexDirection direction = FlexDirection.Row,
	FlexWrap wrap = FlexWrap.NoWrap,
	FlexJustify justifyContent = FlexJustify.Start,
	FlexAlignItems alignItems = FlexAlignItems.Stretch,
	FlexAlignContent alignContent = FlexAlignContent.Stretch)
```

**Child properties:**
- `.FlexBasis(double)` -- initial size before grow/shrink
- `.FlexGrow(double)` -- growth factor (0 = do not grow)
- `.FlexShrink(double)` -- shrink factor (1 = normal, 0 = do not shrink)
- `.FlexAlignSelf(FlexAlignSelf)` -- override parent `alignItems` for this child
- `.FlexOrder(int)` -- visual ordering

**Direction values:** `Row`, `Column`, `RowReverse`, `ColumnReverse`
**Wrap values:** `NoWrap`, `Wrap`, `Reverse`
**Justify values:** `Start`, `Center`, `End`, `SpaceBetween`, `SpaceAround`, `SpaceEvenly`
**AlignItems values:** `Start`, `Center`, `End`, `Stretch`


## Spacing, Padding, and Margin

### Margin

Space outside a view, between it and its siblings or parent.

```csharp
// Uniform margin
new Text("Hello").Margin(16)

// Per-edge margin
new Text("Hello").Margin(left: 8, top: 16, right: 8, bottom: 0)

// Thickness object
new Text("Hello").Margin(new Thickness(8, 16))
```

### Padding

Space inside a layout container, between its edges and its children.

```csharp
new VStack
{
	new Text("Padded content"),
}.Padding(new Thickness(16))
```

Layout containers inherit a default padding from the theme. Override it
explicitly when needed.

### Stack Spacing

Passed as a constructor parameter. It controls the gap between adjacent children.

```csharp
// 12 points between each child
new VStack(spacing: 12)
{
	new Text("A"),
	new Text("B"),
}
```

The default spacing for VStack and HStack is 6 points.


## Alignment

### View Alignment

Control how an individual view aligns within its parent.

```csharp
// Named alignment presets
new Text("Centered").Alignment(Alignment.Center)
new Text("Top-left").Alignment(Alignment.TopLeading)

// Axis-specific
new Text("Left").HorizontalLayoutAlignment(LayoutAlignment.Start)
new Text("Bottom").VerticalLayoutAlignment(LayoutAlignment.End)
```

**Alignment presets:**

| Preset | Horizontal | Vertical |
|--------|-----------|----------|
| `TopLeading` | Start | Start |
| `Top` | Center | Start |
| `TopTrailing` | End | Start |
| `Leading` | Start | Center |
| `Center` | Center | Center |
| `Trailing` | End | Center |
| `BottomLeading` | Start | End |
| `Bottom` | Center | End |
| `BottomTrailing` | End | End |
| `Fill` | Fill | Fill |

### Stack Alignment

The alignment parameter on VStack/HStack controls the cross-axis alignment of
all children.

```csharp
// Children centered horizontally within the vertical stack
new VStack(alignment: LayoutAlignment.Center)
{
	new Text("Short"),
	new Text("A much longer piece of text"),
}
```


## Size Constraints

### Frame

Set explicit width and/or height.

```csharp
new Image("avatar.png").Frame(width: 64, height: 64)
new Text("Banner").Frame(height: 44) // width unconstrained
```

### Fill and Fit

Control whether a view expands to fill available space or shrinks to fit its
content.

```csharp
// Expand to fill parent width
new Text("Full width").FillHorizontal()

// Shrink to content size
new Text("Compact").FitHorizontal()

// Both axes
new Image("bg.png").FillHorizontal().FillVertical()
```


## Safe Area

By default, views respect the safe area insets on iOS and other platforms. Opt
out for edge-to-edge layouts:

```csharp
new Image("hero.jpg")
	.FillHorizontal()
	.FillVertical()
	.IgnoreSafeArea()
```


## Spacer

The `Spacer` control expands to fill available space within a stack, pushing
siblings apart.

```csharp
new HStack
{
	new Text("Left-aligned"),
	new Spacer(),
	new Button("Action", () => { }),
}
```

Use multiple spacers for equal distribution:

```csharp
new HStack
{
	new Spacer(),
	new Text("Centered"),
	new Spacer(),
}
```


## Responsive Patterns

### Platform-Specific Values

Use `OnPlatform.Value()` to provide different values per platform:

```csharp
new VStack(spacing: OnPlatform.Value(iOS: 12f, android: 8f, windows: 16f))
{
	// children
}
```

For more platform-specific patterns, see the
[Platform-Specific Guides](platform-guides.md).

### Device Idiom Checks

Adapt layout based on the device form factor:

```csharp
[Body]
View Body()
{
	if (DeviceInfo.Idiom == DeviceIdiom.Phone)
	{
		return new VStack { /* phone layout */ };
	}
	else
	{
		return new HStack { /* tablet/desktop layout */ };
	}
}
```

### Adaptive Grid Columns

Vary grid columns by idiom:

```csharp
var columns = DeviceInfo.Idiom == DeviceIdiom.Phone ? 2 : 4;
return new VGrid(columns: columns, spacing: 8)
{
	// items
};
```


## BindableLayout

Generate children dynamically from a data collection without using ListView or
CollectionView.

```csharp
var layout = new BindableLayout<string>
{
	ItemsSource = names,
	ItemTemplate = name => new HStack(spacing: 8)
	{
		new Image("person.png").Frame(width: 24, height: 24),
		new Text(name),
	},
};
```

`BindableLayout<T>` observes `INotifyCollectionChanged` and rebuilds children
automatically when the source collection changes.


## Common Mistakes

### Nested ScrollViews

Do not nest ScrollViews with the same orientation. The inner scroll view will
compete with the outer one for touch events.

```csharp
// Wrong -- nested vertical scrolling
new ScrollView
{
	new VStack
	{
		new ScrollView { /* also vertical */ },
	}
}
```

If you need a scrollable list inside a scrollable page, use a ListView or
CollectionView instead. They handle virtualization and nested scrolling properly.
See the [Control Catalog](controls.md) for ListView and CollectionView usage.

### Unconstrained Stacks in ScrollView

A VStack inside a ScrollView is unconstrained vertically. Its children will be
measured with infinite available height. This is correct for scrollable content,
but be aware that `.FillVertical()` on a child inside this arrangement will not
produce the expected result -- there is no finite height to fill.

### Missing Frame on Image

Images without size constraints may collapse to zero or expand unexpectedly. Set
a frame:

```csharp
new Image("photo.jpg").Frame(width: 300, height: 200)
```

### Grid Without Cell Positions

Children added to a Grid without `.Cell()` calls will all stack in row 0,
column 0. Always specify positions:

```csharp
new Grid(columns: new object[] { "*", "*" })
{
	new Text("A").Cell(row: 0, column: 0),
	new Text("B").Cell(row: 0, column: 1),
}
```


## See Also

- [Control Catalog](controls.md) -- full reference for all child controls you
  can place inside layout containers.
- [Performance Optimization](performance.md) -- layout-related performance tips
  including diff algorithm cost and view tree depth.
- [Platform-Specific Guides](platform-guides.md) -- platform differences in
  layout behavior, safe areas, and density-independent measurement.
