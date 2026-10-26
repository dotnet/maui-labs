namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>FlexibleBottomAppBar</c>. Like <see cref="BottomAppBar"/>
/// but with a fully customizable expanded height and horizontal
/// arrangement. The content slot is laid out in a <c>RowScope</c> and
/// filled from this bar's children:
/// <code>
/// new FlexibleBottomAppBar
/// {
///     new IconButton(onClick: ...) { new Icon(painter, "Search") },
///     new IconButton(onClick: ...) { new Icon(painter, "Settings") },
/// }
/// </code>
/// </summary>
public sealed partial class FlexibleBottomAppBar;
