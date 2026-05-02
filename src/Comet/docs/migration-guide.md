# Comet Migration Guide: classic surface → evolved MVU API

This guide shows how to move from the prior Comet surface to the evolved MVU API **without renaming the project**. The framework, package, and namespace remain **Comet**.

## Keep these pieces the same

- `using Comet;`
- `builder.UseCometApp<TApp>()`
- your project and package naming (`Comet`, not a rename)

What changes is the page/component authoring style.

## API mapping

| Classic Comet surface | Evolved Comet surface |
| --- | --- |
| `View` + `[Body]` | `Component` + `Render()` |
| `State<T>` fields inside pages | `Component<TState>.State` + `SetState(...)` |
| Constructor-created `Body = Build;` | `Render()` returning the view tree |
| Ad-hoc constructor parameters | `Component<TState, TProps>` with typed props |
| `Navigation.Navigate(new DetailPage(...))` | `Navigation.Navigate<DetailPage>(new DetailProps { ... })` |
| String routes at call sites | `CometShell.RegisterRoute<TView>("route")` + `GoToAsync<TView>(props)` |

## 1. Counter page: before and after

### Classic `[Body]` page

```csharp
public class CounterPage : View
{
readonly State<int> count = 0;

[Body]
View body() =>
new VStack
{
new Text(() => $"Count: {count.Value}"),
new Button("Increment", () => count.Value++),
};
}
```

### Evolved component page

```csharp
class CounterState
{
public int Count { get; set; }
}

public class CounterPage : Component<CounterState>
{
public override View Render() =>
new VStack
{
new Text($"Count: {State.Count}"),
new Button("Increment", () => SetState(s => s.Count++)),
};
}
```

When to use this:

- the page owns local mutable state
- you want a single mutation entry point (`SetState`)
- you want state shape documented in one typed state object

See the runnable sample in [`sample/CometMauiApp`](../sample/CometMauiApp/README.md).

## 2. Props-driven detail pages

Classic Comet often passed detail data through constructors or query strings. The evolved surface lets detail pages declare typed props:

```csharp
class BeanDetailState
{
public bool ShowRecentShots { get; set; } = true;
}

public class BeanDetailProps
{
public int BeanId { get; set; }
public string Source { get; set; } = "Dashboard";
}

public class BeanDetailPage : Component<BeanDetailState, BeanDetailProps>
{
public override View Render() => new Text($"Bean: {Props.BeanId}");
}
```

Navigate with typed props:

```csharp
Navigation.Navigate<BeanDetailPage>(new BeanDetailProps
{
BeanId = bean.Id,
Source = "Coffee Lab",
});
```

See the full pattern in [`sample/CometBaristaNotes/Pages/CoffeeBeanDetailPage.cs`](../sample/CometBaristaNotes/Pages/CoffeeBeanDetailPage.cs).

## 3. Shell routing, when you want URL-style navigation

If your app already uses the Comet shell wrapper, keep it and register typed routes:

```csharp
CometShell.RegisterRoute<ProjectDetailPage>("project-detail");
await new Button("Open").GoToAsync<ProjectDetailPage>(new ProjectDetailProps
{
Id = 42,
});
```

Use this when route registration is useful across tabs, flyouts, or deep links. Use `Navigation.Navigate<TView>(props)` when you are already inside a `NavigationView` and do not need route strings.

## 4. Reactive values still work

`Reactive<T>` is the forward-facing alias for lightweight reactive values.
For a full treatment of all state primitives, see the
[Reactive State Guide](reactive-state-guide.md).

```csharp
readonly Reactive<string> status = "Ready";

new Text(() => status.Value);
```

Use it for small UI notes, filters, or values that do not need to live inside your typed component state object.

## 5. You can migrate incrementally

You do **not** need to rewrite the whole app in one pass.

- keep legacy `[Body]` pages where they still fit
- introduce `Component<TState>` for new pages
- introduce `Component<TState, TProps>` for new detail flows
- keep `CometApp` as the app root because `UseCometApp<TApp>()` still expects an `IApplication`

The coffee sample demonstrates exactly this incremental approach:

- dashboard and detail pages use the evolved component surface
- the shot logger keeps its existing interop-heavy view implementation and is launched through navigation instead of an eagerly-created root tab
- the top-level sample tabs use `TabView` today because it has active handlers across the supported sample platforms
- the app root stays `CometApp`

Two more migration-ready references now build on that same idea:

- `sample/CometTaskApp` keeps its broader tabbed sample shape, but now uses `CollectionView` for the main task list and a typed-props `TaskDetailPage : Component<TaskDetailState, TaskDetailProps>` for detail/edit flows.
- `sample/CometAllTheLists` drops the MAUI Shell host wrapper in favor of a direct `CometApp` + `TabView` sample shell, while modernizing the inbox flow onto `CollectionView`.

## 6. MAUI 10 guardrails while migrating

As you update sample code, follow these
[MAUI 10 guardrails](troubleshooting.md) to avoid deprecated APIs:

- prefer `Border` over `Frame`
- prefer `CollectionView` over `ListView` for new data lists
- use `MainThread` instead of `Device.*`
- use `DisplayAlertAsync` / `DisplayActionSheetAsync` instead of `DisplayAlert` / `DisplayActionSheet`
- avoid `Compatibility` APIs and namespaces in new code

## 7. Validation flow used for these samples

Run the documented build order from the repository root:

```bash
dotnet build src/Comet.SourceGenerator/Comet.SourceGenerator.csproj -c Release
dotnet build src/Comet/Comet.csproj -c Release
dotnet build tests/Comet.Tests/Comet.Tests.csproj -c Release
dotnet build sample/CometMauiApp/CometMauiApp.csproj -c Release -f net10.0-maccatalyst
dotnet build sample/CometBaristaNotes/CometBaristaNotes.csproj -c Release -f net10.0-maccatalyst
```

For component-surface confidence, run the focused component tests after building:

```bash
dotnet test tests/Comet.Tests/Comet.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~Component"
```


## See Also

- [Reactive State Guide](reactive-state-guide.md) -- comprehensive guide to the
  new Signal, Computed, and Component patterns referenced in this migration.
- [Changelog](changelog.md) -- detailed list of everything that changed,
  including breaking changes and deprecations.
- [Troubleshooting](troubleshooting.md) -- solutions for common issues
  encountered during migration, including state update and binding problems.
