# Control and Component Catalog

This document catalogs every control available in Comet. Controls fall into two
categories: **generated** controls produced by the Roslyn source generator from
MAUI interface definitions, and **handwritten** controls implemented directly in
C#. Both kinds are used identically in application code.


## How Controls Work

Every Comet control inherits from `View`. Properties are exposed as
`Binding<T>` values, which accept either a literal or a `Func<T>` for automatic
reactive binding. Appearance and layout are configured through a fluent
extension-method API that stores values in the environment dictionary.

```csharp
new Text("Hello, Comet!")
	.Color(Colors.White)
	.FontSize(24)
	.Background(Colors.Blue)
	.Margin(16)
	.Frame(width: 200, height: 50)
```

The fluent calls can be chained in any order. Each one sets an environment key
that the handler reads when rendering the native view. See the
[Styling and Theming Guide](styling.md) for design tokens and theme-aware
styling.


## Generated Controls

The source generator reads `[assembly: CometGenerate(...)]` attributes in
`Controls/ControlsGenerator.cs` and produces `View` subclasses with typed
constructor parameters and `Binding<T>` properties. The generator output
includes extension methods and factory helpers.

### Text and Labels

#### Text (generated from ILabel)

Displays a read-only text label.

```csharp
// Static text
new Text("Welcome to Comet")

// Reactive text from state
new Text(() => $"Count: {count.Value}")
	.Color(Colors.Black)
	.FontSize(18)
	.HorizontalTextAlignment(TextAlignment.Center)
```

**Constructor:** `Text(Binding<string> value)`
**Key properties:** `Value` (text content), `MaxLines` (default 1)
**Maps to:** `LabelHandler`


### Input Controls

#### TextField (generated from IEntry)

Single-line text input field.

```csharp
new TextField(() => name.Value, "Enter your name")
	.Color(Colors.Black)
	.FontSize(16)
```

**Constructor:** `TextField(Binding<string> text, Binding<string> placeholder, Action completed = null)`
**Key properties:** `Text`, `Placeholder`, `MaxLength` (default -1 = unlimited)
**Maps to:** `EntryHandler`

#### SecureField (generated from IEntry)

Password entry field. Identical API to TextField but masks input.

```csharp
new SecureField(() => password.Value, "Password")
```

**Constructor:** `SecureField(Binding<string> text, Binding<string> placeholder, Action completed = null)`
**Key properties:** `Text`, `Placeholder`, `IsPassword` (always true), `MaxLength`
**Maps to:** `EntryHandler`

#### TextEditor (generated from IEditor)

Multi-line text editor.

```csharp
new TextEditor(() => notes.Value)
	.Frame(height: 200)
```

**Constructor:** `TextEditor(Binding<string> text)`
**Key properties:** `Text`, `MaxLength` (default -1)
**Maps to:** `EditorHandler`

#### SearchBar (generated from ISearchBar)

Text input with search button.

```csharp
new SearchBar(() => query.Value)
	.OnSearch(() => PerformSearch())
```

**Constructor:** `SearchBar(Binding<string> text, Action search = null)`
**Key properties:** `Text`, `MaxLength`
**Maps to:** `SearchBarHandler`


### Buttons

#### Button (generated from ITextButton)

Standard button with text label.

```csharp
new Button("Tap Me", () => count.Value++)
	.Color(Colors.White)
	.Background(Colors.Blue)
```

**Constructor:** `Button(Binding<string> text, Action clicked = null)`
**Key properties:** `Text`, `Clicked`
**Maps to:** `ButtonHandler`

#### ImageButton (generated from IImageButton)

Button that displays an image instead of text.

```csharp
new ImageButton("icon.png", () => HandleTap())
```

**Constructor:** `ImageButton(Binding<IImageSource> source, Action clicked = null)`
**Key properties:** `Source`, `Clicked`
**Maps to:** `ImageButtonHandler`


### Toggles and Selection

#### Toggle (generated from ISwitch)

On/off switch control.

```csharp
new Toggle(() => isEnabled.Value)
```

**Constructor:** `Toggle(Binding<bool> value)`
**Key properties:** `Value` (maps to `IsOn`)
**Maps to:** `SwitchHandler`

#### CheckBox (generated from ICheckBox)

Checkbox control.

```csharp
new CheckBox(() => isChecked.Value)
```

**Constructor:** `CheckBox(Binding<bool> isChecked)`
**Key properties:** `IsChecked`
**Maps to:** `CheckBoxHandler`

#### RadioButton (handwritten)

Radio button that must be placed inside a `RadioGroup`.

