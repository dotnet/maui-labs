namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>TriStateCheckbox</c> — a checkbox with an
/// indeterminate state in addition to checked/unchecked. The
/// <c>ToggleableState</c> values are <c>On</c>, <c>Off</c>, and
/// <c>Indeterminate</c>:
/// <code>
/// var state = Remember.Of(ToggleableState.Indeterminate);
/// new TriStateCheckbox(
///     state: state.Value,
///     onClick: () =&gt; state.Value = ToggleableState.On)
/// </code>
/// </summary>
public sealed partial class TriStateCheckbox;
