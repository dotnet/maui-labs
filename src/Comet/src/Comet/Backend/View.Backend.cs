#nullable enable
using System;
using Comet.Backend;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// Backend-bridge surface on <see cref="View"/>. Materializes a view's renderable
	/// state into the platform-agnostic <see cref="ICometBackendNode"/> protocol,
	/// emitting <em>only</em> properties the view actually set (defaults never cross the
	/// boundary). This is the seam that replaces the MAUI <c>PropertyMapper</c> path.
	/// </summary>
	/// <remarks>
	/// Phase 1 reads values through the existing typed accessors and emits set-only
	/// patches by comparing against documented defaults; the later typed-storage
	/// conversion swaps the dictionary reads for fields + set-bits behind this same API,
	/// a behavior-preserving change. <c>ApplyAllSetProperties</c> is the hand-authored
	/// golden shape the source generator will eventually emit per control.
	/// </remarks>
	public partial class View
	{
		ICometBackendNode? backendNode;

		/// <summary>The retained backend node for this view, if it has been materialized.</summary>
		/// <remarks>Hot-reload active-view registration is NOT done here: registering every
		/// materialized node (hundreds of list rows) leaked into MauiHotReloadHelper's global
		/// ActiveViews (no IsEnabled gate, never pruned). The bridge registers only the reload
		/// ROOTS ([Body]/Component views), gated on IsEnabled — see CometBackendBridge.</remarks>
		internal ICometBackendNode? Node
		{
			get => backendNode;
			set => backendNode = value;
		}

		/// <summary>
		/// Node-backend twin of the ViewHandler transfer in <c>UpdateFromOldView</c>: the new
		/// view adopts the old view's retained node, events rebind to the new view, and the
		/// new view's set properties are re-emitted (unchanged values no-op on the backend's
		/// retained state, changed values patch through and recompose).
		/// </summary>
		internal void TransferBackendNodeFrom(View oldView, bool isHotReload)
		{
			var node = oldView.Node;
			if (node is null)
				return;

			oldView.Node = null;
			Node = node;
			node.SetEventSink(new ViewEventSink(this));
			// Always re-point the node's owner reference to this new view; only a hot reload
			// (code changed) invalidates the node's materialized content — an ordinary
			// re-render preserves retained state (nav stack, list scroll). See the interface doc.
			node.OnOwnerViewChanged(this, isHotReload);
			ApplyChangedProperties(oldView, node);
		}

		/// <summary>
		/// Creates the platform backend node for this control. Overridden by each
		/// control's generated platform partial (e.g. <c>ComposeTextNode</c>); base
		/// throws so a missing override is a loud error rather than a silent blank view.
		/// Host tests bypass this via a pluggable node factory on the bridge.
		/// </summary>
		protected internal virtual ICometBackendNode CreateBackendNode(Backend.BackendContext context)
			=> throw new NotSupportedException(
				$"{GetType().Name} has no backend node. The control's platform partial must override CreateBackendNode.");

		/// <summary>
		/// Emits every property this view has set onto <paramref name="node"/>. Base
		/// emits the <c>View</c>-common visual/transform properties; control partials
		/// override, call <c>base</c>, then add their own.
		/// </summary>
		protected internal virtual void ApplyAllSetProperties(ICometBackendNode node)
		{
			// Visual. Emit Opacity whenever it was *explicitly* set (env key present), even when it
			// equals 1 — a reactive toggle back to 1 must still reach the node (the set-only patch
			// would otherwise drop it and leave a faded node stuck). Views that never set opacity
			// emit nothing, preserving the defaults-don't-cross contract.
			var opacity = this.GetEnvironment<double?>(EnvironmentKeys.View.Opacity);
			if (opacity is { } op)
				node.ApplyProperty(PropertyIds.Opacity, PropertyValue.From(op));

			if (!IsVisible)
				node.ApplyProperty(PropertyIds.IsVisible, PropertyValue.From(false));

			if (this.GetBackground() is SolidPaint { Color: { } bg })
				node.ApplyProperty(PropertyIds.BackgroundColor, PropertyValue.From(bg));

			var pad = this.GetPadding();
			if (pad.Left != 0 || pad.Top != 0 || pad.Right != 0 || pad.Bottom != 0)
				node.ApplyProperty(PropertyIds.Padding, PropertyValue.FromObject(pad));

			if (this is IStackLayout stack)
				node.ApplyProperty(PropertyIds.Stack_Spacing, PropertyValue.From(stack.Spacing));

			// Opt-in: render this container as a Material Surface (color + shape) rather than a plain
			// Box — used for chat bubbles, which the gold standard draws with Surface(color, shape).
			if (this.GetEnvironment<bool?>(this, "Comet.AsSurface", false) == true)
				node.ApplyProperty(PropertyIds.Container_Surface, PropertyValue.From(true));

			if (this.GetEnvironment<bool?>(this, "Comet.AsCard", false) is true)
				node.ApplyProperty(PropertyIds.Container_Card, PropertyValue.From(true));

			if (this.GetEnvironment<Microsoft.Maui.Graphics.Color[]>(this, "Comet.BackgroundGradient", false) is { Length: > 1 } gradient)
				node.ApplyProperty(PropertyIds.GradientBackground, PropertyValue.FromObject(gradient));

			// Surface styling read from the canonical styling vocabulary (ClipShape / Shadow /
			// Border), so .ClipShape/.Shadow/.RoundedBorder, ButtonStyles, ViewModifier and the
			// .CornerRadius/.Elevation/.Border sugar all flow to the backend through one path.
			if (this.GetClipShape() is { } clip && ToCornerRadii(clip) is { IsZero: false } corners)
				node.ApplyProperty(PropertyIds.CornerRadius, PropertyValue.FromObject(corners));

			if (this.GetShadow() is { } shadow && shadow.Radius > 0)
				node.ApplyProperty(PropertyIds.Shadow, PropertyValue.From((double)shadow.Radius));

			if (this.GetBorder() is { } borderShape)
			{
				var strokeWidth = borderShape.GetLineWidth(this, 0f);
				var strokeColor = borderShape.GetStrokeColor(this, null!);
				if (strokeWidth > 0 && strokeColor is not null)
					node.ApplyProperty(PropertyIds.Border, PropertyValue.FromObject(new BorderSpec(strokeWidth, strokeColor)));
			}

			// Transforms. Translation is emitted whenever explicitly set (env key present), even back
			// to 0 — so a reactive parallax that returns to the origin clears rather than sticking at the
			// last offset (the same always-emit reasoning as Opacity above). Scale/rotation keep the
			// identity-skip (not driven reactively here).
			var t = (ITransform)this;
			if (this.GetEnvironment<double?>(nameof(ITransform.TranslationX)) is { } tx)
				node.ApplyProperty(PropertyIds.TranslationX, PropertyValue.From(tx));
			if (this.GetEnvironment<double?>(nameof(ITransform.TranslationY)) is { } ty)
				node.ApplyProperty(PropertyIds.TranslationY, PropertyValue.From(ty));
			if (t.ScaleX != 1) node.ApplyProperty(PropertyIds.ScaleX, PropertyValue.From(t.ScaleX));
			if (t.ScaleY != 1) node.ApplyProperty(PropertyIds.ScaleY, PropertyValue.From(t.ScaleY));
			if (t.Rotation != 0) node.ApplyProperty(PropertyIds.Rotation, PropertyValue.From(t.Rotation));
			if (t.RotationX != 0) node.ApplyProperty(PropertyIds.RotationX, PropertyValue.From(t.RotationX));
			if (t.RotationY != 0) node.ApplyProperty(PropertyIds.RotationY, PropertyValue.From(t.RotationY));

			if (!IsEnabled)
				node.ApplyProperty(PropertyIds.IsEnabled, PropertyValue.From(false));

			// Tap gestures become a clickable modifier on the backend node.
			if (HasTapGesture())
				node.ApplyProperty(PropertyIds.HasTapGesture, PropertyValue.From(true));

			// A record gesture becomes a detectDragGesturesAfterLongPress modifier (gold RecordButton).
			if (HasRecordGesture())
				node.ApplyProperty(PropertyIds.HasRecordGesture, PropertyValue.From(true));

			// A long-press gesture becomes a combinedClickable onLongClick (Reply row selection).
			if (HasLongPressGesture())
				node.ApplyProperty(PropertyIds.HasLongPressGesture, PropertyValue.From(true));
		}

		// Parse a ClipShape into the per-corner radii the backend nodes consume. Only the rounded
		// shapes carry corner data; any other shape clips but contributes no corner radius.
		static CornerRadii? ToCornerRadii(Comet.Shape shape) => shape switch
		{
			RoundedRectangle rr => new CornerRadii(rr.CornerRadius),
			AsymmetricRoundedRectangle ar => new CornerRadii(ar.TopLeft, ar.TopRight, ar.BottomRight, ar.BottomLeft),
			_ => null,
		};

		bool HasTapGesture()
		{
			var gestures = Gestures;
			if (gestures is null)
				return false;
			for (int i = 0; i < gestures.Count; i++)
				if (gestures[i] is TapGesture)
					return true;
			return false;
		}

		bool HasRecordGesture()
		{
			var gestures = Gestures;
			if (gestures is null)
				return false;
			for (int i = 0; i < gestures.Count; i++)
				if (gestures[i] is RecordGesture)
					return true;
			return false;
		}

		bool HasLongPressGesture()
		{
			var gestures = Gestures;
			if (gestures is null)
				return false;
			for (int i = 0; i < gestures.Count; i++)
				if (gestures[i] is LongPressGesture)
					return true;
			return false;
		}

		/// <summary>
		/// Applies the minimal set of property changes between an old view instance and
		/// this one onto the (transferred) <paramref name="node"/>. Phase 1 re-emits the
		/// full set-only patch; the generator will narrow this to a per-bit comparison.
		/// </summary>
		protected internal virtual void ApplyChangedProperties(View old, ICometBackendNode node)
			=> ApplyAllSetProperties(node);

		/// <summary>
		/// Pushes the current property values to this view's backend node after a reactive
		/// change. Phase 1 re-emits the full set-only patch (cheap: writing a backend
		/// MutableState to an unchanged value is a no-op under structural equality); the
		/// generator will narrow this to the single changed property.
		/// </summary>
		internal void UpdateBackendNode()
		{
			if (Node is { } node)
				ApplyAllSetProperties(node);
		}

		/// <summary>
		/// Handles a no-payload event raised by this view's backend node (e.g. a Compose
		/// button click). Controls override to invoke their reactive handlers. No-op base.
		/// </summary>
		protected internal virtual void OnBackendEvent(Backend.EventId id) { }

		/// <summary>
		/// Handles an event carrying a payload (e.g. a TextField's new string, a Switch's
		/// new bool) so the control can write it back to its bound state. No-op base.
		/// </summary>
		protected internal virtual void OnBackendEvent<T>(Backend.EventId id, T payload) { }

		/// <summary>Handles a gesture raised by this view's backend node (e.g. a Compose
		/// clickable tap), invoking the matching Comet gesture recognizers.</summary>
		protected internal virtual void OnBackendGesture(Backend.GestureKind kind, in Backend.GestureData data)
		{
			var gestures = Gestures;
			if (gestures is null)
				return;

			if (kind == Backend.GestureKind.Tap)
			{
				for (int i = 0; i < gestures.Count; i++)
					if (gestures[i] is TapGesture tap)
						tap.Invoke();
				return;
			}

			if (kind == Backend.GestureKind.LongPress)
			{
				for (int i = 0; i < gestures.Count; i++)
					if (gestures[i] is LongPressGesture lp)
						lp.Invoke();
				return;
			}

			// The voice-record drag arrives as a Pan with the long-press/drag/release phases;
			// feed them to the RecordGesture, which accumulates the offset and applies the
			// swipe-to-cancel threshold (gold RecordButton).
			if (kind == Backend.GestureKind.Pan)
			{
				for (int i = 0; i < gestures.Count; i++)
				{
					if (gestures[i] is not RecordGesture rec)
						continue;
					switch (data.State)
					{
						case Backend.GestureState.Began: rec.Begin(); break;
						case Backend.GestureState.Changed: rec.Drag(data.Delta.X, data.Delta.Y); break;
						case Backend.GestureState.Ended: rec.End(); break;
						case Backend.GestureState.Cancelled: rec.Cancel(); break;
					}
				}
			}
		}
	}

	/// <summary>Routes a backend node's events to its owning Comet view.</summary>
	sealed class ViewEventSink : Backend.ICometEventSink
	{
		readonly View _view;
		public ViewEventSink(View view) => _view = view;

		public void OnEvent(Backend.EventId id) => _view.OnBackendEvent(id);
		public void OnEvent<T>(Backend.EventId id, T payload) => _view.OnBackendEvent(id, payload);
		public void OnGesture(Backend.GestureKind kind, in Backend.GestureData data) => _view.OnBackendGesture(kind, data);
	}
}
