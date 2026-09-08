using AndroidX.Compose.Foundation.Lazy.Staggeredgrid;

namespace AndroidX.Compose;

/// <summary>
/// Factory helpers for <see cref="IStaggeredGridCells"/>, the
/// column/row-count strategy for
/// <see cref="LazyVerticalStaggeredGrid{T}"/> and
/// <see cref="LazyHorizontalStaggeredGrid{T}"/>. Mirrors the Kotlin
/// sealed class <c>StaggeredGridCells</c> with its <c>Fixed</c>,
/// <c>Adaptive</c>, and <c>FixedSize</c> subclasses:
/// <code>
/// new LazyVerticalStaggeredGrid&lt;int&gt;(StaggeredGridCells.Fixed(2),    items, …)
/// new LazyVerticalStaggeredGrid&lt;int&gt;(StaggeredGridCells.Adaptive(120f), items, …)
/// new LazyVerticalStaggeredGrid&lt;int&gt;(StaggeredGridCells.FixedSize(80f),  items, …)
/// </code>
/// </summary>
public static class StaggeredGridCells
{
    /// <summary>
    /// Lay out a fixed number of equally-sized cells across the cross
    /// axis (column count for <see cref="LazyVerticalStaggeredGrid{T}"/>,
    /// row count for <see cref="LazyHorizontalStaggeredGrid{T}"/>).
    /// </summary>
    public static IStaggeredGridCells Fixed(int count) => new StaggeredGridCellsFixed(count);

    /// <summary>
    /// Lay out as many equally-sized cells as fit, given a minimum cell
    /// size (in dp) along the cross axis. The Kotlin
    /// <c>StaggeredGridCells.Adaptive(Dp)</c> constructor is stripped
    /// from the binding (inline-class <c>Dp</c>), so this goes through
    /// a hand-written JNI bridge in <c>ComposeBridges</c>.
    /// </summary>
    public static IStaggeredGridCells Adaptive(float minSizeDp) =>
        ComposeBridges.StaggeredGridCellsAdaptive(minSizeDp);

    /// <summary>
    /// Lay out cells with a fixed cross-axis size (in dp), filling as
    /// many as fit. Like <see cref="Adaptive"/>, the Kotlin
    /// <c>FixedSize(Dp)</c> constructor is stripped (inline-class
    /// <c>Dp</c>), so this routes through a JNI bridge.
    /// </summary>
    public static IStaggeredGridCells FixedSize(float sizeDp) =>
        ComposeBridges.StaggeredGridCellsFixedSize(sizeDp);
}
