# Navigation Guide

Comet provides three navigation patterns that cover common app architectures:
stack-based push/pop navigation, tab-based organization, and Shell route-based
navigation with deep linking. Each pattern uses a declarative, code-only API
that integrates with the reactive state system.


## Stack Navigation with NavigationView

`NavigationView` wraps content in a platform navigation controller and provides
push/pop stack navigation. It extends `ContentView` and implements
`IStackNavigationView`.

### Basic Setup

Wrap a root view in a `NavigationView` to enable stack navigation:

```csharp
using Comet;
using static Comet.CometControls;

public class MyApp : View
{
	[Body]
	View body() =>
		NavigationView(
			new HomePage()
		).Title("My App");
}
```

### Pushing Views

Navigate forward by calling `Navigate()` on the `NavigationView` instance. Every
view inside a `NavigationView` receives a `Navigation` property that references
the enclosing navigation controller.

```csharp
public class HomePage : View
{
	[Body]
	View body() =>
		VStack(
			Text("Welcome"),
			Button("Go to Detail", () =>
			{
				Navigation?.Navigate(new DetailPage());
			})
		);
}
```

Generic overloads create the target view automatically:

```csharp
// Parameterless navigation
Navigation?.Navigate<DetailPage>();

// With an anonymous parameter object
Navigation?.Navigate<DetailPage>(new { ItemId = 42 });

// With strongly-typed parameters
Navigation?.Navigate<DetailPage, DetailParams>(
	new DetailParams { ItemId = 42 }
);
```

### Popping Views

Pop the current view from the stack:

```csharp
// Pop from within a pushed view
Navigation?.Pop();

// Pop to the root view, clearing the entire stack
Navigation?.PopToRoot();

// Static helpers that walk the parent tree
NavigationView.Pop(this);
NavigationView.PopToRoot(this);
```

### Convenience Extension Methods

`ViewExtensions` provides shorthand methods that eliminate explicit
`Navigation` property access:

```csharp
// Navigate on tap
myView.OnTapNavigate(() => new DetailPage());

// Navigate imperatively from any view
this.Navigate(new DetailPage());

// Dismiss the current view (pops or dismisses modal)
this.Dismiss();
```

### NavigationView API Reference

| Method | Description |
|--------|-------------|
| `Navigate(View view)` | Push a view onto the stack |
| `Navigate<TView>()` | Push a new instance of TView |
| `Navigate<TView>(object parameters)` | Push with parameter injection |
| `Pop()` | Remove the top view from the stack |
| `PopToRoot()` | Remove all views except the root |
| `SetPerformNavigate(Action<View>)` | Override the navigate behavior |
| `SetPerformPop(Action)` | Override the pop behavior |
| `SetPerformContentReset(Action<View>)` | Override the pop-to-root behavior |

Properties:

| Property | Type | Description |
|----------|------|-------------|
| `LeadingBarAction` | `Action` | Callback for the leading bar button |
| `LeadingBarIcon` | `string` | Icon for the leading bar button (default "&#9776;") |
| `ToolbarItems` | `List<ToolbarItem>` | Toolbar items in the navigation bar |


## Tab Navigation with TabView

`TabView` organizes content into tabs. It extends `ContainerView` and manages
a collection of `TabItem` objects.

### Basic Tab Setup

```csharp
public class MyApp : View
{
	[Body]
	View body()
	{
		var tabs = new TabView();
		tabs.AddTab("Home", new HomePage());
		tabs.AddTab("Settings", new SettingsPage());
		return tabs;
	}
}
```

### Tab Metadata with Extension Methods

Set tab icons and labels using the fluent `TabViewExtensions`:

```csharp
public class MyApp : View
{
	[Body]
	View body()
	{
		var tabs = new TabView();
		tabs.AddTab("Home",
			new HomePage()
				.TabIcon("house.fill")
				.TabText("Home")
		);
		tabs.AddTab("Profile",
			new ProfilePage()
				.Tab("Profile", "person.fill")
		);
		return tabs;
	}
}
```

Extension methods available on any `View`:

| Method | Description |
|--------|-------------|
| `TabIcon(string image)` | Set the tab icon |
| `TabText(string text)` | Set the tab label |
| `Tab(string text, string image)` | Set both label and icon |

### Responding to Tab Changes

```csharp
var tabs = new TabView();
tabs.SelectedIndexChanged = (index) =>
{
	Console.WriteLine($"Switched to tab {index}");
};
```

### TabView API Reference

| Property | Type | Description |
|----------|------|-------------|
| `SelectedIndex` | `int` | Currently selected tab (get/set) |
| `CurrentTab` | `TabItem` | The active tab item |
| `Tabs` | `IReadOnlyList<TabItem>` | All registered tabs |
| `SelectedIndexChanged` | `Action<int>` | Callback on tab change |

`TabItem` properties: `Title`, `Content`, `Icon`, `BadgeValue`.


## Shell Navigation with CometShell

