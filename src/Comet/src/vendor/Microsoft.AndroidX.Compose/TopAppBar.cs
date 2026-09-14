namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TopAppBar</c>. <c>Title</c> is required;
/// <c>Subtitle</c>, <c>NavigationIcon</c>, and <c>Actions</c> are
/// optional slots:
/// <code>
/// new TopAppBar
/// {
///     Title          = new Text("My App"),
///     Subtitle       = new Text("Inbox"),
///     NavigationIcon = new IconButton(onClick: ...) { new Text("☰") },
///     Actions        = new Row { new IconButton(...) { new Text("⋮") } },
/// }
/// </code>
/// When <c>Subtitle</c> is set, the bar is rendered via the newer
/// two-line <c>TopAppBar-cJHQLPU</c> overload; otherwise it uses the
/// single-line <c>TopAppBar-GHTll3U</c> overload. The branching is
/// driven by the <c>[ComposeFacade(BranchOn=..., AlternateBridge=...)]</c>
/// attribute on the corresponding bridge in <c>ComposeBridges.cs</c>.
///
/// Set <c>ScrollBehavior</c> to a value from
/// <see cref="TopAppBarDefaults"/> to make the bar elevate / collapse
/// in response to scrolling. The same behavior's
/// <see cref="AndroidX.Compose.Material3.ITopAppBarScrollBehavior.NestedScrollConnection"/> must
/// be wired to the scrolling container via
/// <see cref="Modifier.NestedScroll(AndroidX.Compose.UI.Input.NestedScroll.INestedScrollConnection)"/>.
/// </summary>
public sealed partial class TopAppBar;
