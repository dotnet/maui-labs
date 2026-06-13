using Android.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Fluent chain helpers for <see cref="Modifier"/>. Each method is an
/// extension on <see cref="Modifier"/> so callers can write both
/// <c>Modifier.Padding(16).FillMaxWidth()</c> (the static factory on
/// <see cref="Modifier"/> kicks off the chain, then these extensions
/// continue it) and <c>someModifier.Padding(16)</c> (extension on an
/// existing instance).
/// </summary>
public static class ModifierExtensions
{
    /// <summary>
    /// <c>Modifier.padding(all: Dp)</c> — applies <paramref name="all"/>
    /// of padding to every edge.
    /// </summary>
    public static Modifier Padding(this Modifier modifier, Dp all)
    {
        var dp = all.Value;
        return modifier.Append(h => ComposeBridges.ModifierPaddingAll(h, dp));
    }

    /// <summary>
    /// <c>Modifier.padding(horizontal: Dp, vertical: Dp)</c>. Each edge
    /// defaults to <c>0.dp</c>, so callers can name just the axis they
    /// care about (e.g. <c>someModifier.Padding(horizontal: 16)</c>) and
    /// mirror Kotlin's <c>modifier.padding(horizontal = 16.dp)</c>.
    /// </summary>
    public static Modifier Padding(this Modifier modifier, Dp horizontal = default, Dp vertical = default)
    {
        var h = horizontal.Value;
        var v = vertical.Value;
        return modifier.Append(curr => ComposeBridges.ModifierPaddingHV(curr, h, v));
    }

    /// <summary>
    /// <c>Modifier.padding(start: Dp, top: Dp, end: Dp, bottom: Dp)</c>.
    /// Each edge defaults to <c>0.dp</c>, so callers can name just the
    /// edges they want (e.g. <c>someModifier.Padding(top: 8)</c>) and mirror
    /// Kotlin's <c>modifier.padding(top = 8.dp)</c>.
    /// </summary>
    public static Modifier Padding(this Modifier modifier, Dp start = default, Dp top = default, Dp end = default, Dp bottom = default)
    {
        var s = start.Value;
        var t = top.Value;
        var e = end.Value;
        var b = bottom.Value;
        return modifier.Append(curr => ComposeBridges.ModifierPaddingLTRB(curr, s, t, e, b));
    }

    /// <summary>
    /// <c>Modifier.padding(paddingValues: PaddingValues)</c> — apply a
    /// pre-built <see cref="PaddingValues"/> as a single
    /// <c>Modifier.padding</c> op. Useful for forwarding the
    /// <c>PaddingValues</c> a parent <see cref="Scaffold"/> hands its
    /// content lambda into a non-list child that should pad rather
    /// than route into a native <c>contentPadding</c> slot.
    /// </summary>
    public static Modifier Padding(this Modifier modifier, PaddingValues paddingValues)
    {
        ArgumentNullException.ThrowIfNull(paddingValues);
        var pv = paddingValues;
        return modifier.Append(curr => ComposeBridges.ModifierPaddingValues(curr, ((Java.Lang.Object)pv).Handle));
    }

    /// <summary>
    /// <c>Modifier.fillMaxWidth(fraction)</c>. Defaults to filling
    /// the entire available width (<paramref name="fraction"/> = 1).
    /// </summary>
    public static Modifier FillMaxWidth(this Modifier modifier, float fraction = 1f) =>
        modifier.Append(h => ComposeBridges.ModifierFillMaxWidth(h, fraction));

    /// <summary>
    /// <c>Modifier.fillMaxHeight(fraction)</c>.
    /// </summary>
    public static Modifier FillMaxHeight(this Modifier modifier, float fraction = 1f) =>
        modifier.Append(h => ComposeBridges.ModifierFillMaxHeight(h, fraction));

    /// <summary>
    /// <c>Modifier.fillMaxSize(fraction)</c> — fills both width and height.
    /// </summary>
    public static Modifier FillMaxSize(this Modifier modifier, float fraction = 1f) =>
        modifier.Append(h => ComposeBridges.ModifierFillMaxSize(h, fraction));

    /// <summary>
    /// <c>Modifier.height(dp)</c> — sets a fixed height in dp.
    /// Required to give vertically-scrolling content (e.g.
    /// <see cref="LazyColumn{T}"/>) a bounded viewport when it lives
    /// inside an unbounded parent like a regular <see cref="Column"/>.
    /// </summary>
    public static Modifier Height(this Modifier modifier, Dp height)
    {
        var f = height.Value;
        return modifier.Append(h => ComposeBridges.ModifierHeight(h, f));
    }

    /// <summary>
    /// <c>Modifier.width(dp)</c> — sets a fixed width in dp.
    /// </summary>
    public static Modifier Width(this Modifier modifier, Dp width)
    {
        var f = width.Value;
        return modifier.Append(h => ComposeBridges.ModifierWidth(h, f));
    }

    /// <summary>
    /// <c>Modifier.size(dp)</c> — sets both width and height to the
    /// same value in dp.
    /// </summary>
    public static Modifier Size(this Modifier modifier, Dp size)
    {
        var f = size.Value;
        return modifier.Append(h => ComposeBridges.ModifierSizeAll(h, f));
    }

    /// <summary>
    /// <c>Modifier.size(width, height)</c> in dp.
    /// </summary>
    public static Modifier Size(this Modifier modifier, Dp width, Dp height)
    {
        var w = width.Value;
        var h = height.Value;
        return modifier.Append(curr => ComposeBridges.ModifierSizeWH(curr, w, h));
    }

    /// <summary>
    /// <c>Modifier.safeDrawingPadding()</c> — pads for the union of
    /// system bars, IME, and display cutouts. Use as the outer modifier
    /// on a root composable (or inside a <see cref="Scaffold"/>'s body
    /// when no <see cref="Scaffold.TopBar"/> is supplied) to keep
    /// content out of inset regions under edge-to-edge.
    /// </summary>
    public static Modifier SafeDrawingPadding(this Modifier modifier) =>
        modifier.Append(h => ComposeBridges.ModifierSafeDrawingPadding(h));

    /// <summary>
    /// <c>Modifier.systemBarsPadding()</c> — pads for status + nav bars
    /// only (ignoring IME and cutouts). Prefer
    /// <see cref="SafeDrawingPadding"/> in most apps.
    /// </summary>
    public static Modifier SystemBarsPadding(this Modifier modifier) =>
        modifier.Append(h => ComposeBridges.ModifierSystemBarsPadding(h));

    /// <summary>
    /// <c>Modifier.minimumInteractiveComponentSize()</c> — reserves at
    /// least 48dp × 48dp around the composable to keep touch targets
    /// accessible. Add to icon-only buttons, dense list rows, or any
    /// other small clickable surface so its hit-box meets the Material
    /// 3 accessibility minimum. Must appear in the chain BEFORE any
    /// <see cref="Size(Dp)"/> / <see cref="Width(Dp)"/> /
    /// <see cref="Height(Dp)"/> modifier that would limit the
    /// composable's constraints, otherwise the reserved space is
    /// clamped away.
    /// </summary>
    /// <remarks>
    /// This modifier only affects layout — it doesn't enable touch
    /// expansion (the platform already does that via
    /// <c>ViewConfiguration</c>). Its job is to make sure neighboring
    /// composables don't crowd the touch region.
    /// </remarks>
    public static Modifier MinimumInteractiveComponentSize(this Modifier modifier) =>
        modifier.Append(h => ComposeBridges.ModifierMinimumInteractiveComponentSize(h));

    /// <summary>
    /// <c>Modifier.background(color)</c> — paints a flat fill behind the
    /// composable using a <c>RectangleShape</c>. Takes a packed Compose
    /// <c>androidx.compose.ui.graphics.Color</c> value (a <c>ULong</c>
    /// surfaced as a <c>long</c> in the binding because Color is a Kotlin
    /// <c>@JvmInline value class</c>). Build one via
    /// <see cref="Color.FromRgb(byte, byte, byte)"/>,
    /// <see cref="Color.FromHex(string)"/>, or one of the
    /// named constants on <see cref="Color"/> (e.g.
    /// <see cref="Color.Black"/>).
    /// </summary>
    public static Modifier Background(this Modifier modifier, Color color) =>
        modifier.Append(curr => ComposeBridges.ModifierBackground(curr, color, null));

