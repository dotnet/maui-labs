namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>DropdownMenu</c> — the popup that anchors
/// <see cref="DropdownMenuItem"/> children. Pair with a trigger
/// (commonly an <see cref="IconButton"/>) and a <see cref="MutableState{T}"/>
/// to control visibility:
/// <code>
/// var open = Remember(() =&gt; new MutableState&lt;bool&gt;(false));
///
/// new Box
/// {
///     new IconButton(onClick: () =&gt; open.Value = true)
///         { new Text("⋮") },
///     new DropdownMenu(expanded: open.Value, onDismissRequest: () =&gt; open.Value = false)
///     {
///         new DropdownMenuItem(
///             text:    new Text("Refresh"),
///             onClick: () =&gt; { open.Value = false; ... }),
///     },
/// }
/// </code>
/// </summary>
public sealed partial class DropdownMenu;
