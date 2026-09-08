namespace AndroidX.Compose;

/// <summary>
/// Identifies which Compose receiver-scope is currently active so
/// scope-extension calls (e.g. <c>Modifier.Weight</c>) can dispatch
/// to the right Kotlin helper. <see cref="RenderContext.PushScope"/>
/// publishes both the scope handle and its kind for the duration of
/// a container's content lambda.
/// </summary>
internal enum ScopeKind
{
    /// <summary>No scope is active.</summary>
    None = 0,

    /// <summary>
    /// The scope is an <c>androidx.compose.foundation.layout.RowScope</c>
    /// (or one of its conforming subtypes used by app-bar/navigation
    /// rows — anything that implements the <c>RowScope</c> interface).
    /// </summary>
    Row,

    /// <summary>
    /// The scope is an
    /// <c>androidx.compose.foundation.layout.ColumnScope</c>.
    /// </summary>
    Column,

    /// <summary>
    /// The scope is an
    /// <c>androidx.compose.foundation.layout.BoxScope</c>. Read by
    /// alignment-extension modifiers (<c>Modifier.Align</c>,
    /// <c>Modifier.MatchParentSize</c>) to dispatch through the right
    /// Kotlin interface method.
    /// </summary>
    Box,

    /// <summary>
    /// The scope is some other receiver type (e.g.
    /// <c>SingleChoiceSegmentedButtonRowScope</c>) that doesn't
    /// participate in Row/Column weight. Modifiers like
    /// <c>Weight</c> must not be used here.
    /// </summary>
    Other,
}
