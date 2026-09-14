using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>Row</c> composable — lays its children out
/// horizontally. Mirror of <see cref="Column"/>; same collection-init
/// shape:
/// <code>
/// new Row { new Text("A"), new Spacer { Modifier = ... }, new Text("B") }
/// </code>
/// Pass an optional <see cref="Arrangement"/> to control how children
/// are distributed along the horizontal axis, and an optional
/// <see cref="Alignment.Vertical"/> to control how shorter children
/// align across the Row's vertical extent:
/// <code>
/// new Row(
///     horizontalArrangement: Arrangement.SpaceBetween,
///     verticalAlignment:     Alignment.CenterVertically) { /* … */ }
/// </code>
/// </summary>
public sealed class Row : ComposableContainer
{
    readonly Arrangement? _horizontalArrangement;
    readonly Alignment.Vertical? _verticalAlignment;

    /// <summary>
    /// Lay out children left-to-right with Compose's default
    /// horizontal arrangement (<c>Arrangement.Start</c>) and default
    /// vertical alignment (<c>Alignment.Top</c>).
    /// </summary>
    public Row() : this(null, null) { }

    /// <summary>
    /// Lay out children left-to-right using
    /// <paramref name="horizontalArrangement"/> to distribute them along
    /// the horizontal axis. Pass <see langword="null"/> (or use the
    /// parameterless ctor) for Compose's default.
    /// </summary>
    /// <param name="horizontalArrangement">
    /// One of the <see cref="Arrangement"/> static factories. Must wrap
    /// a horizontal-capable Kotlin arrangement
    /// (<see cref="Arrangement.Start"/>, <see cref="Arrangement.End"/>,
    /// <see cref="Arrangement.Center"/>, <see cref="Arrangement.SpaceBetween"/>,
    /// <see cref="Arrangement.SpaceAround"/>, <see cref="Arrangement.SpaceEvenly"/>,
    /// or <see cref="Arrangement.SpacedBy(int)"/>). Passing a
    /// vertical-only arrangement (<see cref="Arrangement.Top"/>,
    /// <see cref="Arrangement.Bottom"/>) throws
    /// <see cref="ArgumentException"/>.
    /// </param>
    public Row(Arrangement? horizontalArrangement) : this(horizontalArrangement, null) { }

    /// <summary>
    /// Lay out children left-to-right using
    /// <paramref name="horizontalArrangement"/> and
    /// <paramref name="verticalAlignment"/>. Either may be
    /// <see langword="null"/> to keep Compose's default for that axis
    /// (<c>Arrangement.Start</c> / <c>Alignment.Top</c>).
    /// </summary>
    /// <param name="horizontalArrangement">
    /// One of the <see cref="Arrangement"/> static factories — see the
    /// single-argument constructor for the full list of permitted
    /// values.
    /// </param>
    /// <param name="verticalAlignment">
    /// One of the <see cref="Alignment.Vertical"/> singletons
    /// (<see cref="Alignment.Vertical.Top"/>,
    /// <see cref="Alignment.Vertical.CenterVertically"/>,
    /// <see cref="Alignment.Vertical.Bottom"/>). Controls how children
    /// shorter than the Row's measured height are placed along its
    /// vertical axis — useful when mixing visually tall and short
    /// children (e.g. a <c>Switch</c> next to a single-line
    /// <c>Text</c>).
    /// </param>
    public Row(Arrangement? horizontalArrangement, Alignment.Vertical? verticalAlignment)
    {
        if (horizontalArrangement is not null && horizontalArrangement.Horizontal is null)
        {
            throw new ArgumentException(
                $"{nameof(horizontalArrangement)} must wrap a horizontal " +
                $"or horizontal-or-vertical Compose Arrangement; got a " +
                $"vertical-only value (e.g. Arrangement.Top / Arrangement.Bottom).",
                nameof(horizontalArrangement));
        }

        _horizontalArrangement = horizontalArrangement;
        _verticalAlignment = verticalAlignment;
    }

    public override void Render(IComposer composer)
    {
        var horizontal = _horizontalArrangement?.Horizontal;
        var vertical = _verticalAlignment?.Java;
        var modifier = BuildModifier();

        int defaults = (int)RowDefault.All;
        if (modifier is not null)   defaults &= ~(int)RowDefault.Modifier;
        if (horizontal is not null) defaults &= ~(int)RowDefault.HorizontalArrangement;
        if (vertical is not null)   defaults &= ~(int)RowDefault.VerticalAlignment;

        var content = ComposableLambdas.Wrap3(composer, (scope, c) =>
        {
            using var __scopeFrame = RenderContext.PushScope(scope, ScopeKind.Row);
            RenderChildren(c);
        });

        ComposeBridges.Row(modifier, horizontal, vertical, content, defaults, composer);
    }
}