`CometShell` provides URI-based navigation with route registration, query
parameter passing, flyout menus, and deep linking support. It follows the same
patterns as .NET MAUI Shell but with a code-only API.

### Route Registration

Register routes before navigating:

```csharp
CometShell.RegisterRoute<HomePage>("home");
CometShell.RegisterRoute<DetailPage>("detail");
CometShell.RegisterRoute<SettingsPage>("settings");
```

### Building a Shell

Construct a `CometShell` with `ShellItem`, `ShellSection`, and `ShellContent`
objects. The fluent builder pattern allows chaining:

```csharp
public class MyApp : View
{
	[Body]
	View body() =>
		new CometShell(
			new ShellItem("Main",
				new ShellSection("Home",
					new ShellContent("Home", () => new HomePage())
				),
				new ShellSection("Settings",
					new ShellContent("Settings", () => new SettingsPage())
				)
			)
		);
}
```

Using the builder API:

```csharp
var shell = new CometShell()
	.AddItem("Browse", item => item
		.AddSection("Products", section => section
			.AddContent<ProductListPage>("Products", "products")
			.AddContent<CartPage>("Cart", "cart")
		)
	)
	.AddItem("Account", item => item
		.AddSection("Profile", section => section
			.AddContent<ProfilePage>("Profile", "profile")
		)
	);
```

### Navigating with Routes

Navigate using type-safe generics or string routes. Extension methods on `View`
delegate to `CometShell.Current`:

```csharp
// Type-safe navigation
await this.GoToAsync<DetailPage>();

// With parameters (applied via NavigationParameterHelper)
await this.GoToAsync<DetailPage>(new { ItemId = 42, Name = "Widget" });

// String route with query parameters
await this.GoToAsync("detail?ItemId=42&Name=Widget");

// Navigate back
await this.GoBackAsync();
```

### Receiving Navigation Parameters

Implement `IQueryAttributable` to receive query parameters on the target view:

```csharp
public class DetailPage : View, IQueryAttributable
{
	readonly State<string> itemName = "";
	readonly State<int> itemId = 0;

	public void ApplyQueryAttributes(Dictionary<string, string> query)
	{
		if (query.TryGetValue("ItemId", out var id))
			itemId.Value = int.Parse(id);
		if (query.TryGetValue("Name", out var name))
			itemName.Value = name;
	}

	[Body]
	View body() =>
		VStack(
			Text($"Item: {itemName.Value}"),
			Text($"ID: {itemId.Value}")
		);
}
```

### CometShell API Reference

**Route management (static):**

| Method | Description |
|--------|-------------|
| `RegisterRoute(string, Type)` | Register a route to a View type |
| `RegisterRoute<TView>(string)` | Generic route registration |
| `UnregisterRoute(string)` | Remove a registered route |
| `HasRoute(string)` | Check if a string route exists |
| `HasRoute<TView>()` | Check if a type route exists |
| `GetRoute<TView>()` | Get the route string for a type |
| `ParseQueryString(string)` | Parse query parameters from a route |

**Navigation (instance):**

| Method | Description |
|--------|-------------|
| `GoToAsync<TView>()` | Navigate by view type |
| `GoToAsync<TView>(object)` | Navigate with parameters |
| `GoToAsync(string)` | Navigate by route string |

**Builder (instance, returns `CometShell`):**

| Method | Description |
|--------|-------------|
| `AddItem(ShellItem)` | Add a navigation item |
| `AddItem(string, Action<ShellItem>)` | Add with configuration callback |
| `WithCurrentItem(ShellItem)` | Set the active item |
| `WithFlyoutHeader(ShellItem)` | Set the flyout header |
| `ShowFlyout(bool)` | Toggle flyout visibility |
| `WithSearchHandler(SearchHandler)` | Set search handler |


## Modal Presentation

Present views modally using the `ModalView` static API or the implicit modal
fallback in `NavigationView`:

```csharp
// Present a view modally
ModalView.Present(new LoginPage());

// Dismiss the current modal
ModalView.Dismiss();
```

When `NavigationView.Navigate(fromView, view)` is called and the source view
has no `NavigationView` parent, it falls back to modal presentation
automatically. If the target view is a `ModalView`, it is always presented
modally regardless of the navigation context.


## Back Button Customization

Customize the back button in Shell navigation using `BackButtonBehavior`:

```csharp
public class CheckoutPage : View
{
	[Body]
	View body() =>
		VStack(
			Text("Checkout")
		)
		.BackButtonBehavior(new BackButtonBehavior
		{
			IsVisible = true,
			Title = "Cancel",
			Command = new Command(() => this.GoBackAsync()),
			IconOverride = "arrow.left"
		});
}
```

`BackButtonBehavior` properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IsVisible` | `bool` | `true` | Show or hide the back button |
| `IsEnabled` | `bool` | `true` | Enable or disable the back button |
| `Title` | `string` | `null` | Custom back button text |
| `Command` | `ICommand` | `null` | Custom command on back press |
| `CommandParameter` | `object` | `null` | Parameter for the command |
| `IconOverride` | `string` | `null` | Custom back icon |


## Data Passing Between Views

Comet supports three approaches to passing data between views.

### Constructor Parameters

The simplest approach for strongly-typed data:

```csharp
public class DetailPage : View
{
	readonly string _itemName;
	readonly int _itemId;

