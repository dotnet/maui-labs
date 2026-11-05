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
	sealed class ComposeContentSwitcherNode : ComposeNode, IBackendRetainsLogicalContentOnOwnerTransfer
	{
		ContentSwitcher _switcher;
		readonly RetainedContentCache<ComposeNode> _content;
		readonly MutableState<int> _index = new(0);
		readonly MutableState<int> _contentVersion = new(0);
		int _indexValue;
		int _activeIndex = -1;
		ComposeNode? _activeNode;

		public ComposeContentSwitcherNode(ContentSwitcher switcher, BackendContext context)
		{
			_switcher = switcher;
			_content = new RetainedContentCache<ComposeNode>(switcher, context);
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowContent;
		}

		public override void Dispose()
		{
			Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowContent;
			if (_activeIndex >= 0)
				_content.SetActive(_activeIndex, false);
			_content.Dispose();
			base.Dispose();
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.ContentSwitcher_Index)
			{
				if (_activeIndex >= 0 && _activeIndex != value.AsInt)
					_content.SetActive(_activeIndex, false);
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
			_content.TransferOwner(
				switcher,
				switcher.Views,
				isHotReload || !string.IsNullOrEmpty(newView.GetKey()));
			if (isHotReload || !string.IsNullOrEmpty(newView.GetKey()))
			{
				_activeIndex = -1;
				_activeNode = null;
				_contentVersion.Value++;
			}
			EnsureActive();
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
			int i = _indexValue;
			if (i < 0 || i >= views.Count)
				return;
			var (node, _) = _content.GetOrMaterialize(i, views[i]);
			var replacedActiveNode = _activeIndex == i &&
				_activeNode is not null &&
				!ReferenceEquals(_activeNode, node);
			_content.SetActive(i, true);
			_activeIndex = i;
			_activeNode = node;
			if (replacedActiveNode)
				_contentVersion.Value++;
			LayoutActive();
		}

		void LayoutActive()
		{
			int i = _indexValue;
			if (i < 0 ||
				i >= _switcher.Views.Count ||
				!_content.TryGet(i, out _, out var activeView) ||
				activeView is null)
				return;
			var bounds = BoundsDp();
			if (bounds.Width <= 0)
				return;
			CometBackendLayoutEngine.Layout(activeView, bounds);
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
			if (_content.TryGet(index, out var active, out _) && active is not null)
				box.Add(active);
			box.Render(composer);
		}
	}
}
#endif
