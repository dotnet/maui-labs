namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TabRow</c>. Container for fixed-width
/// <see cref="Tab"/> / <see cref="LeadingIconTab"/> children that
/// distribute across the available width:
/// <code>
/// new TabRow(selectedTabIndex: tab.Value)
/// {
///     new Tab(selected: tab.Value == 0, onClick: () =&gt; tab.Value = 0)
///     {
///         Text = new Text("Home"),
///     },
///     new Tab(selected: tab.Value == 1, onClick: () =&gt; tab.Value = 1)
///     {
///         Text = new Text("Settings"),
///     },
/// }
/// </code>
/// </summary>
public sealed partial class TabRow;
