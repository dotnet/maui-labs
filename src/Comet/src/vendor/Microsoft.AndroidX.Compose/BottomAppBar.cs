namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>BottomAppBar</c>. The actions slot is laid out in a
/// <c>RowScope</c> and filled from this bar's children;
/// <see cref="FloatingActionButton"/> is an optional
/// trailing slot:
/// <code>
/// new BottomAppBar
/// {
///     FloatingActionButton = new FloatingActionButton(onClick: ...) { ... },
///
///     new IconButton(onClick: ...) { new Icon(painter, "Search") },
///     new IconButton(onClick: ...) { new Icon(painter, "Settings") },
/// }
/// </code>
/// </summary>
public sealed partial class BottomAppBar;