```csharp
new RadioGroup()
{
	new RadioButton("Option A", selected: true),
	new RadioButton("Option B"),
	new RadioButton("Option C"),
}
```

**Constructor:** `RadioButton(string label = null, bool selected = false, Action onClick = null)`
**Key properties:** `Label`, `Selected`, `GroupName`, `Value`
**Events:** `CheckedChanged`
**Maps to:** `RadioButtonHandler`

#### RadioGroup (handwritten)

Container that enforces mutual exclusion among child `RadioButton` controls.

```csharp
new RadioGroup(orientation: Orientation.Horizontal)
{
	new RadioButton("Yes", selected: true),
	new RadioButton("No"),
}
```

**Constructor:** `RadioGroup(Orientation orientation = Orientation.Vertical)`
**Key properties:** `GroupName`, `Orientation`
**Maps to:** `LayoutHandler`


### Value Controls

#### Slider (generated from ISlider)

Continuous value selection along a range.

```csharp
new Slider(() => volume.Value, 0, 1)
```

**Constructor:** `Slider(Binding<double> value, Binding<double> minimum = 0, Binding<double> maximum = 1)`
**Key properties:** `Value`, `Minimum`, `Maximum`
**Maps to:** `SliderHandler`

#### Stepper (generated from IStepper)

Incremental value control with plus/minus buttons.

```csharp
new Stepper(() => quantity.Value, 0, 100, 1)
```

**Constructor:** `Stepper(Binding<double> value, Binding<double> minimum, Binding<double> maximum, Binding<double> interval)`
**Key properties:** `Value`, `Minimum`, `Maximum`, `Interval`
**Maps to:** `StepperHandler`

#### ProgressBar (generated from IProgress)

Displays progress as a filled bar.

```csharp
new ProgressBar(() => downloadProgress.Value)
```

**Constructor:** `ProgressBar(Binding<double> value)`
**Key properties:** `Value` (0.0 to 1.0)
**Maps to:** `ProgressBarHandler`


### Pickers

#### DatePicker (generated from IDatePicker)

Date selection control.

```csharp
new DatePicker(() => selectedDate.Value)
```

**Constructor:** `DatePicker(Binding<DateTime> date, Binding<DateTime> minimumDate = null, Binding<DateTime> maximumDate = null)`
**Key properties:** `Date`, `MinimumDate`, `MaximumDate`
**Maps to:** `DatePickerHandler`

#### TimePicker (generated from ITimePicker)

Time selection control.

```csharp
new TimePicker(() => selectedTime.Value)
```

**Constructor:** `TimePicker(Binding<TimeSpan> time)`
**Key properties:** `Time`
**Maps to:** `TimePickerHandler`

#### Picker (handwritten)

Dropdown selection from a list of strings.

```csharp
new Picker(0, "Red", "Green", "Blue")
```

**Constructor:** `Picker(int selectedIndex, params string[] items)`
**Key properties:** `Items`, `SelectedIndex`, `Title`
**Maps to:** `PickerHandler`


### Indicators

#### ActivityIndicator (generated from IActivityIndicator)

Spinning activity indicator.

```csharp
new ActivityIndicator(() => isLoading.Value)
```

**Constructor:** `ActivityIndicator(Binding<bool> isRunning)`
**Key properties:** `IsRunning` (default true)
**Maps to:** `ActivityIndicatorHandler`

#### IndicatorView (generated from IIndicatorView)

Page indicator dots (typically paired with a CarouselView).

```csharp
new IndicatorView(() => pageCount.Value)
```

**Constructor:** `IndicatorView(Binding<int> count)`
**Key properties:** `Count`
**Maps to:** `IndicatorViewHandler`


### Media

#### Image (handwritten)

Displays an image from a file, URL, or image source.

```csharp
// From file name
new Image("logo.png")

// Reactive source
new Image(() => currentImage.Value)
	.Frame(width: 200, height: 200)
```

**Constructors:**
- `Image(string source)`
- `Image(IImageSource imageSource)`
- `Image(Func<string> source)`
- `Image(Func<IImageSource> bitmap)`

**Key properties:** `ImageSource`, `StringSource`
**Maps to:** `ImageHandler`

#### GraphicsView (handwritten)

Custom drawing surface using Microsoft.Maui.Graphics.

```csharp
new GraphicsView
{
	Draw = (canvas, rect) =>
	{
		canvas.FillColor = Colors.Blue;
		canvas.FillCircle(rect.Center, 50);
	}
}.Frame(width: 200, height: 200)
```

