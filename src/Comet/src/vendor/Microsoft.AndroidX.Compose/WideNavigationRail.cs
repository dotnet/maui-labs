namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>WideNavigationRail</c> — adaptive vertical navigation
/// rail that shows item labels when expanded. Children are
/// <see cref="WideNavigationRailItem"/>s. Stripped from the binding
/// because the underlying composable is annotated
/// <c>@ExperimentalMaterial3ExpressiveApi</c>; reached via
/// <see cref="ComposeBridges"/>.
/// <code>
/// new WideNavigationRail
/// {
///     new WideNavigationRailItem(selected: tab == 0, onClick: () =&gt; tab.Value = 0)
///     {
///         Icon  = new Text("🏠"),
///         Label = new Text("Home"),
///     },
/// }
/// </code>
/// <c>WideNavigationRailItem</c> is a top-level static (not a scope
/// extension), so children render directly without a published scope.
/// </summary>
public sealed partial class WideNavigationRail;
