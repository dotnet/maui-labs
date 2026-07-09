#nullable enable
#if ANDROID
using System.Threading;
using System.Threading.Tasks;
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.Drawer"/> as the real Material
	/// <c>ModalNavigationDrawer</c> widget (the same control the gold-standard Jetchat uses): the
	/// <see cref="ModalDrawerSheet"/> slides over the content with the standard scrim + edge-swipe
	/// gesture, driven by a <see cref="DrawerStateHolder"/>. The Comet <c>Drawer_IsOpen</c> signal is
	/// synced to the drawer's animated state both ways. Owns its two children
	/// (<see cref="IBackendManagesOwnContent"/>), laying the content out at full screen and the sheet
	/// at the standard 360dp width with the shared Yoga engine.</summary>
	sealed class ComposeDrawerNode : ComposeNode, IBackendManagesOwnContent
	{
		// Material 3 ModalDrawerSheet is 360dp wide.
		const float SheetWidthDp = 360f;

		Drawer _drawer;
		readonly BackendContext _context;
		readonly MutableState<bool> _open = new(false);
		readonly MutableState<int> _contentVersion = new(0);
		readonly DrawerStateHolder _holder = new(AndroidX.Compose.Material3.DrawerValue.Closed);
		ComposeNode? _sideNode, _contentNode;

		public ComposeDrawerNode(Drawer drawer, BackendContext context)
		{
			_drawer = drawer;
			_context = context;

			// Re-lay the hosted content/side after every reactive flush so an IME-driven size
			// change (or rotation) reflows them — this own-content node is a leaf to the engine,
			// so the top-level RunLayout can't reach inside it (mirrors ComposeNavigationNode).
			Comet.Reactive.ReactiveScheduler.AfterFlush += ReflowContent;
		}

		/// <summary>The node was transferred to a new Drawer. Re-point always; only a hot reload
		/// re-materializes the side/content subtrees (the code changed) — an ordinary re-render
		/// keeps the materialized content and the open/closed state.</summary>
		public override void OnOwnerViewChanged(View newView, bool isHotReload)
		{
			if (newView is not Drawer drawer)
				return;
			_drawer = drawer;
			if (!isHotReload)
				return;
			_sideNode = null;
			_contentNode = null;
			_contentVersion.Value++;
		}

		public override void Dispose()
			=> Comet.Reactive.ReactiveScheduler.AfterFlush -= ReflowContent;

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Drawer_IsOpen)
				_open.Value = value.AsBool;
		}

		void EnsureContent()
		{
			if (_contentNode is not null)
				return;

			_sideNode = (ComposeNode)CometBackendBridge.Materialize(_drawer.Side, _context);
			_contentNode = (ComposeNode)CometBackendBridge.Materialize(_drawer.Content, _context);
			LayoutContent();
		}

		// Lay the content out full-viewport and the sheet at the standard width. Re-run on every
		// flush so a keyboard-driven AvailableSize change (or rotation) reflows the hosted content
		// instead of leaving it sized to whatever the viewport was when it was first materialized.
		void LayoutContent()
		{
			if (_contentNode is null)
				return;
			var size = ScreenSizeDp();
			CometBackendLayoutEngine.Layout(_drawer.Content, size);
			CometBackendLayoutEngine.Layout(_drawer.Side, new Microsoft.Maui.Graphics.Size(SheetWidthDp, size.Height));
		}

		void ReflowContent()
		{
			if (_contentNode is not null)
				LayoutContent();
		}

		public override void Render(IComposer composer)
		{
			_ = _contentVersion.Value;   // subscribe: a reload re-materializes content
			EnsureContent();

			bool open = _open.Value;

			// Drive the real drawer's animated state from the Comet open-signal. Effects run after
			// composition, so the holder is already bound to its live peer when these fire.
			composer.LaunchedEffect(open, async ct =>
			{
				if (open && _holder.IsClosed)
					await _holder.OpenAsync();
				else if (!open && _holder.IsOpen)
					await _holder.CloseAsync();
			});

			// Reading CurrentValue subscribes this scope, so a user gesture (edge-swipe / scrim tap)
			// that closes the drawer recomposes here and reports it back so the Comet signal clears.
			var current = _holder.CurrentValue;
			composer.LaunchedEffect(current, ct =>
			{
				if (_holder.IsClosed && _open.Value)
					Sink?.OnEvent(EventIds.DrawerClosed);
				// Symmetric: an edge-swipe OPEN must reflect back too, or the signal
				// desyncs and (being equality-gated) swallows the next programmatic close.
				else if (_holder.IsOpen && !_open.Value)
					Sink?.OnEvent(EventIds.DrawerOpened);
				return Task.CompletedTask;
			});

			var sheet = new ModalDrawerSheet();
			sheet.Add(_sideNode!);

			var drawer = new ModalNavigationDrawer(drawerState: _holder)
			{
				Drawer = sheet,
				Content = _contentNode!,
			};
			((ComposableNode)drawer).Modifier = Modifier.Companion.FillMaxSize();
			drawer.Render(composer);
		}
	}
}
#endif
