#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.ContentSwitcher"/>: composes only the active
	/// view, swapped by MutableState. Views materialize LAZILY on first show and stay
	/// materialized (state survives route switches — the gold keeps route state too). Active
	/// content is Yoga-laid to this node's arranged frame (window metrics standalone),
	/// re-flowed per flush like the other own-content hosts.</summary>
	sealed class ComposeContentSwitcherNode : ComposeNode, IBackendManagesOwnContent
	{
		ContentSwitcher _switcher;
		readonly BackendContext _context;
		readonly MutableState<int> _index = new(0);
		readonly MutableState<int> _contentVersion = new(0);
		ComposeNode?[] _nodes = System.Array.Empty<ComposeNode?>();
		int _indexValue;

		public ComposeContentSwitcherNode(ContentSwitcher switcher, BackendContext context)
		{
			_switcher = switcher;
			_context = context;
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowContent;
		}

		public override void Dispose()
			=> Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowContent;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.ContentSwitcher_Index)
			{
				_indexValue = value.AsInt;
				_index.Value = value.AsInt;
				// Materialize + lay the newly active view before it composes.
				EnsureActive();
			}
		}

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not ContentSwitcher switcher)
				return;
			_switcher = switcher;
			if (!isHotReload)
				return;
			_nodes = System.Array.Empty<ComposeNode?>();
			_contentVersion.Value++;
		}

		Size BoundsDp()
		{
			if (FrameWidth > 0 && FrameHeight > 0)
				return new Size(FrameWidth, FrameHeight);
			var size = _switcher.GetWindowMetrics().SizeDp.Peek();
			return size.Width > 0 && size.Height > 0 ? size : ScreenSizeDp();
		}

		void EnsureActive()
		{
			var views = _switcher.Views;
			if (_nodes.Length != views.Count)
			{
				var resized = new ComposeNode?[views.Count];
				System.Array.Copy(_nodes, resized, System.Math.Min(_nodes.Length, views.Count));
				_nodes = resized;
			}
			int i = _indexValue;
			if (i < 0 || i >= views.Count)
				return;
			_nodes[i] ??= (ComposeNode)CometBackendBridge.Materialize(views[i], _context);
			LayoutActive();
		}

		void LayoutActive()
		{
			int i = _indexValue;
			if (i < 0 || i >= _switcher.Views.Count || _nodes.Length <= i || _nodes[i] is null)
				return;
			var bounds = BoundsDp();
			if (bounds.Width <= 0)
				return;
			CometBackendLayoutEngine.Layout(_switcher.Views[i], bounds);
		}

		void ReflowContent() => LayoutActive();

		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : ScreenSizeDp().Width;
			double h = double.IsFinite(heightConstraint) && heightConstraint > 0 ? heightConstraint : ScreenSizeDp().Height;
			return new Size(w, h);
		}

		public override void Render(IComposer composer)
		{
			_ = _contentVersion.Value;
			int index = _index.Value;
			EnsureActive();

			var box = new Box();
			((ComposableNode)box).Modifier = BuildNodeModifier() ?? Modifier.Companion.FillMaxSize();
			if (index >= 0 && index < _nodes.Length && _nodes[index] is { } active)
				box.Add(active);
			box.Render(composer);
		}
	}
}
#endif
