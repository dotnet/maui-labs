namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// Specifies which edge of a scrolling view's content <see cref="ScrollAnchor"/> keeps in view while the content changes.
/// </summary>
/// <remarks>
/// The values mirror Blazor's <c>VirtualizeAnchorMode</c>, so a later <c>Start = 1</c> (for feeds that prepend) can be
/// combined with <see cref="End"/>.
/// </remarks>
[Flags]
public enum ScrollAnchorMode
{
    /// <summary>The view keeps its scroll position when the content changes: the default behavior.</summary>
    None = 0,

    /// <summary>
    /// Keeps the end of the content in view while the viewport is at the end: items appended or removed, an item that
    /// grows (such as a streamed answer), and a viewport that resizes. Scrolling away from the end stops it until the
    /// end is scrolled back into view.
    /// </summary>
    End = 2,
}