	public DetailPage(string name, int id)
	{
		_itemName = name;
		_itemId = id;
	}

	[Body]
	View body() =>
		VStack(
			Text(_itemName),
			Text($"ID: {_itemId}")
		);
}

// Navigate with constructor parameters
Navigation?.Navigate(new DetailPage("Widget", 42));
```

### Navigation Parameters

Anonymous objects or dictionaries are applied via reflection. The
`NavigationParameterHelper` sets matching public properties on the target view:

```csharp
Navigation?.Navigate<ProductPage>(new { ProductId = 7, Category = "Tools" });
```

This is also how `CometShell.GoToAsync` passes data. The helper supports
objects with public properties, `IEnumerable<KeyValuePair<string, string>>`,
and `IQueryAttributable` targets.

### Shared State

For views that need to observe the same data, share a `State<T>` or
`BindingObject` reference. For a comprehensive guide to shared state patterns,
see the [Reactive State Guide](reactive-state-guide.md).

```csharp
public class AppState : BindingObject
{
	public string UserName
	{
		get => GetProperty<string>();
		set => SetProperty(value);
	}
}

// Pass shared state to child views
var state = new AppState();
Navigation?.Navigate(new ProfilePage(state));
```


## Adaptive Navigation

The CometControlsGallery sample demonstrates adaptive navigation that switches
between a sidebar layout on desktop and stack navigation on phone.

The key pattern: check `DeviceInfo.Idiom` and return a different layout:

```csharp
public class AdaptiveLayout : View
{
	readonly Reactive<int> selectedIndex = 0;
	NavigationView _phoneNav;

	[Body]
	View body()
	{
		if (DeviceInfo.Idiom == DeviceIdiom.Phone)
			return BuildPhoneLayout();
		return BuildDesktopLayout();
	}

	View BuildDesktopLayout()
	{
		var sidebar = BuildSidebar();
		var detail = pages[selectedIndex.Value].CreatePage();

		return Grid(
			new object[] { 280, 1, "*" },
			null,
			sidebar.Cell(row: 0, column: 0),
			new Spacer()
				.Background(Colors.Grey)
				.Opacity(0.3f)
				.Cell(row: 0, column: 1),
			NavigationView(detail)
				.Title(pages[selectedIndex.Value].Title)
				.Cell(row: 0, column: 2)
		);
	}

	View BuildPhoneLayout()
	{
		var pageList = BuildPageList();
		_phoneNav = NavigationView(pageList)
			.Title("My App");
		return _phoneNav;
	}

	View BuildPageList() =>
		ScrollView(
			VStack(0,
				pages.Select((page, i) =>
					HStack(12,
						Text(page.Title).FontSize(16),
						new Spacer(),
						Text("\u203A").FontSize(18).Color(Colors.Grey)
					)
					.Padding(new Thickness(16, 12))
					.OnTap(v =>
					{
						var detail = pages[i].CreatePage()
							.Title(pages[i].Title);
						_phoneNav?.Navigate(detail);
					})
				).ToArray()
			)
		);
}
```

On desktop, the sidebar drives a `Reactive<int>` signal that triggers a body
rebuild to swap the detail pane. On phone, tap gestures push views onto a
`NavigationView` stack. The platform adapts at runtime -- no conditional
compilation needed.


## Combining Navigation Patterns

Navigation patterns compose naturally. A `TabView` can contain
`NavigationView` instances, and Shell navigation can present modals:

```csharp
public class MainApp : View
{
	[Body]
	View body()
	{
		var tabs = new TabView();

		tabs.AddTab("Browse",
			NavigationView(new BrowsePage())
				.Title("Browse")
				.TabIcon("list.bullet")
		);

		tabs.AddTab("Account",
			NavigationView(new AccountPage())
				.Title("Account")
				.TabIcon("person")
		);

		return tabs;
	}
}
```

Each tab maintains its own navigation stack. Pushing a view inside one tab does
not affect the other tabs.

For details on NavigationView and TabView APIs, see the
[Control Catalog](controls.md). For platform-specific navigation differences,
see the [Platform-Specific Guides](platform-guides.md). To integrate Shell
navigation with MAUI, see the [MAUI Integration Guide](maui-interop.md).


## See Also

- [Control Catalog](controls.md) -- NavigationView, TabView, and ModalView API
  reference with code examples.
- [Platform-Specific Guides](platform-guides.md) -- platform differences in
  navigation bar behavior and safe area handling.
- [MAUI Integration Guide](maui-interop.md) -- using CometShell alongside MAUI
  Shell and embedding Comet views in MAUI navigation pages.
- [Reactive State Guide](reactive-state-guide.md) -- how reactive state
  integrates with navigation parameters and shared state patterns.
