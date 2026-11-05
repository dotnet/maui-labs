namespace AndroidX.Compose;

/// <summary>
/// Intercepts the system back press from within a composition. Wraps
/// Kotlin's <c>androidx.activity.compose.BackHandler(enabled, onBack)</c>:
/// while this composable is active and <c>enabled</c> is
/// <c>true</c>, the activity's <c>OnBackPressedDispatcher</c> routes the
/// next back gesture to <c>onBack</c> instead of popping the
/// activity / navigator. The registration is added and removed
/// automatically as the composable enters and leaves the composition.
///
/// <para>
/// Use to collapse an in-pane selection / detail view before letting the
/// back press escape, or to confirm a destructive action:
/// </para>
/// <code>
/// var detailOpen = Remember(() =&gt; new MutableState&lt;bool&gt;(false));
///
/// new Column
/// {
///     new BackHandler(
///         onBack:  () =&gt; detailOpen.Value = false,
///         enabled: detailOpen.Value),
///     // ...detail pane...
/// };
/// </code>
/// </summary>
public sealed partial class BackHandler
{
    /// <summary>
    /// Convenience constructor that always intercepts back presses
    /// (<c>enabled: true</c>). Equivalent to
    /// <c>new BackHandler(onBack, enabled: true)</c>.
    /// </summary>
    public BackHandler(Action onBack) : this(onBack, enabled: true) { }
}
