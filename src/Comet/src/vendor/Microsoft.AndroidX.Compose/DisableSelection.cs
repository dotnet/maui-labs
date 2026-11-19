namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>DisableSelection</c> — opts a subtree out of an
/// enclosing <see cref="SelectionContainer"/>. Use this for content
/// inside a selectable region that should remain non-selectable
/// (e.g. action labels or chrome embedded in a body of selectable
/// text).
/// <code>
/// new SelectionContainer
/// {
///     new Text("Selectable body."),
///     new DisableSelection { new Text("Not selectable.") },
/// }
/// </code>
/// Has no effect when used outside a <see cref="SelectionContainer"/>.
/// </summary>
public sealed partial class DisableSelection;
