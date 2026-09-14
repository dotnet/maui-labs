namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>SingleChoiceSegmentedButtonRow</c>. Container for
/// <see cref="SegmentedButton"/> children that exposes a
/// <c>SingleChoiceSegmentedButtonRowScope</c> receiver — the scope is
/// captured here and published via <see cref="RenderContext"/> so child
/// <see cref="SegmentedButton"/>s can pass it to the underlying
/// scope-extension Kotlin static. The container also publishes the
/// per-child row index so each segmented button can resolve its
/// <c>start</c> / <c>middle</c> / <c>end</c> shape.
/// <code>
/// new SingleChoiceSegmentedButtonRow
/// {
///     new SegmentedButton(selected: tab == 0, onClick: () =&gt; tab.Value = 0) { new Text("Day") },
///     new SegmentedButton(selected: tab == 1, onClick: () =&gt; tab.Value = 1) { new Text("Week") },
/// }
/// </code>
/// </summary>
public sealed partial class SingleChoiceSegmentedButtonRow;
