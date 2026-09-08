using Android.Runtime;
using AndroidX.Compose.UI;

namespace AndroidX.Compose;

/// <summary>
/// C# mirror of Kotlin's <c>androidx.compose.ui.Modifier</c> chain.
/// Build a chain by calling a static factory on this type — each call
/// returns a NEW immutable <see cref="Modifier"/> with the op appended.
/// Chain further ops via the matching extension methods in
/// <see cref="ModifierExtensions"/>:
///
/// <code>
/// new Column
/// {
///     Modifier.Padding(16).FillMaxWidth(),
///     new Text("Hello"),
/// }
/// </code>
///
/// On a leaf composable use object-initializer syntax:
///
/// <code>
/// new Text("Hello") { Modifier = Modifier.Padding(8) }
/// </code>
///
/// <see cref="Companion"/> is the empty Modifier — the identity element
/// for <see cref="Then(Modifier)"/> composition. The static factories
/// here delegate to it.
///
/// At <c>Render</c> time the chain is materialized into an
/// <c>IModifier</c> by replaying each op against
/// <c>androidx.compose.ui.Modifier.Companion</c> (resolved via the
/// <c>$$INSTANCE</c> static field Kotlin emits for <c>object</c>
/// declarations) via JNI — see
/// <see cref="ComposeBridges.ModifierCompanionInstance"/>. Building
/// cheap modifier chains every recomposition is the per-composition
/// cost the Tier 1.5 facade pays — Tier 2 codegen would skip it.
/// </summary>
public sealed class Modifier
{
    static readonly Func<IntPtr, IntPtr>[] EmptyOps = Array.Empty<Func<IntPtr, IntPtr>>();
    static readonly Modifier _companion = new Modifier(EmptyOps);

    /// <summary>
    /// The empty Modifier — entry point for the fluent chain.
    /// Mirrors Kotlin's <c>Modifier.Companion</c> object, which is
    /// also the identity element of <c>Modifier.then(...)</c>.
    /// </summary>
    public static Modifier Companion => _companion;

    readonly Func<IntPtr, IntPtr>[] _ops;

    Modifier(Func<IntPtr, IntPtr>[] ops)
    {
        _ops = ops;
    }

    internal Modifier Append(Func<IntPtr, IntPtr> op)
    {
        var arr = new Func<IntPtr, IntPtr>[_ops.Length + 1];
        Array.Copy(_ops, arr, _ops.Length);
        arr[_ops.Length] = op;
        return new Modifier(arr);
    }

