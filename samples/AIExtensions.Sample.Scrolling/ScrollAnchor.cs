namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// Attached properties that keep the end of a scrolling view's content in view while it changes: new items in a log, a
/// chat answer that streams in, a keyboard that shrinks the viewport.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="ModeProperty"/> to <see cref="ScrollAnchorMode.End"/> on a <see cref="CollectionView"/> (or any
/// <see cref="StructuredItemsView"/>, grouped or not) or a vertical <see cref="ScrollView"/>:
/// </para>
/// <code language="xml">
/// &lt;CollectionView scroll:ScrollAnchor.Mode="End" ItemsSource="{Binding Entries}" /&gt;
/// </code>
/// <para>
/// The view starts at the end and stays there while it changes: items appended or removed, any item growing or
/// shrinking (including the last one while text streams into it), the header or footer changing, and the viewport
/// resizing. Following never animates. When the user scrolls away from the end, it stops following
/// (<see cref="IsAnchoredProperty"/> becomes <see langword="false"/>) until the end is scrolled back into view, by the
/// user, by <c>ScrollTo</c>, or by <see cref="ScrollToEnd"/>.
/// </para>
/// <para>
/// It works on Android, iOS, and Mac Catalyst. Elsewhere, and on views it doesn't support (a horizontal list, a
/// <see cref="CarouselView"/>), setting it has no effect and <see cref="IsAnchoredProperty"/> stays
/// <see langword="false"/>.
/// </para>
/// </remarks>
public static class ScrollAnchor
{
    /// <summary>Identifies the <c>Mode</c> attached property.</summary>
    public static readonly BindableProperty ModeProperty = BindableProperty.CreateAttached(
        "Mode", typeof(ScrollAnchorMode), typeof(ScrollAnchor), ScrollAnchorMode.None, propertyChanged: OnModeChanged);

    private static readonly BindablePropertyKey IsAnchoredPropertyKey = BindableProperty.CreateAttachedReadOnly(
        "IsAnchored", typeof(bool), typeof(ScrollAnchor), false);

    /// <summary>Identifies the read-only <c>IsAnchored</c> attached property.</summary>
    public static readonly BindableProperty IsAnchoredProperty = IsAnchoredPropertyKey.BindableProperty;

    private static readonly BindableProperty ControllerProperty = BindableProperty.CreateAttached(
        "Controller", typeof(ScrollAnchorController), typeof(ScrollAnchor), null);

    /// <summary>Gets which edge of <paramref name="view"/>'s content is kept in view.</summary>
    /// <param name="view">The <see cref="CollectionView"/> or <see cref="ScrollView"/>.</param>
    /// <returns>The anchor mode.</returns>
    public static ScrollAnchorMode GetMode(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (ScrollAnchorMode)view.GetValue(ModeProperty);
    }

    /// <summary>Sets which edge of <paramref name="view"/>'s content is kept in view.</summary>
    /// <param name="view">The <see cref="CollectionView"/> or <see cref="ScrollView"/>.</param>
    /// <param name="value">The anchor mode.</param>
    public static void SetMode(BindableObject view, ScrollAnchorMode value)
    {
        ArgumentNullException.ThrowIfNull(view);
        view.SetValue(ModeProperty, value);
    }

    /// <summary>
    /// Gets whether <paramref name="view"/> follows the end of its content: <see langword="false"/> while the user has
    /// scrolled away from it, for example to show a "jump to latest" button, and wherever the view can't follow it
    /// (before it has a handler, while it scrolls horizontally, or on a platform the anchor doesn't support).
    /// </summary>
    /// <param name="view">The <see cref="CollectionView"/> or <see cref="ScrollView"/>.</param>
    /// <returns><see langword="true"/> while the view follows the end of its content.</returns>
    public static bool GetIsAnchored(BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (bool)view.GetValue(IsAnchoredProperty);
    }

    /// <summary>Scrolls <paramref name="view"/> to the end of its content and, if it anchors to the end, follows it again.</summary>
    /// <param name="view">The <see cref="CollectionView"/> or <see cref="ScrollView"/>.</param>
    /// <param name="animate">Whether to animate the scroll.</param>
    public static void ScrollToEnd(VisualElement view, bool animate = true)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.GetValue(ControllerProperty) is ScrollAnchorController controller)
            controller.ScrollToEnd(animate);
        else
            ScrollAnchorController.ScrollToEndWithoutAnchor(view, animate);
    }

    /// <summary>Gets or sets what creates the platform adapters. Replaceable for tests.</summary>
    internal static Func<ScrollAnchorController, View, object, ScrollAnchorAdapter?> AdapterFactory { get; set; } =
        ScrollAnchorAdapter.Create;

    internal static ScrollAnchorController? GetController(BindableObject view) =>
        (ScrollAnchorController?)view.GetValue(ControllerProperty);

    internal static void SetIsAnchored(BindableObject view, bool value) => view.SetValue(IsAnchoredPropertyKey, value);

    private static void OnModeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view || !ScrollAnchorController.Supports(view))
            return;

        var controller = GetController(view);
        if (((ScrollAnchorMode)newValue).HasFlag(ScrollAnchorMode.End))
        {
            if (controller is null)
                view.SetValue(ControllerProperty, new ScrollAnchorController(view));
        }
        else if (controller is not null)
        {
            controller.Dispose();
            view.ClearValue(ControllerProperty);
            SetIsAnchored(view, false);
        }
    }
}
