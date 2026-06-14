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
		// Color/Thickness aren't Java-boxable, so they can't live in a Compose MutableState.
		// They're held as plain fields and a style-version state drives recomposition.
		Microsoft.Maui.Graphics.Color? _background;
		Microsoft.Maui.Thickness _padding;
		CornerRadii _corners;
		float _elevation;
		float _borderWidth;
		Microsoft.Maui.Graphics.Color? _borderColor;
		readonly MutableState<int> _styleVersion = new(0);

		// Yoga-driven layout (when the engine runs): parent-relative frame in Dp + a version
		// state so a re-arrange recomposes. Until arranged, Compose lays the node out natively.
		float _fx, _fy, _fw, _fh;
		bool _hasFrame;
		readonly MutableState<int> _frameVersion = new(0);

		/// <summary>Display density (px per Dp), set by the backend root; used to convert native
		/// pixel measurements (e.g. text) into the Dp space Yoga computes in.</summary>
		public static float Density { get; set; } = 1f;

		protected bool HasFrame => _hasFrame;

		/// <summary>The Yoga-arranged width of this node in Dp (0 until arranged). Own-content
		/// nodes (list, scroll) use it to lay their own content out to the host's width.</summary>
		protected float FrameWidth => _fw;

		/// <summary>True when a non-zero corner radius was set (so a subclass can apply
		/// <see cref="CornerShape"/> to a composable that takes an explicit shape, e.g. a Button).</summary>
		protected bool HasRoundedCorners => !_corners.IsZero;

		/// <summary>The top-left corner radius in Dp (avatars/clips use a uniform radius). Lets a
		/// subclass clip a hosted native View to the same outline Compose would (e.g. ImageView).</summary>
		protected float CornerRadiusDp => (float)_corners.TopLeft;

		protected ICometEventSink? Sink => _sink;

		// ICometBackendNode ----------------------------------------------------

		public void ApplyProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.HasTapGesture)
				_hasTap.Value = value.AsBool;
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
			// first so background/clickable cover the arranged frame.
			if (_hasFrame)
				m = Modifier.Companion
					.AbsoluteOffset(new Dp(_fx), new Dp(_fy))
					.Size(new Dp(_fw), new Dp(_fh));

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

			if (_hasTap.Value)
			{
				var clickable = Modifier.Clickable(() =>
					Sink?.OnGesture(GestureKind.Tap, new GestureData(GestureState.Ended, default)));
				m = m is null ? clickable : m.Then(clickable);
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

		// Yoga-computed parent-relative frame (in Dp); stored + recomposed so BuildNodeModifier
		// positions this node absolutely.
		public void Arrange(Rect frame)
		{
			_fx = (float)frame.X; _fy = (float)frame.Y;
			_fw = (float)frame.Width; _fh = (float)frame.Height;
			_hasFrame = true;
			_frameVersion.Value++;
		}

		public void SetEventSink(ICometEventSink? sink) => _sink = sink;

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
