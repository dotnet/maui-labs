using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>Column</c> composable — lays its children out
/// vertically. Mirror of <see cref="Row"/>; same collection-init shape:
/// <code>
/// new Column { new Text("A"), new Text("B") }
/// </code>
/// Pass an optional <see cref="Arrangement"/> to control how children
/// are distributed along the vertical axis:
/// <code>
/// new Column(verticalArrangement: Arrangement.SpacedBy(8)) { /* … */ }
/// </code>
/// </summary>
public sealed class Column : ComposableContainer
{
    readonly Arrangement? _verticalArrangement;
    readonly Alignment.Horizontal? _horizontalAlignment;

    /// <summary>
    /// Lay out children top-to-bottom with Compose's default
    /// vertical arrangement (<c>Arrangement.Top</c>) and default
    /// horizontal alignment (<c>Alignment.Start</c>).
    /// </summary>
    public Column() : this(null, null) { }

    /// <summary>
    /// Lay out children top-to-bottom using
    /// <paramref name="verticalArrangement"/> to distribute them along
    /// the vertical axis. Pass <see langword="null"/> (or use the
    /// parameterless ctor) for Compose's default.
    /// </summary>
    /// <param name="verticalArrangement">
    /// One of the <see cref="Arrangement"/> static factories. Must wrap
    /// a vertical-capable Kotlin arrangement
    /// (<see cref="Arrangement.Top"/>, <see cref="Arrangement.Bottom"/>,
    /// <see cref="Arrangement.Center"/>, <see cref="Arrangement.SpaceBetween"/>,
    /// <see cref="Arrangement.SpaceAround"/>, <see cref="Arrangement.SpaceEvenly"/>,
    /// or <see cref="Arrangement.SpacedBy(int)"/>). Passing a
    /// horizontal-only arrangement (<see cref="Arrangement.Start"/>,
    /// <see cref="Arrangement.End"/>) throws
    /// <see cref="ArgumentException"/>.
    /// </param>
    public Column(Arrangement? verticalArrangement) : this(verticalArrangement, null) { }

    /// <summary>
    /// Lay out children top-to-bottom using
    /// <paramref name="verticalArrangement"/> and
    /// <paramref name="horizontalAlignment"/>. Either may be
    /// <see langword="null"/> to keep Compose's default for that axis
    /// (<c>Arrangement.Top</c> / <c>Alignment.Start</c>).
    /// </summary>
    /// <param name="verticalArrangement">
    /// One of the <see cref="Arrangement"/> static factories — see the
    /// single-argument constructor for the full list of permitted
    /// values.
    /// </param>
    /// <param name="horizontalAlignment">
    /// One of the <see cref="Alignment.Horizontal"/> singletons
    /// (<see cref="Alignment.Horizontal.Start"/>,
    /// <see cref="Alignment.Horizontal.CenterHorizontally"/>,
    /// <see cref="Alignment.Horizontal.End"/>). Controls how children
    /// narrower than the Column's measured width are placed along its
    /// horizontal axis.
    /// </param>
    public Column(Arrangement? verticalArrangement, Alignment.Horizontal? horizontalAlignment)
    {
        if (verticalArrangement is not null && verticalArrangement.Vertical is null)
        {
            throw new ArgumentException(
                $"{nameof(verticalArrangement)} must wrap a vertical " +
                $"or horizontal-or-vertical Compose Arrangement; got a " +
                $"horizontal-only value (e.g. Arrangement.Start / Arrangement.End).",
                nameof(verticalArrangement));
        }

        _verticalArrangement = verticalArrangement;
        _horizontalAlignment = horizontalAlignment;
    }

    public override void Render(IComposer composer)
    {
        var vertical = _verticalArrangement?.Vertical;
        var horizontal = _horizontalAlignment?.Java;
        var modifier = BuildModifier();

        int defaults = (int)ColumnDefault.All;
        if (modifier is not null)   defaults &= ~(int)ColumnDefault.Modifier;
        if (vertical is not null)   defaults &= ~(int)ColumnDefault.VerticalArrangement;
        if (horizontal is not null) defaults &= ~(int)ColumnDefault.HorizontalAlignment;

        var content = ComposableLambdas.Wrap3(composer, (scope, c) =>
        {
            using var __scopeFrame = RenderContext.PushScope(scope, ScopeKind.Column);
            RenderChildren(c);
        });

        ComposeBridges.Column(modifier, vertical, horizontal, content, defaults, composer);
    }
}
