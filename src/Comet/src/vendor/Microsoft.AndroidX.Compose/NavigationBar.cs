namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>NavigationBar</c>. Container for
/// <see cref="NavigationBarItem"/> children laid out horizontally:
/// <code>
/// new NavigationBar
/// {
///     new NavigationBarItem(selected: tab == 0, onClick: () =&gt; tab.Value = 0)
///     {
///         Icon = new Text("🏠"), Label = new Text("Home"),
///     },
///     new NavigationBarItem(selected: tab == 1, onClick: () =&gt; tab.Value = 1)
///     {
///         Icon = new Text("⚙"), Label = new Text("Settings"),
///     },
/// }
/// </code>
/// The <c>RowScope</c> receiver is published via
/// <see cref="RenderContext"/> so child <c>NavigationBarItem</c>s can
/// pass it to their <c>RowScope</c>-extension static.
/// </summary>
public sealed partial class NavigationBar;
