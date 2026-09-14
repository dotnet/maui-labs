namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ModalWideNavigationRail</c> — the modal-overlay
/// variant of <see cref="WideNavigationRail"/>. Drawn on top of the
/// host screen with a scrim, suitable for narrow-width adaptive
/// layouts. Children flow into the rail body; <c>Header</c> is an
/// optional slot drawn above them.
/// </summary>
/// <remarks>
/// <para>The Kotlin signature has no <c>onDismissRequest</c> callback —
/// dismissal is driven internally by the rail's
/// <see cref="WideNavigationRailState"/>. Because the live
/// <c>expand</c>/<c>collapse</c>/<c>snapTo</c> operations are Kotlin
/// suspend functions and the facade does not yet have a coroutine-scope
/// story, the rail is wired with <c>hideOnCollapse = true</c> (its
/// content visually unmounts on collapse) and the C# facade has no
/// way to observe the collapse. Drive open/close from a separate
/// <see cref="MutableState{T}"/>-of-<see cref="bool"/> gate by mirroring
/// the <see cref="ModalBottomSheet"/> pattern.</para>
/// <code>
/// var show = Remember(() =&gt; new MutableState&lt;bool&gt;(false));
/// var tab  = Remember(() =&gt; new MutableNumberState&lt;int&gt;(0));
/// new Row
/// {
///     new Button(onClick: () =&gt; show.Value = true) { new Text("Menu") },
///     show.Value
///         ? new ModalWideNavigationRail
///         {
///             Header = new Text("Sections"),
///             new WideNavigationRailItem(selected: tab.Value == 0,
///                 onClick: () =&gt; { tab.Value = 0; show.Value = false; })
///             {
///                 Icon  = new Text("🏠"),
///                 Label = new Text("Home"),
///             },
///         }
///         : null,
/// };
/// </code>
/// </remarks>
public sealed partial class ModalWideNavigationRail;
