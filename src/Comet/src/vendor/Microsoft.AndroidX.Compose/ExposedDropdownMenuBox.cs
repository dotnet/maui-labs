namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ExposedDropdownMenuBox</c> — the layout container that
/// anchors an <see cref="ExposedDropdownMenu"/> popup to a
/// <see cref="TextField"/> (or <see cref="OutlinedTextField"/>). Pair
/// with a <see cref="MutableState{T}"/> to drive the expanded/collapsed
/// state:
/// <code>
/// var open     = Remember(() =&gt; new MutableState&lt;bool&gt;(false));
/// var selected = Remember(() =&gt; new MutableState&lt;string&gt;("Apple"));
///
/// new ExposedDropdownMenuBox(expanded: open.Value, onExpandedChange: v =&gt; open.Value = v)
/// {
///     new TextField(value: selected.Value, onValueChange: _ =&gt; { })
///     {
///         Label = new Text("Fruit"),
///     },
///     new ExposedDropdownMenu(expanded: open.Value, onDismissRequest: () =&gt; open.Value = false)
///     {
///         new DropdownMenuItem(text: new Text("Apple"),  onClick: () =&gt; { selected.Value = "Apple";  open.Value = false; }),
///         new DropdownMenuItem(text: new Text("Banana"), onClick: () =&gt; { selected.Value = "Banana"; open.Value = false; }),
///     },
/// }
/// </code>
/// </summary>
/// <remarks>
/// In Material 3 1.4.0.3 the <c>menuAnchor()</c> modifier (which wires
/// the textfield's tap/keyboard events to the box's expansion) is
/// available only on the <see cref="ExposedDropdownMenuBox"/> Kotlin
/// receiver scope, and binding it from C# is tracked separately. For
/// now, drive the expanded state from <c>onExpandedChange</c> directly
/// or from a button callback.
/// </remarks>
public sealed partial class ExposedDropdownMenuBox;
