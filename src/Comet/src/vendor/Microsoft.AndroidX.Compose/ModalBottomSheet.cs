namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>ModalBottomSheet</c> — a modal sheet anchored to the
/// bottom of the screen. The sheet's lifecycle is gated by the caller:
/// keep visibility in a <see cref="MutableState{T}"/> and clear it in
/// <c>onDismissRequest</c>.
/// </summary>
/// <example>
/// <code>
/// var show = Remember(() =&gt; new MutableState&lt;bool&gt;(false));
/// show.Value
///     ? new ModalBottomSheet(onDismissRequest: () =&gt; show.Value = false)
///       {
///           new Column { new Text("Sheet contents") },
///       }
///     : null
/// </code>
/// </example>
/// <remarks>
/// To opt out of the half-expanded resting state, pass an explicit
/// <see cref="SheetStateHolder"/>:
/// <code>
/// var sheet = Remember(() =&gt; new SheetStateHolder(skipPartiallyExpanded: true));
/// new ModalBottomSheet(onDismissRequest: ..., sheetState: sheet)
/// {
///     ConfirmValueChange = v =&gt; !formIsDirty || v != SheetValue.Hidden,
///     new Column { ... },
/// }
/// </code>
/// </remarks>
public sealed partial class ModalBottomSheet;