    /// <summary>
    /// <c>Modifier.background(color, shape)</c> — paints a flat fill
    /// behind the composable, clipped to <paramref name="shape"/>. Pass
    /// <c>null</c> for the default <c>RectangleShape</c>; otherwise build
    /// a shape via <see cref="Shape.RoundedCorners(Dp)"/>,
    /// <see cref="Shape.Circle"/>,
    /// <see cref="Shape.CutCorners(Dp)"/>, etc. The shape is
    /// captured by the closure so its Java peer stays alive across
    /// recompositions.
    /// </summary>
    public static Modifier Background(this Modifier modifier, Color color, Shape? shape) =>
        modifier.Append(curr => ComposeBridges.ModifierBackground(curr, color, shape?.Handle));

    /// <summary>
    /// <c>Modifier.border(width, color)</c> — draws a rectangular stroke
    /// around the composable. <paramref name="width"/> is the stroke
    /// width in density-independent pixels. For rounded corners use
    /// <see cref="Border(Dp, Color, Dp)"/>; for arbitrary shapes use
    /// <see cref="Border(Dp, Color, Shape?)"/>.
    /// </summary>
    public static Modifier Border(this Modifier modifier, Dp width, Color color)
    {
        var w = width.Value;
        long c = color;
        return modifier.Append(curr => ComposeBridges.ModifierBorder(curr, w, c, null));
    }

    /// <summary>
    /// <c>Modifier.border(width, color, RoundedCornerShape(cornerRadius))</c> —
    /// draws a stroke with rounded corners. Match
    /// <paramref name="cornerRadius"/> to a <see cref="Clip(Dp)"/>
    /// earlier in the chain so the corners align (otherwise the
    /// rectangular stroke gets sliced by a rounded clip and you see
    /// jagged corner stubs).
    /// </summary>
    public static Modifier Border(this Modifier modifier, Dp width, Color color, Dp cornerRadius)
    {
        var w = width.Value;
        var r = cornerRadius.Value;
        long c = color;
        if (r <= 0f)
            return modifier.Append(curr => ComposeBridges.ModifierBorder(curr, w, c, null));

        return modifier.Append(curr =>
        {
            IntPtr shape = ComposeBridges.RoundedCornerShape(r);
            try
            {
                return ComposeBridges.ModifierBorder(curr, w, c, shape);
            }
            finally
            {
                if (shape != IntPtr.Zero)
                    JNIEnv.DeleteLocalRef(shape);
            }
        });
    }

    /// <summary>
    /// <c>Modifier.border(width, color, shape)</c> — overload taking an
    /// explicit <see cref="Shape"/> for non-rounded geometry
    /// (cut corners, custom shape factories, etc.). Pass <c>null</c> for
    /// <c>RectangleShape</c>. The shape is captured by the closure so its
    /// Java peer stays alive across recompositions.
    /// </summary>
    public static Modifier Border(this Modifier modifier, Dp width, Color color, Shape? shape)
    {
        var w = width.Value;
        long c = color;
        return modifier.Append(curr => ComposeBridges.ModifierBorder(curr, w, c, shape?.Handle));
    }

    /// <summary>
    /// <c>Modifier.background(brush, shape, alpha)</c> — paints a
    /// <see cref="AndroidX.Compose.UI.Graphics.Brush"/> (gradient,
    /// solid color, or any other Brush subclass) behind the
    /// composable, clipped to <paramref name="shape"/>. Pass
    /// <c>null</c> for the default <see cref="Shape.Rectangle"/>;
    /// supply <paramref name="alpha"/> to fade the brush
    /// (1f = fully opaque, 0f = invisible).
    /// </summary>
    /// <remarks>
    /// The brush is captured by the closure so its Java peer stays
    /// alive across recompositions. For static brushes prefer
    /// constructing once outside the composition (or wrapping the
    /// factory call in <c>composer.Remember(() =&gt; ...)</c>) so the
    /// Java instance is reused frame-to-frame.
    /// </remarks>
    public static Modifier Background(
        this Modifier modifier, AndroidX.Compose.UI.Graphics.Brush brush, Shape? shape = null, float alpha = 1f)
    {
        ArgumentNullException.ThrowIfNull(brush);
        return modifier.Append(curr => ComposeBridges.ModifierBackgroundBrush(curr, brush, shape, alpha));
    }

    /// <summary>
    /// <c>Modifier.border(width, brush, shape)</c> — draws a stroke
    /// painted by the supplied <see cref="AndroidX.Compose.UI.Graphics.Brush"/>
    /// around the composable, clipped to <paramref name="shape"/>. Pass
    /// <c>null</c> for the default <see cref="Shape.Rectangle"/>.
    /// </summary>
    /// <remarks>
    /// The brush is captured by the closure so its Java peer stays
    /// alive across recompositions — see
    /// <see cref="Background(Modifier, AndroidX.Compose.UI.Graphics.Brush, Shape?, float)"/>
    /// for the same notes around per-recomposition allocation.
    /// </remarks>
    public static Modifier Border(
        this Modifier modifier, Dp width, AndroidX.Compose.UI.Graphics.Brush brush, Shape? shape = null)
    {
        ArgumentNullException.ThrowIfNull(brush);
        var w = width.Value;
        return modifier.Append(curr => ComposeBridges.ModifierBorderBrush(curr, w, brush, shape));
    }

    /// <summary>
    /// <c>Modifier.clip(RoundedCornerShape(<paramref name="cornerRadius"/>))</c> —
    /// rounds the four corners by the same radius and clips drawing to the
    /// resulting shape. Pass <c>0</c> for no rounding (rectangle clip).
    /// </summary>
    public static Modifier Clip(this Modifier modifier, Dp cornerRadius)
    {
        var dp = cornerRadius.Value;
        return modifier.Append(curr => ComposeBridges.ModifierClipRoundedCorners(curr, dp));
    }

    /// <summary>
    /// <c>Modifier.clip(shape)</c> — clips drawing (and pointer hits)
    /// to the supplied <paramref name="shape"/>. Use with
    /// <see cref="Shape.Circle"/> for circular clips,
    /// <see cref="Shape.CutCorners(Dp)"/> for chamfered
    /// corners, or any other shape factory. The shape is captured by
    /// the closure so its Java peer stays alive across recompositions.
    /// </summary>
    public static Modifier Clip(this Modifier modifier, Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return modifier.Append(curr => ComposeBridges.ModifierClip(curr, shape.Handle));
    }

    /// <summary>
    /// <c>Modifier.clickable { onClick() }</c> — handles taps with the
    /// default Material indication / ripple. The click handler runs on
    /// the UI thread.
    /// </summary>
    public static Modifier Clickable(this Modifier modifier, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);

