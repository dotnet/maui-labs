using System.Runtime.CompilerServices;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    /// <summary>
    /// Construct an <c>androidx.compose.ui.text.input.TextFieldValue</c>
    /// — the text + caret-selection + IME-composition triple that drives
    /// the <see cref="TextField(MutableState{AndroidX.Compose.UI.Text.Input.TextFieldValue})"/>
    /// overload. Hand-bridged because the Kotlin primary ctor is stripped
    /// from the binding (the <c>selection: TextRange</c> param is a
    /// <c>@JvmInline value class</c>; see issue #204).
    /// </summary>
    public static AndroidX.Compose.UI.Text.Input.TextFieldValue NewTextFieldValue(
        string text = "",
        long selection = 0L,
        AndroidX.Compose.UI.Text.TextRange? composition = null)
        => ComposeBridges.NewTextFieldValueImpl(text, selection, composition);

    /// <summary>
    /// Convenience overload: <c>composer.NewTextFieldValue(...)</c> for
    /// call sites already inside a composable that prefer the same
    /// dotted-on-the-composer shape as <see cref="Remember{T}(IComposer, Func{T}, int, string)"/>.
    /// The composer is unused — this just forwards to
    /// <see cref="NewTextFieldValue(string, long, AndroidX.Compose.UI.Text.TextRange?)"/>
    /// — but it keeps demos consistent with the "everything Compose-related
    /// hangs off <c>c</c>" idiom. Outside composition (button callbacks,
    /// ViewModels) call the static form directly, exactly as Kotlin's
    /// <c>TextFieldValue("")</c> ctor isn't <c>@Composable</c>.
    /// </summary>
    public static AndroidX.Compose.UI.Text.Input.TextFieldValue NewTextFieldValue(
        this IComposer composer,
        string text = "",
        long selection = 0L,
        AndroidX.Compose.UI.Text.TextRange? composition = null)
        => NewTextFieldValue(text, selection, composition);

    /// <summary>
    /// Compose's <c>derivedStateOf { calculation() }</c>: returns a
    /// read-only <see cref="DerivedState{T}"/> whose value is lazily
    /// computed by <paramref name="calculation"/>. Compose tracks which
    /// state values <paramref name="calculation"/> reads and only re-runs
    /// it when one of them changes. Cache the returned instance via
    /// <see cref="Remember{T}(IComposer, Func{T}, int, string)"/> so it
    /// survives recomposition.
    /// </summary>
    public static DerivedState<T> DerivedStateOf<T>(Func<T> calculation)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        var jcw = new ObjectFunction0(() => MutableState<T>.ToJava(calculation()));
        var state = SnapshotStateKt.DerivedStateOf(jcw);
        return new DerivedState<T>(state);
    }

    /// <summary>
    /// Convenience overload: <c>composer.DerivedStateOf(() =&gt; ...)</c> for
    /// call sites that prefer the dotted-on-the-composer shape. The
    /// composer is unused — this just forwards to
    /// <see cref="DerivedStateOf{T}(Func{T})"/>. Wrap the result in
    /// <see cref="Remember{T}(IComposer, Func{T}, int, string)"/> to cache
    /// it across recompositions.
    /// </summary>
    public static DerivedState<T> DerivedStateOf<T>(this IComposer composer, Func<T> calculation)
        => DerivedStateOf(calculation);

    // ---- MutableStateOf ----
    //
    // Single name; five overloads. C# overload resolution prefers a
    // non-generic match over a generic one, so:
    //
    //   c.MutableStateOf(0)       → int    overload → MutableNumberState<int>
    //   c.MutableStateOf(1000L)   → long   overload → MutableNumberState<long>
    //   c.MutableStateOf(0.5f)    → float  overload → MutableNumberState<float>
    //   c.MutableStateOf(Math.PI) → double overload → MutableNumberState<double>
    //   c.MutableStateOf("Ada")   → generic         → MutableState<string>
    //   c.MutableStateOf(false)   → generic         → MutableState<bool>
    //
    // Numeric overloads return MutableNumberState<T> so `count++` / `--` work
    // at the call site without the caller picking a different factory name.

    /// <summary>
    /// Allocate (once per call site) a <see cref="MutableState{T}"/>
    /// holding <paramref name="initial"/>, cached in the composer's slot
    /// table by <see cref="Remember{T}(IComposer, Func{T}, int, string)"/>.
    /// C# parity of Kotlin's <c>remember { mutableStateOf(initial) }</c>.
    /// </summary>
    /// <remarks>
    /// For <c>int</c>, <c>long</c>, <c>float</c>, and <c>double</c>
    /// arguments, C# overload resolution prefers the non-generic numeric
    /// overloads of <see cref="MutableStateOf(IComposer, int, int, string)"/>
    /// etc., so the caller receives a
    /// <see cref="MutableNumberState{T}"/> and can write <c>count++</c>
    /// directly. Use explicit type args (e.g. <c>c.MutableStateOf&lt;long?&gt;(null)</c>)
    /// when the literal can't infer <typeparamref name="T"/>.
    /// </remarks>
    public static MutableState<T> MutableStateOf<T>(
        this IComposer composer,
        T initial,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => composer.Remember(() => new MutableState<T>(initial), line, file);

    /// <summary>
    /// <c>int</c>-typed overload: backed by Compose's
    /// <see cref="IMutableIntState"/> fast path (no <c>Java.Lang.Integer</c>
    /// allocation per read/write) and returns
    /// <see cref="MutableNumberState{T}"/> so the caller can write
    /// <c>count++</c>.
    /// </summary>
    public static MutableNumberState<int> MutableStateOf(
        this IComposer composer,
        int initial,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => composer.Remember(() => new MutableNumberState<int>(initial), line, file);

    /// <summary>
    /// <c>long</c>-typed overload: backed by Compose's
    /// <see cref="IMutableLongState"/> fast path and returns
    /// <see cref="MutableNumberState{T}"/>.
    /// </summary>
    public static MutableNumberState<long> MutableStateOf(
        this IComposer composer,
        long initial,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => composer.Remember(() => new MutableNumberState<long>(initial), line, file);

    /// <summary>
    /// <c>float</c>-typed overload: backed by Compose's
    /// <see cref="IMutableFloatState"/> fast path and returns
    /// <see cref="MutableNumberState{T}"/>.
    /// </summary>
    public static MutableNumberState<float> MutableStateOf(
        this IComposer composer,
        float initial,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => composer.Remember(() => new MutableNumberState<float>(initial), line, file);

    /// <summary>
    /// <c>double</c>-typed overload: Compose has no
    /// <c>MutableDoubleState</c> primitive, so this falls through to the
    /// boxed path — but still returns <see cref="MutableNumberState{T}"/>
    /// so the caller can write <c>x++</c> / <c>x.Value /= 2</c>
    /// idiomatically.
    /// </summary>
    public static MutableNumberState<double> MutableStateOf(
        this IComposer composer,
        double initial,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => composer.Remember(() => new MutableNumberState<double>(initial), line, file);
}