**Key properties:** `Draw`, `StartInteraction`, `EndInteraction`, `DragInteraction`
**Maps to:** `GraphicsViewHandler`


### Shapes

#### ShapeView (handwritten)

Renders a geometric shape (circle, rectangle, rounded rectangle, line, etc.).

```csharp
new ShapeView(new Circle())
	.Fill(Colors.Red)
	.Stroke(Colors.Black, 2)
	.Frame(width: 100, height: 100)
```

**Constructors:**
- `ShapeView(Shape value)`
- `ShapeView(Func<Shape> value)`

**Key properties:** `Shape`
**Maps to:** `ShapeViewHandler` (uses `GraphicsViewHandler` internally)


### Lists and Collections

#### ListView / ListView&lt;T&gt; (handwritten)

Virtualized scrolling list with section support.

```csharp
// Simple static list
new ListView
{
	new Text("Item 1"),
	new Text("Item 2"),
	new Text("Item 3"),
}

// Data-bound typed list
new ListView<string>(() => items.Value)
{
	ViewFor = item => new Text(item),
}
```

**Key properties:** `Header`, `Footer`, `ItemSelected`
**ListView&lt;T&gt; properties:** `ViewFor`, `ItemFor`, `Count`
**Maps to:** `ListViewHandler`

#### CollectionView / CollectionView&lt;T&gt; (handwritten)

Modern collection display with layout options, selection, and infinite scroll.

```csharp
var list = new CollectionView<TodoItem>(() => todos.Value)
{
	ViewFor = item => new HStack
	{
		new CheckBox(() => item.Done),
		new Text(() => item.Title),
	},
};
list.ItemsLayout = GridItemsLayout.Vertical(span: 2, spacing: 8);
list.SelectionMode = SelectionMode.Single;
```

**Key properties:** `ItemsLayout`, `EmptyView`, `SelectionMode`,
`ItemSizingStrategy`, `RemainingItemsThreshold`, `RemainingItemsThresholdReached`
**CollectionView&lt;T&gt; properties:** `ViewFor`, `SelectedItem`, `SelectedItems`,
`IsGrouped`, `GroupHeaderViewFor`, `GroupFooterViewFor`
**Maps to:** `CollectionViewHandler`

#### SectionedListView / SectionedListView&lt;T&gt; (handwritten)

List with section headers and footers.

```csharp
new SectionedListView
{
	new Section(header: new Text("Fruits"))
	{
		new Text("Apple"),
		new Text("Banana"),
	},
	new Section(header: new Text("Vegetables"))
	{
		new Text("Carrot"),
		new Text("Broccoli"),
	},
}
```

**Key properties:** Inherits from `ListView`. `SectionFor`, `SectionCount`.
**Maps to:** `ListViewHandler`

#### BindableLayout / BindableLayout&lt;T&gt; (handwritten)

Generates child views from a data collection inside any layout container.

```csharp
var layout = new BindableLayout<string>
{
	ItemsSource = names,
	ItemTemplate = name => new Text(name),
};
```

**Key properties:** `ItemsSource`, `ItemTemplate`
Inherits from `ContainerView`.


### Navigation

#### NavigationView (handwritten)

Stack-based navigation container with push/pop semantics.

```csharp
new NavigationView
{
	new MainPage()
}

// Push a new page from within a view:
NavigationView.Navigate(this, new DetailPage());

// Pop back:
NavigationView.Pop(this);
```

**Key properties:** `ToolbarItems`, `LeadingBarAction`, `LeadingBarIcon`
**Key methods:** `Navigate(View)`, `Navigate<TView>()`, `Pop()`, `PopToRoot()`
**Maps to:** `NavigationViewHandler`

#### TabView (handwritten)

Tab-based content switching.

```csharp
var tabs = new TabView();
tabs.AddTab("Home", new HomePage());
tabs.AddTab("Settings", new SettingsPage());
```

**Key properties:** `SelectedIndex`, `Tabs`, `CurrentTab`
**Events:** `SelectedIndexChanged`
**Maps to:** `TabViewHandler`

#### ModalView (handwritten)

Presents content as a modal overlay.

```csharp
ModalView.Present(new LoginPage());
ModalView.Dismiss();
```

**Static methods:** `Present(View)`, `Dismiss()`


### Containers

#### ContentView (handwritten)

Single-child content wrapper. Base class for NavigationView and other containers.

```csharp
new ContentView
{
	new Text("Wrapped content")
}
```

**Key properties:** `Content`

#### Border (handwritten)

Draws a border and background around a single child view.

