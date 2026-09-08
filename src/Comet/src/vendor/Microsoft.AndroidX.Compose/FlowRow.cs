namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>FlowRow</c> — like <see cref="Row"/>, but children
/// that overflow the row's width wrap onto a new line below. Use for
/// dynamic chip groups, tag clouds, or any horizontal layout where the
/// number of items is unknown at compile time.
///
/// <code>
/// new FlowRow
/// {
///     new AssistChip(onClick: ...) { Text = new Text("Music") },
///     new AssistChip(onClick: ...) { Text = new Text("Movies") },
///     // …more chips, will wrap when out of horizontal space.
/// }
/// </code>
///
/// v1 wires up the simplest 7-param Kotlin overload (no
/// <c>FlowRowOverflow</c> handle and no scope-receiver helpers for
/// <c>fillMaxRowHeight</c>). Because <c>FlowRowScope</c> extends
/// <c>RowScope</c>, scope-aware modifiers like
/// <see cref="Modifier.Weight(float, bool)"/> and
/// <see cref="Modifier.Align(Alignment.Vertical)"/> work on children
/// here exactly as they do inside a plain <see cref="Row"/>. The
/// overflow indicator slot remains a follow-up.
/// </summary>
public sealed partial class FlowRow;
