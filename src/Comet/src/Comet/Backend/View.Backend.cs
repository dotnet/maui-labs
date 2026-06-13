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
		/// <summary>The retained backend node for this view, if it has been materialized.</summary>
		internal ICometBackendNode? Node { get; set; }

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
			// Visual
			var opacity = this.GetOpacity();
			if (opacity != 1d)
				node.ApplyProperty(PropertyIds.Opacity, PropertyValue.From(opacity));

			if (!IsVisible)
				node.ApplyProperty(PropertyIds.IsVisible, PropertyValue.From(false));

			if (this.GetBackground() is SolidPaint { Color: { } bg })
				node.ApplyProperty(PropertyIds.BackgroundColor, PropertyValue.From(bg));

			var pad = this.GetPadding();
			if (pad.Left != 0 || pad.Top != 0 || pad.Right != 0 || pad.Bottom != 0)
				node.ApplyProperty(PropertyIds.Padding, PropertyValue.FromObject(pad));

			if (this is IStackLayout stack)
				node.ApplyProperty(PropertyIds.Stack_Spacing, PropertyValue.From(stack.Spacing));

			// Surface styling (rounded corners + elevation) — the Material card / chat-bubble blocks.
			if (this.GetEnvironment<CornerRadii?>(this, SurfaceExtensions.CornerRadiusKey, false) is { } corners && !corners.IsZero)
				node.ApplyProperty(PropertyIds.CornerRadius, PropertyValue.FromObject(corners));

			if (this.GetEnvironment<double?>(this, SurfaceExtensions.ElevationKey, false) is { } elevation && elevation > 0)
				node.ApplyProperty(PropertyIds.Shadow, PropertyValue.From(elevation));

			if (this.GetEnvironment<BorderSpec?>(this, SurfaceExtensions.BorderKey, false) is { } border && border.Width > 0)
				node.ApplyProperty(PropertyIds.Border, PropertyValue.FromObject(border));

			// Transforms — emit only when they differ from identity.
			var t = (ITransform)this;
			if (t.TranslationX != 0) node.ApplyProperty(PropertyIds.TranslationX, PropertyValue.From(t.TranslationX));
			if (t.TranslationY != 0) node.ApplyProperty(PropertyIds.TranslationY, PropertyValue.From(t.TranslationY));
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
		}

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
			if (kind != Backend.GestureKind.Tap)
				return;
			var gestures = Gestures;
			if (gestures is null)
				return;
			for (int i = 0; i < gestures.Count; i++)
				if (gestures[i] is TapGesture tap)
					tap.Invoke();
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
