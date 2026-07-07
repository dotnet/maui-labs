#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;
using Microsoft.Maui.Graphics;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.SearchBar"/> as the REAL M3 state-based
	/// search pair: the collapsed <see cref="AndroidX.Compose.SearchBar"/> at the node's
	/// Yoga frame plus the <see cref="ExpandedDockedSearchBar"/> popup, sharing one
	/// <see cref="SearchBarState"/>/<see cref="SearchBarTextFieldState"/> (expansion on
	/// focus, back-collapse — all inside the widget). Slot + content views are owned
	/// (<see cref="IBackendManagesOwnContent"/>); typed text streams into the control's
	/// Query signal via a snapshot flow.</summary>
	sealed class ComposeSearchBarNode : ComposeNode, IBackendManagesOwnContent
	{
		// M3 search bar container height (dp).
		const float BarHeightDp = 56f;

		Comet.SearchBar _bar;
		readonly BackendContext _context;
		readonly SearchBarState _searchState = new();
		readonly SearchBarTextFieldState _textState = new();
		readonly MutableState<int> _contentVersion = new(0);
		ComposeNode? _placeholder, _leading, _trailing, _content;
		bool _built;

		public ComposeSearchBarNode(Comet.SearchBar bar, BackendContext context)
		{
			_bar = bar;
			_context = context;
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowContent;
		}

		public override void Dispose()
			=> Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowContent;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value) { }

		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not Comet.SearchBar bar)
				return;
			_bar = bar;
			if (!isHotReload)
				return;
			_built = false;
			_placeholder = _leading = _trailing = _content = null;
			_contentVersion.Value++;
		}

		void EnsureContent()
		{
			if (_built)
				return;
			_built = true;
			_placeholder = (ComposeNode)CometBackendBridge.Materialize(_bar.PlaceholderView, _context);
			_content = (ComposeNode)CometBackendBridge.Materialize(_bar.ContentView, _context);
			if (_bar.LeadingView is { } leading)
				_leading = (ComposeNode)CometBackendBridge.Materialize(leading, _context);
			if (_bar.TrailingView is { } trailing)
				_trailing = (ComposeNode)CometBackendBridge.Materialize(trailing, _context);
			LayoutContent();
		}

		// The popup content wraps to its natural height at the bar's width; re-laid per
		// flush so query-driven result changes reflow.
		void LayoutContent()
		{
			if (_content is null)
				return;
			double width = FrameWidth > 0 ? FrameWidth : ScreenSizeDp().Width - 32;
			CometBackendLayoutEngine.LayoutContent(_bar.ContentView, width);
		}

		void ReflowContent()
		{
			if (_content is not null)
				LayoutContent();
		}

		// In-flow footprint: the collapsed bar. The expanded popup overlays (widget-managed).
		public override Size Measure(double widthConstraint, double heightConstraint)
		{
			double w = double.IsFinite(widthConstraint) && widthConstraint > 0
				? widthConstraint : ScreenSizeDp().Width;
			return new Size(w, BarHeightDp);
		}

		SearchBarInputField MakeInputField() => new(_textState, _searchState)
		{
			Placeholder = _placeholder,
			LeadingIcon = _leading,
			TrailingIcon = _trailing,
			OnSearch = q => _bar.OnSearch?.Invoke(q),
		};

		public override void Render(IComposer composer)
		{
			_ = _contentVersion.Value;
			EnsureContent();

			// Stream every edit into the CURRENT control's Query signal (equality-gated
			// there; _bar re-points on owner re-render, so don't capture it).
			var capturedText = _textState;
			composer.LaunchedEffect(true, async ct =>
			{
				await foreach (var text in ComposeExtensions.SnapshotFlow(() => capturedText.Text)
					.WithCancellation(ct))
				{
					Comet.ThreadHelper.RunOnMainThread(() =>
					{
						_bar.Query.Value = text;
						Comet.Reactive.ReactiveScheduler.EnsureFlushScheduled();
					});
				}
			});

			var box = new Box();
			((ComposableNode)box).Modifier = BuildNodeModifier() ?? Modifier.Companion;

			var bar = new AndroidX.Compose.SearchBar(_searchState) { InputField = MakeInputField() };
			box.Add(bar);

			var expanded = new ExpandedDockedSearchBar(_searchState) { InputField = MakeInputField() };
			expanded.Add(_content!);
			box.Add(expanded);

			box.Render(composer);
		}
	}
}
#endif
