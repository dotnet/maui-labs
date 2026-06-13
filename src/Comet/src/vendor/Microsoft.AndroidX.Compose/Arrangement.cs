using BindingArrangement = AndroidX.Compose.Foundation.Layout.Arrangement;

namespace AndroidX.Compose;

/// <summary>
/// Compose's <c>Arrangement</c> — how a <see cref="Row"/> or
/// <see cref="Column"/> distributes its children along its main axis.
/// Pass one of the static factories to
/// <see cref="Row(Arrangement?)"/>'s <c>horizontalArrangement</c> or
/// <see cref="Column(Arrangement?)"/>'s <c>verticalArrangement</c>:
///
/// <code>
/// new Row(horizontalArrangement: Arrangement.SpaceBetween) { /* … */ }
/// new Column(verticalArrangement:  Arrangement.SpacedBy(8)) { /* … */ }
/// </code>
///
/// Each factory wraps the corresponding member of the bound
/// <c>androidx.compose.foundation.layout.Arrangement</c> Kotlin singleton:
/// <list type="bullet">
/// <item><description><see cref="Start"/> / <see cref="End"/> — horizontal-only.</description></item>
/// <item><description><see cref="Top"/> / <see cref="Bottom"/> — vertical-only.</description></item>
/// <item><description><see cref="Center"/>, <see cref="SpaceBetween"/>,
/// <see cref="SpaceAround"/>, <see cref="SpaceEvenly"/>,
/// <see cref="SpacedBy(int)"/> / <see cref="SpacedBy(Dp)"/> — usable in
/// either orientation.</description></item>
/// </list>
/// Passing a horizontal-only arrangement to <see cref="Column"/> (or
/// vice-versa) throws <see cref="ArgumentException"/> at the
/// container's constructor — the orientation check is fail-fast so
/// mistakes surface at the call site, not during composition.
/// </summary>
public sealed class Arrangement
{
    static readonly Lazy<Arrangement> _start =
        new(() => new Arrangement(BindingArrangement.Instance.Start));
    static readonly Lazy<Arrangement> _end =
        new(() => new Arrangement(BindingArrangement.Instance.End));
    static readonly Lazy<Arrangement> _top =
        new(() => new Arrangement(BindingArrangement.Instance.Top));
    static readonly Lazy<Arrangement> _bottom =
        new(() => new Arrangement(BindingArrangement.Instance.Bottom));
    static readonly Lazy<Arrangement> _center =
        new(() => new Arrangement(BindingArrangement.Instance.Center));
    static readonly Lazy<Arrangement> _spaceBetween =
        new(() => new Arrangement(BindingArrangement.Instance.SpaceBetween));
    static readonly Lazy<Arrangement> _spaceAround =
        new(() => new Arrangement(BindingArrangement.Instance.SpaceAround));
    static readonly Lazy<Arrangement> _spaceEvenly =
        new(() => new Arrangement(BindingArrangement.Instance.SpaceEvenly));

    /// <summary>Pack children toward the start of the main axis. Horizontal only.</summary>
    public static Arrangement Start => _start.Value;

    /// <summary>Pack children toward the end of the main axis. Horizontal only.</summary>
    public static Arrangement End => _end.Value;

    /// <summary>Pack children toward the top of the main axis. Vertical only.</summary>
    public static Arrangement Top => _top.Value;

    /// <summary>Pack children toward the bottom of the main axis. Vertical only.</summary>
    public static Arrangement Bottom => _bottom.Value;

    /// <summary>Center the children along the main axis. Either orientation.</summary>
    public static Arrangement Center => _center.Value;

    /// <summary>
    /// Place children so the first sits at the start, the last at the
    /// end, and the rest are evenly spaced between them. Either orientation.
    /// </summary>
    public static Arrangement SpaceBetween => _spaceBetween.Value;

    /// <summary>
    /// Place children with equal space surrounding each one — including a
    /// half-space at the start and end. Either orientation.
    /// </summary>
    public static Arrangement SpaceAround => _spaceAround.Value;

    /// <summary>
    /// Place children with equal space between them — including full
    /// equal-sized gaps at the start and end. Either orientation.
    /// </summary>
    public static Arrangement SpaceEvenly => _spaceEvenly.Value;

    /// <summary>
    /// Place children with <paramref name="dp"/> density-independent
    /// pixels of space between consecutive items. Either orientation.
    /// Replaces the common <c>Spacer().Width(dp)</c> /
    /// <c>Spacer().Height(dp)</c> idiom between every pair of children.
    /// </summary>
    /// <remarks>
    /// Kept alongside <see cref="SpacedBy(Dp)"/> so a literal call like
    /// <c>SpacedBy(8)</c> still resolves to the <see cref="int"/> overload
    /// without going through the implicit <c>int → Dp</c> conversion.
    /// </remarks>
    public static Arrangement SpacedBy(int dp) =>
        new(BindingArrangement.Instance.SpacedBy((float)dp));

    /// <summary>
    /// Place children with <paramref name="dp"/> of space between
    /// consecutive items. Either orientation. Accepts a typed
    /// <see cref="Dp"/> — useful when a layout constant is already hoisted
    /// (<c>Arrangement.SpacedBy(padding)</c>) or when constructed via
    /// arithmetic (<c>Arrangement.SpacedBy(basePad * 2)</c>).
    /// </summary>
    public static Arrangement SpacedBy(Dp dp) =>
        new(BindingArrangement.Instance.SpacedBy(dp.Value));

    internal BindingArrangement.IHorizontal? Horizontal { get; }
    internal BindingArrangement.IVertical? Vertical { get; }

    Arrangement(BindingArrangement.IHorizontal horizontal)
    {
        Horizontal = horizontal;
    }

    Arrangement(BindingArrangement.IVertical vertical)
    {
        Vertical = vertical;
    }

    Arrangement(BindingArrangement.IHorizontalOrVertical both)
    {
        Horizontal = both;
        Vertical = both;
    }
}