    /// <summary>
    /// Concatenate <paramref name="other"/> onto this chain, returning
    /// a new <see cref="Modifier"/>. Mirrors Kotlin's
    /// <c>Modifier.then(other)</c> — the receiver's ops apply first,
    /// then <paramref name="other"/>'s ops.
    /// </summary>
    public Modifier Then(Modifier other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other._ops.Length == 0) return this;
        if (_ops.Length == 0) return other;
        var arr = new Func<IntPtr, IntPtr>[_ops.Length + other._ops.Length];
        Array.Copy(_ops, arr, _ops.Length);
        Array.Copy(other._ops, 0, arr, _ops.Length, other._ops.Length);
        return new Modifier(arr);
    }

    // ---- Static chain entry points (factories) ----
    //
    // Each factory delegates to the matching extension method on
    // ModifierExtensions via _companion. Together they let callers
    // write `Modifier.X(...)` to start a chain and `.Y(...)` to
    // continue it, matching Kotlin's `Modifier.x(...).y(...)` 1:1.

    /// <inheritdoc cref="ModifierExtensions.Padding(Dp)"/>
    public static Modifier Padding(Dp all) => _companion.Padding(all);

    /// <inheritdoc cref="ModifierExtensions.Padding(Modifier, Dp, Dp)"/>
    public static Modifier Padding(Dp horizontal = default, Dp vertical = default) => _companion.Padding(horizontal, vertical);

    /// <inheritdoc cref="ModifierExtensions.Padding(Modifier, Dp, Dp, Dp, Dp)"/>
    public static Modifier Padding(Dp start = default, Dp top = default, Dp end = default, Dp bottom = default) => _companion.Padding(start, top, end, bottom);

    /// <inheritdoc cref="ModifierExtensions.Padding(PaddingValues)"/>
    public static Modifier Padding(PaddingValues paddingValues) => _companion.Padding(paddingValues);

    /// <inheritdoc cref="ModifierExtensions.FillMaxWidth(float)"/>
    public static Modifier FillMaxWidth(float fraction = 1f) => _companion.FillMaxWidth(fraction);

    /// <inheritdoc cref="ModifierExtensions.FillMaxHeight(float)"/>
    public static Modifier FillMaxHeight(float fraction = 1f) => _companion.FillMaxHeight(fraction);

    /// <inheritdoc cref="ModifierExtensions.FillMaxSize(float)"/>
    public static Modifier FillMaxSize(float fraction = 1f) => _companion.FillMaxSize(fraction);

    /// <inheritdoc cref="ModifierExtensions.Height(Dp)"/>
    public static Modifier Height(Dp height) => _companion.Height(height);

    /// <inheritdoc cref="ModifierExtensions.Width(Dp)"/>
    public static Modifier Width(Dp width) => _companion.Width(width);

    /// <inheritdoc cref="ModifierExtensions.Size(Dp)"/>
    public static Modifier Size(Dp size) => _companion.Size(size);

    /// <inheritdoc cref="ModifierExtensions.Size(Dp, Dp)"/>
    public static Modifier Size(Dp width, Dp height) => _companion.Size(width, height);

    /// <inheritdoc cref="ModifierExtensions.SafeDrawingPadding()"/>
    public static Modifier SafeDrawingPadding() => _companion.SafeDrawingPadding();

    /// <inheritdoc cref="ModifierExtensions.SystemBarsPadding()"/>
    public static Modifier SystemBarsPadding() => _companion.SystemBarsPadding();

    /// <inheritdoc cref="ModifierExtensions.MinimumInteractiveComponentSize()"/>
    public static Modifier MinimumInteractiveComponentSize() => _companion.MinimumInteractiveComponentSize();

    /// <inheritdoc cref="ModifierExtensions.Background(Color)"/>
    public static Modifier Background(Color color) => _companion.Background(color);

    /// <inheritdoc cref="ModifierExtensions.Background(Color, Shape?)"/>
    public static Modifier Background(Color color, Shape? shape) => _companion.Background(color, shape);

    /// <inheritdoc cref="ModifierExtensions.Border(Dp, Color)"/>
    public static Modifier Border(Dp width, Color color) => _companion.Border(width, color);

    /// <inheritdoc cref="ModifierExtensions.Border(Dp, Color, Dp)"/>
    public static Modifier Border(Dp width, Color color, Dp cornerRadius) => _companion.Border(width, color, cornerRadius);

    /// <inheritdoc cref="ModifierExtensions.Border(Dp, Color, Shape?)"/>
    public static Modifier Border(Dp width, Color color, Shape? shape) => _companion.Border(width, color, shape);

    /// <inheritdoc cref="ModifierExtensions.Clip(Dp)"/>
    public static Modifier Clip(Dp cornerRadius) => _companion.Clip(cornerRadius);

    /// <inheritdoc cref="ModifierExtensions.Clip(Shape)"/>
    public static Modifier Clip(Shape shape) => _companion.Clip(shape);

    /// <inheritdoc cref="ModifierExtensions.Clickable(Action)"/>
    public static Modifier Clickable(Action onClick) => _companion.Clickable(onClick);

    /// <inheritdoc cref="ModifierExtensions.DragAndDropTarget(Func{DragAndDropEvent, bool}, DragAndDropTarget)"/>
    public static Modifier DragAndDropTarget(Func<DragAndDropEvent, bool> shouldStartDragAndDrop, DragAndDropTarget target) => _companion.DragAndDropTarget(shouldStartDragAndDrop, target);

    /// <inheritdoc cref="ModifierExtensions.VerticalScroll(ScrollState, bool, bool)"/>
    public static Modifier VerticalScroll(ScrollState state, bool enabled = true, bool reverseScrolling = false) => _companion.VerticalScroll(state, enabled, reverseScrolling);

    /// <inheritdoc cref="ModifierExtensions.HorizontalScroll(ScrollState, bool, bool)"/>
    public static Modifier HorizontalScroll(ScrollState state, bool enabled = true, bool reverseScrolling = false) => _companion.HorizontalScroll(state, enabled, reverseScrolling);

    /// <inheritdoc cref="ModifierExtensions.Draggable(DraggableState, Orientation, bool)"/>
    public static Modifier Draggable(DraggableState state, Orientation orientation, bool enabled = true) => _companion.Draggable(state, orientation, enabled);

    /// <inheritdoc cref="ModifierExtensions.NestedScroll(AndroidX.Compose.UI.Input.NestedScroll.INestedScrollConnection)"/>
    public static Modifier NestedScroll(AndroidX.Compose.UI.Input.NestedScroll.INestedScrollConnection connection) => _companion.NestedScroll(connection);

    /// <inheritdoc cref="ModifierExtensions.Weight(float, bool)"/>
    public static Modifier Weight(float weight, bool fill = true) => _companion.Weight(weight, fill);

    /// <inheritdoc cref="ModifierExtensions.WidthIn(Dp?, Dp?)"/>
    public static Modifier WidthIn(Dp? min = null, Dp? max = null) => _companion.WidthIn(min, max);

    /// <inheritdoc cref="ModifierExtensions.HeightIn(Dp?, Dp?)"/>
    public static Modifier HeightIn(Dp? min = null, Dp? max = null) => _companion.HeightIn(min, max);

    /// <inheritdoc cref="ModifierExtensions.SizeIn(Dp?, Dp?, Dp?, Dp?)"/>
    public static Modifier SizeIn(Dp? minWidth = null, Dp? minHeight = null, Dp? maxWidth = null, Dp? maxHeight = null) => _companion.SizeIn(minWidth, minHeight, maxWidth, maxHeight);

    /// <inheritdoc cref="ModifierExtensions.RequiredSize(Dp)"/>
    public static Modifier RequiredSize(Dp size) => _companion.RequiredSize(size);

    /// <inheritdoc cref="ModifierExtensions.RequiredSize(Dp, Dp)"/>
    public static Modifier RequiredSize(Dp width, Dp height) => _companion.RequiredSize(width, height);

    /// <inheritdoc cref="ModifierExtensions.RequiredWidth(Dp)"/>
    public static Modifier RequiredWidth(Dp width) => _companion.RequiredWidth(width);

    /// <inheritdoc cref="ModifierExtensions.RequiredHeight(Dp)"/>
    public static Modifier RequiredHeight(Dp height) => _companion.RequiredHeight(height);

    /// <inheritdoc cref="ModifierExtensions.DefaultMinSize(Dp?, Dp?)"/>
    public static Modifier DefaultMinSize(Dp? minWidth = null, Dp? minHeight = null) => _companion.DefaultMinSize(minWidth, minHeight);

    /// <inheritdoc cref="ModifierExtensions.WrapContentSize(bool)"/>
    public static Modifier WrapContentSize(bool unbounded = false) => _companion.WrapContentSize(unbounded);

    /// <inheritdoc cref="ModifierExtensions.WrapContentWidth(bool)"/>
    public static Modifier WrapContentWidth(bool unbounded = false) => _companion.WrapContentWidth(unbounded);

    /// <inheritdoc cref="ModifierExtensions.WrapContentHeight(bool)"/>
    public static Modifier WrapContentHeight(bool unbounded = false) => _companion.WrapContentHeight(unbounded);

    /// <inheritdoc cref="ModifierExtensions.AspectRatio(float, bool)"/>
    public static Modifier AspectRatio(float ratio, bool matchHeightConstraintsFirst = false) => _companion.AspectRatio(ratio, matchHeightConstraintsFirst);

    /// <inheritdoc cref="ModifierExtensions.Offset(Dp?, Dp?)"/>
    public static Modifier Offset(Dp? x = null, Dp? y = null) => _companion.Offset(x, y);

    /// <inheritdoc cref="ModifierExtensions.AbsoluteOffset(Dp?, Dp?)"/>
    public static Modifier AbsoluteOffset(Dp? x = null, Dp? y = null) => _companion.AbsoluteOffset(x, y);

    /// <inheritdoc cref="ModifierExtensions.ZIndex(float)"/>
    public static Modifier ZIndex(float z) => _companion.ZIndex(z);

    /// <inheritdoc cref="ModifierExtensions.Alpha(float)"/>
    public static Modifier Alpha(float alpha) => _companion.Alpha(alpha);

    /// <inheritdoc cref="ModifierExtensions.Rotate(float)"/>
    public static Modifier Rotate(float degrees) => _companion.Rotate(degrees);

    /// <inheritdoc cref="ModifierExtensions.Scale(float)"/>
    public static Modifier Scale(float scale) => _companion.Scale(scale);

    /// <inheritdoc cref="ModifierExtensions.Scale(float, float)"/>
    public static Modifier Scale(float scaleX, float scaleY) => _companion.Scale(scaleX, scaleY);

    /// <inheritdoc cref="ModifierExtensions.Shadow(Dp, Shape?)"/>
    public static Modifier Shadow(Dp elevation, Shape? shape = null) => _companion.Shadow(elevation, shape);

    /// <inheritdoc cref="ModifierExtensions.GraphicsLayer(float?, float?, float?, float?, float?, float?, float?, float?, float?, float?, long?, Shape?, bool?)"/>
    public static Modifier GraphicsLayer(float? scaleX = null, float? scaleY = null, float? alpha = null, float? translationX = null, float? translationY = null, float? shadowElevation = null, float? rotationX = null, float? rotationY = null, float? rotationZ = null, float? cameraDistance = null, long? transformOrigin = null, Shape? shape = null, bool? clip = null) => _companion.GraphicsLayer(scaleX, scaleY, alpha, translationX, translationY, shadowElevation, rotationX, rotationY, rotationZ, cameraDistance, transformOrigin, shape, clip);

    /// <inheritdoc cref="ModifierExtensions.ImePadding()"/>
    public static Modifier ImePadding() => _companion.ImePadding();

    /// <inheritdoc cref="ModifierExtensions.NavigationBarsPadding()"/>
    public static Modifier NavigationBarsPadding() => _companion.NavigationBarsPadding();

    /// <inheritdoc cref="ModifierExtensions.StatusBarsPadding()"/>
    public static Modifier StatusBarsPadding() => _companion.StatusBarsPadding();

    /// <inheritdoc cref="ModifierExtensions.DisplayCutoutPadding()"/>
    public static Modifier DisplayCutoutPadding() => _companion.DisplayCutoutPadding();

    /// <inheritdoc cref="ModifierExtensions.CaptionBarPadding()"/>
    public static Modifier CaptionBarPadding() => _companion.CaptionBarPadding();

    /// <inheritdoc cref="ModifierExtensions.MandatorySystemGesturesPadding()"/>
    public static Modifier MandatorySystemGesturesPadding() => _companion.MandatorySystemGesturesPadding();

    /// <inheritdoc cref="ModifierExtensions.SafeContentPadding()"/>
    public static Modifier SafeContentPadding() => _companion.SafeContentPadding();

    /// <inheritdoc cref="ModifierExtensions.SafeGesturesPadding()"/>
    public static Modifier SafeGesturesPadding() => _companion.SafeGesturesPadding();

    /// <inheritdoc cref="ModifierExtensions.SystemGesturesPadding()"/>
    public static Modifier SystemGesturesPadding() => _companion.SystemGesturesPadding();

    /// <inheritdoc cref="ModifierExtensions.WaterfallPadding()"/>
    public static Modifier WaterfallPadding() => _companion.WaterfallPadding();

    /// <inheritdoc cref="ModifierExtensions.TestTag(string)"/>
    public static Modifier TestTag(string tag) => _companion.TestTag(tag);

    /// <inheritdoc cref="ModifierExtensions.Align(Alignment)"/>
    public static Modifier Align(Alignment alignment) => _companion.Align(alignment);

    /// <inheritdoc cref="ModifierExtensions.Align(Alignment.Vertical)"/>
    public static Modifier Align(Alignment.Vertical alignment) => _companion.Align(alignment);

    /// <inheritdoc cref="ModifierExtensions.Align(Alignment.Horizontal)"/>
    public static Modifier Align(Alignment.Horizontal alignment) => _companion.Align(alignment);

    /// <inheritdoc cref="ModifierExtensions.MatchParentSize()"/>
    public static Modifier MatchParentSize() => _companion.MatchParentSize();

    /// <inheritdoc cref="ModifierExtensions.Focusable(bool)"/>
    public static Modifier Focusable(bool enabled = true) => _companion.Focusable(enabled);

    /// <inheritdoc cref="ModifierExtensions.FocusGroup()"/>
    public static Modifier FocusGroup() => _companion.FocusGroup();

    /// <inheritdoc cref="ModifierExtensions.OnFocusChanged(Action{FocusState})"/>
    public static Modifier OnFocusChanged(Action<FocusState> onFocusChanged) => _companion.OnFocusChanged(onFocusChanged);

    /// <inheritdoc cref="ModifierExtensions.FocusRequester(FocusRequester)"/>
    public static Modifier FocusRequester(FocusRequester requester) => _companion.FocusRequester(requester);

    /// <inheritdoc cref="ModifierExtensions.CombinedClickable(Action, Action?, Action?)"/>
    public static Modifier CombinedClickable(Action onClick, Action? onLongClick = null, Action? onDoubleClick = null) => _companion.CombinedClickable(onClick, onLongClick, onDoubleClick);

    /// <inheritdoc cref="ModifierExtensions.Selectable(bool, Action)"/>
    public static Modifier Selectable(bool selected, Action onClick) => _companion.Selectable(selected, onClick);

    /// <inheritdoc cref="ModifierExtensions.Toggleable(bool, Action{bool})"/>
    public static Modifier Toggleable(bool value, Action<bool> onValueChange) => _companion.Toggleable(value, onValueChange);

    /// <inheritdoc cref="ModifierExtensions.Semantics(string)"/>
    public static Modifier Semantics(string contentDescription) => _companion.Semantics(contentDescription);

    /// <inheritdoc cref="ModifierExtensions.Semantics(bool, string)"/>
    public static Modifier Semantics(bool mergeDescendants, string contentDescription) => _companion.Semantics(mergeDescendants, contentDescription);

    /// <inheritdoc cref="ModifierExtensions.Semantics(SemanticsRole)"/>
    public static Modifier Semantics(SemanticsRole role) => _companion.Semantics(role);

    /// <inheritdoc cref="ModifierExtensions.Semantics(string?, SemanticsRole?)"/>
    public static Modifier Semantics(string? contentDescription, SemanticsRole? role) => _companion.Semantics(contentDescription, role);

    /// <inheritdoc cref="ModifierExtensions.Semantics(bool, string?, SemanticsRole?)"/>
    public static Modifier Semantics(bool mergeDescendants, string? contentDescription, SemanticsRole? role) => _companion.Semantics(mergeDescendants, contentDescription, role);

    /// <inheritdoc cref="ModifierExtensions.ClearAndSetSemantics(string)"/>
    public static Modifier ClearAndSetSemantics(string contentDescription) => _companion.ClearAndSetSemantics(contentDescription);

    /// <inheritdoc cref="ModifierExtensions.ClearAndSetSemantics(string?, SemanticsRole?)"/>
    public static Modifier ClearAndSetSemantics(string? contentDescription, SemanticsRole? role) => _companion.ClearAndSetSemantics(contentDescription, role);

    /// <inheritdoc cref="ModifierExtensions.DetectTapGestures(Action{Offset}?, Action{Offset}?, Action{Offset}?, Action{Offset}?, object?)"/>
    public static Modifier DetectTapGestures(Action<Offset>? onTap = null, Action<Offset>? onPress = null, Action<Offset>? onLongPress = null, Action<Offset>? onDoubleTap = null, object? key = null) => _companion.DetectTapGestures(onTap, onPress, onLongPress, onDoubleTap, key);

    /// <inheritdoc cref="ModifierExtensions.DetectDragGestures(Modifier, Action{Offset}, Action{Offset}?, Action?, Action?, object?)"/>
    public static Modifier DetectDragGestures(Action<Offset> onDrag, Action<Offset>? onDragStart = null, Action? onDragEnd = null, Action? onDragCancel = null, object? key = null) => _companion.DetectDragGestures(onDrag, onDragStart, onDragEnd, onDragCancel, key);

    /// <inheritdoc cref="ModifierExtensions.DetectTransformGestures(Modifier, Action{Offset, Offset, float, float}, bool, object?)"/>
    public static Modifier DetectTransformGestures(Action<Offset, Offset, float, float> onGesture, bool panZoomLock = false, object? key = null) => _companion.DetectTransformGestures(onGesture, panZoomLock, key);

    /// <inheritdoc cref="ModifierExtensions.Semantics(Action{SemanticsScope})"/>
    public static Modifier Semantics(Action<SemanticsScope> properties) => _companion.Semantics(properties);

    /// <inheritdoc cref="ModifierExtensions.Semantics(bool, Action{SemanticsScope})"/>
    public static Modifier Semantics(bool mergeDescendants, Action<SemanticsScope> properties) => _companion.Semantics(mergeDescendants, properties);

    /// <inheritdoc cref="ModifierExtensions.ClearAndSetSemantics(Action{SemanticsScope})"/>
    public static Modifier ClearAndSetSemantics(Action<SemanticsScope> properties) => _companion.ClearAndSetSemantics(properties);

    /// <summary>
    /// <c>Modifier.padding(paddingValues)</c> — pads using the
    /// <c>PaddingValues</c> handle a layout (e.g. <see cref="Scaffold"/>)
    /// passes to its content lambda. Internal: only Scaffold-shaped
    /// composables that receive a runtime <c>PaddingValues</c> need it.
    /// </summary>
    internal Modifier Padding(IntPtr paddingValues) =>
        Append(curr => ComposeBridges.ModifierPaddingValues(curr, paddingValues));

    /// <summary>
    /// Materialize the chain into a managed <c>IModifier</c> wrapper.
    /// Returns <c>null</c> when the chain is empty (no ops appended) so
    /// callers can keep the Kotlin <c>$default</c> bit set and let
    /// Compose substitute the real default.
    /// </summary>
    internal IModifier? Build() => Build(IntPtr.Zero);

    /// <summary>
    /// Materialize the chain into a managed <c>IModifier</c> wrapper,
    /// optionally prepending a <c>Modifier.padding(contentPadding)</c>
    /// op against the supplied <see cref="IntPtr"/> handle. The padding
    /// op runs FIRST, before any user ops — semantically equivalent to
    /// <c>Modifier.padding(contentPadding).then(this)</c> but without
    /// allocating a managed <see cref="Modifier"/> wrapper. Used by
    /// <see cref="ComposableNode.BuildModifier"/> to apply the
    /// <see cref="Scaffold"/>-supplied <c>PaddingValues</c> to the body
    /// node on every measure pass without per-pass allocations
    /// (issue #46).
    /// </summary>
    internal IModifier? Build(IntPtr contentPadding)
    {
        bool hasContentPadding = contentPadding != IntPtr.Zero;
        if (_ops.Length == 0 && !hasContentPadding)
            return null;

        // `current` always holds the latest live local ref. On the
        // happy path we zero it out after handing ownership to
        // GetObject; the finally then no-ops. On any exception path
        // — JNI op throwing, GetObject throwing, anything in between
        // — the finally deletes whichever local ref is still live so
        // we never leak.
        IntPtr current = IntPtr.Zero;
        try
        {
            current = ComposeBridges.ModifierCompanionInstance();
            if (hasContentPadding)
            {
                IntPtr next = ComposeBridges.ModifierPaddingValues(current, contentPadding);
                JNIEnv.DeleteLocalRef(current);
                current = next;
            }
            for (int i = 0; i < _ops.Length; i++)
            {
                IntPtr next = _ops[i](current);
                JNIEnv.DeleteLocalRef(current);
                current = next;
            }

            var result = Java.Lang.Object.GetObject<IModifier>(current, JniHandleOwnership.TransferLocalRef)!;
            current = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (current != IntPtr.Zero)
                JNIEnv.DeleteLocalRef(current);
        }
    }
}
