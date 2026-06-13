namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>FlowColumn</c> — vertical mirror of <see cref="FlowRow"/>.
/// Children are stacked top-to-bottom in the first column; once a
/// column runs out of vertical space, subsequent children flow into a
/// new column to the right.
///
/// <code>
/// new FlowColumn
/// {
///     new Text("Item A"),
///     new Text("Item B"),
///     // …more items, will start a new column when out of vertical space.
/// }
/// </code>
///
/// As with <see cref="FlowRow"/>, the v1 facade uses the simpler
/// 7-param overload — no <c>FlowColumnOverflow</c> slot and no
/// scope-receiver helper for <c>fillMaxColumnWidth</c>. Because
/// <c>FlowColumnScope</c> extends <c>ColumnScope</c>, scope-aware
/// modifiers like <see cref="Modifier.Weight(float, bool)"/> and
/// <see cref="Modifier.Align(Alignment.Horizontal)"/> work on
/// children here exactly as they do inside a plain
/// <see cref="Column"/>.
/// </summary>
public sealed partial class FlowColumn;
