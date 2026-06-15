#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.Fab"/> as the REAL Material 3
	/// <c>FloatingActionButton</c> (or <c>ExtendedFloatingActionButton</c> when extended) — not a
	/// styled pill. The FAB is a self-sizing native overlay (Option C): <see cref="Measure"/> reports
	/// its intrinsic size (the real content measured by the engine + the FAB's documented insets) so
	/// the parent's Yoga layout can corner-pin it, then <see cref="Render"/> positions it at that
	/// offset and lets the native FAB size + lay out its own content, pinning only the gold's height.
	/// Owns its content (<see cref="IBackendManagesOwnContent"/>).</summary>
	sealed class ComposeFabNode : ComposeNode, IBackendManagesOwnContent
	{
		// Material extended-FAB content insets (dp): start 16, icon→text gap 12, end 20.
		const float PadStart = 16f, Gap = 12f, PadEnd = 20f, MinWidth = 48f;

		readonly Fab _fab;
		readonly BackendContext _context;
		ComposeNode? _icon, _label;
		bool _built;

		public ComposeFabNode(Fab fab, BackendContext context)
		{
			_fab = fab;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		void EnsureContent()
		{
			if (_built)
				return;
			_built = true;
			_icon = (ComposeNode)CometBackendBridge.Materialize(_fab.IconView, _context);
			_label = (ComposeNode)CometBackendBridge.Materialize(_fab.LabelView, _context);
		}

		// Intrinsic size for the parent's Yoga layout: the real content's measured width + the FAB's
		// content insets (the icon/label come from the engine's measure, so the label width reflects
		// the actual font). Height is the gold's pinned value.
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

			// Position at the Yoga offset but let the FAB self-size its width (the native control
			// measures its own content); pin only the gold's height.
			var modifier = Modifier.Companion
				.AbsoluteOffset(new Dp(FrameX), new Dp(FrameY))
				.Height(new Dp((float)_fab.Height));

			void OnClick() => Sink?.OnEvent(EventIds.Clicked);

			if (_fab.Extended)
			{
				var fab = new ExtendedFloatingActionButton(onClick: OnClick, expanded: true)
				{
					Icon = _icon!,
					Text = _label!,
				};
				if (_fab.ContainerColor is { } cc) fab.ContainerColor = ToComposeColor(cc);
				if (_fab.ContentColor is { } fc) fab.ContentColor = ToComposeColor(fc);
				((ComposableNode)fab).Modifier = modifier;
				fab.Render(composer);
			}
			else
			{
				var row = new Row(Arrangement.SpacedBy(new Dp(Gap)), AndroidX.Compose.Alignment.Vertical.CenterVertically);
				row.Add(_icon!);
				row.Add(_label!);

				var fab = new FloatingActionButton(OnClick);
				if (_fab.ContainerColor is { } cc) fab.ContainerColor = ToComposeColor(cc);
				if (_fab.ContentColor is { } fc) fab.ContentColor = ToComposeColor(fc);
				((ComposableNode)fab).Modifier = modifier.WidthIn(min: new Dp(MinWidth));
				fab.Add(row);
				fab.Render(composer);
			}
		}
	}
}
#endif
