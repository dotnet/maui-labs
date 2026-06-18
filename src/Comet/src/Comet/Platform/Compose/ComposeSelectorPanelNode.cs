#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.SelectorPanel"/> as the gold-standard Jetchat
	/// <c>SelectorExpanded</c>: a single Material <c>Surface</c> that swaps its content by the active
	/// selector index (or composes nothing when collapsed). The direct generalization of
	/// <see cref="ComposeDrawerNode"/> — a <see cref="MutableState{T}"/> index instead of a bool, one of
	/// N materialized children instead of two slots. Owns its children
	/// (<see cref="IBackendManagesOwnContent"/>): the Yoga engine treats this as a measured leaf, so the
	/// active panel's height grows the footer (and shrinks the sibling message list) on the next reactive
	/// reflow — the lighter, layout-driven equivalent of the gold's bottom-anchored
	/// <c>Surface(tonalElevation = 8.dp)</c>. While a panel is open a <see cref="BackHandler"/> routes the
	/// system back press to a dismiss event (the control writes the index back to 0).</summary>
	sealed class ComposeSelectorPanelNode : ComposeNode, IBackendManagesOwnContent
	{
		readonly SelectorPanel _panel;
		readonly BackendContext _context;
		readonly MutableState<int> _selector = new(0);   // drives Render (content swap)
		int _selectorValue;                               // drives Measure (layout reflow)
		ComposeNode?[] _nodes = System.Array.Empty<ComposeNode?>();
		bool _initialized;

		public ComposeSelectorPanelNode(SelectorPanel panel, BackendContext context)
		{
			_panel = panel;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.SelectorPanel_Index)
			{
				_selectorValue = value.AsInt;   // plain field, read by Measure outside composition
				_selector.Value = value.AsInt;   // MutableState, read by Render to recompose
			}
		}

		void EnsureContent()
		{
			if (_initialized)
				return;
			_initialized = true;

			var panels = _panel.Panels;
			_nodes = new ComposeNode?[panels.Count];
			for (int i = 0; i < panels.Count; i++)
				if (panels[i] is { } v)
					_nodes[i] = (ComposeNode)CometBackendBridge.Materialize(v, _context);
		}

		View? ActiveView()
		{
			var panels = _panel.Panels;
			return _selectorValue > 0 && _selectorValue < panels.Count ? panels[_selectorValue] : null;
		}

		// Own-content leaf: report the active panel's natural height so the Yoga engine grows the footer
		// (and shrinks the message list). Returns zero when collapsed, so the slot disappears. Re-run by
		// ComposeBackendRoot after every reactive flush, so a selector change reflows the layout.
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			EnsureContent();

			var view = ActiveView();
			if (view is null)
				return Size.Zero;

			double width = double.IsInfinity(widthConstraint) || widthConstraint <= 0
				? global::Android.Content.Res.Resources.System!.DisplayMetrics!.WidthPixels / Density
				: widthConstraint;

			// Lay the active panel out to the host width (wrapping its height) AND push the frames onto
			// its node subtree, so Render below can draw it positioned. Mirrors the list's per-row layout.
			return CometBackendLayoutEngine.LayoutContent(view, width);
		}

		public override void Render(IComposer composer)
		{
			EnsureContent();

			int s = _selector.Value;   // subscribe so a selector change recomposes this scope
			ComposeNode? node = s > 0 && s < _nodes.Length ? _nodes[s] : null;
			if (node is null)
				return;   // collapsed (NONE) or a dialog-handled selector (DM) → compose nothing

			// The expandable panel = Surface(tonalElevation = 8.dp). The facade Surface doesn't expose
			// tonalElevation, so the app supplies the M3 elevation-8 surface color via .Background(); a
			// real Surface painted with it is pixel-identical (the same approach the footer uses for the
			// 2.dp bar). The node frame (BuildNodeModifier) positions + sizes the Surface in the footer.
			var surface = new AndroidX.Compose.Surface { Modifier = BuildNodeModifier() };
			if (Background is { } bg)
				surface.Color = ToComposeColor(bg);

			// Gold UserInput.kt: `BackHandler(onBack = dismissKeyboard)` while a selector is visible.
			surface.Add(new BackHandler(() => Sink?.OnEvent(EventIds.SelectorPanelDismissed), enabled: true));
			surface.Add(node);
			((ComposableNode)surface).Render(composer);
		}
	}
}
#endif
