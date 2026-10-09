using System.Numerics;

namespace AndroidX.Compose;

/// <summary>
/// Thin <see cref="MutableState{T}"/> subclass that adds <c>operator ++/--</c>
/// for numeric <typeparamref name="T"/> so user code reads almost
/// identically to Kotlin:
/// <code>
/// var count = c.MutableStateOf(0);  // → MutableNumberState&lt;int&gt; via overload resolution
/// count++;                          // mutates the underlying value
/// Text($"Count: {count}");          // ToString() forwards to Value
/// </code>
/// All primitive-fast-path machinery lives on the
/// <see cref="MutableState{T}"/> base; this class exists purely so the
/// <c>++/--</c> operators (which require <see cref="INumber{TSelf}"/> at
/// compile time) can be expressed on the C# type. Constructed via
/// <c>new MutableNumberState&lt;T&gt;(initial)</c> or — typically — via the
/// <see cref="ComposeExtensions.MutableStateOf(AndroidX.Compose.Runtime.IComposer, int, int, string)"/>
/// family of numeric overloads.
/// </summary>
public class MutableNumberState<T> : MutableState<T> where T : INumber<T>
{
    /// <summary>
    /// Wrap a fresh numeric Compose state initialized with
    /// <paramref name="initial"/>. Delegates to <see cref="MutableState{T}"/>,
    /// which picks the
    /// <see cref="AndroidX.Compose.Runtime.IMutableIntState"/>/<c>Long</c>/<c>Float</c>
    /// fast path for the matching <typeparamref name="T"/>.
    /// </summary>
    public MutableNumberState(T initial) : base(initial) { }

    /// <summary>Pre-increment: equivalent to <c>s.Value = s.Value + T.One</c>.</summary>
    public static MutableNumberState<T> operator ++(MutableNumberState<T> s) { s.Value++; return s; }

    /// <summary>Pre-decrement: equivalent to <c>s.Value = s.Value - T.One</c>.</summary>
    public static MutableNumberState<T> operator --(MutableNumberState<T> s) { s.Value--; return s; }
}