        var lambda = new ComposableLambda0(onClick);
        return modifier.Append(curr => ComposeBridges.ModifierClickable(curr, lambda));
    }

    /// <summary>
    /// <c>Modifier.dragAndDropTarget(shouldStartDragAndDrop, target)</c> —
    /// marks the composable as a drop zone for drags originating from other
    /// apps or other composables. The
    /// <paramref name="shouldStartDragAndDrop"/> predicate runs once per
    /// drag-start; return <c>true</c> to opt this target in for that drag
    /// (a common gate is <c>e =&gt; e.MimeTypes.Any(m =&gt; m.StartsWith("image/"))</c>).
    /// While the drag is in progress Compose forwards events to
    /// <paramref name="target"/>; assign its <see cref="DragAndDropTarget.OnDrop"/>
    /// to handle the dropped payload.
    ///
    /// Both arguments should be hoisted into
    /// <c>composer.Remember</c> so the
    /// underlying <c>DragAndDropTargetElement</c> keeps a stable identity
    /// across recompositions:
    /// <code>
    /// var target = composer.Remember(() =&gt; new DragAndDropTarget { OnDrop = e =&gt; { /* ... */ return true; } });
    /// new Column
    /// {
    ///     Modifier.FillMaxSize()
    ///         .DragAndDropTarget(e =&gt; e.MimeTypes.Any(m =&gt; m.StartsWith("image/")), target),
    ///     /* children */
    /// };
    /// </code>
    /// </summary>
    /// <param name="shouldStartDragAndDrop">Predicate invoked when a drag
    /// begins. Return <c>true</c> to register interest in the drag (and
    /// receive subsequent events on <paramref name="target"/>), <c>false</c>
    /// to ignore it.</param>
    /// <param name="target">Receiver of drag events for this composable. At
    /// minimum set <see cref="DragAndDropTarget.OnDrop"/>; the optional
    /// <see cref="DragAndDropTarget.OnEntered"/> /
    /// <see cref="DragAndDropTarget.OnExited"/> /
    /// <see cref="DragAndDropTarget.OnStarted"/> /
    /// <see cref="DragAndDropTarget.OnEnded"/> hooks drive
    /// hover/session visuals.</param>
    public static Modifier DragAndDropTarget(this Modifier modifier,
        Func<DragAndDropEvent, bool> shouldStartDragAndDrop,
        DragAndDropTarget target)
    {
        ArgumentNullException.ThrowIfNull(shouldStartDragAndDrop);
        ArgumentNullException.ThrowIfNull(target);
        var predicate = new ShouldStartDragAndDropCallback(shouldStartDragAndDrop);
        return modifier.Append(curr => ComposeBridges.ModifierDragAndDropTarget(curr, predicate, target));
    }

    /// <summary>
    /// <c>Modifier.verticalScroll(state)</c> — makes a non-Lazy parent
    /// (e.g. a regular <see cref="Column"/> or <see cref="Box"/>)
    /// vertically scrollable when its content overflows. Hold the
    /// <paramref name="state"/> across recompositions with
    /// <c>composer.Remember</c>:
    /// <code>
    /// var scroll = Remember(() =&gt; new ScrollState());
    /// new Column { Modifier.VerticalScroll(scroll), /* children */ };
    /// </code>
    /// Prefer <see cref="LazyColumn{T}"/> for long lists of like-shaped
    /// items — it only composes the children currently on screen. Use
    /// <see cref="VerticalScroll"/> for a small, known set of children
    /// that simply might overflow on smaller screens.
    /// </summary>
    /// <param name="state">Scroll position state to drive (and read
    /// the current offset from).</param>
    /// <param name="enabled">When <c>false</c>, the user can no longer
    /// scroll the content via touch gestures. <see cref="ScrollState"/>
    /// still updates as the layout changes, but this binding does not
    /// yet expose programmatic scrolling (Kotlin's
    /// <c>scrollTo</c> / <c>animateScrollTo</c> are <c>suspend</c>
    /// functions, not yet wired up).</param>
    /// <param name="reverseScrolling">When <c>true</c>, flip the
    /// scroll direction so the start of the content sits at the
    /// bottom of the viewport (and dragging up reveals earlier
    /// content). Defaults to <c>false</c>.</param>
    public static Modifier VerticalScroll(this Modifier modifier, ScrollState state, bool enabled = true, bool reverseScrolling = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        var jvm = state.Jvm;
        return modifier.Append(curr =>
            ComposeBridges.ModifierVerticalScroll(
                curr, ((Java.Lang.Object)jvm).Handle, enabled, reverseScrolling));
    }

    /// <summary>
    /// <c>Modifier.horizontalScroll(state)</c> — makes a non-Lazy
    /// parent (e.g. a regular <see cref="Row"/> or <see cref="Box"/>)
    /// horizontally scrollable when its content overflows. See
    /// <see cref="VerticalScroll"/> for the typical usage shape; prefer
    /// <see cref="LazyRow{T}"/> for long horizontally-scrollable lists.
    /// </summary>
    /// <param name="state">Scroll position state to drive (and read
    /// the current offset from).</param>
    /// <param name="enabled">When <c>false</c>, the user can no longer
    /// scroll the content via touch gestures. <see cref="ScrollState"/>
    /// still updates as the layout changes, but this binding does not
    /// yet expose programmatic scrolling (Kotlin's
    /// <c>scrollTo</c> / <c>animateScrollTo</c> are <c>suspend</c>
    /// functions, not yet wired up).</param>
    /// <param name="reverseScrolling">When <c>true</c>, flip the
    /// scroll direction so the start of the content sits at the end
    /// of the viewport. Defaults to <c>false</c>.</param>
    public static Modifier HorizontalScroll(this Modifier modifier, ScrollState state, bool enabled = true, bool reverseScrolling = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        var jvm = state.Jvm;
        return modifier.Append(curr =>
            ComposeBridges.ModifierHorizontalScroll(
                curr, ((Java.Lang.Object)jvm).Handle, enabled, reverseScrolling));
    }

    /// <summary>
    /// <c>Modifier.draggable(state, orientation, enabled)</c> — drag
    /// gesture handler that reports raw drag deltas (in pixels along
    /// the chosen <see cref="Orientation"/>) to the supplied
    /// <see cref="DraggableState"/>. Unlike scroll, this modifier does
    /// not consume the deltas itself — your <c>onDelta</c> callback
    /// inside <see cref="DraggableState"/> decides what to do with the
    /// movement (typically: update an offset state that another
    /// modifier reads).
    /// </summary>
    /// <param name="state">State holder that receives drag deltas.
    /// Build via <c>new DraggableState(delta =&gt; ...)</c> inside a
    /// <c>composer.Remember</c> call, or via
    /// <see cref="ComposeExtensions.RememberDraggableState(Action{float}, int, string)"/>
    /// for stable Java identity across recompositions when the
    /// callback closure changes.</param>
    /// <param name="orientation">Axis the gesture operates on —
    /// <see cref="Orientation.Vertical"/> for up-down drags,
    /// <see cref="Orientation.Horizontal"/> for left-right drags.</param>
    /// <param name="enabled">When <c>false</c>, the modifier ignores
    /// touch input. Defaults to <c>true</c>.</param>
    public static Modifier Draggable(this Modifier modifier, DraggableState state, Orientation orientation, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        var jvm = state.Jvm;
        var jvmOrientation = orientation == Orientation.Horizontal
            ? AndroidX.Compose.Foundation.Gestures.Orientation.Horizontal!
            : AndroidX.Compose.Foundation.Gestures.Orientation.Vertical!;
        return modifier.Append(curr =>
            ComposeBridges.ModifierDraggable(
                curr,
                ((Java.Lang.Object)jvm).Handle,
                ((Java.Lang.Object)jvmOrientation).Handle,
                enabled));
    }

    /// <summary>
    /// <c>Modifier.nestedScroll(connection)</c> — bridge a scrollable
    /// container to a nested-scrolling parent (most commonly a
    /// Material 3 <see cref="TopAppBar"/> / <see cref="MediumTopAppBar"/>
    /// / <see cref="LargeTopAppBar"/> that collapses on scroll).
    /// Apply this modifier to the scrolling container — usually a
    /// <c>LazyColumn</c>, <c>LazyRow</c>, or a <see cref="Column"/>
    /// with <c>VerticalScroll(ScrollState)</c> — and Compose will
    /// route scroll deltas through <paramref name="connection"/> so
    /// the parent can consume some of them before the container does.
    /// </summary>
    /// <param name="connection">
    /// The connection to forward scroll deltas to. Pair with an
    /// <see cref="AndroidX.Compose.Material3.ITopAppBarScrollBehavior"/>
    /// by passing
    /// <see cref="AndroidX.Compose.Material3.ITopAppBarScrollBehavior.NestedScrollConnection"/>;
    /// the scroll behavior is also set as the bar's
    /// <c>ScrollBehavior</c> property so both sides agree on the
    /// shared <see cref="AndroidX.Compose.Material3.TopAppBarState"/>.
    /// </param>
    public static Modifier NestedScroll(this Modifier modifier, AndroidX.Compose.UI.Input.NestedScroll.INestedScrollConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return modifier.Append(curr => ComposeBridges.ModifierNestedScroll(curr, connection));
    }

    /// <summary>
    /// <c>Modifier.weight(weight, fill = true)</c> — only valid inside a
    /// <see cref="Row"/> or <see cref="Column"/> (or any container that
    /// publishes <see cref="ScopeKind.Row"/> / <see cref="ScopeKind.Column"/>).
    /// Distributes the leftover space along the parent's main axis in
    /// proportion to other weighted children. Set <paramref name="fill"/>
    /// to <c>false</c> to let the child be smaller than its allotted slot.
    /// </summary>
    public static Modifier Weight(this Modifier modifier, float weight, bool fill = true)
    {
        return modifier.Append(curr =>
        {
            IntPtr scope = RenderContext.CurrentScope;
            ScopeKind kind = RenderContext.CurrentScopeKind;
            return kind switch
            {
                ScopeKind.Row    => ComposeBridges.RowScopeModifierWeight(scope, curr, weight, fill),
                ScopeKind.Column => ComposeBridges.ColumnScopeModifierWeight(scope, curr, weight, fill),
                _ => throw new InvalidOperationException(
                    "Modifier.Weight() can only be used inside a Row, Column, or " +
                    "Row/Column-shaped scope (BottomAppBar, NavigationBar, …). " +
                    $"Current scope kind: {kind}.")
            };
        });
    }

    /// <summary>
    /// <c>Modifier.widthIn(min, max)</c> — adds a min and/or max width
    /// constraint. Pass <c>null</c> for either bound to leave it
    /// unconstrained (Kotlin's <c>Dp.Unspecified</c> default), so
    /// <c>WidthIn(min: 100)</c> caps only the lower bound.
    /// </summary>
    public static Modifier WidthIn(this Modifier modifier, Dp? min = null, Dp? max = null) =>
        modifier.Append(curr => ComposeBridges.ModifierWidthIn(curr, min, max));

    /// <summary>
    /// <c>Modifier.heightIn(min, max)</c> — adds a min and/or max height
    /// constraint. Pass <c>null</c> for either bound to leave it
    /// unconstrained.
    /// </summary>
    public static Modifier HeightIn(this Modifier modifier, Dp? min = null, Dp? max = null) =>
        modifier.Append(curr => ComposeBridges.ModifierHeightIn(curr, min, max));

    /// <summary>
    /// <c>Modifier.sizeIn(minWidth, minHeight, maxWidth, maxHeight)</c> —
    /// constrains both axes. Pass <c>null</c> for any bound to leave it
    /// unconstrained.
    /// </summary>
    public static Modifier SizeIn(this Modifier modifier, Dp? minWidth = null, Dp? minHeight = null, Dp? maxWidth = null, Dp? maxHeight = null) =>
        modifier.Append(curr => ComposeBridges.ModifierSizeIn(curr, minWidth, minHeight, maxWidth, maxHeight));

    /// <summary>
    /// <c>Modifier.requiredSize(size)</c> — declares an exact size that
    /// bypasses the parent's constraints (the composable is allowed to
    /// be drawn outside the parent if needed). Use sparingly; prefer
    /// <see cref="Size(Dp)"/> for normal layout.
    /// </summary>
    public static Modifier RequiredSize(this Modifier modifier, Dp size)
    {
        var dp = size.Value;
        return modifier.Append(curr => ComposeBridges.ModifierRequiredSizeAll(curr, dp));
    }

    /// <summary>
    /// <c>Modifier.requiredSize(width, height)</c> — overload taking
    /// independent width and height (each bypassing parent constraints).
    /// </summary>
    public static Modifier RequiredSize(this Modifier modifier, Dp width, Dp height)
    {
        var w = width.Value;
        var h = height.Value;
        return modifier.Append(curr => ComposeBridges.ModifierRequiredSizeWH(curr, w, h));
    }

    /// <summary>
    /// <c>Modifier.requiredWidth(width)</c> — declares an exact width
    /// that bypasses the parent's constraints.
    /// </summary>
    public static Modifier RequiredWidth(this Modifier modifier, Dp width)
    {
        var w = width.Value;
        return modifier.Append(curr => ComposeBridges.ModifierRequiredWidth(curr, w));
    }

    /// <summary>
    /// <c>Modifier.requiredHeight(height)</c> — declares an exact height
    /// that bypasses the parent's constraints.
    /// </summary>
    public static Modifier RequiredHeight(this Modifier modifier, Dp height)
    {
        var h = height.Value;
        return modifier.Append(curr => ComposeBridges.ModifierRequiredHeight(curr, h));
    }

    /// <summary>
    /// <c>Modifier.defaultMinSize(minWidth, minHeight)</c> — supplies a
    /// minimum size that's only used when the parent doesn't already
    /// constrain that dimension. Useful for default-sized buttons /
    /// chips that should grow with content but never shrink below a
    /// hit-target floor. Pass <c>null</c> for either bound to leave it
    /// unspecified.
    /// </summary>
    public static Modifier DefaultMinSize(this Modifier modifier, Dp? minWidth = null, Dp? minHeight = null) =>
        modifier.Append(curr => ComposeBridges.ModifierDefaultMinSize(curr, minWidth, minHeight));

    /// <summary>
    /// <c>Modifier.wrapContentSize(unbounded)</c> — measures content
    /// without imposing the parent's constraints, then centers the
    /// result inside the parent's available space. Set
    /// <paramref name="unbounded"/> to <c>true</c> to let content
    /// overflow the parent.
    /// </summary>
    public static Modifier WrapContentSize(this Modifier modifier, bool unbounded = false) =>
        modifier.Append(curr => ComposeBridges.ModifierWrapContentSize(curr, unbounded));

    /// <summary>
    /// <c>Modifier.wrapContentWidth(unbounded)</c> — same as
    /// <see cref="WrapContentSize(bool)"/> but only relaxes the width
    /// axis.
    /// </summary>
    public static Modifier WrapContentWidth(this Modifier modifier, bool unbounded = false) =>
        modifier.Append(curr => ComposeBridges.ModifierWrapContentWidth(curr, unbounded));

    /// <summary>
    /// <c>Modifier.wrapContentHeight(unbounded)</c> — same as
    /// <see cref="WrapContentSize(bool)"/> but only relaxes the height
    /// axis.
    /// </summary>
    public static Modifier WrapContentHeight(this Modifier modifier, bool unbounded = false) =>
        modifier.Append(curr => ComposeBridges.ModifierWrapContentHeight(curr, unbounded));

    /// <summary>
    /// <c>Modifier.aspectRatio(ratio, matchHeightConstraintsFirst)</c> —
    /// forces the composable's width-to-height ratio.
    /// <paramref name="ratio"/> is <c>width / height</c>: <c>16f / 9f</c>
    /// for a wide video frame, <c>1f</c> for a square. When
    /// <paramref name="matchHeightConstraintsFirst"/> is <c>true</c>, the
    /// height constraint is preferred when both width and height are
    /// bounded; otherwise width wins (Kotlin's default).
    /// </summary>
    public static Modifier AspectRatio(this Modifier modifier, float ratio, bool matchHeightConstraintsFirst = false) =>
        modifier.Append(curr => ComposeBridges.ModifierAspectRatio(curr, ratio, matchHeightConstraintsFirst));

    /// <summary>
    /// <c>Modifier.offset(x, y)</c> — shifts the composable's draw
    /// position by (x, y) without affecting the layout slot the parent
    /// allocates for it. Layout-direction-aware (start/end on RTL).
    /// Pass <c>null</c> for either axis to leave it at <c>0.dp</c>.
    /// </summary>
    public static Modifier Offset(this Modifier modifier, Dp? x = null, Dp? y = null) =>
        modifier.Append(curr => ComposeBridges.ModifierOffset(curr, x, y));

    /// <summary>
    /// <c>Modifier.absoluteOffset(x, y)</c> — like <see cref="Offset"/>
    /// but always uses absolute (left/right) axes, ignoring layout
    /// direction.
    /// </summary>
    public static Modifier AbsoluteOffset(this Modifier modifier, Dp? x = null, Dp? y = null) =>
        modifier.Append(curr => ComposeBridges.ModifierAbsoluteOffset(curr, x, y));

    /// <summary>
    /// <c>Modifier.zIndex(z)</c> — sets the draw order within a parent
    /// layout. Children with higher <paramref name="z"/> draw on top of
    /// siblings with lower values. Defaults to <c>0f</c>.
    /// </summary>
    public static Modifier ZIndex(this Modifier modifier, float z) =>
        modifier.Append(curr => ComposeBridges.ModifierZIndex(curr, z));

    /// <summary>
    /// <c>Modifier.alpha(alpha)</c> — applies an alpha multiplier to
    /// the composable's draw output. <c>0f</c> is fully transparent;
    /// <c>1f</c> is fully opaque. Cheap to animate (forces a graphics
    /// layer).
    /// </summary>
    public static Modifier Alpha(this Modifier modifier, float alpha) =>
        modifier.Append(curr => ComposeBridges.ModifierAlpha(curr, alpha));

    /// <summary>
    /// <c>Modifier.rotate(degrees)</c> — rotates the composable around
    /// its center by <paramref name="degrees"/>. Negative values rotate
    /// counter-clockwise.
    /// </summary>
    public static Modifier Rotate(this Modifier modifier, float degrees) =>
        modifier.Append(curr => ComposeBridges.ModifierRotate(curr, degrees));

    /// <summary>
    /// <c>Modifier.scale(scale)</c> — uniform scale around the
    /// composable's center.
    /// </summary>
    public static Modifier Scale(this Modifier modifier, float scale) =>
        modifier.Append(curr => ComposeBridges.ModifierScaleUniform(curr, scale));

    /// <summary>
    /// <c>Modifier.scale(scaleX, scaleY)</c> — independent X / Y scale
    /// around the composable's center.
    /// </summary>
    public static Modifier Scale(this Modifier modifier, float scaleX, float scaleY) =>
        modifier.Append(curr => ComposeBridges.ModifierScaleXY(curr, scaleX, scaleY));

    /// <summary>
    /// <c>Modifier.shadow(elevation, shape)</c> — draws a soft drop
    /// shadow under the composable. <paramref name="elevation"/> is the
    /// shadow's apparent depth in dp. Pass a non-null
    /// <paramref name="shape"/> to clip the shadow to a custom outline;
    /// otherwise it follows Kotlin's default <c>RectangleShape</c>. The
    /// shape is captured by the closure so its Java peer stays alive
    /// across recompositions. Kotlin's default for the underlying
    /// <c>clip</c> parameter (<c>elevation &gt; 0.dp</c>) is honored.
    /// </summary>
    public static Modifier Shadow(this Modifier modifier, Dp elevation, Shape? shape = null)
    {
        var e = elevation.Value;
        return modifier.Append(curr => ComposeBridges.ModifierShadow(curr, e, shape?.Handle));
    }

    /// <summary>
    /// <c>Modifier.graphicsLayer(...)</c> — applies a draw-time
    /// graphics layer to the composable. All parameters default to
    /// <c>null</c>, which lets Compose substitute its own
    /// per-property defaults (identity for transforms, full opacity
    /// for <paramref name="alpha"/>, etc.). Supply only the
    /// properties you want to override.
    /// </summary>
    /// <param name="scaleX">Horizontal scale (1.0 = no scaling).</param>
    /// <param name="scaleY">Vertical scale (1.0 = no scaling).</param>
    /// <param name="alpha">Opacity in [0, 1].</param>
    /// <param name="translationX">Horizontal translation in pixels.</param>
    /// <param name="translationY">Vertical translation in pixels.</param>
    /// <param name="shadowElevation">Elevation in pixels (use <see cref="Shadow"/> to specify in Dp).</param>
    /// <param name="rotationX">Rotation around the X axis in degrees.</param>
    /// <param name="rotationY">Rotation around the Y axis in degrees.</param>
    /// <param name="rotationZ">Rotation around the Z axis in degrees (clockwise).</param>
    /// <param name="cameraDistance">Distance of the camera from the rotation pivot, in pixels.</param>
    /// <param name="transformOrigin">Packed <c>TransformOrigin</c> value (use <see cref="TransformOrigin.Pack(float, float)"/>); the default is the center (0.5, 0.5).</param>
    /// <param name="shape">Clip / shadow outline shape (default = rectangle).</param>
    /// <param name="clip">Whether to clip content to <paramref name="shape"/>.</param>
    public static Modifier GraphicsLayer(this Modifier modifier,
        float? scaleX = null,
        float? scaleY = null,
        float? alpha = null,
        float? translationX = null,
        float? translationY = null,
        float? shadowElevation = null,
        float? rotationX = null,
        float? rotationY = null,
        float? rotationZ = null,
        float? cameraDistance = null,
        long? transformOrigin = null,
        Shape? shape = null,
        bool? clip = null)
    {
        return modifier.Append(curr => ComposeBridges.ModifierGraphicsLayer(
            curr,
            scaleX,
            scaleY,
            alpha,
            translationX,
            translationY,
            shadowElevation,
            rotationX,
            rotationY,
            rotationZ,
            cameraDistance,
            transformOrigin,
            shape?.Handle,
            clip));
    }

    /// <summary>
    /// <c>Modifier.imePadding()</c> — pads the composable so it sits
    /// above the soft keyboard (IME) when it's visible. Use under
    /// edge-to-edge to keep input controls visible.
    /// </summary>
    public static Modifier ImePadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierImePadding(curr));

    /// <summary>
    /// <c>Modifier.navigationBarsPadding()</c> — pads for the system
    /// navigation bar inset only.
    /// </summary>
    public static Modifier NavigationBarsPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierNavigationBarsPadding(curr));

    /// <summary>
    /// <c>Modifier.statusBarsPadding()</c> — pads for the status bar
    /// inset only.
    /// </summary>
    public static Modifier StatusBarsPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierStatusBarsPadding(curr));

    /// <summary>
    /// <c>Modifier.displayCutoutPadding()</c> — pads for display cutouts
    /// (notches, hole-punch cameras) so content doesn't get clipped by
    /// hardware features.
    /// </summary>
    public static Modifier DisplayCutoutPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierDisplayCutoutPadding(curr));

    /// <summary>
    /// <c>Modifier.captionBarPadding()</c> — pads for the caption bar
    /// inset (window decorations on freeform / desktop windowing modes).
    /// On phones without a caption bar this is a no-op.
    /// </summary>
    public static Modifier CaptionBarPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierCaptionBarPadding(curr));

    /// <summary>
    /// <c>Modifier.mandatorySystemGesturesPadding()</c> — pads for the
    /// subset of gesture insets the system always reserves for itself
    /// (e.g. the bottom home-gesture strip), even when the user opts
    /// out of edge gestures.
    /// </summary>
    public static Modifier MandatorySystemGesturesPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierMandatorySystemGesturesPadding(curr));

    /// <summary>
    /// <c>Modifier.safeContentPadding()</c> — union of
    /// <see cref="SafeDrawingPadding"/> and
    /// <see cref="SafeGesturesPadding"/>. Use for content that should
    /// avoid both visual obstructions and gesture zones.
    /// </summary>
    public static Modifier SafeContentPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierSafeContentPadding(curr));

    /// <summary>
    /// <c>Modifier.safeGesturesPadding()</c> — union of
    /// <see cref="MandatorySystemGesturesPadding"/> +
    /// <see cref="SystemGesturesPadding"/> + the tappable-element
    /// insets. Use to keep interactive UI out of the system's gesture
    /// zones.
    /// </summary>
    public static Modifier SafeGesturesPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierSafeGesturesPadding(curr));

    /// <summary>
    /// <c>Modifier.systemGesturesPadding()</c> — pads for the system
    /// gesture insets (the edge regions where the OS may interpret
    /// swipes as system gestures such as back / home).
    /// </summary>
    public static Modifier SystemGesturesPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierSystemGesturesPadding(curr));

    /// <summary>
    /// <c>Modifier.waterfallPadding()</c> — pads for waterfall display
    /// insets (the curved edges of waterfall-screen devices). No-op on
    /// flat-screen phones.
    /// </summary>
    public static Modifier WaterfallPadding(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierWaterfallPadding(curr));

    /// <summary>
    /// <c>Modifier.testTag(tag)</c> — attaches a stable identifier for
    /// UI testing frameworks (Compose UI Test, Espresso). Has no visual
    /// effect; only affects the semantics tree.
    /// </summary>
    public static Modifier TestTag(this Modifier modifier, string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return modifier.Append(curr => ComposeBridges.ModifierTestTag(curr, tag));
    }

    /// <summary>
    /// <c>Modifier.align(alignment)</c> — positions the child within
    /// a parent <see cref="Box"/>. Only valid inside a <see cref="Box"/>
    /// (any container that publishes <see cref="ScopeKind.Box"/>).
    /// </summary>
    public static Modifier Align(this Modifier modifier, Alignment alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        return modifier.Append(curr =>
        {
            IntPtr scope = RenderContext.CurrentScope;
            if (RenderContext.CurrentScopeKind != ScopeKind.Box)
                throw new InvalidOperationException(
                    "Modifier.Align(Alignment) is only valid inside a Box. " +
                    $"Current scope kind: {RenderContext.CurrentScopeKind}.");
            return ComposeBridges.BoxScopeAlign(scope, curr, ((Java.Lang.Object)alignment).Handle);
        });
    }

    /// <summary>
    /// <c>Modifier.align(alignment)</c> for a Row child — aligns the
    /// child vertically within the row. Only valid inside a
    /// <see cref="Row"/>.
    /// </summary>
    public static Modifier Align(this Modifier modifier, Alignment.Vertical alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        return modifier.Append(curr =>
        {
            IntPtr scope = RenderContext.CurrentScope;
            if (RenderContext.CurrentScopeKind != ScopeKind.Row)
                throw new InvalidOperationException(
                    "Modifier.Align(Alignment.Vertical) is only valid inside a Row. " +
                    $"Current scope kind: {RenderContext.CurrentScopeKind}.");
            return ComposeBridges.RowScopeAlignVertical(scope, curr, ((Java.Lang.Object)alignment.Java).Handle);
        });
    }

    /// <summary>
    /// <c>Modifier.align(alignment)</c> for a Column child — aligns the
    /// child horizontally within the column. Only valid inside a
    /// <see cref="Column"/>.
    /// </summary>
    public static Modifier Align(this Modifier modifier, Alignment.Horizontal alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        return modifier.Append(curr =>
        {
            IntPtr scope = RenderContext.CurrentScope;
            if (RenderContext.CurrentScopeKind != ScopeKind.Column)
                throw new InvalidOperationException(
                    "Modifier.Align(Alignment.Horizontal) is only valid inside a Column. " +
                    $"Current scope kind: {RenderContext.CurrentScopeKind}.");
            return ComposeBridges.ColumnScopeAlignHorizontal(scope, curr, ((Java.Lang.Object)alignment.Java).Handle);
        });
    }

    /// <summary>
    /// <c>Modifier.matchParentSize()</c> — sizes the child to match
    /// the parent <see cref="Box"/>'s measured size without
    /// participating in measurement. Only valid inside a
    /// <see cref="Box"/>.
    /// </summary>
    public static Modifier MatchParentSize(this Modifier modifier) =>
        modifier.Append(curr =>
        {
            IntPtr scope = RenderContext.CurrentScope;
            if (RenderContext.CurrentScopeKind != ScopeKind.Box)
                throw new InvalidOperationException(
                    "Modifier.MatchParentSize() is only valid inside a Box. " +
                    $"Current scope kind: {RenderContext.CurrentScopeKind}.");
            return ComposeBridges.BoxScopeMatchParentSize(scope, curr);
        });

    /// <summary>
    /// <c>Modifier.focusable(enabled = true)</c> — marks the node as a
    /// focus target. Combine with <see cref="FocusRequester(AndroidX.Compose.FocusRequester)"/>
    /// to programmatically move focus, or with
    /// <see cref="OnFocusChanged(Action{FocusState})"/>
    /// to observe focus changes.
    /// </summary>
    public static Modifier Focusable(this Modifier modifier, bool enabled = true) =>
        modifier.Append(curr => ComposeBridges.ModifierFocusable(curr, enabled));

    /// <summary>
    /// <c>Modifier.focusGroup()</c> — groups focusable descendants so
    /// two-dimensional focus search treats them as a single unit.
    /// </summary>
    public static Modifier FocusGroup(this Modifier modifier) =>
        modifier.Append(curr => ComposeBridges.ModifierFocusGroup(curr));

    /// <summary>
    /// <c>Modifier.onFocusChanged { ... }</c> — invokes <paramref name="onFocusChanged"/>
    /// whenever the node gains, loses, or has its focus state mutated
    /// (capture / release). The callback receives an immutable
    /// <see cref="FocusState"/> snapshot.
    /// </summary>
    public static Modifier OnFocusChanged(this Modifier modifier, Action<FocusState> onFocusChanged)
    {
        ArgumentNullException.ThrowIfNull(onFocusChanged);
        var f1 = new ComposableLambda1(arg =>
        {
            if (arg is null) return;
            var fs = Android.Runtime.Extensions.JavaCast<AndroidX.Compose.UI.Focus.IFocusState>(arg);
            onFocusChanged(FocusState.From(fs));
        });
        return modifier.Append(curr => ComposeBridges.ModifierOnFocusChanged(curr, f1));
    }

    /// <summary>
    /// <c>Modifier.focusRequester(requester)</c> — installs
    /// <paramref name="requester"/> on the node so the caller can
    /// programmatically move focus by calling
    /// <see cref="FocusRequester.RequestFocus"/>.
    /// </summary>
    public static Modifier FocusRequester(this Modifier modifier, FocusRequester requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return modifier.Append(curr =>
            ComposeBridges.ModifierFocusRequester(curr, ((Java.Lang.Object)requester.Java).Handle));
    }

    /// <summary>
    /// <c>Modifier.combinedClickable(...)</c> — clickable with optional
    /// long-press and double-tap handlers. <paramref name="onClick"/> is
    /// required; pass <c>null</c> for either of the other two to fall
    /// back to Kotlin's "ignore that gesture" defaults.
    /// </summary>
    public static Modifier CombinedClickable(this Modifier modifier,
        Action onClick,
        Action? onLongClick = null,
        Action? onDoubleClick = null)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        var click = new ComposableLambda0(onClick);
        var longClick = onLongClick is null ? null : new ComposableLambda0(onLongClick);
        var doubleClick = onDoubleClick is null ? null : new ComposableLambda0(onDoubleClick);
        return modifier.Append(curr =>
            ComposeBridges.ModifierCombinedClickable(curr, longClick, doubleClick, click));
    }

    /// <summary>
    /// <c>Modifier.selectable(selected, onClick)</c> — marks the node
    /// as a selectable choice in a single-selection group (e.g. a
    /// radio button group). Sets up the right accessibility semantics
    /// and forwards taps to <paramref name="onClick"/>.
    /// </summary>
    public static Modifier Selectable(this Modifier modifier, bool selected, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        var click = new ComposableLambda0(onClick);
        return modifier.Append(curr =>
            ComposeBridges.ModifierSelectable(curr, selected, click));
    }

    /// <summary>
    /// <c>Modifier.toggleable(value, onValueChange)</c> — marks the
    /// node as a binary toggle (e.g. a checkbox row). Sets up the
    /// right accessibility semantics and forwards taps to
    /// <paramref name="onValueChange"/> with the negated value.
    /// </summary>
    public static Modifier Toggleable(this Modifier modifier, bool value, Action<bool> onValueChange)
    {
        ArgumentNullException.ThrowIfNull(onValueChange);
        var f1 = new ComposableLambda1(arg =>
        {
            bool v = arg is Java.Lang.Boolean jb && jb.BooleanValue();
            onValueChange(v);
        });
        return modifier.Append(curr =>
            ComposeBridges.ModifierToggleable(curr, value, f1));
    }

    /// <summary>
    /// <c>Modifier.semantics { contentDescription = ... }</c> — adds
    /// a content description for accessibility (TalkBack reads it
    /// aloud when the node is focused). Doesn't merge descendant
    /// semantics; call the overload taking <c>mergeDescendants</c> for
    /// that.
    /// </summary>
    public static Modifier Semantics(this Modifier modifier, string contentDescription) =>
        modifier.Semantics(mergeDescendants: false, contentDescription, role: null);

    /// <summary>
    /// <c>Modifier.semantics(mergeDescendants) { contentDescription = ... }</c> —
    /// set <paramref name="mergeDescendants"/> to <c>true</c> for a
    /// container that should announce itself instead of its children
    /// (e.g. a card with a label and a value).
    /// </summary>
    public static Modifier Semantics(this Modifier modifier, bool mergeDescendants, string contentDescription) =>
        modifier.Semantics(mergeDescendants, contentDescription, role: null);

    /// <summary>
    /// <c>Modifier.semantics { role = ... }</c> — tags the node with
    /// an accessibility <see cref="SemanticsRole"/> (e.g.
    /// <see cref="SemanticsRole.Button"/>). Useful when wrapping a
    /// custom composable that's clickable but isn't a real
    /// <see cref="Button"/>, so TalkBack announces "button" instead
    /// of just the content description.
    /// </summary>
    public static Modifier Semantics(this Modifier modifier, SemanticsRole role) =>
        modifier.Semantics(mergeDescendants: false, contentDescription: null, role: role);

    /// <summary>
    /// <c>Modifier.semantics { contentDescription = ...; role = ... }</c> —
    /// combine a content description with a role in a single
    /// semantics block. Either argument may be omitted by passing
    /// <c>null</c>.
    /// </summary>
    public static Modifier Semantics(this Modifier modifier, string? contentDescription, SemanticsRole? role) =>
        modifier.Semantics(mergeDescendants: false, contentDescription, role);

    /// <summary>
    /// <c>Modifier.semantics(mergeDescendants) { contentDescription = ...; role = ... }</c> —
    /// full form. Pass <c>null</c> for either property to skip it.
    /// At least one of <paramref name="contentDescription"/> or
    /// <paramref name="role"/> must be supplied.
    /// </summary>
    public static Modifier Semantics(this Modifier modifier, bool mergeDescendants, string? contentDescription, SemanticsRole? role)
    {
        if (contentDescription is null && role is null)
            throw new ArgumentException(
                "At least one of contentDescription or role must be supplied.",
                nameof(contentDescription));
        var properties = new ComposableLambda1(arg =>
        {
            if (arg is Java.Lang.Object obj)
            {
                if (contentDescription is not null)
                    ComposeBridges.SemanticsSetContentDescription(obj.Handle, contentDescription);
                if (role is not null)
                    ComposeBridges.SemanticsSetRole(obj.Handle, (int)role.Value);
            }
        });
        return modifier.Append(curr =>
            ComposeBridges.ModifierSemantics(curr, mergeDescendants, properties));
    }

    /// <summary>
    /// <c>Modifier.clearAndSetSemantics { contentDescription = ... }</c> —
    /// like <see cref="Semantics(string)"/>, but discards the
    /// descendant semantics first. Use when a custom composable
    /// should appear as a single accessibility node with a curated
    /// description, hiding implementation details.
    /// </summary>
    public static Modifier ClearAndSetSemantics(this Modifier modifier, string contentDescription) =>
        modifier.ClearAndSetSemantics(contentDescription, role: null);

    /// <summary>
    /// <c>Modifier.clearAndSetSemantics { contentDescription = ...; role = ... }</c> —
    /// combine a content description with a <see cref="SemanticsRole"/>
    /// in a single clear-and-set block. At least one of
    /// <paramref name="contentDescription"/> or <paramref name="role"/>
    /// must be supplied.
    /// </summary>
    public static Modifier ClearAndSetSemantics(this Modifier modifier, string? contentDescription, SemanticsRole? role)
    {
        if (contentDescription is null && role is null)
            throw new ArgumentException(
                "At least one of contentDescription or role must be supplied.",
                nameof(contentDescription));
        var properties = new ComposableLambda1(arg =>
        {
            if (arg is Java.Lang.Object obj)
            {
                if (contentDescription is not null)
                    ComposeBridges.SemanticsSetContentDescription(obj.Handle, contentDescription);
                if (role is not null)
                    ComposeBridges.SemanticsSetRole(obj.Handle, (int)role.Value);
            }
        });
        return modifier.Append(curr =>
            ComposeBridges.ModifierClearAndSetSemantics(curr, properties));
    }

    /// <summary>
    /// <c>Modifier.pointerInput(Unit) { detectTapGestures(...) }</c> —
    /// detect the four basic single-pointer tap gestures (tap, press,
    /// long-press, double-tap) and invoke the supplied C# callbacks on
    /// the UI thread with the tap position as an <see cref="Offset"/>
    /// in local layout pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All four callback parameters are optional; pass only the ones
    /// you care about. When all four are <c>null</c> this modifier is
    /// a no-op (but still allocates the per-pointer-input state
    /// holder, so prefer omitting it entirely in that case).
    /// </para>
    /// <para>
    /// Unlike <see cref="Clickable(Action)"/>, this modifier
    /// supplies <strong>no Material indication / ripple</strong>, no
    /// accessibility semantics, and no role — it's the low-level
    /// gesture primitive. Use <see cref="Clickable(Action)"/>
    /// for ordinary clickable surfaces; reach for
    /// <see cref="DetectTapGestures"/> when you need long-press,
    /// double-tap, or precise tap positions.
    /// </para>
    /// <para>
    /// <strong>Callback freshness gotcha.</strong> The Kotlin
    /// <c>pointerInput</c> modifier only restarts its coroutine when
    /// its key changes — <em>not</em> when the handler instance
    /// changes. Because all C# callback adapters share the same Java
    /// class, simply rebuilding this modifier with different lambdas
    /// will NOT pick up the new callbacks. To refresh, either
    /// (a) supply a varying <paramref name="key"/> derived from the
    /// values your callbacks close over, or (b) have the callbacks
    /// read mutable state via a remembered
    /// <c>MutableState&lt;T&gt;</c> so they pick up new values on
    /// each invocation.
    /// </para>
    /// </remarks>
    /// <param name="onTap">Fired once the system confirms the gesture
    /// is a simple tap (not the start of a long-press or double-tap).</param>
    /// <param name="onPress">Fired the moment the finger goes down,
    /// before the system has decided which gesture this will become.
    /// Useful for "pressed" visual feedback.</param>
    /// <param name="onLongPress">Fired after the system's
    /// long-press timeout (typically ~500ms) while the finger is still
    /// down.</param>
    /// <param name="onDoubleTap">Fired when a second tap arrives
    /// within the system double-tap timeout (typically ~300ms) of the
    /// first.</param>
    /// <param name="key">Identity key passed to Kotlin's
    /// <c>pointerInput</c>. Defaults to a stable singleton
    /// (<c>Kotlin.Unit</c>), so the gesture detector coroutine starts
    /// once and never restarts. Pass any value whose <c>Equals</c>
    /// changes when callbacks should reset.</param>
    public static Modifier DetectTapGestures(this Modifier modifier,
        Action<Offset>? onTap = null,
        Action<Offset>? onPress = null,
        Action<Offset>? onLongPress = null,
        Action<Offset>? onDoubleTap = null,
        object? key = null)
    {
        var tapCb = onTap is null ? null : new OffsetCallback(onTap);
        var pressCb = onPress is null ? null : new OffsetPressCallback(onPress);
        var longPressCb = onLongPress is null ? null : new OffsetCallback(onLongPress);
        var doubleTapCb = onDoubleTap is null ? null : new OffsetCallback(onDoubleTap);

        var block = new PointerInputBlock(tapCb, pressCb, longPressCb, doubleTapCb);
        return AppendPointerInput(modifier, key, block);
    }

    /// <summary>
    /// <c>Modifier.pointerInput(key) { detectDragGestures(...) }</c> —
    /// detect single-pointer drag gestures (touch-down, drag, lift).
    /// Each callback receives positions / deltas in local layout
    /// pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="onDrag"/> is required; the start / end / cancel
    /// callbacks are optional and default to no-ops on the Kotlin
    /// side. The drag delta in <paramref name="onDrag"/> is the
    /// per-frame movement, not the cumulative pan; callers tracking
    /// totals across the gesture maintain a running sum keyed by
    /// <paramref name="onDragStart"/> / <paramref name="onDragEnd"/>.
    /// </para>
    /// <para>
    /// The same callback-freshness gotcha as
    /// <see cref="DetectTapGestures(Modifier, Action{Offset}?, Action{Offset}?, Action{Offset}?, Action{Offset}?, object?)"/>
    /// applies — vary <paramref name="key"/> to restart the gesture
    /// coroutine with new lambdas, otherwise the captured callbacks
    /// from the first composition stay live.
    /// </para>
    /// </remarks>
    /// <param name="onDragStart">Fired once at the moment a drag is
    /// recognised (after the system's touch-slop threshold). Position
    /// is the pointer location at touch-down.</param>
    /// <param name="onDragEnd">Fired when the gesture completes
    /// (pointer lifted within the view).</param>
    /// <param name="onDragCancel">Fired when the gesture is cancelled
    /// (a parent intercepted, multi-touch confused the detector,
    /// etc.).</param>
    /// <param name="onDrag">Required. Fired once per pointer move with
    /// the per-frame delta. Use <see cref="Offset.X"/> /
    /// <see cref="Offset.Y"/> as <c>float</c> pixel deltas.</param>
    /// <param name="key">Identity key — same semantics as on
    /// <see cref="DetectTapGestures(Modifier, Action{Offset}?, Action{Offset}?, Action{Offset}?, Action{Offset}?, object?)"/>.</param>
    public static Modifier DetectDragGestures(this Modifier modifier,
        Action<Offset> onDrag,
        Action<Offset>? onDragStart = null,
        Action? onDragEnd = null,
        Action? onDragCancel = null,
        object? key = null)
    {
        ArgumentNullException.ThrowIfNull(onDrag);

        var startCb  = onDragStart  is null ? null : new OffsetCallback(onDragStart);
        var endCb    = onDragEnd    is null ? null : new UnitCallback(onDragEnd);
        var cancelCb = onDragCancel is null ? null : new UnitCallback(onDragCancel);
        var dragCb   = new DragCallback(onDrag);

        var block = new DragGestureBlock(startCb, endCb, cancelCb, dragCb);
        return AppendPointerInput(modifier, key, block);
    }

    /// <summary>
    /// <c>Modifier.pointerInput(key) { detectTransformGestures(...) }</c>
    /// — detect multi-pointer transform gestures (pinch zoom, two-
    /// finger pan, rotate). The single <paramref name="onGesture"/>
    /// callback receives the centroid, cumulative pan delta, zoom
    /// multiplier, and rotation in degrees on every pointer move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compose's <c>detectTransformGestures</c> applies the per-frame
    /// deltas continuously — there are no separate start / end
    /// callbacks. The caller maintains its own state holder if it
    /// wants to detect "pinch starts" (e.g. by latching on the first
    /// non-1.0 zoom value).
    /// </para>
    /// <para>
    /// The same callback-freshness gotcha as the other
    /// <c>Modifier.detect*Gestures</c> extensions applies; vary
    /// <paramref name="key"/> to restart with new lambdas.
    /// </para>
    /// </remarks>
    /// <param name="onGesture">Required. Receives
    /// <c>(centroid, pan, zoom, rotation)</c>. <c>centroid</c> is the
    /// average position of all active pointers; <c>pan</c> is the
    /// per-frame pan delta; <c>zoom</c> is the per-frame multiplier
    /// (1.0 means no zoom this frame); <c>rotation</c> is the
    /// per-frame rotation in degrees.</param>
    /// <param name="panZoomLock">When <see langword="true"/>, locks
    /// out rotation while the user is panning or zooming — useful
    /// for image / map viewers that don't want stray rotations.
    /// Defaults to <see langword="false"/> matching Kotlin.</param>
    /// <param name="key">Identity key — see
    /// <see cref="DetectTapGestures(Modifier, Action{Offset}?, Action{Offset}?, Action{Offset}?, Action{Offset}?, object?)"/>.</param>
    public static Modifier DetectTransformGestures(this Modifier modifier,
        Action<Offset, Offset, float, float> onGesture,
        bool panZoomLock = false,
        object? key = null)
    {
        ArgumentNullException.ThrowIfNull(onGesture);

        var gestureCb = new TransformGestureCallback(onGesture);
        var block = new TransformGestureBlock(panZoomLock, gestureCb);
        return AppendPointerInput(modifier, key, block);
    }

    // Shared plumbing for DetectTapGestures / DetectDragGestures /
    // DetectTransformGestures: resolve the user-supplied `key` to a
    // Java object (Kotlin uses reference equality for the default-
    // overload form, so we want a stable JNI object for the "no key"
    // path — Kotlin.Unit.Instance is the canonical singleton), then
    // wrap the gesture-block JCW in a PointerInputEventHandler and
    // apply Modifier.pointerInput(key, handler). Both keyObj and the
    // block are kept alive across the JNI call via GC.KeepAlive.
    static Modifier AppendPointerInput(Modifier modifier, object? key, Java.Lang.Object block)
    {
        var keyObj = key switch
        {
            null => (Java.Lang.Object)Kotlin.Unit.Instance!,
            Java.Lang.Object jlo => jlo,
            string s => new Java.Lang.String(s),
            int i => Java.Lang.Integer.ValueOf(i),
            long l => Java.Lang.Long.ValueOf(l),
            bool b => Java.Lang.Boolean.ValueOf(b),
            _ => new Java.Lang.String(key.ToString() ?? ""),
        };

        return modifier.Append(curr =>
        {
            var handlerLocal = IntPtr.Zero;
            try
            {
                handlerLocal = ComposeBridges.NewPointerInputEventHandler(block.Handle);
                return ComposeBridges.ModifierPointerInput(
                    curr, keyObj.Handle, handlerLocal);
            }
            finally
            {
                if (handlerLocal != IntPtr.Zero)
                    Android.Runtime.JNIEnv.DeleteLocalRef(handlerLocal);
                GC.KeepAlive(keyObj);
                GC.KeepAlive(block);
            }
        });
    }

    /// <summary>
    /// <c>Modifier.semantics { ... }</c> — fluent builder form that
    /// exposes all supported semantic properties (selected, role,
    /// content/state description, onClick label) on
    /// <see cref="SemanticsScope"/>. Doesn't merge descendant
    /// semantics; use the overload taking <paramref name="properties"/>
    /// + <c>mergeDescendants</c> for that.
    /// </summary>
    /// <param name="properties">Builder callback. Each chained call on
    /// the supplied <see cref="SemanticsScope"/> sets one property on
    /// the underlying Kotlin <c>SemanticsPropertyReceiver</c>.</param>
    public static Modifier Semantics(this Modifier modifier, Action<SemanticsScope> properties) =>
        modifier.Semantics(mergeDescendants: false, properties);

    /// <summary>
    /// <c>Modifier.semantics(mergeDescendants) { ... }</c> — fluent
    /// builder form. Set <paramref name="mergeDescendants"/> to
    /// <c>true</c> for a container that should announce itself as a
    /// single accessibility node instead of exposing its children
    /// (e.g. a card with a label and a value).
    /// </summary>
    /// <param name="mergeDescendants">Whether to merge descendant
    /// semantics into this node.</param>
    /// <param name="properties">Builder callback. See
    /// <see cref="SemanticsScope"/> for available property setters.</param>
    public static Modifier Semantics(this Modifier modifier, bool mergeDescendants, Action<SemanticsScope> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var lambda = WrapSemanticsBuilder(properties);
        return modifier.Append(curr =>
            ComposeBridges.ModifierSemantics(curr, mergeDescendants, lambda));
    }

    /// <summary>
    /// <c>Modifier.clearAndSetSemantics { ... }</c> — fluent builder
    /// form. Discards the descendant semantics first, then applies
    /// the properties set in <paramref name="properties"/>. Use when
    /// a custom composable should appear as a single accessibility
    /// node with a curated description, hiding implementation
    /// details.
    /// </summary>
    /// <param name="properties">Builder callback. See
    /// <see cref="SemanticsScope"/> for available property setters.</param>
    public static Modifier ClearAndSetSemantics(this Modifier modifier, Action<SemanticsScope> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var lambda = WrapSemanticsBuilder(properties);
        return modifier.Append(curr =>
            ComposeBridges.ModifierClearAndSetSemantics(curr, lambda));
    }

    // Shared body for the two builder-form overloads above. Each
    // invocation of the returned ComposableLambda1 gets a fresh
    // SemanticsScope bound to the JNI handle Compose hands us, and
    // the scope is Invalidate()d the moment the user callback returns
    // so leaked references throw a clear error on reuse.
    static ComposableLambda1 WrapSemanticsBuilder(Action<SemanticsScope> properties) =>
        new(arg =>
        {
            if (arg is Java.Lang.Object obj)
            {
                var scope = new SemanticsScope(obj.Handle);
                try { properties(scope); }
                finally { scope.Invalidate(); }
            }
        });
}
