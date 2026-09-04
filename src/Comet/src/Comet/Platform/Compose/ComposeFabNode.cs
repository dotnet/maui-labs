#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.Fab"/> as the REAL Material 3
	/// <c>ExtendedFloatingActionButton</c> — never a styled pill. Uses
	/// <see cref="ExtendedFloatingActionButton.RenderDirect"/> to call the Kotlin bridge with
	/// <c>ComposableLambda</c>-wrapped slot content directly, bypassing the generated
	/// slot-property wiring that had an empty-render bug. The FAB is a self-sizing native overlay
	/// (Option C): <see cref="Measure"/> reports its intrinsic size (the real content measured
	/// by the engine + the FAB's documented insets) so the parent's Yoga layout can corner-pin
	/// it, then <see cref="Render"/> positions it at that offset and lets the native FAB size +
	/// lay out its own content. Owns its content (<see cref="IBackendManagesOwnContent"/>).</summary>
	sealed class ComposeFabNode : ComposeNode, IBackendManagesOwnContent
	{
		// Material extended-FAB content insets (dp): start 16, icon→text gap 12, end 20.
		const float PadStart = 16f, Gap = 12f, PadEnd = 20f, MinWidth = 48f;

		Fab _fab;
		readonly BackendContext _context;
		readonly MutableState<bool> _extended;
		ComposeNode? _icon, _label;
		bool _built;

		public ComposeFabNode(Fab fab, BackendContext context)
		{
			_fab = fab;
			_context = context;
			_extended = new MutableState<bool>(fab.Extended);

			// Subscribe to the reactive extended signal so Compose recomposes when it changes.
			if (fab.ExtendedSignal is { } sig)
			{
				_extended.Value = sig.Peek();
				sig.PropertyChanged += (_, __) =>
				{
					bool v = sig.Peek();
					Comet.ThreadHelper.RunOnMainThread(() => _extended.Value = v);
				};
			}
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		/// <summary>Re-point at the new Fab; only a hot reload rebuilds the icon/label slots
		/// (an ordinary re-render keeps them).</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not Fab fab)
				return;
			_fab = fab;
			if (!isHotReload)
				return;
			_built = false;
			_icon = null;
			_label = null;
		}

		void EnsureContent()
		{
			if (_built)
				return;
			_built = true;
			// Always build both slot nodes — ExtendedFloatingActionButton uses them in both
			// expanded and contracted states (icon shows in both; label only shows when expanded).
			_icon = (ComposeNode)CometBackendBridge.Materialize(_fab.IconView, _context);
			_label = (ComposeNode)CometBackendBridge.Materialize(_fab.LabelView, _context);
			// The icon/label inherit Opacity=0 from the parent FAB's environment (the FAB starts
			// hidden via .Opacity(0)). Their visibility is driven by the FAB's own alpha modifier
			// on the ExtendedFloatingActionButton composable — reset slot content to full opacity.
			var fullOpacity = PropertyValue.From(1.0);
			_icon.ApplyProperty(PropertyIds.Opacity, in fullOpacity);
			_label.ApplyProperty(PropertyIds.Opacity, in fullOpacity);
		}

		// Intrinsic size for the parent's Yoga layout: the real content's measured width + the
		// FAB's content insets. Always returns the extended (full) width so Yoga can corner-pin
		// the FAB; the FAB self-sizes when contracted (the parent layout is unaffected since it's
		// an overlay, not in flow). Height is the gold's pinned value.
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			EnsureContent();
			var icon = CometBackendLayoutEngine.Measure(_fab.IconView);
			var label = CometBackendLayoutEngine.Measure(_fab.LabelView);
			double width = System.Math.Max(MinWidth, PadStart + icon.Width + Gap + label.Width + PadEnd);
			return new Size(width, _fab.Height);
		}

		public override void Render(IComposer composer)
		{
			EnsureContent();

			// Subscribe to _extended so Compose recomposes when the extended state changes.
			bool fabExtended = _extended.Value;

			// Reactive visibility: keep the FAB COMPOSED even when hidden — leaving/re-entering
			// composition discards the slot content's state and it renders empty on return — so
			// apply alpha + push off-screen to keep it alive but invisible and non-interactive.
			float alpha = SubscribeAndGetAlpha();
			float offsetY = alpha <= 0f ? FrameY + 2000f : FrameY;

			// When contracted, Compose shrinks the FAB to a square of _fab.Height dp.
			// Yoga computed FrameX for the extended (wide) FAB; shift right by the width difference
			// so the RIGHT edge stays pinned (BottomEnd alignment is preserved in both states).
			float adjustedX = fabExtended ? FrameX : FrameX + FrameWidth - (float)_fab.Height;
			var modifier = Modifier.Companion.AbsoluteOffset(new Dp(adjustedX), new Dp(offsetY));
			if (alpha < 1f)
				modifier = modifier.Alpha(alpha);

			void OnClick() => Sink?.OnEvent(EventIds.Clicked);

			// Drive the real ExtendedFloatingActionButton via the direct bridge path. The
			// `expanded` parameter animates between extended (icon+label pill) and contracted
			// (icon-only rounded square) states — matching AnimatingFabContent's behaviour in
			// the gold. Both FABs use this path: ProfileFab (reactive extended) and
			// JumpToBottom (always extended = always showing icon + label).
			ExtendedFloatingActionButton.RenderDirect(
				icon:           _icon!,
				text:           _label!,
				onClick:        OnClick,
				expanded:       fabExtended,
				containerColor: _fab.ContainerColor is { } cc ? ToComposeColor(cc) : null,
				contentColor:   _fab.ContentColor is { } fc ? ToComposeColor(fc) : null,
				modifier:       modifier,
				composer:       composer);
		}
	}
}
#endif
