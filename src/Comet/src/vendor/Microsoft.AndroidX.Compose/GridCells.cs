using AndroidX.Compose.Foundation.Lazy.Grid;

namespace AndroidX.Compose;

/// <summary>
/// Factory helpers for <see cref="IGridCells"/>, the column/row-count
/// strategy for <see cref="LazyVerticalGrid{T}"/> and
/// <see cref="LazyHorizontalGrid{T}"/>. Mirrors the Kotlin sealed class
/// <c>GridCells</c> with its <c>Fixed</c> and <c>Adaptive</c> subclasses:
/// <code>
/// new LazyVerticalGrid&lt;int&gt;(GridCells.Fixed(3), items, i =&gt; new Text($"{i}"))
/// new LazyVerticalGrid&lt;int&gt;(GridCells.Adaptive(80f), items, i =&gt; new Text($"{i}"))
/// </code>
/// </summary>
public static class GridCells
{
    /// <summary>
    /// Lay out a fixed number of equally-sized cells across the cross
    /// axis (column count for <see cref="LazyVerticalGrid{T}"/>, row
    /// count for <see cref="LazyHorizontalGrid{T}"/>).
    /// </summary>
    public static IGridCells Fixed(int count) => new GridCellsFixed(count);

    /// <summary>
    /// Lay out as many equally-sized cells as fit, given a minimum cell
    /// size (in dp) along the cross axis. The Kotlin
    /// <c>GridCells.Adaptive(Dp)</c> constructor is stripped from the
    /// binding (inline-class <c>Dp</c>), so this goes through a
    /// hand-written JNI bridge in <c>ComposeBridges</c>.
    /// </summary>
    public static IGridCells Adaptive(float minSizeDp) => ComposeBridges.GridCellsAdaptive(minSizeDp);
}