```csharp
new Border
{
	new Text("Bordered content")
}
.Background(Colors.LightGray)
.ClipShape(new RoundedRectangle(8))
.Stroke(Colors.Gray, 1)
```

**Key properties:** `Content`, `Shape`, `Stroke`, `StrokeThickness`
**Maps to:** `LayoutHandler`

#### RefreshView (generated from IRefreshView)

Pull-to-refresh wrapper.

```csharp
new RefreshView(() => isRefreshing.Value)
```

**Constructor:** `RefreshView(Binding<bool> isRefreshing)`
**Key properties:** `IsRefreshing`
**Maps to:** `RefreshViewHandler`

#### SwipeView (handwritten)

Swipe-to-reveal actions on a content view.

```csharp
var swipe = new SwipeView
{
	new Text("Swipe me"),
};
swipe.RightItems = new SwipeItems
{
	new SwipeItem
	{
		Text = "Delete",
		BackgroundColor = Colors.Red,
		OnInvoked = () => Delete(),
	}
};
```

**Key properties:** `Content`, `LeftItems`, `RightItems`, `TopItems`, `BottomItems`
**Maps to:** `SwipeViewHandler`

#### ScrollView (handwritten)

Scrollable content wrapper. See the [Layout System Guide](layout.md) for details
on scroll orientation, nested scroll handling, and content sizing.

#### FlyoutView (generated from IFlyoutView)

Side-panel flyout navigation.

**Constructor:** `FlyoutView()`
**Key properties:** `FlyoutWidth`, `IsGestureEnabled`, `FlyoutBehavior`
**Maps to:** `FlyoutViewHandler`


### Utility

#### Spacer (handwritten)

Flexible space that fills available room in a stack layout.

```csharp
new HStack
{
	new Text("Left"),
	new Spacer(),
	new Text("Right"),
}
```

**Maps to:** `SpacerHandler`

#### Toolbar (generated from IToolbar)

Navigation toolbar (typically managed by NavigationView).

**Constructor:** `Toolbar(Binding<bool> backButtonVisible, Binding<bool> isVisible)`
**Maps to:** `ToolbarHandler`


### Application

#### CometApp (handwritten)

The application entry point. Extends `View` and implements `IApplication`.

```csharp
public class MyApp : CometApp
{
	[Body]
	View Body() => new NavigationView
	{
		new MainPage()
	};
}

// In MauiProgram.cs:
builder.UseCometApp<MyApp>();
```

**Key properties:** `CurrentApp`, `CurrentWindow`, `MauiContext`, `Windows`
**Maps to:** `ApplicationHandler`


## Fluent API Reference

All views support these common fluent extension methods:

| Method | Description |
|--------|-------------|
| `.Color(Color)` | Text/foreground color |
| `.Background(Paint)` | Background color or paint |
| `.FontSize(double)` | Font size |
| `.FontWeight(FontWeight)` | Font weight (Bold, Light, etc.) |
| `.Font(Font)` | Full font specification |
| `.Margin(float)` | Uniform margin |
| `.Margin(left, top, right, bottom)` | Individual edge margins |
| `.Padding(Thickness)` | Content padding |
| `.Frame(width, height)` | Fixed size constraints |
| `.FillHorizontal()` | Expand to fill parent width |
| `.FillVertical()` | Expand to fill parent height |
| `.Shadow(Shadow)` | Drop shadow |
| `.ClipShape(IShape)` | Clip to shape boundary |
| `.Stroke(Color, width)` | Border stroke |
| `.OnTapGesture(Action)` | Tap gesture handler |
| `.Tag(string)` | Automation ID for testing |
| `.HorizontalTextAlignment(TextAlignment)` | Horizontal text alignment |
| `.VerticalTextAlignment(TextAlignment)` | Vertical text alignment |


## Handler Mapping Summary

Every Comet control maps to a MAUI handler registered in
`AppHostBuilderExtensions.UseCometHandlers()`. The full mapping is listed in the
[Handler Architecture](handlers.md) documentation.


## See Also

- [Layout System](layout.md) -- VStack, HStack, ZStack, Grid, and other layout
  containers used to arrange controls.
- [Form Handling](forms.md) -- in-depth coverage of form controls, two-way
  binding, and validation patterns.
- [Styling and Theming](styling.md) -- design tokens, control styles, and
  theme-aware styling for controls.
- [Handler Architecture](handlers.md) -- how controls map to MAUI handlers and
  how to customize or create new handlers.
- [Animations and Gestures](animations.md) -- animate control properties and
  attach gesture recognizers.
