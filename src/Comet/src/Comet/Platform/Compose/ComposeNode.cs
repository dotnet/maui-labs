#nullable enable
#if ANDROID
using System.Collections.Generic;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// A retained backend node that bridges Comet's imperative diff
	/// (<see cref="ICometBackendNode"/>) to Jetpack Compose's declarative
	/// recomposition (the vendored <see cref="ComposableNode"/>).
	/// </summary>
	/// <remarks>
	/// The node is created once when a Comet view mounts and lives across
	/// recompositions. Each renderable property is a retained Compose
	/// <see cref="MutableState{T}"/>: <see cref="ApplyProperty"/> writes
	/// <c>.Value</c> (the single steady-state JNI call — <c>setValue</c>), and
	/// <see cref="ComposableNode.Render"/> reads it inside composition so Compose
	/// recomposes only the narrowest scope. Structural changes bump
	/// <see cref="_childVersion"/>, which container renders read so a child
	/// insert/remove/move recomposes the container.
	/// </remarks>
	abstract class ComposeNode : ComposableNode, ICometBackendNode
	{
		protected readonly List<ComposeNode> Children = new();
		readonly MutableState<int> _childVersion = new(0);
		ICometEventSink? _sink;
		readonly MutableState<bool> _hasTap = new(false);
		readonly MutableState<bool> _hasRecord = new(false);
		// Color/Thickness aren't Java-boxable, so they can't live in a Compose MutableState.
		// They're held as plain fields and a style-version state drives recomposition.
		Microsoft.Maui.Graphics.Color? _background;
		Microsoft.Maui.Thickness _padding;
		CornerRadii _corners;
		float _elevation;
		float _borderWidth;
		Microsoft.Maui.Graphics.Color? _borderColor;
		// Reactive translation (Dp): a render-time offset (no parent reflow) for transforms such as the
		// profile-photo parallax. Folded into the Yoga frame offset in BuildNodeModifier.
		float _translationX, _translationY;
		// Reactive visibility: opacity (1 = opaque) and IsVisible. A fully-transparent or
		// invisible node fades out AND stops receiving taps, so a hidden overlay (e.g. the
		// JumpToBottom FAB) doesn't intercept touches. Driven by a reactive property push.
		float _opacity = 1f;
		bool _isVisible = true;
		readonly MutableState<int> _styleVersion = new(0);

		// Yoga-driven layout (when the engine runs): parent-relative frame in Dp + a version
		// state so a re-arrange recomposes. Until arranged, Compose lays the node out natively.
		float _fx, _fy, _fw, _fh;
		bool _hasFrame;
		float _contentTopInset;
		readonly MutableState<int> _frameVersion = new(0);

		/// <summary>Display density (px per Dp), set by the backend root; used to convert native
		/// pixel measurements (e.g. text) into the Dp space Yoga computes in.</summary>
		public static float Density { get; set; } = 1f;

		/// <summary>The app's current available size in Dp — the ComposeView's actual laid-out
		/// size, kept current by <see cref="ComposeBackendRoot"/> (shrinks when the soft
		/// keyboard resizes the window under AdjustResize, changes on rotation). Zero until
		/// the first layout; consumers fall back to DisplayMetrics then.</summary>
		public static Microsoft.Maui.Graphics.Size AvailableSize { get; set; }

		protected bool HasFrame => _hasFrame;

		/// <summary>The Yoga-arranged width of this node in Dp (0 until arranged). Own-content
		/// nodes (list, scroll) use it to lay their own content out to the host's width.</summary>
		protected float FrameWidth => _fw;

		/// <summary>The Yoga-arranged parent-relative position in Dp (0 until arranged). A self-sizing
		/// native control (e.g. a FAB overlay) positions itself at this offset WITHOUT applying the
		/// Yoga size, so the real control measures + lays out its own content (Option C / overlays).</summary>
		protected float FrameX => _fx;
		protected float FrameY => _fy;

		/// <summary>For nodes that build their own modifier (bypassing <see cref="BuildNodeModifier"/>):
		/// subscribe to style + frame changes so a reactive opacity/visibility/position update
		/// recomposes, and return the effective alpha (<c>IsVisible ? Opacity : 0</c>). A fully
		/// transparent node should not be rendered at all (so it doesn't intercept input).</summary>
		protected float SubscribeAndGetAlpha()
		{
			_ = _styleVersion.Value;
			_ = _frameVersion.Value;
			return _isVisible ? _opacity : 0f;
		}

		/// <summary>True when a non-zero corner radius was set (so a subclass can apply
		/// <see cref="CornerShape"/> to a composable that takes an explicit shape, e.g. a Button).</summary>
		protected bool HasRoundedCorners => !_corners.IsZero;

		/// <summary>The top-left corner radius in Dp (avatars/clips use a uniform radius). Lets a
		/// subclass clip a hosted native View to the same outline Compose would (e.g. ImageView).</summary>
		protected float CornerRadiusDp => (float)_corners.TopLeft;

		/// <summary>The Comet padding (Dp). The Yoga engine doesn't inset leaf content, so a leaf
		/// that needs interior padding (e.g. a borderless text field) applies this itself.</summary>
		protected Microsoft.Maui.Thickness Padding => _padding;

		/// <summary>The background color set via <c>.Background()</c>, if any. Lets a container render
		/// as a Material <c>Surface</c> (color + shape) rather than a plain Box.</summary>
		protected Microsoft.Maui.Graphics.Color? Background => _background;

		protected ICometEventSink? Sink => _sink;

		// ICometBackendNode ----------------------------------------------------

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.HasTapGesture)
				_hasTap.Value = value.AsBool;
			else if (id == PropertyIds.HasRecordGesture)
				_hasRecord.Value = value.AsBool;
			else if (id == PropertyIds.Opacity)
			{
				_opacity = (float)value.AsDouble;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.IsVisible)
			{
				_isVisible = value.AsBool;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.BackgroundColor)
			{
				_background = value.AsColor;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.Padding)
			{
				_padding = value.AsObject is Microsoft.Maui.Thickness t ? t : default;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.CornerRadius)
			{
				_corners = value.AsObject is CornerRadii c ? c : default;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.Shadow)
			{
				_elevation = (float)value.AsDouble;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.Border)
			{
				if (value.AsObject is BorderSpec b)
				{
					_borderWidth = (float)b.Width;
					_borderColor = b.Color;
				}
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.TranslationX)
			{
				_translationX = (float)value.AsDouble;
				_styleVersion.Value++;
			}
			else if (id == PropertyIds.TranslationY)
			{
				_translationY = (float)value.AsDouble;
				_styleVersion.Value++;
			}
			else
				ApplyControlProperty(id, in value);
		}

		/// <summary>Applies a control-specific property. Common (View-level) properties are
		/// handled by <see cref="ApplyProperty"/> before this is called.</summary>
		protected abstract void ApplyControlProperty(PropertyId id, in PropertyValue value);

		/// <summary>Builds the modifier chain this node applies to its composable: background,
		/// padding, and a clickable when the Comet view has a tap gesture. Reads run inside
		/// composition (via MutableState) so changes recompose. Returns null when none apply.</summary>
		protected Modifier? BuildNodeModifier()
		{
			_ = _styleVersion.Value; // subscribe so background/padding changes recompose
			_ = _frameVersion.Value; // subscribe so a re-arrange recomposes

			Modifier? m = null;

			// Yoga-positioned: place + size this node absolutely within its (Box) parent. Applied
			// first so background/clickable cover the arranged frame. A reactive translation shifts the
			// render position without a parent reflow (the photo parallax).
			if (_hasFrame)
				m = Modifier.Companion
					.AbsoluteOffset(new Dp(_fx + _translationX), new Dp(_fy + _translationY))
					.Size(new Dp(_fw), new Dp(_fh));
			else if (_translationX != 0f || _translationY != 0f)
				m = Modifier.Companion.AbsoluteOffset(new Dp(_translationX), new Dp(_translationY));

			// Reactive visibility: fade the whole node (background + content) — applied early so
			// everything painted below inherits the alpha. An invisible node collapses to alpha 0.
			float alpha = _isVisible ? _opacity : 0f;
			if (alpha < 1f)
				m = (m ?? Modifier.Companion).Alpha(alpha);

			// baselineHeight: inset the content down within the (already-grown) box so the text's
			// first baseline lands at the requested offset. Applied after Size so it insets inside.
			if (_contentTopInset > 0)
				m = (m ?? Modifier.Companion).Padding(0f, _contentTopInset, 0f, 0f);

			// Card surface / chat bubble: raise (shadow) then round the corners, so the shadow
			// follows the rounded outline and the background/content below are clipped to it.
			bool rounded = !_corners.IsZero;
			if (_elevation > 0)
				m = (m ?? Modifier.Companion).Shadow(new Dp(_elevation), rounded ? CornerShape() : null);

			if (rounded)
				m = (m ?? Modifier.Companion).Clip(CornerShape());

			if (_background is { } bg)
				m = (m ?? Modifier.Companion).Background(ToComposeColor(bg));

			// Stroke (e.g. avatar ring) following the same rounded outline, drawn over the fill.
			if (_borderWidth > 0 && _borderColor is { } bc)
				m = (m ?? Modifier.Companion).Border(new Dp(_borderWidth), ToComposeColor(bc),
					rounded ? CornerShape() : null);

			// Native padding only when Compose lays out natively; under Yoga the engine has
			// already inset the children (their absolute frames include the padding), so applying
			// it again here would double it and push content off the edge.
			var p = _padding;
			if (!_hasFrame && (p.Left != 0 || p.Top != 0 || p.Right != 0 || p.Bottom != 0))
				m = (m is null ? Modifier.Companion : m)
					.Padding((float)p.Left, (float)p.Top, (float)p.Right, (float)p.Bottom);

			// Skip the clickable when faded out / invisible so a hidden overlay doesn't eat taps.
			if (_hasTap.Value && alpha > 0f)
			{
				var clickable = Modifier.Clickable(() =>
					Sink?.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default)));
				m = m is null ? clickable : m.Then(clickable);
			}

			// Press-and-hold voice-record drag (gold RecordButton's detectDragGesturesAfterLongPress):
			// long-press engages, per-frame deltas (converted px→dp) flow back as Pan phases, and the
			// Comet RecordGesture accumulates the offset + applies the swipe-to-cancel threshold.
			if (_hasRecord.Value && alpha > 0f)
			{
				float density = Density;
				var record = Modifier.Companion.DetectDragGesturesAfterLongPress(
					onDrag: d => Sink?.OnGesture(GestureKind.Pan, new GestureData(
						GestureState.Changed, default, new Point(d.X / density, d.Y / density))),
					onDragStart: _ => Sink?.OnGesture(GestureKind.Pan, new GestureData(GestureState.Began, default)),
					onDragEnd: () => Sink?.OnGesture(GestureKind.Pan, new GestureData(GestureState.Ended, default)),
					onDragCancel: () => Sink?.OnGesture(GestureKind.Pan, new GestureData(GestureState.Cancelled, default)));
				m = m is null ? record : m.Then(record);
			}

			return m;
		}

		// The clip/shadow outline. Uniform corners use the single-radius factory; otherwise
		// per-corner (LTR: TopLeft→topStart, TopRight→topEnd, BottomRight→bottomEnd, BottomLeft→bottomStart).
		protected AndroidX.Compose.Shape CornerShape() => _corners.IsUniform
			? AndroidX.Compose.Shape.RoundedCorners(new Dp((float)_corners.TopLeft))
			: AndroidX.Compose.Shape.RoundedCorners(
				new Dp((float)_corners.TopLeft), new Dp((float)_corners.TopRight),
				new Dp((float)_corners.BottomRight), new Dp((float)_corners.BottomLeft));

		protected static AndroidX.Compose.Color ToComposeColor(Microsoft.Maui.Graphics.Color c) => AndroidX.Compose.Color.FromArgb(
			(byte)(c.Alpha * 255), (byte)(c.Red * 255), (byte)(c.Green * 255), (byte)(c.Blue * 255));

		public void InsertChild(int index, ICometBackendNode child)
		{
			Children.Insert(index, (ComposeNode)child);
			_childVersion.Value++;
		}

		public void RemoveChildAt(int index)
		{
			Children.RemoveAt(index);
			_childVersion.Value++;
		}

		public void MoveChild(int fromIndex, int toIndex)
		{
			var node = Children[fromIndex];
			Children.RemoveAt(fromIndex);
			Children.Insert(toIndex, node);
			_childVersion.Value++;
		}

		// Leaf intrinsic size for the Yoga engine (overridden by measurable leaves, e.g. text).
		// Container nodes defer to Yoga and need no intrinsic size.
		public virtual Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;

		// First text baseline (Dp from the top), for baseline-aligned rows; null = no text baseline.
		public virtual double? MeasureBaseline(double width, double height) => null;

		// baselineHeight content inset (Dp): the engine grew the box by this and we pad the top to match.
		public void SetContentTopInset(double dp)
		{
			_contentTopInset = (float)dp;
			_frameVersion.Value++;
		}

		// Yoga-computed parent-relative frame (in Dp); stored + recomposed so BuildNodeModifier
		// positions this node absolutely. Only bump the version when the frame actually changes, so a
		// full-tree reflow (run after every reactive flush) recomposes just the nodes that moved/resized
		// — not the whole subtree on every keystroke.
		public void Arrange(Rect frame)
		{
			float nx = (float)frame.X, ny = (float)frame.Y, nw = (float)frame.Width, nh = (float)frame.Height;
			bool changed = !_hasFrame || nx != _fx || ny != _fy || nw != _fw || nh != _fh;
			_fx = nx; _fy = ny; _fw = nw; _fh = nh;
			_hasFrame = true;
			if (changed)
				_frameVersion.Value++;
		}

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;

		/// <summary>A (hot) reload adopted this retained node onto a rebuilt view. Own-content
		/// subclasses re-point their view reference and invalidate materialized content.</summary>
		public virtual void OnOwnerViewChanged(View newView) { }

		public void Dispose() { }

		// Composition helpers --------------------------------------------------

		/// <summary>Adds this node's children to a freshly-built vendored container,
		/// subscribing to structural changes so the container recomposes on
		/// insert/remove/move.</summary>
		protected void AddChildrenTo(ComposableContainer container)
		{
			_ = _childVersion.Value; // subscribe
			foreach (var child in Children)
				container.Add(child);
		}
	}
}
#endif
